using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Mcmod;

/// Options shared by the workspace operations. Rid overrides the environment rid (spec §5.1).
/// Cancelling the token stops an operation; one that has started changing the workspace undoes
/// its changes first.
public record OperationOptions(
    bool DryRun = false,
    bool IgnoreScripts = false,
    bool NoDependencies = false,
    string? Rid = null,
    CancellationToken CancellationToken = default);

/// One entry of the workspace lock, as listed to the user.
public record InstalledPackage(string Identity, string Version, bool Explicit, string Kind);

/// The workspace operations: install, uninstall, update and list (spec §6–§9).
///
/// Every operation loads the lock (migrating a v3 tooth_lock.json), determines the environment,
/// computes the new package set with the graph solver, then uninstalls what leaves the set (in
/// reverse install order) and installs what enters it (in install order). An update of a package
/// is an uninstall of the old and an install of the new version (spec §7).
///
/// Operations are all or nothing (spec §7). An operation first plans every uninstall and install
/// against a view of the workspace as the earlier steps leave it, checking everything and
/// fetching every archive; a failure then changes nothing. It then applies the plans through one
/// WorkspaceTransaction, journaled next to the lock in .lip, and writes the lock last; a failure
/// (or cancellation) undoes every change and leaves the lock as it was. An operation other than a
/// dry run holds the workspace's WorkspaceMutex throughout, and first finishes or undoes what an
/// interrupted operation left in the journal (protocol/workspace.md §8.4).
///
/// Installed packages are pinned: they enter the solver as they are, rebuilt from the lock
/// without network access, and their archives are opened from the lock only when needed. If the
/// request cannot be satisfied with every installed package pinned, it is solved again with only
/// the explicitly installed packages pinned, so that dependencies may change version. Packages
/// that no explicitly installed package needs any more are removed.
///
/// Locks migrated from v3 have no archive identity: such packages are reopened by resolving
/// their tag again (archive key "path@version"). The variants of one tooth have distinct
/// identities (spec §10.2 rule 1), but a lock may still hold two entries of one identity (e.g. a
/// v3 lock with tooth paths differing only in case); they are kept, listed and uninstalled as
/// separate entries, while the solver sees the identity once (spec §6 condition 3 applies to new solves).
///
/// A package can change identity between versions: a tooth.json names its own path in "tooth",
/// so when its repository moves (GitHub redirects the old path), the newer tags name the new
/// path, and a renamed repository gives the package another tooth name. Updating such a package replaces it under its new identity, and installing it under
/// either path does not add a second copy (see MovedFrom); the graph solver treats both
/// identities as one package (GraphSolver.IsRenamed) and both paths as one repository.
public class McmodService(
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    ILockStore lockStore,
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    IToothCompat toothCompat,
    IDocumentParser documentParser,
    IReadOnlyList<IMcmodInstaller> installers,
    Func<IResolver, IGraphSolver>? createSolver = null,
    Func<string>? workspaceRoot = null,
    IDirectoryLinker? directoryLinker = null) {
  private readonly IFileSystem _fs = fileSystem;
  private readonly IUserInteraction _ui = userInteraction;
  private readonly ILockStore _lockStore = lockStore;
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly IToothCompat _toothCompat = toothCompat;
  private readonly IDocumentParser _documentParser = documentParser;
  private readonly IReadOnlyList<IMcmodInstaller> _installers = installers;
  private readonly Func<IResolver, IGraphSolver> _createSolver =
      createSolver ?? (resolver => new GraphSolver(resolver));
  private readonly Func<string> _workspaceRoot =
      workspaceRoot ?? fileSystem.Directory.GetCurrentDirectory;
  private readonly WorkspaceLayout _layout = new(
      fileSystem, directoryLinker ?? new DirectoryLinker(fileSystem), userInteraction);

  public async Task Install(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);
    List<(string Argument, PackageRequest Request)> requests = ParseRequests(packages);

    if (options.NoDependencies) {
      await InstallWithoutDependencies(workspace, requests, options);
      return;
    }

    List<Ref> requestedRefs = [.. requests.Select(r => r.Request.Ref)];
    List<Candidate> candidates = await RequestedCandidates(workspace, requests);
    List<string> identities = [.. candidates.Select(c => c.Package.Identity)];
    List<string> moved = [];
    for (int i = 0; i < candidates.Count; i++) {
      Candidate candidate = candidates[i];
      string identity = candidate.Package.Identity;
      if (workspace.Lock.Packages.Any(p => p.Explicit && SameIdentity(p.Identity, identity))) {
        throw new InvalidOperationException(
            $"Cannot install package '{identity}' because it is already explicitly installed.");
      }

      // A package installed under the identity it had before its repository moved is the same
      // package: installing it under the new identity replaces it instead of adding a copy.
      if (await MovedFrom(workspace, candidate, requestedRefs[i]) is LockedPackage from) {
        if (from.Explicit) {
          throw new InvalidOperationException(
              $"Cannot install package '{identity}' because it is already explicitly installed"
              + $" as '{from.Identity}'. Run 'lip update {from.Chain[0].Ref}' to replace it with"
              + $" '{identity}'.");
        }

        await ReportMoved(from, candidate, options);
        moved.Add(from.Identity);
      }
    }

    await Execute(workspace, options, Changes(workspace, options, await MakePlan(
        workspace,
        [.. requests.Select(r => r.Request)],
        unpinned: [.. identities, .. moved],
        explicitIdentities: ExplicitIdentities(workspace.Lock))));
  }

  public async Task Uninstall(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);

    List<LockedPackage> targets = [];
    foreach (string package in packages) {
      List<LockedPackage> matched = MatchInstalled(workspace, package);
      if (matched.Count == 0) {
        matched = await MatchMoved(workspace, package);
      }

      if (matched.Count == 0) {
        throw new InvalidOperationException($"Package '{package}' is not installed.");
      }

      if (!options.NoDependencies && matched.Any(p => !p.Explicit)) {
        throw new InvalidOperationException(
            $"Package '{package}' is not explicitly installed and cannot be uninstalled without "
            + "--no-dependencies.");
      }

      targets.AddRange(matched.Where(p => !targets.Contains(p)));
    }

    // Removing some of several entries of one identity (see the class remarks) leaves the
    // identity installed, so the package set does not change: such entries
    // are uninstalled directly, like every target with --no-dependencies.
    List<LockedPackage> direct = options.NoDependencies
        ? targets
        : [.. targets.GroupBy(p => Fold(p.Identity))
            .Where(g => workspace.Lock.Packages.Count(p => Fold(p.Identity) == g.Key) > g.Count())
            .SelectMany(g => g)];
    if (options.NoDependencies) {
      await _ui.PrintWarning(
          "--no-dependencies is enabled. This operation may result in a broken workspace.");
    }

    // The direct uninstalls come first, then what the plan of the rest changes; one
    // transaction applies both.
    Workspace original = workspace;
    List<Change> changes = [];
    if (direct.Count > 0) {
      List<LockedPackage> remaining = [.. workspace.Lock.Packages];
      foreach (LockedPackage target in Enumerable.Reverse(workspace.Lock.Packages)
          .Where(direct.Contains)) {
        changes.Add(new UninstallChange(target));
        remaining.Remove(target);
      }

      targets = [.. targets.Except(direct)];
      if (targets.Count == 0) {
        await Execute(original, options, new ChangeSet(changes, Task.FromResult));
        return;
      }

      workspace = workspace with { Lock = LockWith(workspace, remaining) };
    }

    HashSet<string> removed = [.. targets.Select(p => Fold(p.Identity))];
    Plan plan = await MakePlan(
        workspace,
        [],
        unpinned: [],
        explicitIdentities: [.. ExplicitIdentities(workspace.Lock).Where(i => !removed.Contains(Fold(i)))]);

    foreach (Candidate kept in plan.Members.Where(c => removed.Contains(Fold(c.Package.Identity)))) {
      await _ui.PrintInfo(
          $"Package '{kept.Package.Identity}' is still required by other packages; keeping it "
          + "as a dependency.");
    }

    ChangeSet planned = Changes(workspace, options, plan);
    await Execute(original, options, planned with { Changes = [.. changes, .. planned.Changes] });
  }

  public async Task Update(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);

    List<(string Argument, PackageRequest Request)> requests;
    // Without arguments, the installed package each request comes from.
    List<LockedPackage> sources = [];
    if (packages.Count == 0) {
      // Every explicitly installed package, requested again by the ref it was installed from: a
      // spec ref at its newest version, a branch at its head, a tag where it points now, a rev
      // as it is, a URL as cached and a local file with its current content.
      requests = [];
      foreach (IGrouping<string, LockedPackage> group in workspace.Lock.Packages
          .Where(p => p.Explicit && p.Chain.Count > 0)
          .GroupBy(p => Fold(p.Identity))) {
        LockedPackage package = group.First();
        Ref reference = Ref.Parse(package.Chain[0].Ref, allowFile: true);
        string? reason = reference.Kind switch {
          RefKind.File when !_fs.File.Exists(reference.LocalPath) =>
              $"{reference.LocalPath}, which it was installed from, does not exist any more",
          // Written by development builds that installed from paths in the workspace.
          RefKind.Relative => $"it was installed from {reference}, which is not a package lip "
              + "can request; install it from a .mcmod file to update it",
          _ => null,
        };
        if (reason is not null) {
          await _ui.PrintWarning($"Keeping package '{package.Identity}' as it is: {reason}.");
          continue;
        }

        requests.Add((package.Identity, new PackageRequest(reference, null)));
        sources.Add(package);
      }
    } else {
      requests = ParseRequests(packages);
    }

    List<Ref> requestedRefs = [.. requests.Select(r => r.Request.Ref)];
    List<Candidate> candidates = await RequestedCandidates(
        workspace, requests, allowSameIdentity: packages.Count == 0);
    List<string> identities = [];
    List<PackageRequest> requested = [];
    List<string> moved = [];
    for (int i = 0; i < candidates.Count; i++) {
      Candidate candidate = candidates[i];
      string identity = candidate.Package.Identity;
      // The newest version may have another identity (spec §9, "Updating": the package is then
      // replaced), e.g. a tooth.json whose repository moved and whose "tooth" now names the new
      // path. Without arguments, the request comes from the installed package it replaces;
      // otherwise that package is found by MovedFrom.
      LockedPackage? from = packages.Count == 0
          ? (SameIdentity(sources[i].Identity, identity) ? null : sources[i])
          : workspace.Lock.Packages.Any(p => SameIdentity(p.Identity, identity))
              ? null
              : await MovedFrom(workspace, candidate, requestedRefs[i])
                  ?? throw new InvalidOperationException(
                      $"Cannot update package '{requests[i].Argument}' because it is not"
                      + " currently installed.");
      if (from is not null) {
        await ReportMoved(from, candidate, options);
        moved.Add(from.Identity);
      }

      // Without arguments, a package installed under its old identity and the same package
      // installed under its new one are requested again as one package.
      if (identities.Any(other => SameIdentity(other, identity))) {
        continue;
      }

      identities.Add(identity);
      requested.Add(requests[i].Request);
    }

    HashSet<string> replaced = [.. moved.Select(Fold)];
    await Execute(workspace, options, Changes(workspace, options, await MakePlan(
        workspace,
        requested,
        unpinned: [.. identities, .. moved],
        explicitIdentities: [
          .. ExplicitIdentities(workspace.Lock).Where(i => !replaced.Contains(Fold(i))),
        ])));
  }

  public async Task<IReadOnlyList<InstalledPackage>> List() {
    LockFile lockFile = await _lockStore.Load(_workspaceRoot());
    return [
      .. lockFile.Packages.Select(p => new InstalledPackage(
          p.Identity, p.Version, p.Explicit, p.Kind)),
    ];
  }

  /// The versions of a spec ref, lowest first.
  public async Task<IReadOnlyList<string>> Versions(string package) {
    if (!Ref.TryParse(package, out Ref? reference, out _) || reference.Kind != RefKind.Spec
        || reference.GitRef is not null) {
      throw new ArgumentException($"'{package}' is not a package path.", nameof(package));
    }

    IReadOnlyList<SpecVersion> versions = await _archiveProvider.ListVersions(reference.Target);
    return [.. versions.Reverse().Select(v => v.Version.ToString())];
  }

  /// The root document (tooth.json or manifest.json) of the archive a package argument points
  /// to: for a spec ref, its newest version, the given one, or the commit its git ref resolves
  /// to; otherwise the archive of the URL or local file.
  public async Task<string> View(string package) {
    PackageRequest request = PackageArguments.Parse(
        package, _fs.Path.GetFullPath(_workspaceRoot()), _fs);

    ArchiveHandle archive;
    if (request.Ref is { Kind: RefKind.Spec, GitRef: GitRef gitRef }) {
      archive = await _archiveProvider.OpenCommit(
          request.Ref.Target, await _archiveProvider.ResolveGitRef(request.Ref.Target, gitRef));
    } else if (request.Ref.Kind == RefKind.Spec) {
      IReadOnlyList<SpecVersion> versions = await _archiveProvider.ListVersions(
          request.Ref.Target);
      SpecVersion version = versions.FirstOrDefault(
          v => request.Range?.IsSatisfiedBy(v.Version) ?? true)
          ?? throw new InvalidOperationException(
              $"No version of '{request.Ref.Target}' matches {request.Range?.ToString() ?? "*"}.");
      archive = await _archiveProvider.OpenVersion(request.Ref.Target, version);
    } else {
      archive = await _archiveProvider.Open(request.Ref, request.Base);
    }

    HashSet<string> keys = [.. archive.Source.Keys];
    string file = keys.Contains(ManifestFormat.ToothFileName) ? ManifestFormat.ToothFileName
        : keys.Contains(ManifestFormat.ManifestFileName) ? ManifestFormat.ManifestFileName
        : throw new InvalidOperationException(
            $"{archive.Key} has neither {ManifestFormat.ToothFileName} nor "
            + $"{ManifestFormat.ManifestFileName}.");

    using Stream stream = await archive.Source.OpenRead(file);
    using JsonDocument document = await JsonDocument.ParseAsync(stream);
    return JsonSerializer.Serialize(document.RootElement, _indented);
  }

  private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

  #region Planning

  private async Task<Workspace> OpenWorkspace(OperationOptions options) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    LockFile lockFile = await _lockStore.Load(root);
    LipEnvironment environment = DetermineEnvironment(lockFile, options.Rid);
    Resolver resolver = new(_archiveProvider, _archiveReader, _toothCompat, Warn);
    return new Workspace(root, lockFile, environment, resolver, _createSolver(resolver));
  }

  /// Takes the workspace lock, then finishes or undoes what an interrupted operation left in the
  /// journal. A dry run writes nothing, so it takes no lock and only reports the journal.
  private async Task<WorkspaceMutex?> BeginOperation(OperationOptions options) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    if (options.DryRun) {
      await RecoverInterrupted(root, options);
      return null;
    }

    _lockStore.CreateLipDirectory(root);
    WorkspaceMutex mutex = WorkspaceMutex.Acquire(_fs, _lockStore.GetMutexPath(root), root);
    try {
      await RecoverInterrupted(root, options);
    }
    catch {
      mutex.Dispose();
      throw;
    }

    return mutex;
  }

  /// The journal of the operation applying changes to a workspace.
  private string JournalPath(string root) => _lockStore.GetJournalPath(root);

  private async Task RecoverInterrupted(string root, OperationOptions options) {
    string journal = JournalPath(root);
    if (!_fs.File.Exists(journal)) {
      return;
    }

    if (options.DryRun) {
      await _ui.PrintWarning(
          "An interrupted lip operation left changes in this workspace, which the next install,"
          + " uninstall or update finishes or undoes. The dry run plans with the workspace as it"
          + " is now.");
      return;
    }

    WorkspaceTransaction.RecoveryResult result = WorkspaceTransaction.Recover(
        _fs,
        journal,
        commitLock: () => _lockStore.CommitStaged(root),
        discardLock: () => _lockStore.DiscardStaged(root))!;
    string started = result.Started is null ? "" : $" (it started at {result.Started})";
    string errors = string.Concat(result.Errors.Select(e => $"\n  - {e}"));
    if (result.Committed) {
      await _ui.PrintInfo(
          $"Finished an interrupted lip operation that had completed{started}: deleted the"
          + " backups it left.");
      if (result.Errors.Count > 0) {
        await _ui.PrintWarning($"Some of them could not be deleted:{errors}");
      }

      return;
    }

    if (result.Errors.Count > 0) {
      throw new InvalidOperationException(
          $"A lip operation on this workspace was interrupted{started}, and its changes cannot"
          + $" all be undone:{errors}\nFix the problem (e.g. close programs using these files),"
          + $" then run lip again. '{journal}' records what is left to undo.");
    }

    await _ui.PrintWarning(
        $"A lip operation on this workspace was interrupted{started}; undid its"
        + $" {result.Changes} changes. The workspace and its lock are as they were before it.");
  }

  /// The environment of the workspace (spec §5.1). lip manages server workspaces only, so the
  /// platform is always server; client workspaces are left to launchers implementing the same
  /// spec. The rid is explicit, then the lock, then the RID lip itself runs on.
  private static LipEnvironment DetermineEnvironment(LockFile lockFile, string? explicitRid) {
    string server = McmodPlatform.Server.ToSpecString();
    if (lockFile.Platform is { } platform && platform != server) {
      throw new InvalidOperationException(
          $"The lock records the platform \"{platform}\", but lip manages server workspaces only."
          + " Client workspaces are managed by launchers such as LeviLauncher.");
    }

    if (lockFile.Runtime == "") {
      throw new LockFormatException("The lock records an empty runtime.");
    }

    return new LipEnvironment(
        McmodPlatform.Server,
        explicitRid ?? lockFile.Runtime ?? RuntimeInformation.RuntimeIdentifier);
  }

  private void Warn(string message) => _ui.PrintWarning(message).GetAwaiter().GetResult();

  /// Parses package arguments; local paths are relative to the workspace root, which is the
  /// current directory of lip.
  private List<(string, PackageRequest)> ParseRequests(IReadOnlyList<string> packages) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    return [.. packages.Select(p => (p, PackageArguments.Parse(p, root, _fs)))];
  }

  /// The first candidate of every request: the package the request names. Fails when a request
  /// has no candidate, or, unless allowSameIdentity, when two requests name the same package.
  /// A request for the old path of a repository that moved is replaced by the request for its
  /// new path (see FirstCandidate).
  private async Task<List<Candidate>> RequestedCandidates(
      Workspace workspace,
      List<(string Argument, PackageRequest Request)> requests,
      bool allowSameIdentity = false) {
    List<Candidate> candidates = [];
    for (int i = 0; i < requests.Count; i++) {
      (string argument, PackageRequest request) = requests[i];
      (Candidate candidate, PackageRequest requested) =
          await FirstCandidate(workspace, argument, request);
      requests[i] = (argument, requested);
      string identity = candidate.Package.Identity;
      if (!allowSameIdentity && candidates.Any(c => SameIdentity(c.Package.Identity, identity))) {
        throw new InvalidOperationException(
            $"Multiple requested packages are '{identity}'. Please request each package once.");
      }

      candidates.Add(candidate);
    }

    return candidates;
  }

  /// The first candidate of a request, and the request it comes from: the given one, or, when
  /// the request names the old path of a tooth.json repository that moved, the same request for
  /// the path the tooth.json declares (see MovedRequest).
  private async Task<(Candidate Candidate, PackageRequest Request)> FirstCandidate(
      Workspace workspace, string argument, PackageRequest request) {
    await _ui.PrintInfo($"Resolving {argument}...");
    List<string> diagnostics = [];
    Candidate candidate = await FirstCandidateOrDefault(workspace, request, diagnostics.Add)
        ?? throw new InvalidOperationException(
            $"No package found for '{argument}'"
            + (diagnostics.Count == 0 ? "." : ":" + string.Concat(
                diagnostics.Take(10).Select(d => $"\n  - {d}"))));

    // A tooth.json package is identified by its "tooth" path (spec §10.2 rule 1), whatever path
    // it is requested by: a repository that moved still answers at its old path (GitHub
    // redirects it) while its tooth.json names the new one, or the other way round at its older
    // tags.
    string tooth = ToothPath(candidate.Package.Manifest);
    if (candidate.Package.Kind == ToothCompat.KindName && candidate.Chain.Count == 1
        && request.Ref.Kind == RefKind.Spec
        && !string.Equals(tooth, request.Ref.Target, StringComparison.OrdinalIgnoreCase)) {
      if (await MovedRequest(workspace, request, candidate, tooth)
          is (Candidate moved, PackageRequest movedRequest)) {
        await _ui.PrintInfo(
            $"{request.Ref.Target} redirects to {tooth}, the package path its tooth.json"
            + $" declares; requesting {movedRequest.Ref} instead.");
        return (moved, movedRequest);
      }

      await _ui.PrintWarning(
          $"The tooth.json of {request.Ref.Target} declares the package path '{tooth}'; the"
          + $" package is identified as '{candidate.Package.Identity}'.");
    }

    return (candidate, request);
  }

  private static async Task<Candidate?> FirstCandidateOrDefault(
      Workspace workspace, PackageRequest request, Action<string>? onDiagnostic = null) {
    await foreach (Candidate candidate in workspace.Resolver.ResolveLazy(
        request.Ref, request.Base, request.Range, workspace.Environment,
        onDiagnostic: onDiagnostic)) {
      return candidate;
    }

    return null;
  }

  /// The request for the declared path of a tooth.json package requested by another path, with
  /// its first candidate, when the requested path is the old path of the repository: asked at
  /// the requested path, the repository answers at the declared one (GitHub redirects the old
  /// path of a repository that was transferred or renamed), and the declared path yields the
  /// same package from the same commit. Requested by its own path, the package and its updates
  /// no longer depend on the redirect, which stops when someone takes the old path again. The
  /// variant and the range are kept. A fork, or another repository whose tooth.json names some
  /// path, does not redirect there; nor does the new path of a moved repository to the old path
  /// that its older versions declare. Null otherwise.
  ///
  /// A pinned ref keeps its path: a package from a pinned spec ref stands in for the versions of
  /// that spec only (spec §6), and the dependencies of other packages still name the old path.
  private async Task<(Candidate, PackageRequest)?> MovedRequest(
      Workspace workspace, PackageRequest request, Candidate candidate, string tooth) {
    if (request.Ref.GitRef is not null
        || !Ref.TryParse(tooth, out Ref? path, out _) || path.Kind != RefKind.Spec
        || path.GitRef is not null || path.Variant != ""
        || candidate.Archive.Identity is not GitArchiveIdentity { Commit.Length: > 0 } from
        || !await _archiveProvider.RedirectsTo(request.Ref.Target, tooth)) {
      return null;
    }

    PackageRequest moved = request with { Ref = path.WithVariant(request.Ref.Variant) };
    Candidate? atPath;
    try {
      atPath = await FirstCandidateOrDefault(workspace, moved);
    }
    catch (ArchiveUnavailableException) {
      return null;
    }

    return atPath is { Package.Kind: ToothCompat.KindName, Chain.Count: 1 }
        && SameIdentity(atPath.Package.Identity, candidate.Package.Identity)
        && atPath.Package.Version.ToString() == candidate.Package.Version.ToString()
        && atPath.Archive.Identity is GitArchiveIdentity to
        && string.Equals(to.Commit, from.Commit, StringComparison.OrdinalIgnoreCase)
        && to.Path == from.Path
        ? (atPath, moved)
        : null;
  }

  private async Task<Plan> MakePlan(
      Workspace workspace,
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyCollection<string> unpinned,
      IReadOnlyCollection<string> explicitIdentities) {
    HashSet<string> unpinnedSet = [.. unpinned.Select(Fold)];
    HashSet<string> explicitSet = [.. explicitIdentities.Select(Fold)];

    List<Group> groups = [];
    foreach (IGrouping<string, LockedPackage> grouping in workspace.Lock.Packages
        .GroupBy(p => Fold(p.Identity))) {
      LockedPackage representative = grouping.FirstOrDefault(p => p.Explicit) ?? grouping.First();
      Candidate? pinned = unpinnedSet.Contains(grouping.Key)
          ? null
          : await PinnedCandidate(workspace, representative);
      groups.Add(new Group(grouping.Key, [.. grouping], representative, pinned));
    }

    List<Group> frozen = [.. groups.Where(g => g.Pinned is null && !unpinnedSet.Contains(g.Key))];
    List<Candidate> pinnedAll = [.. groups.Where(g => g.Pinned is not null).Select(g => g.Pinned!)];

    SolvedGraph graph;
    try {
      graph = await workspace.Solver.SolveGraph(requests, pinnedAll, workspace.Environment);
    }
    catch (DependencyResolutionException withInstalled) {
      List<Candidate> pinnedExplicit = [
        .. groups
            .Where(g => g.Pinned is not null && explicitSet.Contains(g.Key)
                && g.Entries.Any(p => p.Explicit))
            .Select(g => g.Pinned!),
      ];
      if (pinnedExplicit.Count == pinnedAll.Count) {
        throw;
      }

      string reason = withInstalled.Message.Split('\n').Skip(1).FirstOrDefault()?.Trim(' ', '-')
          ?? withInstalled.Message;
      await _ui.PrintInfo(
          $"The installed dependencies do not fit the request ({reason}); resolving them again.");
      graph = await workspace.Solver.SolveGraph(requests, pinnedExplicit, workspace.Environment);
    }

    // Keep what the requests and the explicitly installed packages need.
    HashSet<int> needed = [];
    Stack<int> pending = new(graph.RequestSatisfiers.Where(i => i >= 0));
    for (int i = 0; i < graph.Order.Count; i++) {
      string identity = Fold(graph.Order[i].Package.Identity);
      // Packages whose lock entry cannot be read may depend on any installed dependency, so
      // then no installed dependency is removed (explicit ones the user removes still are).
      if (explicitSet.Contains(identity)
          || (frozen.Count > 0 && groups.Any(g => g.Key == identity && g.Pinned is not null
              && !g.Entries.Any(p => p.Explicit)))) {
        pending.Push(i);
      }
    }

    foreach (Group group in frozen) {
      if (graph.Order.Any(c => Fold(c.Package.Identity) == group.Key)) {
        throw new InvalidOperationException(
            $"The request needs '{group.Representative.Identity}', which is installed, but lip "
            + "cannot read its lock entry in this environment.");
      }
    }

    while (pending.TryPop(out int index)) {
      if (needed.Add(index)) {
        foreach (int dependency in graph.Dependencies[index]) {
          pending.Push(dependency);
        }
      }
    }

    HashSet<string> finalExplicit = [
      .. explicitSet,
      .. graph.RequestSatisfiers.Where(i => i >= 0).Select(i => Fold(graph.Order[i].Package.Identity)),
    ];
    if (frozen.Count > 0) {
      await _ui.PrintWarning(
          "Cannot read the lock entries of "
          + string.Join(", ", frozen.Select(g => g.Representative.Identity))
          + "; they are left as they are.");
      finalExplicit.UnionWith(frozen.Where(g => g.Entries.Any(p => p.Explicit)).Select(g => g.Key));
    }

    return new Plan(
        [.. graph.Order.Where((_, i) => needed.Contains(i))],
        groups,
        frozen,
        finalExplicit);
  }

  #endregion

  #region Applying

  /// What an operation changes, in order, and how the lock entries left at the end are ordered
  /// and marked.
  private sealed record ChangeSet(
      List<Change> Changes, Func<List<LockedPackage>, Task<List<LockedPackage>>> Finish);

  private abstract record Change;

  private sealed record UninstallChange(LockedPackage Package) : Change;

  private sealed record InstallChange(Candidate Candidate, bool Explicit) : Change;

  private sealed record MarkExplicitChange(LockedPackage Package) : Change;

  /// The changes from the installed packages to the planned package set: uninstalling what
  /// leaves or changes (in reverse install order), then installing what enters (in install
  /// order).
  private ChangeSet Changes(Workspace workspace, OperationOptions options, Plan plan) {
    Dictionary<string, Candidate> members = plan.Members.ToDictionary(
        c => Fold(c.Package.Identity), StringComparer.Ordinal);

    // Groups whose package stays exactly as installed.
    HashSet<string> kept = [];
    foreach (Group group in plan.Groups) {
      if (plan.Frozen.Contains(group)
          || (members.TryGetValue(group.Key, out Candidate? member)
              && IsSamePackage(group.Representative, member))) {
        kept.Add(group.Key);
      }
    }

    List<Change> changes = [];
    foreach (LockedPackage package in Enumerable.Reverse(workspace.Lock.Packages)) {
      if (!kept.Contains(Fold(package.Identity))) {
        changes.Add(new UninstallChange(package));
      }
    }

    foreach (Candidate candidate in plan.Members) {
      string key = Fold(candidate.Package.Identity);
      if (!kept.Contains(key)) {
        changes.Add(new InstallChange(candidate, plan.Explicit.Contains(key)));
      }
    }

    async Task<List<LockedPackage>> Finish(List<LockedPackage> remaining) {
      // Lock order is install order: frozen entries first (their dependencies are unknown),
      // then the members of the new set.
      List<LockedPackage> ordered = [
        .. plan.Frozen.SelectMany(g => g.Entries),
        .. plan.Members.SelectMany(c => remaining.Where(
            p => Fold(p.Identity) == Fold(c.Package.Identity))),
      ];
      ordered.AddRange(remaining.Where(p => !ordered.Contains(p)));

      List<LockedPackage> final = [];
      foreach (LockedPackage package in ordered) {
        bool isExplicit = plan.Explicit.Contains(Fold(package.Identity));
        if (isExplicit != package.Explicit && kept.Contains(Fold(package.Identity))) {
          await _ui.PrintInfo((options.DryRun ? "Dry run: would mark" : "Marking")
              + $" package '{package.Identity}' version {package.Version} as "
              + (isExplicit ? "explicitly installed." : "not explicitly installed."));
        }

        final.Add(package with { Explicit = isExplicit });
      }

      return final;
    }

    return new ChangeSet(changes, Finish);
  }

  private async Task InstallWithoutDependencies(
      Workspace workspace,
      List<(string Argument, PackageRequest Request)> requests,
      OperationOptions options) {
    await _ui.PrintWarning(
        "--no-dependencies is enabled. This operation may result in a broken workspace.");

    List<(Candidate Candidate, Ref Requested)> candidates = [];
    foreach ((string argument, PackageRequest request) in requests) {
      (Candidate candidate, _) = await FirstCandidate(workspace, argument, request);
      if (candidates.Any(
          c => SameIdentity(c.Candidate.Package.Identity, candidate.Package.Identity))) {
        throw new InvalidOperationException(
            $"Multiple requested packages are '{candidate.Package.Identity}'. Please request "
            + "each package once.");
      }

      candidates.Add((candidate, request.Ref));
    }

    List<Change> changes = [];
    foreach ((Candidate candidate, Ref requested) in candidates) {
      List<LockedPackage> existing = [
        .. workspace.Lock.Packages.Where(
            p => SameIdentity(p.Identity, candidate.Package.Identity)),
      ];
      if (existing.Count == 0
          && await MovedFrom(workspace, candidate, requested) is LockedPackage from) {
        await ReportMoved(from, candidate, options);
        existing.Add(from);
      }

      if (existing.Count > 0 && existing.All(p => IsSamePackage(p, candidate))) {
        changes.Add(new MarkExplicitChange(existing[0]));
        continue;
      }

      changes.AddRange(Enumerable.Reverse(existing).Select(p => new UninstallChange(p)));
      changes.Add(new InstallChange(candidate, Explicit: true));
    }

    await Execute(workspace, options, new ChangeSet(changes, Task.FromResult));
  }

  /// Plans every change against a view of the workspace as the earlier changes leave it, so
  /// that every check and every fetch happens before anything changes; then applies the plans
  /// in one transaction and writes the lock last. A failure while applying (or cancellation)
  /// undoes every change and leaves the lock as it was (spec §7). In a dry run, reports the
  /// plans instead.
  private async Task Execute(Workspace workspace, OperationOptions options, ChangeSet changeSet) {
    WorkspaceLayout.LayoutPlan? layout = null;
    if (changeSet.Changes.Any(c => c is not MarkExplicitChange)) {
      layout = _layout.Plan(workspace.Root);
      if (options.DryRun) {
        await _layout.ReportDryRun(layout);
      }
    }

    WorkspaceView view = new(_fs, layout);
    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    List<(string Message, IMcmodInstaller Installer, PackagePlan Plan)> plans = [];
    foreach (Change change in changeSet.Changes) {
      options.CancellationToken.ThrowIfCancellationRequested();
      switch (change) {
        case UninstallChange(LockedPackage package): {
            string what = $" package '{package.Identity}' version {package.Version}";
            if (options.DryRun) {
              await _ui.PrintInfo("Dry run: would uninstall" + what);
            }

            IMcmodInstaller installer = Installer(package.Kind);
            plans.Add(("Uninstalling" + what, installer, await installer.PlanUninstall(
                package, Context(workspace, options, remaining), view)));
            remaining.Remove(package);
            break;
          }
        case InstallChange(Candidate candidate, bool isExplicit): {
            string what = $" package '{candidate.Package.Identity}' version"
                + $" {candidate.Package.Version} from {candidate.Archive.Key}";
            if (options.DryRun) {
              await _ui.PrintInfo("Dry run: would install" + what);
            }

            IMcmodInstaller installer = Installer(candidate.Package.Kind);
            PackagePlan plan = await installer.PlanInstall(
                candidate, Context(workspace, options, remaining), view);
            plans.Add(("Installing" + what, installer, plan));
            remaining.Add(Locked(candidate, isExplicit, plan.OwnedFiles));
            break;
          }
        case MarkExplicitChange(LockedPackage package):
          if (!package.Explicit) {
            await _ui.PrintInfo((options.DryRun ? "Dry run: would mark" : "Marking")
                + $" package '{package.Identity}' version {package.Version} as explicitly"
                + " installed.");
          }

          remaining[remaining.IndexOf(package)] = package with { Explicit = true };
          break;
        default:
          throw new ArgumentException($"Unknown change {change}.", nameof(changeSet));
      }
    }

    LockFile final = LockWith(workspace, await changeSet.Finish(remaining));
    if (options.DryRun) {
      return;
    }

    options.CancellationToken.ThrowIfCancellationRequested();
    if (plans.Count == 0) {
      await _lockStore.Save(workspace.Root, final);
      return;
    }

    using WorkspaceTransaction transaction = new(
        _fs, JournalPath(workspace.Root), plans.SelectMany(p => p.Plan.WrittenPaths));
    try {
      if (layout is not null) {
        await _layout.Apply(layout, transaction);
      }

      foreach ((string message, IMcmodInstaller installer, PackagePlan plan) in plans) {
        await _ui.PrintInfo(message);
        await installer.Apply(plan, transaction, options.CancellationToken);
      }

      options.CancellationToken.ThrowIfCancellationRequested();
      // The commit point: the journal records the commit, then the lock moves into place.
      await _lockStore.Stage(workspace.Root, final);
      transaction.Commit();
      _lockStore.CommitStaged(workspace.Root);
    }
    catch (Exception ex) {
      await _ui.PrintWarning(
          (ex is OperationCanceledException ? "Cancelled" : "Failed")
          + "; undoing the changes of this operation.");
      try {
        _lockStore.DiscardStaged(workspace.Root);
      }
      catch (Exception discard) when (discard is IOException or UnauthorizedAccessException) {
        // A staged lock is never read, and the next save replaces it.
      }

      IReadOnlyList<string> errors = transaction.Rollback();
      foreach (string error in errors) {
        await _ui.PrintWarning($"Rollback: {error}");
      }

      if (errors.Count > 0) {
        await _ui.PrintWarning(
            "Some changes could not be undone; the next install, uninstall or update of this"
            + " workspace tries again.");
      }

      throw;
    }

    foreach (string error in transaction.Complete()) {
      await _ui.PrintWarning(error);
    }
  }

  private static LockedPackage Locked(
      Candidate candidate, bool isExplicit, IReadOnlyList<string> files) => new() {
        Identity = candidate.Package.Identity,
        Version = candidate.Package.Version.ToString(),
        Kind = candidate.Package.Kind,
        Explicit = isExplicit,
        Chain = [
          .. candidate.Chain.Select(h => new LockedHop(h.Ref.ToString(), h.TargetIndex) {
            Commit = h.Commit,
          }),
        ],
        ArchiveKey = candidate.Archive.Key,
        Archive = candidate.Archive.Identity,
        Files = [.. files],
        Manifest = candidate.Package.Manifest,
      };

  private InstallContext Context(
      Workspace workspace, OperationOptions options, List<LockedPackage> remaining) {
    return new InstallContext(
        workspace.Root,
        workspace.Environment,
        LockWith(workspace, remaining),
        options.DryRun,
        options.IgnoreScripts);
  }

  private IMcmodInstaller Installer(string kind) {
    return _installers.FirstOrDefault(i => i.CanHandle(kind))
        ?? throw new InvalidOperationException($"No installer for package kind '{kind}'.");
  }

  /// The lock with the given packages and the environment, once known (spec §9).
  private static LockFile LockWith(Workspace workspace, List<LockedPackage> packages) {
    return workspace.Lock with {
      Platform = workspace.Environment.Platform.ToSpecString(),
      Runtime = workspace.Environment.Runtime,
      Packages = [.. packages],
    };
  }

  #endregion

  #region Locked packages

  /// Rebuilds an installed package from its lock entry. Returns null when the entry cannot be
  /// read in this environment (e.g. a tooth.json variant for another rid).
  private async Task<Candidate?> PinnedCandidate(Workspace workspace, LockedPackage package) {
    try {
      ToothConversion? tooth = null;
      IPackage? rebuilt;
      if (package.Kind == ToothCompat.KindName) {
        tooth = await ToothCompat.FromLocked(package, workspace.Environment);
        rebuilt = tooth?.Package;
      } else {
        rebuilt = RebuildPackage(package);
      }

      if (rebuilt is null) {
        return null;
      }

      // Migrated v3 entries have no identity: that of the repository of the tooth path. Tooth
      // packages have no relative refs, so it is never used as a base; it is never written to
      // the lock either.
      ArchiveIdentity identity = package.Archive ?? new GitArchiveIdentity(
          ArchiveProvider.GetRepoUrl(ToothPath(package.Manifest)), "", "");
      List<ChainHop> chain = [
        .. package.Chain.Select(h => new ChainHop(
            Ref.Parse(h.Ref, allowFile: true), h.Target, h.Commit)),
      ];
      ArchiveHandle archive = new(
          package.ArchiveKey,
          new DeferredSource(async () => {
            Sources.ISource source = await OpenLockedArchive(workspace, package);
            // The archive of a tooth.json package mounts its assets (spec §10.2); resolving the
            // chain again already yields that archive.
            return tooth is null || source is MountSource
                ? source
                : tooth.OpenArchive(
                    new ArchiveHandle(package.ArchiveKey, source, identity), _archiveProvider)
                    .Source;
          }),
          identity);
      return new Candidate(rebuilt, archive, chain);
    }
    catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException
        or InvalidOperationException or KeyNotFoundException) {
      await _ui.PrintWarning(
          $"Cannot read the lock entry of '{package.Identity}': {ex.Message}");
      return null;
    }
  }

  /// The package of a manifest.json lock entry; null for an unknown kind.
  private McmodPackage? RebuildPackage(LockedPackage package) {
    if (package.Kind != McmodPackage.KindName) {
      return null;
    }

    byte[] manifest = Encoding.UTF8.GetBytes(package.Manifest.GetRawText());
    return _documentParser.Parse(manifest) is PackageDocument document
        ? new McmodPackage(document, package.Manifest)
        : throw new FormatException("the locked manifest is not a package document.");
  }

  /// Opens the archive recorded in the lock and verifies its identity (spec §9). Two cases
  /// re-resolve the recorded chain instead, accepting only the recorded archive:
  /// - migrated v3 entries, which record no identity: the tag "path@version" is resolved again;
  /// - nested ZIP archives, which are not cached on their own: the chain is resolved again,
  ///   which reopens the parent archive and extracts the ZIP again, and the result must have the
  ///   same SHA-256.
  private async Task<Sources.ISource> OpenLockedArchive(
      Workspace workspace, LockedPackage package) {
    if (package.Archive is ArchiveIdentity identity) {
      try {
        return (await _archiveProvider.OpenLocked(package.ArchiveKey, identity)).Source;
      }
      catch (ArchiveUnavailableException) when (identity is ZipArchiveIdentity { Url: null }) {
        ZipArchiveIdentity zip = (ZipArchiveIdentity)identity;
        return (await Reresolve(workspace, package, c =>
            c.Archive.Identity is ZipArchiveIdentity found
            && string.Equals(found.Sha256, zip.Sha256, StringComparison.OrdinalIgnoreCase)
            && found.Path == zip.Path)).Archive.Source;
      }
    }

    return (await Reresolve(workspace, package, c => c.Archive.Key == package.ArchiveKey))
        .Archive.Source;
  }

  private async Task<Candidate> Reresolve(
      Workspace workspace, LockedPackage package, Func<Candidate, bool> accept) {
    if (package.Chain.Count == 0) {
      throw new ArchiveUnavailableException(
          $"{package.ArchiveKey}: the lock records no resolution chain.");
    }

    Ref reference = Ref.Parse(package.Chain[0].Ref, allowFile: true);
    if (reference.GitRef is { Kind: not GitRefKind.Rev }
        && GitRef.TryParse($"rev:{package.Chain[0].Commit}", out GitRef? rev, out _)) {
      // The branch or tag may have moved; the locked package came from the recorded commit.
      // Only nested ZIP archives of manifest.json packages are re-resolved, so losing the tag
      // version of a "v" + SemVer tag (which only tooth.json packages take over) changes nothing.
      reference = reference.WithGitRef(rev);
    }

    SemVersion version = SemVersion.Parse(package.Version, SemVersionStyles.Strict);
    List<string> diagnostics = [];
    await foreach (Candidate candidate in workspace.Resolver.ResolveLazy(
        reference,
        null,
        new ExactVersionConstraint(version),
        workspace.Environment,
        onDiagnostic: diagnostics.Add)) {
      if (accept(candidate)) {
        return candidate;
      }
    }

    throw new ArchiveUnavailableException(
        $"{package.ArchiveKey}: resolving {reference} again did not yield the locked archive"
        + string.Concat(diagnostics.Take(5).Select(d => $"; {d}")));
  }

  /// The installed packages an uninstall argument names by the old path of a moved repository:
  /// tooth.json packages installed from a spec ref of the same repository name whose path the
  /// argument's path redirects to (see MovedRequest, which records the new path), with the
  /// variant and git ref rules of MatchInstalled. Only asked when the argument names nothing
  /// else, so that uninstalling does not otherwise contact any repository.
  private async Task<List<LockedPackage>> MatchMoved(Workspace workspace, string argument) {
    if (!Ref.TryParse(argument, out Ref? wanted, out _) || wanted.Kind != RefKind.Spec) {
      return [];
    }

    List<LockedPackage> matched = [];
    // By repository, as GitRemoteClient.GetRepoKey tells them apart.
    Dictionary<string, bool> asked = [];
    foreach (LockedPackage package in workspace.Lock.Packages) {
      if (package.Kind != ToothCompat.KindName || package.Chain.Count == 0
          || !Ref.TryParse(package.Chain[0].Ref, out Ref? first, out _)
          || first.Kind != RefKind.Spec
          || !string.Equals(
              RepositoryName(first.Target), RepositoryName(wanted.Target),
              StringComparison.OrdinalIgnoreCase)
          || (wanted.Variant != "" && first.Variant != wanted.Variant)
          || (wanted.GitRef is not null && first.GitRef != wanted.GitRef)) {
        continue;
      }

      string repository = GitRemoteClient.GetRepoKey(ArchiveProvider.GetRepoUrl(first.Target));
      if (!asked.TryGetValue(repository, out bool redirects)) {
        redirects = await _archiveProvider.RedirectsTo(wanted.Target, first.Target);
        asked[repository] = redirects;
      }

      if (redirects) {
        matched.Add(package);
      }
    }

    return matched;
  }

  /// Matches an uninstall argument: an identity (ignoring ASCII case), or the first ref of the
  /// chain as written at install time (e.g. "github.com/owner/repo#variant" for tooth
  /// packages, or the path of the local file it was installed from); a ref without a variant
  /// matches every variant, and one without a git ref every git ref.
  private List<LockedPackage> MatchInstalled(Workspace workspace, string argument) {
    LockFile lockFile = workspace.Lock;
    List<LockedPackage> byIdentity = [
      .. lockFile.Packages.Where(p => SameIdentity(p.Identity, argument)),
    ];
    if (byIdentity.Count > 0) {
      return byIdentity;
    }

    Ref? wanted = PackageArguments.LocalFileRef(argument, workspace.Root, _fs);
    if (wanted is null && !Ref.TryParse(argument, allowFile: true, out wanted, out _)) {
      return byIdentity;
    }

    bool hasVariant = wanted.Variant != "";
    return [
      .. lockFile.Packages.Where(p => {
        if (p.Chain.Count == 0
            || !Ref.TryParse(p.Chain[0].Ref, allowFile: true, out Ref? first, out _)) {
          return false;
        }

        return first.Kind == wanted.Kind
            && string.Equals(first.Target, wanted.Target, StringComparison.OrdinalIgnoreCase)
            && (!hasVariant || first.Variant == wanted.Variant)
            && (wanted.GitRef is null || first.GitRef == wanted.GitRef);
      }),
    ];
  }

  /// The installed package a requested candidate is a later version of under another identity,
  /// when no installed package has the candidate's identity; null when there is none. This is
  /// how lip recognises a tooth.json package whose repository moved (e.g. a transfer to another
  /// GitHub organisation, whose old path GitHub redirects): its newer tags name the new path in
  /// "tooth", so they have another identity (spec §10.2 rule 1) than the version installed. The
  /// installed package must have the candidate's variant and be either
  /// - installed from the same spec ref as the candidate, or as the request the candidate was
  ///   resolved for (requested, which FirstCandidate may have replaced by the new path of a
  ///   moved repository), comparing spec, ignoring ASCII case, and variant: resolving that ref
  ///   again led to the candidate, whatever its identity; or
  /// - a tooth.json package whose repository publishes the candidate: the candidate's version
  ///   tag is at the candidate's commit in the installed package's repository, and the installed
  ///   version's tag is at the same commit in both repositories. The two paths then name one
  ///   repository (or the old one redirects to the new one). A fork shares the tags made before
  ///   it, but not the commits released by the fork, so it is not taken for the original. Only
  ///   installed packages whose repository has the candidate's repository name are asked, so
  ///   that installing another package does not contact the repositories of the installed ones.
  private async Task<LockedPackage?> MovedFrom(
      Workspace workspace, Candidate candidate, Ref? requested = null) {
    if (candidate.Chain.Count == 0 || candidate.Chain[0].Ref.Kind != RefKind.Spec
        || workspace.Lock.Packages.Any(p => SameIdentity(p.Identity, candidate.Package.Identity))) {
      return null;
    }

    Ref first = candidate.Chain[0].Ref;
    List<Ref> firsts = requested is { Kind: RefKind.Spec } ? [first, requested] : [first];
    List<(LockedPackage Package, Ref From)> installed = [];
    foreach (LockedPackage package in workspace.Lock.Packages) {
      if (package.Chain.Count > 0
          && Ref.TryParse(package.Chain[0].Ref, allowFile: true, out Ref? from, out _)) {
        if (firsts.Any(f => from.Kind == f.Kind && from.Variant == f.Variant
            && string.Equals(from.Target, f.Target, StringComparison.OrdinalIgnoreCase))) {
          return package;
        }

        installed.Add((package, from));
      }
    }

    if (candidate.Package.Kind != ToothCompat.KindName) {
      return null;
    }

    if (candidate.Archive.Identity is not GitArchiveIdentity { Commit.Length: > 0 } released
        || !GitRef.TryParse(
            $"tag:v{candidate.Package.Version}", out GitRef? releasedTag, out _)) {
      return null;
    }

    string label = candidate.Package.Variant;
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) {
      RepositoryName(first.Target),
      RepositoryName(ToothPath(candidate.Package.Manifest)),
    };
    foreach ((LockedPackage package, Ref from) in installed) {
      if (package.Kind != ToothCompat.KindName || from.Kind != RefKind.Spec
          || from.Variant != label
          || !(names.Contains(RepositoryName(from.Target))
              || names.Contains(RepositoryName(ToothPath(package.Manifest))))
          || !GitRef.TryParse($"tag:v{package.Version}", out GitRef? tag, out _)) {
        continue;
      }

      try {
        if (!string.Equals(
            released.Commit,
            await _archiveProvider.ResolveGitRef(from.Target, releasedTag),
            StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        string commit = (from.GitRef is null ? package.Chain[0].Commit : null)
            ?? await _archiveProvider.ResolveGitRef(from.Target, tag);
        if (string.Equals(
            commit,
            await _archiveProvider.ResolveGitRef(first.Target, tag),
            StringComparison.OrdinalIgnoreCase)) {
          return package;
        }
      }
      catch (ArchiveUnavailableException) {
        // Not the same repository as far as lip can tell.
      }
    }

    return null;
  }

  private async Task ReportMoved(
      LockedPackage from, Candidate candidate, OperationOptions options) {
    await _ui.PrintInfo(
        $"Package '{from.Identity}' is now '{candidate.Package.Identity}'"
        + (candidate.Package.Kind == ToothCompat.KindName && from.Kind == ToothCompat.KindName
            ? " (its tooth.json declares a new path)"
            : "")
        + (options.DryRun ? "; it would be replaced by " : "; replacing it with ")
        + $"'{candidate.Package.Identity}' version {candidate.Package.Version}.");
  }

  /// The tooth path a tooth.json package's recorded tooth.json declares (spec §10.2 rule 1); ""
  /// for other packages.
  private static string ToothPath(JsonElement manifest) =>
      manifest.ValueKind == JsonValueKind.Object
      && manifest.TryGetProperty("tooth", out JsonElement tooth)
      && tooth.ValueKind == JsonValueKind.String
          ? tooth.GetString()!
          : "";

  /// The last segment of a path.
  private static string RepositoryName(string path) => path[(path.LastIndexOf('/') + 1)..];

  #endregion

  private static List<string> ExplicitIdentities(LockFile lockFile) =>
      [.. lockFile.Packages.Where(p => p.Explicit).Select(p => p.Identity)];

  /// Whether a candidate is the installed package: same identity, version and archive key, and
  /// the same archive content where the lock records it (a URL or local file read again, or a
  /// branch that moved on, may yield other content under the same key and version).
  private static bool IsSamePackage(LockedPackage package, Candidate candidate) =>
      SameIdentity(package.Identity, candidate.Package.Identity)
      && package.Version == candidate.Package.Version.ToString()
      && package.ArchiveKey == candidate.Archive.Key
      && (package.Archive is null || package.Archive == candidate.Archive.Identity);

  private static bool SameIdentity(string a, string b) => PackageNames.AreEqual(a, b);

  private static string Fold(string identity) => PackageNames.Fold(identity);

  private sealed record Workspace(
      string Root,
      LockFile Lock,
      LipEnvironment Environment,
      IResolver Resolver,
      IGraphSolver Solver);

  /// The lock entries of one identity, and the candidate pinning them (null when unpinned or
  /// unreadable).
  private sealed record Group(
      string Key,
      List<LockedPackage> Entries,
      LockedPackage Representative,
      Candidate? Pinned);

  private sealed record Plan(
      IReadOnlyList<Candidate> Members,
      List<Group> Groups,
      List<Group> Frozen,
      HashSet<string> Explicit);
}
