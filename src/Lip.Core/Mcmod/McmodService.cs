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
/// file (with the tooth file it records) without network access, and their archives (the commit
/// it records for a remote repository, otherwise what their source names now) and assets are
/// opened only when needed. If the request cannot be satisfied with every installed package
/// pinned, it is solved again with only the explicitly installed packages pinned, so that
/// dependencies may change version. Packages that no explicitly installed package needs any more
/// are removed.
public class McmodService(
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    LockStore lockStore,
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    McmodInstaller installer,
    Func<string>? workspaceRoot = null,
    IDirectoryLinker? directoryLinker = null) {
  private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

  private readonly IFileSystem _fs = fileSystem;
  private readonly IUserInteraction _ui = userInteraction;
  private readonly LockStore _lockStore = lockStore;
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly McmodInstaller _installer = installer;
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
    if (packages.Count == 0) {
      // Every explicitly installed package, requested again by its source: a remote repository
      // at its newest version, a branch at its head, a tag where it points now, a rev as it is,
      // a URL downloaded again and a local path with its current content.
      requests = [];
      foreach (LockedPackage package in workspace.Lock.Packages.Where(p => p.Explicit)) {
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
    LockFile? lockFile = await _lockStore.Load(_workspaceRoot());
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
      throw new ArgumentException(
          $"'{package}' is not a remote repository without a ref.", nameof(package));
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
    // The tag version of the archive, the version of a package of an earlier format.
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
              $"'{request.Ref.Target}' has no version tags.");
      archive = await _archiveProvider.OpenVersion(request.Ref.Target, version);
      tagVersion = version.Version;
    } else {
      archive = await _archiveProvider.Open(request.Ref);
    }

    ToothDocument document = await _archiveReader.Read(archive);
    if (!document.IsLegacy) {
      return JsonSerializer.Serialize(document.Json, _indented);
    }

    string? runtime = null;
    try {
      runtime = (await _lockStore.Load(_fs.Path.GetFullPath(_workspaceRoot())))?.Runtime;
    }
    catch (LockFormatException) {
      // A workspace file lip cannot read records no environment it can use.
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
  private static LipEnvironment DetermineEnvironment(LockFile? lockFile, string? explicitRid) {
    string server = McmodPlatform.Server.ToSpecString();
    if (lockFile?.Platform is { } platform && platform != server) {
      throw new InvalidOperationException(
          $"The workspace file records the platform \"{platform}\", but lip manages server"
          + " workspaces only. Client workspaces are managed by launchers such as LeviLauncher.");
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

    List<Candidate> pinnedAll = [
      .. workspace.Lock.Packages
          .Where(p => !unpinnedSet.Contains(Fold(p.Identity)))
          .Select(p => PinnedCandidate(workspace, p)),
    ];

    SolvedGraph graph;
    try {
      graph = await workspace.Solver.SolveGraph(requests, pinnedAll, workspace.Environment);
    }
    catch (DependencyResolutionException withInstalled) {
      List<Candidate> pinnedExplicit = [
        .. pinnedAll.Where(c => explicitSet.Contains(Fold(c.Package.Identity))),
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
    WorkspaceLayout.LayoutPlan? layout = null;
    if (changeSet.Changes.Any(c => c is not MarkExplicitChange)) {
      layout = _layout.Plan(workspace.Root);
      if (options.DryRun) {
        await _layout.ReportDryRun(layout);
      }
    }

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
      IReadOnlyList<string> errors = transaction.Rollback(() => {
        try {
          _lockStore.DiscardStaged(workspace.Root);
        }
        catch (Exception discard) when (discard is IOException or UnauthorizedAccessException) {
          // A staged workspace file is never read, and the next save replaces it.
        }
      });
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
  /// "Reinstalling"): with the tooth file the workspace file records, and the archive of the
  /// commit it records for a remote repository, otherwise the archive its source names now,
  /// opened when first needed. Fails when it is no candidate in the environment, e.g. when its
  /// tooth file has no entry for another --rid (protocol/workspace.md §3).
  private Candidate PinnedCandidate(Workspace workspace, LockedPackage package) {
    try {
      Package rebuilt = new(
          package.Tooth,
          package.Variant,
          SemVersion.Parse(package.Version, SemVersionStyles.Strict),
          McmodInstaller.LockedEntry(package, workspace.Environment)) {
        Manifest = package.Manifest,
      };
      Ref source = Ref.Parse(package.Source, allowFile: true);
      if (source.Kind == RefKind.Spec && package.Commit is null) {
        throw new FormatException("it records no commit for a package of a remote repository.");
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
          $"Cannot read the entry of '{package.Identity}' in the workspace file: {ex.Message}",
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

  /// The commit of a candidate from a remote repository (lowercase hex), or null.
  private static string? CommitOf(Candidate candidate) =>
      candidate.Source.Kind == RefKind.Spec ? candidate.Archive.Commit?.ToLowerInvariant() : null;

  private static bool SameIdentity(string a, string b) => PackageNames.AreEqual(a, b);

  private static string Fold(string identity) => PackageNames.Fold(identity);

  private sealed record Workspace(
      string Root,
      LockFile Lock,
      LipEnvironment Environment,
      IResolver Resolver,
      GraphSolver Solver);

  /// The new package set in installation order, and the folded identities of its explicitly
  /// installed members.
  private sealed record Plan(IReadOnlyList<Candidate> Members, HashSet<string> Explicit);
}
