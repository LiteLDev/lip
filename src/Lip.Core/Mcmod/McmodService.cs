using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lip.Core.Infrastructure;
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
/// Every operation loads the workspace file, determines the environment, computes the new
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
/// file without network access, and their archives and assets are opened from their identifiers
/// only when needed. If the request cannot be satisfied with every installed package pinned, it
/// is solved again with only the explicitly installed packages pinned, so that dependencies may
/// change version. Packages that no explicitly installed package needs any more are removed.
public class McmodService(
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    ILockStore lockStore,
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    IMcmodInstaller installer,
    Func<IResolver, IGraphSolver>? createSolver = null,
    Func<string>? workspaceRoot = null,
    IDirectoryLinker? directoryLinker = null) {
  private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

  private readonly IFileSystem _fs = fileSystem;
  private readonly IUserInteraction _ui = userInteraction;
  private readonly ILockStore _lockStore = lockStore;
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly IMcmodInstaller _installer = installer;
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

    List<Candidate> candidates = await RequestedCandidates(workspace, requests);
    foreach (Candidate candidate in candidates) {
      string identity = candidate.Package.Identity;
      if (workspace.Lock.Packages.Any(p => p.Explicit && SameIdentity(p.Identity, identity))) {
        throw new InvalidOperationException(
            $"Cannot install package '{identity}' because it is already explicitly installed.");
      }
    }

    await Execute(workspace, options, await Changes(workspace, options, await MakePlan(
        workspace,
        [.. requests.Select(r => r.Request)],
        unpinned: [.. candidates.Select(c => c.Package.Identity)],
        explicitIdentities: ExplicitIdentities(workspace.Lock))));
  }

  public async Task Uninstall(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);

    List<LockedPackage> targets = [];
    foreach (string package in packages) {
      List<LockedPackage> matched = MatchInstalled(workspace, package);
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

    // Removing some of several entries of one identity (e.g. tooth paths differing in case)
    // leaves the identity installed, so the package set does not change: such entries are
    // uninstalled directly, like every target with --no-dependencies.
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
        explicitIdentities: [
          .. ExplicitIdentities(workspace.Lock).Where(i => !removed.Contains(Fold(i))),
        ]);

    foreach (Candidate kept in plan.Members.Where(c => removed.Contains(Fold(c.Package.Identity)))) {
      await _ui.PrintInfo(
          $"Package '{kept.Package.Identity}' is still required by other packages; keeping it "
          + "as a dependency.");
    }

    ChangeSet planned = await Changes(workspace, options, plan);
    await Execute(original, options, planned with { Changes = [.. changes, .. planned.Changes] });
  }

  public async Task Update(IReadOnlyList<string> packages, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);

    List<(string Argument, PackageRequest Request)> requests;
    if (packages.Count == 0) {
      // Every explicitly installed package, requested again by its source: a remote repository
      // at its newest version, a branch at its head, a tag where it points now, a rev as it is,
      // a URL as cached and a local path with its current content.
      requests = [];
      foreach (IGrouping<string, LockedPackage> group in workspace.Lock.Packages
          .Where(p => p.Explicit)
          .GroupBy(p => Fold(p.Identity))) {
        LockedPackage package = group.First();
        Ref source = Ref.Parse(package.Source, allowFile: true);
        if (source.Kind == RefKind.File && !_fs.File.Exists(source.LocalPath)
            && !_fs.Directory.Exists(source.LocalPath)) {
          await _ui.PrintWarning(
              $"Keeping package '{package.Identity}' as it is: {source.LocalPath}, which it was"
              + " installed from, does not exist any more.");
          continue;
        }

        requests.Add((package.Identity, new PackageRequest(source, null)));
      }
    } else {
      requests = ParseRequests(packages);
    }

    List<Candidate> candidates = await RequestedCandidates(
        workspace, requests, allowSameIdentity: packages.Count == 0);
    List<string> identities = [];
    List<PackageRequest> requested = [];
    for (int i = 0; i < candidates.Count; i++) {
      string identity = candidates[i].Package.Identity;
      if (!workspace.Lock.Packages.Any(p => SameIdentity(p.Identity, identity))) {
        throw new InvalidOperationException(
            $"Cannot update package '{requests[i].Argument}' because it is not currently"
            + " installed.");
      }

      if (identities.Any(other => SameIdentity(other, identity))) {
        continue;
      }

      identities.Add(identity);
      requested.Add(requests[i].Request);
    }

    await Execute(workspace, options, await Changes(workspace, options, await MakePlan(
        workspace,
        requested,
        unpinned: identities,
        explicitIdentities: ExplicitIdentities(workspace.Lock))));
  }

  public async Task<IReadOnlyList<InstalledPackage>> List() {
    LockFile lockFile = await _lockStore.Load(_workspaceRoot());
    return [
      .. lockFile.Packages.Select(p => new InstalledPackage(p.Identity, p.Version, p.Explicit)),
    ];
  }

  /// The versions of the version tags of a remote repository, lowest first.
  public async Task<IReadOnlyList<string>> Versions(string package) {
    if (!Ref.TryParse(package, out Ref? reference, out _) || reference.Kind != RefKind.Spec
        || reference.GitRef is not null) {
      throw new ArgumentException($"'{package}' is not a remote repository.", nameof(package));
    }

    IReadOnlyList<SpecVersion> versions = await _archiveProvider.ListVersions(reference.Target);
    return [.. versions.Reverse().Select(v => v.Version.ToString()).Distinct()];
  }

  /// The tooth file of the first archive a package argument names (protocol/cli.md §5.6): for a
  /// remote repository, its newest version tag, or the commit its ref selects; otherwise the
  /// archive of the URL or local path. A tooth file of an earlier format is written as
  /// protocol/package.md §5 reads it for the environment of protocol/cli.md §4.
  public async Task<string> View(string package) {
    PackageRequest request = PackageArguments.Parse(
        package, _fs.Path.GetFullPath(_workspaceRoot()), _fs);

    ArchiveHandle archive;
    if (request.Ref is { Kind: RefKind.Spec, GitRef: GitRef gitRef }) {
      archive = await _archiveProvider.OpenCommit(
          request.Ref.Target, await _archiveProvider.ResolveGitRef(request.Ref.Target, gitRef));
    } else if (request.Ref.Kind == RefKind.Spec) {
      SpecVersion version = (await _archiveProvider.ListVersions(request.Ref.Target))
          .FirstOrDefault()
          ?? throw new InvalidOperationException(
              $"'{request.Ref.Target}' has no version tags.");
      archive = await _archiveProvider.OpenVersion(request.Ref.Target, version);
    } else {
      archive = await _archiveProvider.Open(request.Ref);
    }

    ToothDocument document = await _archiveReader.Read(archive);
    if (!document.IsLegacy) {
      return JsonSerializer.Serialize(document.Json, _indented);
    }

    string? runtime = null;
    try {
      runtime = (await _lockStore.Load(_fs.Path.GetFullPath(_workspaceRoot()))).Runtime;
    }
    catch (LockFormatException) {
      // A workspace file lip cannot read records no environment it can use.
    }

    LipEnvironment environment = new(
        McmodPlatform.Server, runtime ?? RuntimeInformation.RuntimeIdentifier);
    return JsonSerializer.Serialize(document.ReadFor(environment).Json, _indented);
  }

  #region Planning

  private async Task<Workspace> OpenWorkspace(OperationOptions options) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    LockFile lockFile = await _lockStore.Load(root);
    LipEnvironment environment = DetermineEnvironment(lockFile, options.Rid);
    Resolver resolver = new(_archiveProvider, _archiveReader, Warn);
    foreach (LockedPackage package in lockFile.Packages) {
      if (package.Archive is GitArchiveIdentity git
          && Ref.TryParse(package.Source, out Ref? source, out _)) {
        resolver.PinCommit(source, git.Commit);
      }
    }

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
        + $" {result.Changes} changes. The workspace and its workspace file are as they were"
        + " before it.");
  }

  /// The environment of the workspace (protocol/cli.md §4). lip manages server workspaces only,
  /// so the platform is always server. The runtime is --rid, then the workspace file, then the
  /// RID lip itself runs on.
  private static LipEnvironment DetermineEnvironment(LockFile lockFile, string? explicitRid) {
    string server = McmodPlatform.Server.ToSpecString();
    if (lockFile.Platform is { } platform && platform != server) {
      throw new InvalidOperationException(
          $"The workspace file records the platform \"{platform}\", but lip manages server"
          + " workspaces only. Client workspaces are managed by launchers such as LeviLauncher.");
    }

    if (lockFile.Runtime == "") {
      throw new LockFormatException("The workspace file records an empty runtime.");
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
  private async Task<List<Candidate>> RequestedCandidates(
      Workspace workspace,
      List<(string Argument, PackageRequest Request)> requests,
      bool allowSameIdentity = false) {
    List<Candidate> candidates = [];
    foreach ((string argument, PackageRequest request) in requests) {
      Candidate candidate = await FirstCandidate(workspace, argument, request);
      string identity = candidate.Package.Identity;
      if (!allowSameIdentity && candidates.Any(c => SameIdentity(c.Package.Identity, identity))) {
        throw new InvalidOperationException(
            $"Multiple requested packages are '{identity}'. Please request each package once.");
      }

      candidates.Add(candidate);
    }

    return candidates;
  }

  private async Task<Candidate> FirstCandidate(
      Workspace workspace, string argument, PackageRequest request) {
    await _ui.PrintInfo($"Resolving {argument}...");
    List<string> diagnostics = [];
    await foreach (Candidate candidate in workspace.Resolver.ResolveLazy(
        request.Ref, request.Range, workspace.Environment, onDiagnostic: diagnostics.Add)) {
      return candidate;
    }

    throw new InvalidOperationException(
        $"No package found for '{argument}'"
        + (diagnostics.Count == 0 ? "." : ":" + string.Concat(
            diagnostics.Take(10).Select(d => $"\n  - {d}"))));
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
      // Packages whose entry cannot be read may depend on any installed dependency, so then no
      // installed dependency is removed (explicit ones the user removes still are).
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
            + "cannot read its entry in the workspace file in this environment.");
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
    if (frozen.Count > 0) {
      await _ui.PrintWarning(
          "Cannot read the entries of "
          + string.Join(", ", frozen.Select(g => g.Representative.Identity))
          + " in the workspace file; they are left as they are.");
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

    // Groups whose package stays exactly as installed.
    HashSet<string> kept = [];
    foreach (Group group in plan.Groups) {
      if (plan.Frozen.Contains(group)
          || (members.TryGetValue(group.Key, out Candidate? member)
              && await IsSamePackage(group.Representative, member))) {
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
      // The workspace file lists packages in installation order: frozen entries first (their
      // dependencies are unknown), then the members of the new set.
      List<LockedPackage> ordered = [
        .. plan.Frozen.SelectMany(g => g.Entries),
        .. plan.Members.SelectMany(c => remaining.Where(
            p => Fold(p.Identity) == Fold(c.Package.Identity))),
      ];
      ordered.AddRange(remaining.Where(p => !ordered.Contains(p)));

      List<LockedPackage> final = [];
      foreach (LockedPackage package in ordered) {
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
      List<LockedPackage> existing = [
        .. workspace.Lock.Packages.Where(
            p => SameIdentity(p.Identity, candidate.Package.Identity)),
      ];
      bool same = existing.Count > 0;
      foreach (LockedPackage package in existing) {
        same = same && await IsSamePackage(package, candidate);
      }

      if (same) {
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
  /// in one transaction and writes the workspace file last. A failure while applying (or
  /// cancellation) undoes every change and leaves the workspace file as it was
  /// (protocol/workspace.md §6). In a dry run, reports the plans instead.
  private async Task Execute(Workspace workspace, OperationOptions options, ChangeSet changeSet) {
    WorkspaceLayout.LayoutPlan? layout = null;
    if (changeSet.Changes.Any(c => c is not MarkExplicitChange)) {
      layout = _layout.Plan(workspace.Root);
      if (options.DryRun) {
        await _layout.ReportDryRun(layout);
      }
    }

    IReadOnlyList<InstalledMember> members = await Members(workspace, changeSet);
    WorkspaceView view = new(_fs, layout);
    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    List<(string Message, PackagePlan Plan)> plans = [];
    foreach (Change change in changeSet.Changes) {
      options.CancellationToken.ThrowIfCancellationRequested();
      switch (change) {
        case UninstallChange(LockedPackage package): {
            string what = $" package '{package.Identity}' version {package.Version}";
            if (options.DryRun) {
              await _ui.PrintInfo("Dry run: would uninstall" + what);
            }

            plans.Add(("Uninstalling" + what, await _installer.PlanUninstall(
                package, Context(workspace, options, remaining, members), view)));
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
                candidate, Context(workspace, options, remaining, members), view);
            plans.Add(("Installing" + what, plan));
            List<AssetIdentity?> assets = [];
            foreach (AssetHandle asset in candidate.Assets) {
              assets.Add(await asset.GetIdentity());
            }

            remaining.Add(Locked(candidate, isExplicit, plan.OwnedFiles, assets));
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

      foreach ((string message, PackagePlan plan) in plans) {
        await _ui.PrintInfo(message);
        await _installer.Apply(plan, transaction, options.CancellationToken);
      }

      options.CancellationToken.ThrowIfCancellationRequested();
      // The commit point: the journal records the commit, then the workspace file moves into
      // place.
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
        // A staged workspace file is never read, and the next save replaces it.
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

  /// The packages installed once the changes are made, with the mod names of mods when a mod is
  /// installed (they decide its mod manifest, protocol/workspace.md §4 step 3).
  private async Task<IReadOnlyList<InstalledMember>> Members(
      Workspace workspace, ChangeSet changeSet) {
    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    List<Candidate> installed = [];
    foreach (Change change in changeSet.Changes) {
      switch (change) {
        case UninstallChange(LockedPackage package):
          remaining.Remove(package);
          break;
        case InstallChange(Candidate candidate, _):
          installed.Add(candidate);
          break;
      }
    }

    bool needsModNames = installed.Any(c => c.Package.IsMod);
    List<InstalledMember> members = [];
    foreach (LockedPackage package in remaining) {
      if (ToPackage(package) is not Package rebuilt) {
        continue;
      }

      // The mod manifest of an installed mod is read from its recorded asset; when that cannot
      // be obtained, the operation fails early (protocol/workspace.md §6 rule 1).
      Candidate? candidate = needsModNames && rebuilt.IsMod
          ? await PinnedCandidate(workspace, package)
          : null;
      members.Add(new InstalledMember(
          rebuilt.Identity,
          rebuilt.Version,
          candidate is null ? null : (await candidate.GetModManifest())?.Name));
    }

    foreach (Candidate candidate in installed) {
      members.Add(new InstalledMember(
          candidate.Package.Identity, candidate.Package.Version, await ModName(candidate)));
    }

    return members;

    // A mod manifest that cannot be read fails installing its package, not this.
    static async Task<string?> ModName(Candidate candidate) {
      try {
        return (await candidate.GetModManifest())?.Name;
      }
      catch (McmodInstallException) {
        return null;
      }
    }
  }

  private static LockedPackage Locked(
      Candidate candidate,
      bool isExplicit,
      IReadOnlyList<string> files,
      List<AssetIdentity?> assets) => new() {
        Tooth = candidate.Package.Tooth,
        Variant = candidate.Package.Variant,
        Version = candidate.Package.Version.ToString(),
        Explicit = isExplicit,
        Source = candidate.Source.ToString(),
        Archive = candidate.Archive.Identity,
        Assets = assets,
        Entry = candidate.Package.Entry.Json,
        Files = [.. files],
        ToothFormatVersion = candidate.Package.Entry.Legacy ? 3 : null,
      };

  private static InstallContext Context(
      Workspace workspace,
      OperationOptions options,
      List<LockedPackage> remaining,
      IReadOnlyList<InstalledMember> members) {
    return new InstallContext(
        workspace.Root,
        workspace.Environment,
        LockWith(workspace, remaining),
        options.DryRun,
        options.IgnoreScripts,
        members);
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

  /// The package an entry of the workspace file records, or null when it cannot be read.
  private static Package? ToPackage(LockedPackage package) {
    try {
      return new Package(
          package.Tooth,
          package.Variant,
          SemVersion.Parse(package.Version, SemVersionStyles.Strict),
          McmodInstaller.LockedEntry(package));
    }
    catch (Exception ex) when (ex is McmodInstallException or FormatException) {
      return null;
    }
  }

  /// Rebuilds an installed package from the workspace file (protocol/workspace.md §5,
  /// "Reinstalling"): its archive and assets are those the workspace file records, opened and
  /// verified when first needed. Returns null when the entry cannot be read, or does not apply
  /// to the environment (e.g. another --rid).
  private async Task<Candidate?> PinnedCandidate(Workspace workspace, LockedPackage package) {
    try {
      Package rebuilt = ToPackage(package) ?? throw new FormatException("the entry is invalid.");
      ToothEntry entry = rebuilt.Entry;
      if (!entry.Contains(workspace.Environment)) {
        return null;
      }

      if (entry.Assets.Count != package.Assets.Count) {
        throw new FormatException(
            $"it records {package.Assets.Count} asset identifiers for {entry.Assets.Count} assets.");
      }

      Ref source = Ref.Parse(package.Source, allowFile: true);
      string key = $"{package.Source} (installed)";
      ArchiveHandle archive = new(
          key,
          new DeferredSource(async () =>
              (await _archiveProvider.OpenLocked(key, package.Archive)).Source),
          package.Archive);
      List<AssetHandle> assets = [];
      for (int i = 0; i < entry.Assets.Count; i++) {
        EntryAsset asset = entry.Assets[i];
        assets.Add(asset.Type == AssetType.Self
            ? AssetHandle.Self(archive)
            : AssetHandle.Locked(
                _archiveProvider,
                asset.Type,
                package.Assets[i] ?? throw new FormatException(
                    $"it records no asset identifier for asset {i}."),
                AssetHandle.FormatOf(asset, entry.Legacy)));
      }

      return new Candidate(rebuilt, archive, source, assets);
    }
    catch (Exception ex) when (ex is FormatException or ArgumentException
        or InvalidOperationException) {
      await _ui.PrintWarning(
          $"Cannot read the entry of '{package.Identity}' in the workspace file: {ex.Message}");
      return null;
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

    Ref? wanted = PackageArguments.LocalFileRef(argument, workspace.Root, _fs);
    if (wanted is null && !Ref.TryParse(argument, allowFile: true, out wanted, out _)) {
      return byIdentity;
    }

    bool hasVariant = wanted.Variant != "";
    return [
      .. lockFile.Packages.Where(p =>
          Ref.TryParse(p.Source, allowFile: true, out Ref? source, out _)
          && source.Kind == wanted.Kind
          && string.Equals(source.Target, wanted.Target, StringComparison.OrdinalIgnoreCase)
          && (!hasVariant || source.Variant == wanted.Variant)
          && (wanted.GitRef is null || source.GitRef == wanted.GitRef)),
    ];
  }

  #endregion

  private static List<string> ExplicitIdentities(LockFile lockFile) =>
      [.. lockFile.Packages.Where(p => p.Explicit).Select(p => p.Identity)];

  /// Whether a candidate is the installed package (protocol/workspace.md §5): same identity,
  /// version, archive identifier and asset identifiers. The asset identifiers of a candidate
  /// that is not installed are known once its assets are downloaded.
  private static async Task<bool> IsSamePackage(LockedPackage package, Candidate candidate) {
    if (!SameIdentity(package.Identity, candidate.Package.Identity)
        || package.Version != candidate.Package.Version.ToString()
        || package.Archive.Canonical != candidate.Archive.Identity.Canonical
        || package.Assets.Count != candidate.Assets.Count) {
      return false;
    }

    for (int i = 0; i < candidate.Assets.Count; i++) {
      AssetIdentity? identity = await candidate.Assets[i].GetIdentity();
      AssetIdentity? recorded = package.Assets[i];
      if (identity?.Url != recorded?.Url
          || !string.Equals(identity?.Sha256, recorded?.Sha256, StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
    }

    return true;
  }

  private static bool SameIdentity(string a, string b) => PackageNames.AreEqual(a, b);

  private static string Fold(string identity) => PackageNames.Fold(identity);

  private sealed record Workspace(
      string Root,
      LockFile Lock,
      LipEnvironment Environment,
      IResolver Resolver,
      IGraphSolver Solver);

  /// The workspace file entries of one identity, and the candidate pinning them (null when
  /// unpinned or unreadable).
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
