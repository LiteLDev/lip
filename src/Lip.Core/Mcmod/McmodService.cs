using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Mcmod;

/// Options shared by the workspace operations. Rid overrides the runtime of the environment
/// (protocol/cli.md §4). Cancelling the token stops an operation; one that has started changing
/// the workspace undoes its changes first.
public record OperationOptions(
    bool DryRun = false,
    bool IgnoreScripts = false,
    bool NoDependencies = false,
    string? Rid = null,
    CancellationToken CancellationToken = default);

/// One package of the workspace file, as listed to the user.
public record InstalledPackage(string Identity, string Version, bool Explicit);

/// The workspace operations: install, uninstall, update and list (protocol/workspace.md §3–§7,
/// protocol/cli.md §5).
///
/// Every operation loads the workspace file (or the one protocol/compatibility.md §3 derives from
/// a tooth_lock.json), determines the environment, computes the new
/// package set with the graph solver, then uninstalls what leaves the set (in reverse
/// installation order) and installs what enters it (in installation order). Updating a package
/// uninstalls the old package and installs the new one.
///
/// Operations are all or nothing (protocol/workspace.md §6). An operation first plans every
/// uninstall and install against a view of the workspace as the earlier steps leave it, checking
/// everything and fetching every archive and asset; a failure then changes nothing. It then
/// applies the plans through one WorkspaceTransaction, journaled next to the workspace file in
/// .lip, and writes the workspace file last; a failure (or cancellation) undoes every change and
/// leaves the workspace file as it was. An operation other than a dry run holds the workspace's
/// WorkspaceMutex throughout, and first finishes or undoes what an interrupted operation left in
/// the journal (protocol/workspace.md §7.4).
///
/// Installed packages are pinned: they enter the solver as they are, rebuilt from the workspace
/// file (with the package file it records) without network access, and their archives (the commit
/// it records for a remote repository, otherwise what their source names now) and assets are
/// opened only when needed. If the request cannot be satisfied with every installed package
/// pinned, it is solved again with only the explicitly installed packages pinned, so that
/// dependencies may change version; their installed versions are tried first. Packages that no explicitly installed package needs any more
/// are removed.
public class McmodService(
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    LockStore lockStore,
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    McmodInstaller installer,
    Func<string>? workspaceRoot = null) {
  private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

  /// The workspace file as the user sees it from the workspace root.
  private const string WorkspaceFile = LockStore.LipDirectory + "/" + LockStore.FileName;

  private readonly IFileSystem _fs = fileSystem;
  private readonly IUserInteraction _ui = userInteraction;
  private readonly LockStore _lockStore = lockStore;
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly McmodInstaller _installer = installer;
  private readonly Func<string> _workspaceRoot =
      workspaceRoot ?? fileSystem.Directory.GetCurrentDirectory;
  private readonly WorkspaceLayout _layout = new(fileSystem, userInteraction);

  public async Task Install(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);
    List<(string Argument, PackageRequest Request)> requests = ParseRequests(packages);

    if (options.NoDependencies) {
      await InstallWithoutDependencies(workspace, requests, options);
      return;
    }

    List<Candidate> candidates = await RequestedCandidates(workspace, requests);
    for (int i = 0; i < candidates.Count; i++) {
      string identity = candidates[i].Package.Identity;
      if (workspace.Lock.Packages.FirstOrDefault(
          p => p.Explicit && SameIdentity(p.Identity, identity)) is { } installed) {
        throw new InvalidOperationException(
            $"Cannot install '{requests[i].Argument}': package '{identity}' is already explicitly"
            + $" installed (version {installed.Version}). To change its version, run"
            + $" 'lip update {requests[i].Argument}' instead.");
      }
    }

    // A requested dependency package keeps its version when it satisfies its request
    // (protocol/cli.md §5.1), recording the source of the request when it is one of the
    // request's candidates; otherwise it is chosen again.
    Dictionary<string, Candidate> requested = new(StringComparer.Ordinal);
    List<string> unpinned = [];
    for (int i = 0; i < candidates.Count; i++) {
      string identity = candidates[i].Package.Identity;
      if (workspace.Lock.Packages.FirstOrDefault(
          p => SameIdentity(p.Identity, identity)) is not { } dependency) {
        continue;
      }

      Candidate installed = PinnedCandidate(workspace, dependency);
      if (await workspace.Solver.RequestCandidate(
          requests[i].Request, installed, workspace.Environment) is { } satisfier) {
        requested[Fold(identity)] = installed with { Source = satisfier.Source };
      } else {
        unpinned.Add(identity);
      }
    }

    await Execute(workspace, options, await Changes(workspace, options, await MakePlan(
        workspace,
        [.. requests.Select(r => r.Request)],
        unpinned: unpinned,
        explicitIdentities: ExplicitIdentities(workspace.Lock),
        pinnedAs: requested)));
  }

  public async Task Uninstall(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);

    List<LockedPackage> targets = [];
    foreach (string package in packages) {
      List<LockedPackage> matched = MatchInstalled(workspace, package);
      if (matched.Count == 0) {
        throw new InvalidOperationException(
            $"Package '{package}' is not installed in this workspace, so it cannot be"
            + " uninstalled. Run 'lip list' to see the installed packages and their identities.");
      }

      RunTrace.Write("workspace", $"'{package}' matches the installed packages "
          + string.Join(", ", matched.Select(p => $"{p.Identity}@{p.Version}")));
      if (!options.NoDependencies && matched.FirstOrDefault(p => !p.Explicit) is { } dependency) {
        throw new InvalidOperationException(
            $"Package '{dependency.Identity}' was installed as a dependency of other packages, not"
            + " requested by you, so lip uninstalls it by itself once no package needs it. To"
            + " uninstall it anyway, pass --no-dependencies (the packages that need it may then"
            + " stop working).");
      }

      targets.AddRange(matched.Where(p => !targets.Contains(p)));
    }

    if (options.NoDependencies) {
      await _ui.PrintWarning(
          "--no-dependencies is enabled. This operation may result in a broken workspace.");
      await Execute(workspace, options, new ChangeSet(
          [
            .. Enumerable.Reverse(workspace.Lock.Packages)
                .Where(targets.Contains)
                .Select(p => new UninstallChange(p)),
          ],
          Task.FromResult));
      return;
    }

    HashSet<string> removed = [.. targets.Select(p => Fold(p.Identity))];
    Plan plan = await MakePlan(
        workspace,
        [],
        unpinned: [],
        explicitIdentities: [
          .. ExplicitIdentities(workspace.Lock).Where(i => !removed.Contains(Fold(i))),
        ]);

    foreach (Candidate kept in plan.Members.Where(c => removed.Contains(Fold(c.Package.Identity)))) {
      await _ui.PrintInfo(
          $"Package '{kept.Package.Identity}' is still required by other packages; keeping it "
          + "as a dependency.");
    }

    await Execute(workspace, options, await Changes(workspace, options, plan));
  }

  public async Task Update(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);

    List<(string Argument, PackageRequest Request)> requests;
    // Without arguments, the identities of the packages kept as they are, which stay pinned even
    // when another request gives a package of their identity (protocol/cli.md §5.3).
    List<string> keptAsIs = [];
    if (packages.Count == 0) {
      // Every explicitly installed package, requested again by its source: a remote repository
      // at its newest version, a branch at its head, a tag where it points now, a rev as it is,
      // a URL downloaded again and a local path with its current content.
      requests = [];
      foreach (LockedPackage package in workspace.Lock.Packages.Where(p => p.Explicit)) {
        Ref source = ParseRecordedSource(package);
        if (source.Kind == RefKind.File && !_fs.File.Exists(source.LocalPath)
            && !_fs.Directory.Exists(source.LocalPath)) {
          await _ui.PrintWarning(
              $"Keeping package '{package.Identity}' as it is: {source.LocalPath}, which it was"
              + " installed from, does not exist any more.");
          keptAsIs.Add(package.Identity);
          continue;
        }

        requests.Add((package.Identity, new PackageRequest(source, null)));
      }
    } else {
      requests = ParseRequests(packages);
    }

    // A URL is downloaded again (protocol/workspace.md §5 "Updating").
    foreach ((_, PackageRequest request) in requests) {
      if (request.Ref.Kind == RefKind.Url) {
        await _archiveProvider.Forget(request.Ref.Target);
      }
    }

    List<Candidate> candidates = await RequestedCandidates(
        workspace, requests, allowSameIdentity: true);
    List<string> identities = [];
    List<PackageRequest> requested = [];
    // Without arguments, the identities of the packages whose source now gives a package of
    // another identity, which replaces them (protocol/workspace.md §5 "Updating").
    List<string> replaced = [];
    for (int i = 0; i < candidates.Count; i++) {
      string identity = candidates[i].Package.Identity;
      if (packages.Count == 0) {
        if (!SameIdentity(requests[i].Argument, identity)) {
          replaced.Add(requests[i].Argument);
        }
      } else if (!workspace.Lock.Packages.Any(p => SameIdentity(p.Identity, identity))) {
        throw new InvalidOperationException(
            $"Cannot update '{requests[i].Argument}': package '{identity}' is not installed in"
            + $" this workspace. To install it, run 'lip install {requests[i].Argument}'.");
      }

      // Every request goes to the solver, which fails when no one package satisfies all the
      // requests giving an identity (protocol/workspace.md §3).
      if (!identities.Any(other => SameIdentity(other, identity))
          && !keptAsIs.Any(kept => SameIdentity(kept, identity))) {
        identities.Add(identity);
      }

      requested.Add(requests[i].Request);
    }

    Plan plan = await MakePlan(
        workspace,
        requested,
        unpinned: [.. identities, .. replaced],
        explicitIdentities: [
          .. ExplicitIdentities(workspace.Lock)
              .Where(e => !replaced.Any(r => SameIdentity(r, e))),
        ]);

    // The source a requested package is recorded with: an installed package that its recorded
    // source gives again is not replaced, so it keeps that source even when another request
    // gives the same package (protocol/workspace.md §5); but a request with a ref that gives it
    // wins, since only its own candidates satisfy it (protocol/workspace.md §3).
    List<Candidate> members = [.. plan.Members];
    foreach (bool byRecordedSource in new[] { true, false }) {
      for (int i = 0; i < candidates.Count; i++) {
        Candidate candidate = candidates[i];
        int member = members.FindIndex(
            c => SameIdentity(c.Package.Identity, candidate.Package.Identity));
        if (member < 0 || !IsSameCandidate(candidate, members[member])) {
          continue;
        }

        LockedPackage? installed = workspace.Lock.Packages.FirstOrDefault(
            p => SameIdentity(p.Identity, candidate.Package.Identity));
        if (byRecordedSource
            ? installed is not null && installed.Source == candidate.Source.ToString()
                && IsSamePackage(installed, candidate)
            : candidate.Source is { Kind: RefKind.Spec, GitRef: not null }) {
          members[member] = candidate;
        }
      }
    }

    await Execute(workspace, options, await Changes(
        workspace, options, plan with { Members = members }));
  }

  /// The packages of the workspace file (protocol/cli.md §5.4). The workspace file derived from a
  /// tooth_lock.json is only read, for the environment a workspace operation would give it,
  /// without the commits it would record, which are not listed.
  public async Task<IReadOnlyList<InstalledPackage>> List() {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    LockFile? lockFile = await _lockStore.Load(root)
        ?? await ReadToothLock(root, DetermineEnvironment(null, null), resolveCommits: false);
    return [
      .. (lockFile?.Packages ?? [])
          .Select(p => new InstalledPackage(p.Identity, p.Version, p.Explicit)),
    ];
  }

  /// The versions of the version tags of a remote repository, lowest first (protocol/cli.md
  /// §5.5): the package argument must be a remote repository without a ref.
  public async Task<IReadOnlyList<string>> Versions(string package) {
    Ref reference = PackageArguments.Parse(
        package, _fs.Path.GetFullPath(_workspaceRoot()), _fs).Ref;
    if (reference.Kind != RefKind.Spec || reference.GitRef is not null) {
      string what = reference.Kind switch {
        RefKind.Url => "a URL",
        RefKind.File => "a local path",
        _ => $"a remote repository with the ref '@{reference.GitRef}', which selects one commit",
      };
      // No parameter name: it would add "(Parameter 'package')" to the message the user sees.
      throw new ArgumentException(
          $"Cannot list the versions of '{package}': it is {what}. Only a remote repository"
          + " without a ref, such as 'github.com/<owner>/<repo>', has versions to list.");
    }

    RunTrace.Write("resolve", $"Listing the versions of {reference.Target}");
    IReadOnlyList<SpecVersion> versions = await _archiveProvider.ListVersions(reference.Target);
    return [.. versions.Reverse().Select(v => v.Version.ToString()).Distinct()];
  }

  /// The package file of the first archive a package argument names (protocol/cli.md §5.6), as
  /// JSON: for a remote repository, its newest version tag, or the commit its ref selects;
  /// otherwise the archive of the URL or local path. For an archive without a package file, the
  /// package file protocol/compatibility.md §2 derives from its tooth.json for the environment of
  /// protocol/cli.md §4.
  public async Task<string> View(string package) {
    PackageRequest request = PackageArguments.Parse(
        package, _fs.Path.GetFullPath(_workspaceRoot()), _fs);

    ArchiveHandle archive;
    // The tag version of the archive, the version of a package read from a tooth.json.
    SemVersion? tagVersion = null;
    if (request.Ref is { Kind: RefKind.Spec, GitRef: GitRef gitRef }) {
      archive = await _archiveProvider.OpenCommit(
          request.Ref.Target, await _archiveProvider.ResolveGitRef(request.Ref.Target, gitRef));
      tagVersion = gitRef.Kind switch {
        GitRefKind.Version => SemVersion.Parse(gitRef.Name, SemVersionStyles.Strict),
        GitRefKind.Tag => ArchiveProvider.ParseVersionTag($"refs/tags/{gitRef.Name}"),
        _ => null,
      };
    } else if (request.Ref.Kind == RefKind.Spec) {
      SpecVersion version = (await _archiveProvider.ListVersions(request.Ref.Target))
          .FirstOrDefault()
          ?? throw new InvalidOperationException(
              $"Cannot show the package file of '{package}': the repository {request.Ref.Target}"
              + " has no version tags (tags named like 'v1.2.3'). Name a branch, tag or commit"
              + $" instead, e.g. '{request.Ref.Target}@branch:main'.");
      archive = await _archiveProvider.OpenVersion(request.Ref.Target, version);
      tagVersion = version.Version;
    } else {
      archive = await _archiveProvider.Open(request.Ref);
    }

    RunTrace.Write("resolve", $"Showing the package file of {archive.Key}");
    PackageDocument document = await _archiveReader.Read(archive);
    if (!document.IsLegacy) {
      return JsonSerializer.Serialize(document.Json, _indented);
    }

    string? runtime = null;
    try {
      runtime = (await _lockStore.Load(_fs.Path.GetFullPath(_workspaceRoot())))?.Runtime;
    }
    catch (LockFormatException ex) {
      // A workspace file lip cannot read records no environment it can use.
      RunTrace.Write("workspace", $"Ignoring the unreadable workspace file: {ex.Message}");
    }

    LipEnvironment environment = new(
        McmodPlatform.Server, runtime ?? RuntimeInformation.RuntimeIdentifier);
    return JsonSerializer.Serialize(document.ReadFor(environment, tagVersion).Json, _indented);
  }

  #region Planning

  private async Task<Workspace> OpenWorkspace(OperationOptions options) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    LockFile? recorded = await _lockStore.Load(root);
    LipEnvironment environment = DetermineEnvironment(recorded, options.Rid);
    recorded ??= await ReadToothLock(root, environment, resolveCommits: true);
    RunTrace.Write("workspace", $"Workspace {root}: "
        + (recorded is null
            ? "no workspace file yet"
            : $"{recorded.Packages.Count} installed packages, runtime {recorded.Runtime}")
        + $"; using the environment ({environment.Platform.ToSpecString()},"
        + $" {environment.Runtime})" + (options.Rid is null ? "" : " from --rid"));
    // A workspace without a workspace file gets the environment of its first operation.
    LockFile lockFile = recorded ?? new LockFile {
      Platform = environment.Platform.ToSpecString(),
      Runtime = environment.Runtime,
      Packages = [],
    };
    Resolver resolver = new(_archiveProvider, _archiveReader, Warn);
    foreach (LockedPackage package in lockFile.Packages) {
      if (package.Commit is string commit
          && Ref.TryParse(package.Source, out Ref? source, out _)) {
        resolver.PinCommit(source, commit);
      }
    }

    return new Workspace(root, lockFile, environment, resolver, new GraphSolver(resolver));
  }

  /// The workspace file protocol/compatibility.md §3 derives from the tooth_lock.json of a
  /// workspace without a workspace file, for its environment; null when it has none that
  /// Section 3 reads. With resolveCommits, the commit of each package is that of the tag
  /// "v&lt;version&gt;" of its remote repository, and it fails when that cannot be obtained.
  private async Task<LockFile?> ReadToothLock(
      string root, LipEnvironment environment, bool resolveCommits) {
    if (ToothLockMigration.Read(_fs, root) is not { } members) {
      return null;
    }

    if (resolveCommits) {
      await _ui.PrintInfo(
          $"Migrating {ToothLockMigration.FileName} of lip v0.x to the workspace file.");
    }

    return await ToothLockMigration.Derive(
        members, _fs, root, environment, resolveCommits ? TagCommit : null);
  }

  /// The commit the tag "v&lt;version&gt;" of a remote repository points to, or null when it
  /// cannot be obtained.
  private async Task<string?> TagCommit(string repo, SemVersion version) {
    if (!GitRef.TryParse(version.ToString(), out GitRef? gitRef, out _)) {
      return null;
    }

    try {
      return await _archiveProvider.ResolveGitRef(repo, gitRef);
    }
    catch (ArchiveUnavailableException) {
      return null;
    }
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
    RunTrace.Write("workspace", $"Took the workspace lock {_lockStore.GetMutexPath(root)}");
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

    RunTrace.Write("workspace", $"Recovering the interrupted operation recorded in {journal}");
    WorkspaceTransaction.RecoveryResult result = WorkspaceTransaction.Recover(
        _fs,
        journal,
        commitLock: () => _lockStore.CommitStaged(root),
        discardLock: () => _lockStore.DiscardStaged(root))!;
    RunTrace.Write("workspace", $"Recovery: started {result.Started ?? "(unknown)"},"
        + $" {(result.Committed ? "committed" : "not committed")}, {result.Changes} changes,"
        + $" {result.Errors.Count} errors");
    foreach (Exception error in result.Errors) {
      RunTrace.Write("workspace", $"Recovery error: {error}");
    }

    string started = result.Started is null ? "" : $" (it started at {result.Started})";
    string errors = string.Concat(result.Errors.Select(e => $"\n  - {e.Message}"));
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
          $"A lip operation on this workspace was interrupted{started}, and lip cannot undo all"
          + $" of its changes yet:{errors}\nFix the problem (e.g. stop the server and other"
          + " programs using these files), then run lip again; it retries the rest. Do not delete"
          + $" '{journal}': it records what is left to undo.",
          // The exceptions behind the list, so that the report and the hints (e.g. for a file
          // in use) see them.
          result.Errors.Count == 1
              ? result.Errors[0]
              : new AggregateException("The changes that could not be undone.", result.Errors));
    }

    await _ui.PrintWarning(
        $"A lip operation on this workspace was interrupted{started}; undid its"
        + $" {result.Changes} changes. The workspace and its workspace file are as they were"
        + " before it.");
  }

  /// The environment of the workspace (protocol/cli.md §4). lip manages server workspaces only,
  /// so the platform is always server. The runtime is --rid, then the workspace file, then the
  /// RID lip itself runs on.
  private static LipEnvironment DetermineEnvironment(LockFile? lockFile, string? explicitRid) {
    string server = McmodPlatform.Server.ToSpecString();
    if (lockFile?.Platform is { } platform && platform != server) {
      throw new InvalidOperationException(
          $"The workspace file {WorkspaceFile} records the platform \"{platform}\" in its"
          + $" \"platform\" field, but lip manages server workspaces only (\"{server}\")."
          + " Client workspaces are managed by launchers such as LeviLauncher.");
    }

    return new LipEnvironment(
        McmodPlatform.Server,
        explicitRid ?? lockFile?.Runtime ?? RuntimeInformation.RuntimeIdentifier);
  }

  private void Warn(string message) => _ui.PrintWarning(message).GetAwaiter().GetResult();

  /// Parses package arguments; local paths are relative to the workspace root, which is the
  /// current directory of lip.
  private List<(string, PackageRequest)> ParseRequests(IReadOnlyList<string> packages) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    return [.. packages.Select(p => (p, PackageArguments.Parse(p, root, _fs)))];
  }

  /// The first candidate of every request: the package the request names. Fails when a request
  /// has no candidate, or, unless allowSameIdentity, when two requests name the same package
  /// (protocol/cli.md §5.1).
  private async Task<List<Candidate>> RequestedCandidates(
      Workspace workspace,
      List<(string Argument, PackageRequest Request)> requests,
      bool allowSameIdentity = false) {
    List<Candidate> candidates = [];
    foreach ((string argument, PackageRequest request) in requests) {
      Candidate candidate = await FirstCandidate(workspace, argument, request);
      string identity = candidate.Package.Identity;
      int earlier = candidates.FindIndex(c => SameIdentity(c.Package.Identity, identity));
      if (!allowSameIdentity && earlier >= 0) {
        throw new InvalidOperationException(
            $"The arguments '{requests[earlier].Argument}' and '{argument}' both name the package"
            + $" '{identity}'. Request each package once.");
      }

      candidates.Add(candidate);
    }

    return candidates;
  }

  private async Task<Candidate> FirstCandidate(
      Workspace workspace, string argument, PackageRequest request) {
    await _ui.PrintInfo($"Resolving {argument}...");
    List<ResolveDiagnostic> diagnostics = [];
    await foreach (Candidate candidate in workspace.Resolver.ResolveLazyWithCauses(
        request.Ref, request.Range, workspace.Environment, diagnostics.Add)) {
      RunTrace.Write("resolve", $"'{argument}' is {candidate.Package.Identity}"
          + $"@{candidate.Package.Version} from {candidate.Archive.Key}");
      return candidate;
    }

    const int Shown = 10;
    string summary = $"No package found for '{argument}' that can be installed in this workspace"
        + $" ({workspace.Environment.Platform.ToSpecString()}, {workspace.Environment.Runtime}).";
    string message = summary
        + (diagnostics.Count == 0 ? "" : " Every place lip looked failed:" + string.Concat(
            diagnostics.Take(Shown).Select(d => $"\n  - {d.Message}")))
        + (diagnostics.Count > Shown ? $"\n  ... and {diagnostics.Count - Shown} more" : "");
    // The exceptions behind the diagnostics are kept as causes, so that the failure report
    // shows them and their hints (e.g. for network failures) apply. The summary as their
    // message is part of the message above, so it is not shown twice.
    List<Exception> causes = [.. diagnostics.Select(d => d.Cause).OfType<Exception>()];
    if (causes.Count == 0) {
      // When the package was found, the reasons above say what is wrong; spelling advice fits
      // only when nothing (or a repository without versions, maybe the wrong one) was found.
      bool notFound = diagnostics.All(
          d => d.Message.Contains("has no version tags", StringComparison.Ordinal));
      throw new InvalidOperationException(notFound
          ? message + "\nCheck the spelling of the package and the version you asked for."
          : message);
    }

    throw new InvalidOperationException(message, new AggregateException(summary, causes));
  }

  /// Solves the requests with the installed packages pinned, except the unpinned identities;
  /// pinnedAs gives, by folded identity, the candidate an installed package is pinned as. When
  /// that fails, solves them again with only the explicitly installed packages pinned, trying
  /// the installed versions of the dependency packages first.
  private async Task<Plan> MakePlan(
      Workspace workspace,
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyCollection<string> unpinned,
      IReadOnlyCollection<string> explicitIdentities,
      IReadOnlyDictionary<string, Candidate>? pinnedAs = null) {
    HashSet<string> unpinnedSet = [.. unpinned.Select(Fold)];
    HashSet<string> explicitSet = [.. explicitIdentities.Select(Fold)];

    List<Candidate> pinnedAll = [
      .. workspace.Lock.Packages
          .Where(p => !unpinnedSet.Contains(Fold(p.Identity)))
          .Select(p => pinnedAs?.GetValueOrDefault(Fold(p.Identity))
              ?? PinnedCandidate(workspace, p)),
    ];

    SolvedGraph graph;
    try {
      graph = await workspace.Solver.SolveGraph(requests, pinnedAll, workspace.Environment);
    }
    catch (DependencyResolutionException withInstalled) {
      List<Candidate> pinnedExplicit = [];
      List<Candidate> preferred = [];
      foreach (Candidate candidate in pinnedAll) {
        (explicitSet.Contains(Fold(candidate.Package.Identity)) ? pinnedExplicit : preferred)
            .Add(candidate);
      }

      if (preferred.Count == 0) {
        throw;
      }

      // In full, with its causes, since only the retry's failure is thrown if the retry fails.
      RunTrace.Write("resolve", "With every installed package kept: " + withInstalled);
      string reason = withInstalled.Message.Split('\n').Skip(1).FirstOrDefault()
          ?.Trim(' ', '-', ':', '.')
          ?? withInstalled.Message.TrimEnd('.');
      await _ui.PrintInfo(
          $"The installed dependency packages do not fit the request ({reason}); choosing their"
          + " versions again.");
      graph = await workspace.Solver.SolveGraph(
          requests, pinnedExplicit, workspace.Environment, preferred);
    }

    // Keep what the requests and the explicitly installed packages need.
    HashSet<int> needed = [];
    Stack<int> pending = new(graph.RequestSatisfiers.Where(i => i >= 0));
    for (int i = 0; i < graph.Order.Count; i++) {
      if (explicitSet.Contains(Fold(graph.Order[i].Package.Identity))) {
        pending.Push(i);
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
      .. graph.RequestSatisfiers.Where(i => i >= 0)
          .Select(i => Fold(graph.Order[i].Package.Identity)),
    ];
    return new Plan([.. graph.Order.Where((_, i) => needed.Contains(i))], finalExplicit);
  }

  #endregion

  #region Applying

  /// What an operation changes, in order, and how the workspace file entries left at the end are
  /// ordered and marked.
  private sealed record ChangeSet(
      List<Change> Changes, Func<List<LockedPackage>, Task<List<LockedPackage>>> Finish);

  private abstract record Change;

  private sealed record UninstallChange(LockedPackage Package) : Change;

  private sealed record InstallChange(Candidate Candidate, bool Explicit) : Change;

  private sealed record MarkExplicitChange(LockedPackage Package) : Change;

  /// The changes from the installed packages to the planned package set: uninstalling what
  /// leaves or changes (in reverse installation order), then installing what enters (in
  /// installation order).
  private async Task<ChangeSet> Changes(Workspace workspace, OperationOptions options, Plan plan) {
    Dictionary<string, Candidate> members = plan.Members.ToDictionary(
        c => Fold(c.Package.Identity), StringComparer.Ordinal);

    // The identities of the packages that stay exactly as installed.
    HashSet<string> kept = [
      .. workspace.Lock.Packages
          .Where(p => members.TryGetValue(Fold(p.Identity), out Candidate? member)
              && IsSamePackage(p, member))
          .Select(p => Fold(p.Identity)),
    ];

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
      // The workspace file lists the members of the new set in installation order.
      List<LockedPackage> final = [];
      foreach (LockedPackage package in plan.Members.Select(c => remaining.First(
          p => SameIdentity(p.Identity, c.Package.Identity)))) {
        string key = Fold(package.Identity);
        bool isExplicit = plan.Explicit.Contains(key);
        if (isExplicit != package.Explicit && kept.Contains(key)) {
          await _ui.PrintInfo((options.DryRun ? "Dry run: would mark" : "Marking")
              + $" package '{package.Identity}' version {package.Version} as "
              + (isExplicit ? "explicitly installed." : "not explicitly installed."));
        }

        LockedPackage entry = package with { Explicit = isExplicit };
        // A kept package requested by another source, e.g. "<repo>@<version>" for a package
        // installed from "<repo>", records the source it is requested by now.
        if (isExplicit && kept.Contains(key)
            && members.TryGetValue(key, out Candidate? member)
            && member.Source.ToString() != package.Source) {
          entry = entry with { Source = member.Source.ToString() };
        }

        final.Add(entry);
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

    List<Candidate> candidates = await RequestedCandidates(workspace, requests);
    List<Change> changes = [];
    foreach (Candidate candidate in candidates) {
      LockedPackage? existing = workspace.Lock.Packages.FirstOrDefault(
          p => SameIdentity(p.Identity, candidate.Package.Identity));
      if (existing is not null && IsSamePackage(existing, candidate)) {
        changes.Add(new MarkExplicitChange(existing));
        continue;
      }

      if (existing is not null) {
        changes.Add(new UninstallChange(existing));
      }

      changes.Add(new InstallChange(candidate, Explicit: true));
    }

    await Execute(workspace, options, new ChangeSet(changes, Task.FromResult));
  }

  /// Plans every change against a view of the workspace as the earlier changes leave it, so
  /// that every check and every fetch happens before anything changes; then applies the plans
  /// in one transaction and writes the workspace file last. A failure while applying (or
  /// cancellation) undoes every change and leaves the workspace file as it was
  /// (protocol/workspace.md §6). In a dry run, reports the plans instead.
  private async Task Execute(Workspace workspace, OperationOptions options, ChangeSet changeSet) {
    // The changes are planned against the layout an operation that changes the workspace makes
    // first; it is checked and made only when the operation may change the workspace
    // (protocol/workspace.md §2).
    WorkspaceLayout.LayoutPlan layout = _layout.Plan(workspace.Root);
    WorkspaceView view = new(_fs, layout);
    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    List<(string Message, PackagePlan Plan)> plans = [];
    HashSet<PackagePlan> installs = new(ReferenceEqualityComparer.Instance);
    foreach (Change change in changeSet.Changes) {
      options.CancellationToken.ThrowIfCancellationRequested();
      RunTrace.Write("workspace", "Planning " + change switch {
        UninstallChange(LockedPackage p) => $"to uninstall {p.Identity}@{p.Version}",
        InstallChange(Candidate c, bool e) => $"to install {c.Package.Identity}"
            + $"@{c.Package.Version} from {c.Archive.Key}{(e ? " (explicit)" : "")}",
        MarkExplicitChange(LockedPackage p) => $"to mark {p.Identity}@{p.Version} explicit",
        _ => change.GetType().Name,
      });
      switch (change) {
        case UninstallChange(LockedPackage package): {
            string what = $" package '{package.Identity}' version {package.Version}";
            if (options.DryRun) {
              await _ui.PrintInfo("Dry run: would uninstall" + what);
            }

            plans.Add(("Uninstalling" + what, await _installer.PlanUninstall(
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

            PackagePlan plan = await _installer.PlanInstall(
                candidate, Context(workspace, options, remaining), view);
            plans.Add(("Installing" + what, plan));
            installs.Add(plan);
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
          throw new ArgumentException(
              $"Internal error: lip planned an unknown kind of change ({change}). This is a bug"
              + " in lip.", nameof(changeSet));
      }
    }

    bool changesWorkspace = plans.Any(p => p.Plan.MayChange);
    if (changesWorkspace) {
      _layout.Check(layout);
      if (options.DryRun) {
        await _layout.ReportDryRun(layout);
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
      if (changesWorkspace) {
        _layout.Apply(layout, transaction);
      }

      // The files each package installed owns as its writes happened: hook commands may have
      // changed which files matching preserve were kept (protocol/workspace.md §4). Each write
      // is checked against the owners as they are then.
      FileOwners owners = _installer.Owners(workspace.Root, workspace.Lock.Packages);
      Dictionary<string, IReadOnlyList<string>> owned = new(StringComparer.Ordinal);
      foreach ((string message, PackagePlan plan) in plans) {
        await _ui.PrintInfo(message);
        IReadOnlyList<string> files =
            await _installer.Apply(plan, transaction, owners, options.CancellationToken);
        if (installs.Contains(plan)) {
          owned[Fold(plan.Name)] = files;
        } else {
          owners.Release(plan.Name);
        }
      }

      final = final with {
        Packages = [
          .. final.Packages.Select(
              p => owned.TryGetValue(Fold(p.Identity), out IReadOnlyList<string>? actual)
                  ? p with { Files = [.. actual] }
                  : p),
        ],
      };
      options.CancellationToken.ThrowIfCancellationRequested();
      // The commit point: the journal records the commit, then the workspace file moves into
      // place.
      await _lockStore.Stage(workspace.Root, final);
      transaction.Commit();
      _lockStore.CommitStaged(workspace.Root);
    }
    catch (Exception ex) {
      RunTrace.Write("workspace", $"Undoing the changes after {ex.GetType().Name}: {ex.Message}");
      await _ui.PrintWarning(
          (ex is OperationCanceledException ? "Cancelled" : "The operation failed")
          + "; undoing its changes to the workspace.");
      IReadOnlyList<Exception> errors = transaction.Rollback(() => {
        try {
          _lockStore.DiscardStaged(workspace.Root);
        }
        catch (Exception discard) when (discard is IOException or UnauthorizedAccessException) {
          // A staged workspace file is never read, and the next save replaces it.
          RunTrace.Write("workspace", $"Could not delete the staged workspace file: {discard}");
        }
      });
      foreach (Exception error in errors) {
        RunTrace.Write("workspace", $"Rollback error: {error}");
        await _ui.PrintWarning(error.Message);
      }

      if (errors.Count > 0) {
        await _ui.PrintWarning(
            "Some changes could not be undone. Stop the server and other programs using the"
            + " workspace; the next install, uninstall or update of this workspace tries again.");
      }

      throw;
    }

    RunTrace.Write("workspace", $"Committed {plans.Count} package changes");
    foreach (Exception error in transaction.Complete()) {
      RunTrace.Write("workspace", $"Cleanup warning: {error}");
      await _ui.PrintWarning(error.Message);
    }
  }

  private static LockedPackage Locked(
      Candidate candidate, bool isExplicit, IReadOnlyList<string> files) => new() {
        Manifest = candidate.Package.Manifest,
        Variant = candidate.Package.Variant,
        Explicit = isExplicit,
        Source = candidate.Source.ToString(),
        Commit = CommitOf(candidate),
        Files = [.. files],
      };

  private static InstallContext Context(
      Workspace workspace,
      OperationOptions options,
      List<LockedPackage> remaining) {
    return new InstallContext(
        workspace.Root,
        workspace.Environment,
        LockWith(workspace, remaining),
        options.DryRun,
        options.IgnoreScripts);
  }

  /// The workspace file with the given packages and the environment, once known.
  private static LockFile LockWith(Workspace workspace, List<LockedPackage> packages) {
    return workspace.Lock with {
      Platform = workspace.Environment.Platform.ToSpecString(),
      Runtime = workspace.Environment.Runtime,
      Packages = [.. packages],
    };
  }

  #endregion

  #region Installed packages

  /// Rebuilds an installed package from the workspace file (protocol/workspace.md §5,
  /// "Reinstalling"): with the package file the workspace file records, and the archive of the
  /// commit it records for a remote repository, otherwise the archive its source names now,
  /// opened when first needed. Fails when it is no candidate in the environment, e.g. when its
  /// package file has no entry for another --rid (protocol/workspace.md §3).
  private Candidate PinnedCandidate(Workspace workspace, LockedPackage package) {
    try {
      Package rebuilt = new(
          package.Id,
          package.Variant,
          SemVersion.Parse(package.Version, SemVersionStyles.Strict),
          McmodInstaller.LockedEntry(package, workspace.Environment)) {
        Manifest = package.Manifest,
      };
      Ref source = ParseRecordedSource(package);
      if (source.Kind == RefKind.Spec && package.Commit is null) {
        throw new LockFormatException(
            $"The entry of '{package.Identity}' has no \"commit\" field, which every package"
            + $" from a remote repository ({source}) must have.");
      }

      string key = $"{package.Source} (installed)";
      ArchiveHandle archive = new(
          key,
          new DeferredSource(async () => (source.Kind == RefKind.Spec
              ? await _archiveProvider.OpenCommit(source.Target, package.Commit!)
              : await _archiveProvider.Open(source.WithVariant(""))).Source),
          source.Kind == RefKind.Spec ? package.Commit : null);
      return Candidate.Create(rebuilt, archive, source, _archiveProvider);
    }
    catch (Exception ex) when (ex is McmodInstallException or FormatException
        or ArgumentException or InvalidOperationException) {
      throw new InvalidOperationException(
          $"Cannot use the installed package '{package.Identity}' as the workspace file"
          + $" {WorkspaceFile} records it, in the environment"
          + $" ({workspace.Environment.Platform.ToSpecString()}, {workspace.Environment.Runtime})."
          // LockedEntry throws McmodInstallException when the package has no entry for the
          // environment; a LockFormatException already says what to do.
          + (ex is McmodInstallException && workspace.Environment.Runtime != workspace.Lock.Runtime
              ? $" The workspace was installed for the runtime {workspace.Lock.Runtime}; run lip"
                  + " without --rid, or with that runtime."
              : ex is LockFormatException
                  ? ""
                  : " If the file was edited by hand, undo the edit."),
          ex);
    }
  }

  /// The source an entry of the workspace file records.
  private static Ref ParseRecordedSource(LockedPackage package) {
    try {
      return Ref.Parse(package.Source, allowFile: true);
    }
    catch (FormatException ex) {
      throw new LockFormatException(
          $"The workspace file {WorkspaceFile} records \"{package.Source}\" in the \"ref\" field"
          + $" of package '{package.Identity}', which is not a valid package source. If the file"
          + " was edited by hand, undo the edit.",
          ex);
    }
  }

  /// Matches an uninstall argument (protocol/cli.md §5.2): an identity (ignoring ASCII case), or
  /// else the source, with the locations compared ignoring ASCII case; an argument without a
  /// variant matches every variant, and one without a ref every ref.
  private List<LockedPackage> MatchInstalled(Workspace workspace, string argument) {
    LockFile lockFile = workspace.Lock;
    List<LockedPackage> byIdentity = [
      .. lockFile.Packages.Where(p => SameIdentity(p.Identity, argument)),
    ];
    if (byIdentity.Count > 0) {
      return byIdentity;
    }

    Ref wanted = PackageArguments.Parse(argument, workspace.Root, _fs).Ref;
    bool hasVariant = wanted.Variant != "";
    return [
      .. lockFile.Packages.Where(p =>
          Ref.TryParse(p.Source, allowFile: true, out Ref? source, out _)
          && source.Kind == wanted.Kind
          && PackageNames.AreEqual(source.Target, wanted.Target)
          && (!hasVariant || source.Variant == wanted.Variant)
          && (wanted.GitRef is null || source.GitRef == wanted.GitRef)),
    ];
  }

  #endregion

  private static List<string> ExplicitIdentities(LockFile lockFile) =>
      [.. lockFile.Packages.Where(p => p.Explicit).Select(p => p.Identity)];

  /// Whether a candidate is the installed package (protocol/workspace.md §5): same identity,
  /// version and commit.
  private static bool IsSamePackage(LockedPackage package, Candidate candidate) =>
      SameIdentity(package.Identity, candidate.Package.Identity)
      && package.Version == candidate.Package.Version.ToString()
      && string.Equals(package.Commit, CommitOf(candidate), StringComparison.OrdinalIgnoreCase);

  /// Whether two candidates are the same package: same identity, version and commit.
  private static bool IsSameCandidate(Candidate a, Candidate b) =>
      SameIdentity(a.Package.Identity, b.Package.Identity)
      && a.Package.Version == b.Package.Version
      && string.Equals(CommitOf(a), CommitOf(b), StringComparison.OrdinalIgnoreCase);

  /// The commit of a candidate from a remote repository (lowercase hex), or null.
  private static string? CommitOf(Candidate candidate) =>
      candidate.Source.Kind == RefKind.Spec ? candidate.Archive.Commit?.ToLowerInvariant() : null;

  private static bool SameIdentity(string a, string b) => PackageNames.AreEqual(a, b);

  private static string Fold(string identity) => PackageNames.Fold(identity);

  private sealed record Workspace(
      string Root,
      LockFile Lock,
      LipEnvironment Environment,
      Resolver Resolver,
      GraphSolver Solver);

  /// The new package set in installation order, and the folded identities of its explicitly
  /// installed members.
  private sealed record Plan(IReadOnlyList<Candidate> Members, HashSet<string> Explicit);
}
