using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Mcmod;

/// Options shared by the workspace operations. Rid overrides the environment rid (spec §5.1).
public record OperationOptions(
    bool DryRun = false,
    bool IgnoreScripts = false,
    bool NoDependencies = false,
    string? Rid = null);

/// One entry of the workspace lock, as listed to the user.
public record InstalledPackage(string Identity, string Version, bool Explicit, string Kind);

/// The workspace operations: install, uninstall, update and list (spec §6–§8).
///
/// Every operation loads the lock (migrating a v3 tooth_lock.json), determines the environment,
/// computes the new package set with the graph solver, then uninstalls what leaves the set (in
/// reverse install order) and installs what enters it (in install order), saving the lock after
/// every step. An update of a package is an uninstall of the old and an install of the new
/// version (spec §7).
///
/// Installed packages are pinned: they enter the solver as they are, rebuilt from the lock
/// without network access, and their archives are opened from the lock only when needed. If the
/// request cannot be satisfied with every installed package pinned, it is solved again with only
/// the explicitly installed packages pinned, so that dependencies may change version. Packages
/// that no explicitly installed package needs any more are removed.
///
/// Locks migrated from v3 have no archive identity: such packages are reopened by resolving
/// their tag again (archive key "path@version"). A v3 lock may hold two variants of one tooth
/// under the same identity; they are kept, listed and uninstalled as separate entries, while
/// the solver sees the identity once (spec §6.3 applies to new solves).
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
    Workspace workspace = await OpenWorkspace(options);
    List<(string Argument, PackageRequest Request)> requests = ParseRequests(packages);

    if (options.NoDependencies) {
      await InstallWithoutDependencies(workspace, requests, options);
      return;
    }

    List<string> identities = await RequestedIdentities(workspace, requests);
    foreach (string identity in identities) {
      if (workspace.Lock.Packages.Any(p => p.Explicit && SameIdentity(p.Identity, identity))) {
        throw new InvalidOperationException(
            $"Cannot install package '{identity}' because it is already explicitly installed.");
      }
    }

    await Apply(workspace, options, await MakePlan(
        workspace,
        [.. requests.Select(r => r.Request)],
        unpinned: identities,
        explicitIdentities: ExplicitIdentities(workspace.Lock)));
  }

  public async Task Uninstall(IReadOnlyList<string> packages, OperationOptions options) {
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

    // Removing some of several entries of one identity (two variants of one tooth from a v3
    // lock) leaves the identity installed, so the package set does not change: such entries
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

    if (direct.Count > 0) {
      List<LockedPackage> remaining = [.. workspace.Lock.Packages];
      foreach (LockedPackage target in Enumerable.Reverse(workspace.Lock.Packages)
          .Where(direct.Contains)) {
        await UninstallEntry(workspace, options, target, remaining);
      }

      await SaveFinal(workspace, options, remaining);
      targets = [.. targets.Except(direct)];
      if (targets.Count == 0) {
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

    await Apply(workspace, options, plan);
  }

  public async Task Update(IReadOnlyList<string> packages, OperationOptions options) {
    Workspace workspace = await OpenWorkspace(options);

    List<(string Argument, PackageRequest Request)> requests;
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
      }
    } else {
      requests = ParseRequests(packages);
    }

    List<string> identities = await RequestedIdentities(workspace, requests);
    for (int i = 0; i < identities.Count; i++) {
      if (!workspace.Lock.Packages.Any(p => SameIdentity(p.Identity, identities[i]))) {
        throw new InvalidOperationException(
            $"Cannot update package '{requests[i].Argument}' because it is not currently "
            + "installed.");
      }
    }

    await Apply(workspace, options, await MakePlan(
        workspace,
        [.. requests.Select(r => r.Request)],
        unpinned: identities,
        explicitIdentities: ExplicitIdentities(workspace.Lock)));
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
    return new Workspace(
        root,
        lockFile,
        environment,
        resolver,
        _createSolver(resolver),
        new LayoutState());
  }

  /// Makes the workspace layout right (spec §7) before the first package of an operation is
  /// installed or uninstalled, so that a failure while resolving or solving changes nothing. In
  /// a dry run, reports what that would do.
  private async Task PrepareLayout(Workspace workspace, OperationOptions options) {
    if (workspace.Layout.Prepared) {
      return;
    }

    await _layout.Prepare(workspace.Root, options.DryRun);
    workspace.Layout.Prepared = true;
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

    if (lockFile.Rid == "") {
      throw new LockFormatException("The lock records an empty rid.");
    }

    return new LipEnvironment(
        McmodPlatform.Server,
        explicitRid ?? lockFile.Rid ?? RuntimeInformation.RuntimeIdentifier);
  }

  private void Warn(string message) => _ui.PrintWarning(message).GetAwaiter().GetResult();

  /// Parses package arguments; local paths are relative to the workspace root, which is the
  /// current directory of lip.
  private List<(string, PackageRequest)> ParseRequests(IReadOnlyList<string> packages) {
    string root = _fs.Path.GetFullPath(_workspaceRoot());
    return [.. packages.Select(p => (p, PackageArguments.Parse(p, root, _fs)))];
  }

  /// The identity of the first candidate of every request: the package the request names. Fails
  /// when a request has no candidate or two requests name the same package.
  private async Task<List<string>> RequestedIdentities(
      Workspace workspace, List<(string Argument, PackageRequest Request)> requests) {
    List<string> identities = [];
    foreach ((string argument, PackageRequest request) in requests) {
      Candidate candidate = await FirstCandidate(workspace, argument, request);
      string identity = candidate.Package.Identity;
      if (identities.Any(i => SameIdentity(i, identity))) {
        throw new InvalidOperationException(
            $"Multiple requested packages are '{identity}'. Please request each package once.");
      }

      identities.Add(identity);
    }

    return identities;
  }

  private async Task<Candidate> FirstCandidate(
      Workspace workspace, string argument, PackageRequest request) {
    await _ui.PrintInfo($"Resolving {argument}...");
    List<string> diagnostics = [];
    await foreach (Candidate candidate in workspace.Resolver.ResolveLazy(
        request.Ref, request.Base, request.Range, workspace.Environment,
        onDiagnostic: diagnostics.Add)) {
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

  private async Task Apply(Workspace workspace, OperationOptions options, Plan plan) {
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

    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    foreach (LockedPackage package in Enumerable.Reverse(workspace.Lock.Packages)) {
      if (!kept.Contains(Fold(package.Identity))) {
        await UninstallEntry(workspace, options, package, remaining);
      }
    }

    foreach (Candidate candidate in plan.Members) {
      string key = Fold(candidate.Package.Identity);
      if (!kept.Contains(key)) {
        await InstallCandidate(
            workspace, options, candidate, plan.Explicit.Contains(key), remaining);
      }
    }

    // Lock order is install order: frozen entries first (their dependencies are unknown), then
    // the members of the new set.
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

    await SaveFinal(workspace, options, final);
  }

  private async Task InstallWithoutDependencies(
      Workspace workspace,
      List<(string Argument, PackageRequest Request)> requests,
      OperationOptions options) {
    await _ui.PrintWarning(
        "--no-dependencies is enabled. This operation may result in a broken workspace.");

    List<Candidate> candidates = [];
    foreach ((string argument, PackageRequest request) in requests) {
      Candidate candidate = await FirstCandidate(workspace, argument, request);
      if (candidates.Any(c => SameIdentity(c.Package.Identity, candidate.Package.Identity))) {
        throw new InvalidOperationException(
            $"Multiple requested packages are '{candidate.Package.Identity}'. Please request "
            + "each package once.");
      }

      candidates.Add(candidate);
    }

    List<LockedPackage> remaining = [.. workspace.Lock.Packages];
    foreach (Candidate candidate in candidates) {
      List<LockedPackage> existing = [
        .. remaining.Where(p => SameIdentity(p.Identity, candidate.Package.Identity)),
      ];
      if (existing.Count > 0 && existing.All(p => IsSamePackage(p, candidate))) {
        int index = remaining.IndexOf(existing[0]);
        remaining[index] = existing[0] with { Explicit = true };
        continue;
      }

      foreach (LockedPackage package in Enumerable.Reverse(existing)) {
        await UninstallEntry(workspace, options, package, remaining);
      }

      await InstallCandidate(workspace, options, candidate, isExplicit: true, remaining);
    }

    await SaveFinal(workspace, options, remaining);
  }

  private async Task UninstallEntry(
      Workspace workspace,
      OperationOptions options,
      LockedPackage package,
      List<LockedPackage> remaining) {
    await PrepareLayout(workspace, options);
    await _ui.PrintInfo((options.DryRun ? "Dry run: would uninstall" : "Uninstalling")
        + $" package '{package.Identity}' version {package.Version}");

    await Installer(package.Kind).Uninstall(package, Context(workspace, options, remaining));
    remaining.Remove(package);
    await SaveProgress(workspace, options, remaining);
  }

  private async Task InstallCandidate(
      Workspace workspace,
      OperationOptions options,
      Candidate candidate,
      bool isExplicit,
      List<LockedPackage> remaining) {
    await PrepareLayout(workspace, options);
    await _ui.PrintInfo((options.DryRun ? "Dry run: would install" : "Installing")
        + $" package '{candidate.Package.Identity}' version {candidate.Package.Version}"
        + $" from {candidate.Archive.Key}");

    IReadOnlyList<string> files = await Installer(candidate.Package.Kind).Install(
        candidate, Context(workspace, options, remaining));
    remaining.Add(new LockedPackage {
      Identity = candidate.Package.Identity,
      Version = candidate.Package.Version.ToString(),
      Kind = candidate.Package.Kind,
      Explicit = isExplicit,
      Chain = [
        .. candidate.Chain.Select(h => new LockedHop(h.Ref.ToString(), h.ArtifactIndex) {
          Commit = h.Commit,
        }),
      ],
      ArchiveKey = candidate.Archive.Key,
      Archive = candidate.Archive.Identity,
      Files = [.. files],
      Manifest = candidate.Package.Manifest,
    });
    await SaveProgress(workspace, options, remaining);
  }

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

  /// Saves the lock after every step, so that it reflects the workspace if a later step fails.
  private async Task SaveProgress(
      Workspace workspace, OperationOptions options, List<LockedPackage> remaining) {
    if (!options.DryRun) {
      await _lockStore.Save(workspace.Root, LockWith(workspace, remaining));
    }
  }

  private async Task SaveFinal(
      Workspace workspace, OperationOptions options, List<LockedPackage> packages) {
    if (!options.DryRun) {
      await _lockStore.Save(workspace.Root, LockWith(workspace, packages));
    }
  }

  /// The lock with the given packages and the environment, once known (spec §8).
  private static LockFile LockWith(Workspace workspace, List<LockedPackage> packages) {
    return workspace.Lock with {
      Platform = workspace.Environment.Platform.ToSpecString(),
      Rid = workspace.Environment.Rid,
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

      // Migrated v3 entries have no identity. Tooth packages have no relative refs, so it is
      // never used as a base; it is never written to the lock either.
      ArchiveIdentity identity = package.Archive ?? new GitArchiveIdentity(
          ArchiveProvider.GetRepoUrl(package.Identity), "", "");
      List<ChainHop> chain = [
        .. package.Chain.Select(h => new ChainHop(
            Ref.Parse(h.Ref, allowFile: true), h.Artifact, h.Commit)),
      ];
      ArchiveHandle archive = new(
          package.ArchiveKey,
          new DeferredSource(async () => {
            Sources.ISource source = await OpenLockedArchive(workspace, package);
            // The archive of a converted tooth.json mounts its assets (spec §9); resolving the
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
    return _documentParser.Parse(manifest) is ConcreteDocument document
        ? new McmodPackage(document, package.Manifest)
        : throw new FormatException("the locked manifest is not a concrete document.");
  }

  /// Opens the archive recorded in the lock and verifies its identity (spec §8). Two cases
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
      IGraphSolver Solver,
      LayoutState Layout);

  /// Whether the layout was prepared during the operation; shared by copies of the workspace.
  private sealed class LayoutState {
    public bool Prepared { get; set; }
  }

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
