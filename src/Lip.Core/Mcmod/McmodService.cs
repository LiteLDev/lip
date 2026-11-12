using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Mcmod;

/// Options shared by the workspace operations. Platform overrides the platform of the environment
/// (protocol/lip.md, "Environment"). Cancelling the token stops an operation; one that has started
/// changing the workspace undoes its changes first.
public record OperationOptions(
    bool DryRun = false,
    string? Platform = null,
    CancellationToken CancellationToken = default);

/// One package of the state file, as listed to the user.
public record InstalledPackage(string Identity, string Version, bool Explicit);

/// The workspace operations: install, uninstall, update and list (protocol/lip-workspace.md,
/// protocol/lip.md).
///
/// Every operation loads the state file (or the one protocol/lip-compatibility.md,
/// "tooth_lock.json" derives from a tooth_lock.json), determines the environment, computes the new
/// package set with the graph solver, then uninstalls what leaves the set (in reverse installation
/// order) and installs what enters it (in installation order). Updating a package uninstalls the
/// old package and installs the new one.
///
/// Operations are all or nothing (protocol/lip-workspace.md, "Atomicity"). An operation first plans
/// every uninstall and install against a view of the workspace as the earlier steps leave it,
/// checking everything and fetching every archive and asset; a failure then changes nothing. It
/// then applies the plans through one WorkspaceTransaction, journaled next to the state file in
/// .lip, and writes the state file last; a failure (or cancellation) undoes every change and leaves
/// the state file as it was. An operation other than a dry run holds the workspace's WorkspaceMutex
/// throughout, and first finishes or undoes what an interrupted operation left in the journal
/// (protocol/lip-workspace.md, "Carrying out an operation").
///
/// Installed packages are pinned: they enter the solver as they are, rebuilt from the workspace
/// file (with the package file it records) without network access, and their archives (the commit
/// it records for a remote repository, otherwise what their source names now) and assets are
/// opened only when needed. A candidate the solver resolves with the identity, version and commit
/// of an installed package is that installed package, which is not replaced. If the request
/// cannot be satisfied with every installed package pinned, it is solved again with only the
/// explicitly installed packages pinned, so that
/// dependencies may change version; their installed versions are tried first. An update solves
/// so from the start, so that the dependency packages give way to the newest versions of the
/// requested packages. Packages that no explicitly installed package needs any more are removed.
public class McmodService(
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    LockStore lockStore,
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    McmodInstaller installer,
    Func<string>? workspaceRoot = null) {
  private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

  /// The state file as the user sees it from the workspace root.
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
    // (protocol/lip-install.md), recording the source of the request when it is one of the
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
      if (matched.FirstOrDefault(p => !p.Explicit) is { } dependency) {
        throw new InvalidOperationException(
            $"Package '{dependency.Identity}' was installed as a dependency of other packages, not"
            + " requested by you, so lip uninstalls it by itself once no package needs it.");
      }

      targets.AddRange(matched.Where(p => !targets.Contains(p)));
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
    // when another request gives a package of their identity (protocol/lip-update.md).
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

    // A URL is downloaded again (protocol/lip-workspace.md, "Operations" "Updating").
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
    // another identity, which replaces them (protocol/lip-workspace.md, "Operations" "Updating").
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
      // requests giving an identity (protocol/lip-workspace.md, "Valid states").
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
        ],
        // A requested package gets its earliest candidate that Section 3 allows, so the
        // installed dependency packages keep their versions only where they fit it
        // (protocol/lip-workspace.md, "Operations" "Updating", protocol/lip-update.md).
        preferDependencyPackages: true);

    // The source a requested package is recorded with: an installed package that its recorded
    // source gives again is not replaced, so it keeps that source even when another request
    // gives the same package (protocol/lip-workspace.md, "Operations"); but a request with a ref
    // that gives it wins, since only its own candidates satisfy it (protocol/lip-workspace.md,
    // "Valid states").
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
          // The planned package keeps its package file, which may be the installed one's.
          members[member] = members[member] with { Source = candidate.Source };
        }
      }
    }

    await Execute(workspace, options, await Changes(
        workspace, options, plan with { Members = members }));
  }

  /// The packages of the state file (protocol/lip-list.md). The state file derived from a
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

  /// lip run (protocol/lip-run.md): takes the workspace lock, recovers an interrupted operation,
  /// releases the lock, then runs the server, bedrock_server_mod.exe at the workspace root, for the
  /// environment of protocol/lip.md, "Environment" (no --platform), and returns its exit code.
  /// The server reads the bytes read from input, and the line "stop" when lip is first asked to
  /// stop (listen); inputIsConsole when input is an interactive Windows console. Throws when a
  /// step before the server starts fails.
  public async Task<int> Run(
      Stream input, bool inputIsConsole, StopListener listen, IProgramStarter starter) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    LockFile? lockFile;
    using (await BeginOperation(new OperationOptions())) {
      // Read once a recovered operation is complete, so that it records the platform it gave.
      lockFile = await _lockStore.Load(root);
    }

    LipEnvironment environment = DetermineEnvironment(lockFile, null);
    string server = _fs.Path.Join(root, WorkspaceProgram.ServerFileName);
    if (!_fs.File.Exists(server)) {
      throw new FileNotFoundException(
          $"There is no server to run: '{server}' is not a file."
          + (WorkspaceProgram.IsWindowsPlatform(environment.Platform)
              ? " Install the server first, e.g. with 'lip install github.com/LiteLDev/LeviLamina'."
              : $" Install a package that provides it for the platform {environment.Platform},"
                + " or start the server of the workspace yourself."),
          server);
    }

    ProgramHost host = ProgramHost.Current(environment.Platform, root);
    bool throughWine = WorkspaceProgram.RunsThroughWine(host);
    string program = throughWine ? "wine" : server;
    try {
      ProgramStartInfo start = WorkspaceProgram.Server(host);
      return await ServerSession.Run(starter, start, input, inputIsConsole, listen);
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
        or InvalidOperationException) {
      throw new InvalidOperationException(
          $"Could not start the server: lip could not run {program} in '{root}' for the"
          + $" platform {environment.Platform}"
          + (throughWine
              ? ". Install Wine, so that 'wine' is found through PATH, or install the server for"
                + " the platform lip runs on in a new workspace, with 'lip install"
                + " <PACKAGES>... --platform <RID>'."
              : "."),
          ex);
    }
  }

  /// The result of lip pkg view (protocol/lip-pkg.md, "lip pkg view"), as JSON: "manifest", the
  /// package file of the first archive a package argument names (for a remote repository, its
  /// newest version tag, or the commit its ref selects; otherwise the archive of the URL or local
  /// path), and "versions", the versions of the version tags of a remote repository, lowest first,
  /// whatever its variant and ref. For an archive without a package file, the package file
  /// protocol/lip-compatibility.md, "tooth.json" derives from its tooth.json for the side server
  /// and the platform the state file records, or win-x64 without a state file.
  public async Task<string> View(string package) {
    PackageRequest request = PackageArguments.Parse(
        package, _fs.Path.GetFullPath(_workspaceRoot()), _fs);

    ArchiveHandle archive;
    // The tag version of the archive, the version of a package read from a tooth.json.
    SemVersion? tagVersion = null;
    IReadOnlyList<SpecVersion> versions = [];
    if (request.Ref.Kind == RefKind.Spec) {
      RunTrace.Write("resolve", $"Listing the versions of {request.Ref.Target}");
      versions = await _archiveProvider.ListVersions(request.Ref.Target);
    }

    if (request.Ref is { Kind: RefKind.Spec, GitRef: GitRef gitRef }) {
      archive = await _archiveProvider.OpenCommit(
          request.Ref.Target, await _archiveProvider.ResolveGitRef(request.Ref.Target, gitRef));
      tagVersion = gitRef.Kind switch {
        GitRefKind.Version => SemVersion.Parse(gitRef.Name, SemVersionStyles.Strict),
        GitRefKind.Tag => ArchiveProvider.ParseVersionTag($"refs/tags/{gitRef.Name}"),
        _ => null,
      };
    } else if (request.Ref.Kind == RefKind.Spec) {
      SpecVersion version = versions.FirstOrDefault()
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
    JsonObject result = new() {
      ["manifest"] = JsonSerializer.SerializeToNode(
          request.Ref.Kind == RefKind.Spec && BedrockServer.IsRepository(request.Ref.Target)
              // lip defines the packages of the server itself.
              ? (await new Resolver(_archiveProvider, _archiveReader)
                  .ServerDocument(request.Ref.Target, tagVersion)).Json
              : await ManifestJson(archive, tagVersion)),
      // ListVersions gives the newest first.
      ["versions"] = new JsonArray([
        .. versions.Reverse().Select(v => v.Version.ToString()).Distinct()
            .Select(v => (JsonNode?)JsonValue.Create(v)),
      ]),
    };
    return result.ToJsonString(_indented);
  }

  /// The package file of an archive, as lip pkg view shows it: read for the side server and the
  /// platform the state file records, or DefaultPlatform when there is no state file. Only a
  /// package file derived from a tooth.json depends on them, so the state file is read only for
  /// one; a state file that cannot be read fails the command, as it fails a workspace operation.
  private async Task<JsonElement> ManifestJson(ArchiveHandle archive, SemVersion? tagVersion) {
    PackageDocument document = await _archiveReader.Read(archive);
    if (!document.IsLegacy) {
      return document.Json;
    }

    string? platform =
        (await _lockStore.Load(_fs.Path.GetFullPath(_workspaceRoot())))?.Platform;
    LipEnvironment environment = new(McmodSide.Server, platform ?? DefaultPlatform);
    return document.ReadFor(environment, tagVersion).Json;
  }

  #region Planning

  private async Task<Workspace> OpenWorkspace(OperationOptions options) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    LockFile? recorded = await _lockStore.Load(root);
    LipEnvironment environment = DetermineEnvironment(recorded, options.Platform);
    recorded ??= await ReadToothLock(root, environment, resolveCommits: true);
    RunTrace.Write("workspace", $"Workspace {root}: "
        + (recorded is null
            ? "no state file yet"
            : $"{recorded.Packages.Count} installed packages, platform {recorded.Platform}")
        + $"; using the environment ({environment.Side.ToSpecString()},"
        + $" {environment.Platform})" + (options.Platform is null ? "" : " from --platform"));
    // A workspace without a state file gets the environment of its first operation.
    LockFile lockFile = recorded ?? new LockFile {
      Side = environment.Side.ToSpecString(),
      Platform = environment.Platform,
      Packages = [],
    };
    Resolver resolver = new(_archiveProvider, _archiveReader, Warn);
    foreach (LockedPackage package in lockFile.Packages) {
      if (package.Commit is string commit
          && Ref.TryParse(package.Source, out Ref? source, out _)) {
        resolver.PinCommit(source, commit);
      }
    }

    Workspace? workspace = null;
    workspace = new Workspace(root, lockFile, environment, resolver, new GraphSolver(
        new InstalledPackageResolver(resolver, c => AsInstalled(workspace!, c))));
    return workspace;
  }

  /// The installed package with the identity, version and commit of a candidate, with the
  /// source of the candidate; otherwise the candidate. Such a package is not replaced
  /// (protocol/lip-workspace.md, "Operations"), so it is planned with the package file the state
  /// file records, whatever the package file of the candidate says.
  private Candidate AsInstalled(Workspace workspace, Candidate candidate) =>
      workspace.Lock.Packages.FirstOrDefault(p => IsSamePackage(p, candidate)) is { } installed
          ? PinnedCandidate(workspace, installed) with { Source = candidate.Source }
          : candidate;

  /// A resolver whose candidates are mapped, for the graph solver.
  private sealed class InstalledPackageResolver(IResolver resolver, Func<Candidate, Candidate> map)
      : ICausalResolver {
    public async IAsyncEnumerable<Candidate> ResolveLazy(
        Ref source,
        IVersionConstraint? range,
        LipEnvironment environment,
        Action<string>? onDiagnostic = null) {
      await foreach (Candidate candidate in resolver.ResolveLazy(
          source, range, environment, onDiagnostic)) {
        yield return map(candidate);
      }
    }

    // The exceptions behind the diagnostics of the resolver it wraps, when it hands them over.
    public async IAsyncEnumerable<Candidate> ResolveLazyWithCauses(
        Ref source,
        IVersionConstraint? range,
        LipEnvironment environment,
        Action<ResolveDiagnostic> onDiagnostic) {
      IAsyncEnumerable<Candidate> candidates = resolver is ICausalResolver withCauses
          ? withCauses.ResolveLazyWithCauses(source, range, environment, onDiagnostic)
          : resolver.ResolveLazy(
              source, range, environment, d => onDiagnostic(new ResolveDiagnostic(d, null)));
      await foreach (Candidate candidate in candidates) {
        yield return map(candidate);
      }
    }
  }

  /// The state file protocol/lip-compatibility.md, "tooth_lock.json" derives from the
  /// tooth_lock.json of a workspace without a state file, for its environment; null when it has
  /// none that Section 3 reads. With resolveCommits, the commit of each package is that of the tag
  /// "v&lt;version&gt;" of its remote repository, and it fails when that cannot be obtained.
  private async Task<LockFile?> ReadToothLock(
      string root, LipEnvironment environment, bool resolveCommits) {
    if (ToothLockMigration.Read(_fs, root) is not { } members) {
      return null;
    }

    if (resolveCommits) {
      await _ui.PrintInfo(
          $"Migrating {ToothLockMigration.FileName} of lip v0.x to the state file.");
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
      // A dry run changes no file, the cache included (protocol/lip.md, "Dry run").
      _archiveProvider.LeaveCacheUnchanged();
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
        + $" {result.Changes} changes. The workspace and its state file are as they were"
        + " before it.");
  }

  /// The platform of the environment when neither --platform nor the state file gives one,
  /// whatever system lip runs on (protocol/lip.md, "Environment").
  public const string DefaultPlatform = "win-x64";

  /// The environment of the workspace (protocol/lip.md, "Environment"). lip manages server
  /// workspaces only, so the side is always server. The platform is --platform, then the state
  /// file, then DefaultPlatform.
  private static LipEnvironment DetermineEnvironment(
      LockFile? lockFile,
      string? explicitPlatform) {
    string server = McmodSide.Server.ToSpecString();
    if (lockFile?.Side is { } side && side != server) {
      throw new InvalidOperationException(
          $"The state file {WorkspaceFile} records the side \"{side}\" in its"
          + $" \"side\" field, but lip manages server workspaces only (\"{server}\")."
          + " Client workspaces are managed by launchers such as LeviLauncher.");
    }

    return new LipEnvironment(
        McmodSide.Server,
        explicitPlatform ?? lockFile?.Platform ?? DefaultPlatform);
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
  /// (protocol/lip-install.md).
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
        + $" ({workspace.Environment.Side.ToSpecString()}, {workspace.Environment.Platform}).";
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
  /// the installed versions of the dependency packages first. With preferDependencyPackages,
  /// the dependency packages are only tried first from the start, so that they give way to the
  /// requested packages' earlier candidates.
  private async Task<Plan> MakePlan(
      Workspace workspace,
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyCollection<string> unpinned,
      IReadOnlyCollection<string> explicitIdentities,
      IReadOnlyDictionary<string, Candidate>? pinnedAs = null,
      bool preferDependencyPackages = false) {
    HashSet<string> unpinnedSet = [.. unpinned.Select(Fold)];
    HashSet<string> explicitSet = [.. explicitIdentities.Select(Fold)];

    List<Candidate> pinnedAll = [
      .. workspace.Lock.Packages
          .Where(p => !unpinnedSet.Contains(Fold(p.Identity)))
          .Select(p => pinnedAs?.GetValueOrDefault(Fold(p.Identity))
              ?? PinnedCandidate(workspace, p)),
    ];
    List<Candidate> pinnedExplicit = [];
    List<Candidate> preferred = [];
    foreach (Candidate candidate in pinnedAll) {
      (explicitSet.Contains(Fold(candidate.Package.Identity)) ? pinnedExplicit : preferred)
          .Add(candidate);
    }

    SolvedGraph graph;
    try {
      graph = preferDependencyPackages
          ? await workspace.Solver.SolveGraph(
              requests, pinnedExplicit, workspace.Environment, preferred)
          : await workspace.Solver.SolveGraph(requests, pinnedAll, workspace.Environment);
    }
    catch (DependencyResolutionException withInstalled) {
      if (preferDependencyPackages || preferred.Count == 0) {
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

  /// What an operation changes, in order, and how the state file entries left at the end are
  /// ordered and marked.
  private sealed record ChangeSet(
      List<Change> Changes, Func<List<LockedPackage>, Task<List<LockedPackage>>> Finish);

  private abstract record Change;

  private sealed record UninstallChange(LockedPackage Package) : Change;

  private sealed record InstallChange(Candidate Candidate, bool Explicit) : Change;

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
      // The state file lists the members of the new set in installation order.
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

  /// Plans every change against a view of the workspace as the earlier changes leave it, so
  /// that every check and every fetch happens before anything changes; then applies the plans
  /// in one transaction and writes the state file last. A failure while applying (or
  /// cancellation) undoes every change and leaves the state file as it was
  /// (protocol/lip-workspace.md, "Atomicity"). In a dry run, reports the plans instead.
  private async Task Execute(Workspace workspace, OperationOptions options, ChangeSet changeSet) {
    // The changes are planned against the layout an operation that changes the workspace makes
    // first; it is checked and made only when the operation may change the workspace
    // (protocol/lip-workspace.md, "Environment and layout"). Paths are compared as the
    // environment makes them.
    WorkspacePaths paths = WorkspacePaths.For(workspace.Environment);
    WorkspaceLayout.LayoutPlan layout = _layout.Plan(workspace.Root, paths);
    WorkspaceView view = new(_fs, layout, paths);
    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    List<(string Message, PackagePlan Plan)> plans = [];
    HashSet<PackagePlan> installs = new(ReferenceEqualityComparer.Instance);
    foreach (Change change in changeSet.Changes) {
      options.CancellationToken.ThrowIfCancellationRequested();
      RunTrace.Write("workspace", "Planning " + change switch {
        UninstallChange(LockedPackage p) => $"to uninstall {p.Identity}@{p.Version}",
        InstallChange(Candidate c, bool e) => $"to install {c.Package.Identity}"
            + $"@{c.Package.Version} from {c.Archive.Key}{(e ? " (explicit)" : "")}",
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
        default:
          throw new ArgumentException(
              $"Internal error: lip planned an unknown kind of change ({change}). This is a bug"
              + " in lip.", nameof(changeSet));
      }
    }

    bool hooksRun = plans.Any(p => p.Plan.RunsHooks);
    bool changesWorkspace = plans.Any(p => p.Plan.MayChange(hooksRun));
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
        _fs, JournalPath(workspace.Root), plans.SelectMany(p => p.Plan.WrittenPaths), paths);
    try {
      if (changesWorkspace) {
        _layout.Apply(layout, transaction);
      }

      // The files each package installed owns as its writes happened: hook commands may have
      // changed which files matching preserve were kept (protocol/lip-workspace.md, "Installing and
      // uninstalling"). Each write is checked against the owners as they are then.
      FileOwners owners = _installer.Owners(
          workspace.Root, workspace.Lock.Packages, workspace.Environment);
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
      // The commit point: the journal records the commit, then the state file moves into
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
          // A staged state file is never read, and the next save replaces it.
          RunTrace.Write("workspace", $"Could not delete the staged state file: {discard}");
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
        options.DryRun);
  }

  /// The state file with the given packages and the environment, once known.
  private static LockFile LockWith(Workspace workspace, List<LockedPackage> packages) {
    return workspace.Lock with {
      Side = workspace.Environment.Side.ToSpecString(),
      Platform = workspace.Environment.Platform,
      Packages = [.. packages],
    };
  }

  #endregion

  #region Installed packages

  /// Rebuilds an installed package from the state file (protocol/lip-workspace.md, "Operations",
  /// "Reinstalling"): with the package file the state file records, and the archive of the
  /// commit it records for a remote repository, otherwise the archive its source names now,
  /// opened when first needed. Fails when it is no candidate in the environment, e.g. when its
  /// package file has no entry for another --platform (protocol/lip-workspace.md, "Valid states").
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
          $"Cannot use the installed package '{package.Identity}' as the state file"
          + $" {WorkspaceFile} records it, in the environment"
          + $" ({workspace.Environment.Side.ToSpecString()}, {workspace.Environment.Platform})."
          // LockedEntry throws McmodInstallException when the package has no entry for the
          // environment; a LockFormatException already says what to do.
          + (ex is McmodInstallException
              && workspace.Environment.Platform != workspace.Lock.Platform
              ? $" The workspace was installed for the platform {workspace.Lock.Platform}; run"
                  + " lip without --platform, or with that platform."
              : ex is LockFormatException
                  ? ""
                  : " If the file was edited by hand, undo the edit."),
          ex);
    }
  }

  /// The source an entry of the state file records.
  private static Ref ParseRecordedSource(LockedPackage package) {
    try {
      return Ref.Parse(package.Source, allowFile: true);
    }
    catch (FormatException ex) {
      throw new LockFormatException(
          $"The state file {WorkspaceFile} records \"{package.Source}\" in the \"ref\" field"
          + $" of package '{package.Identity}', which is not a valid package source. If the file"
          + " was edited by hand, undo the edit.",
          ex);
    }
  }

  /// Matches an uninstall argument (protocol/lip-uninstall.md): an identity (ignoring ASCII case),
  /// or else the source, with the locations compared ignoring ASCII case; an argument without a
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

  /// Whether a candidate is the installed package (protocol/lip-workspace.md, "Operations"): same
  /// identity, version and commit.
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
