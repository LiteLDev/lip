using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Install;

/// Thrown when installing or uninstalling a package fails (protocol/workspace.md §4). Failures
/// detected while planning leave the workspace untouched.
public class McmodInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Installs and uninstalls packages (protocol/workspace.md §4).
///
/// Installing writes the files of the assets of the entry, in order (step 2): each asset is
/// mounted at its index; "self" and "zip" assets carry out their placements, "mcmod" assets are
/// written whole into the mod directory, and the assets of an entry derived from format 3 are
/// carried out as lip v0.37.0 did (ToothPlacements). The mod manifest of a mod then gets its
/// "dependencies" checked or written (step 3, ModManifestFile).
///
/// Ownership: a file is owned by a package when the package wrote it. A file that already exists
/// and matches preserve_files is kept and not written, so it is not owned by the package, unless
/// the lock already records it as owned by a package of the same identity (reinstall or update
/// while the old entry is still in the lock), in which case ownership carries over.
///
/// Paths are recorded and compared in their canonical form (WorkspaceLayout.CanonicalPath): a
/// file placed under plugins/ is written and owned under mods/. Globs match both forms. The mods
/// and plugins directories are never deleted, and remove_files never matches them (so a
/// directory pattern naming them removes nothing). .lip, where lip keeps the lock, is never
/// written, deleted or matched.
public partial class McmodInstaller(
    IFileSystem fileSystem,
    ICommandRunner commandRunner,
    IUserInteraction userInteraction) : IMcmodInstaller {
  private static readonly JsonSerializerOptions _manifestOptions = new() {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  private readonly IFileSystem _fs = fileSystem;
  private readonly ICommandRunner _commandRunner = commandRunner;
  private readonly IUserInteraction _ui = userInteraction;

  /// How workspace paths are compared: ignoring case on Windows, ordinal elsewhere.
  public static StringComparer PathComparer => WorkspaceLayout.PathComparer;

  private static readonly StringComparison PathComparison =
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

  /// Installs the package on its own: plans it, then applies the plan in a transaction of its
  /// own. Returns the files the package owns.
  public async Task<IReadOnlyList<string>> Install(Candidate candidate, InstallContext context) {
    PackagePlan plan = await PlanInstall(candidate, context, new WorkspaceView(_fs));
    if (!context.DryRun) {
      await ApplyAlone(plan);
    }

    return plan.OwnedFiles;
  }

  /// Uninstalls the package on its own, like Install.
  public async Task Uninstall(LockedPackage package, InstallContext context) {
    PackagePlan plan = await PlanUninstall(package, context, new WorkspaceView(_fs));
    if (!context.DryRun) {
      await ApplyAlone(plan);
    }
  }

  private async Task ApplyAlone(PackagePlan plan) {
    using WorkspaceTransaction transaction = new(_fs, reserved: plan.WrittenPaths);
    try {
      await Apply(plan, transaction, CancellationToken.None);
    }
    catch {
      foreach (string error in transaction.Rollback()) {
        await _ui.PrintWarning($"Rollback: {error}");
      }

      throw;
    }

    transaction.Commit();
    foreach (string error in transaction.Complete()) {
      await _ui.PrintWarning(error);
    }
  }

  public async Task<PackagePlan> PlanInstall(
      Candidate candidate, InstallContext context, WorkspaceView view) {
    Package package = candidate.Package;
    ToothEntry entry = package.Entry;
    string name = package.Identity;
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);

    // Every asset is obtained before anything is written or run.
    List<(string Mount, ISource Source)> mounts = [];
    for (int i = 0; i < candidate.Assets.Count; i++) {
      try {
        mounts.Add((Mount(i), await candidate.Assets[i].Open()));
      }
      catch (ArchiveUnavailableException ex) {
        throw new McmodInstallException(
            $"{name}: cannot obtain asset {i}: {ex.Message}", ex);
      }
    }

    MountSource source = new(mounts);
    List<PlannedWrite> plan = await Plan(candidate, source, root, context, view);
    List<PlannedWrite> writes = [.. plan.Where(w => !w.Keep)];
    foreach (PlannedWrite write in writes) {
      view.AddFile(write.FullPath);
    }

    if (context.DryRun) {
      foreach (PlannedWrite write in plan) {
        await _ui.PrintInfo(write.Keep
            ? $"Would keep {write.Target} (preserved)"
            : $"Would write {write.Target}");
      }
    } else {
      // Fetch content now, so that a fetch failure changes nothing.
      try {
        await source.Prefetch(
            writes.Where(w => w.Source == source).Select(w => w.SourceKey).Distinct());
      }
      catch (Exception ex) {
        throw new McmodInstallException(
            $"{name}: cannot fetch the archive content: {ex.Message}", ex);
      }
    }

    // pre_install, the writes, post_install.
    List<PlannedStep> steps = [];
    if (!context.IgnoreScripts && entry.Hooks.PreInstall.Count > 0) {
      steps.Add(new ScriptStep("pre_install", entry.Hooks.PreInstall));
    }

    steps.AddRange(writes.Select(w => new WriteStep(w.Target, w.FullPath, w.Source, w.SourceKey)));
    if (!context.IgnoreScripts && entry.Hooks.PostInstall.Count > 0) {
      steps.Add(new ScriptStep("post_install", entry.Hooks.PostInstall));
    }

    return new PackagePlan(name, root, OwnedFiles(plan), steps);
  }

  public async Task<PackagePlan> PlanUninstall(
      LockedPackage package, InstallContext context, WorkspaceView view) {
    ToothEntry entry = LockedEntry(package);
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);

    // The paths to delete are those of the workspace before pre_uninstall runs.
    HashSet<string> ownedByOthers = new(
        context.Lock.Packages
            .Where(p => !PackageNames.AreEqual(p.Identity, package.Identity))
            .SelectMany(p => p.Files)
            .Select(NormalizeRelative),
        PathComparer);

    // 2. Owned files not matching preserve_files.
    List<string> files = [];
    HashSet<string> fileSet = new(PathComparer);
    foreach (string file in package.Files.Select(NormalizeRelative)) {
      if (!IsSafeRelativePath(file) || WorkspaceLayout.IsLipPath(file)
          || ownedByOthers.Contains(file)) {
        continue;
      }
      if (MatchesAny(entry.PreserveFiles, file)) {
        continue;
      }
      if (view.FileExists(ToFullPath(root, file)) && fileSet.Add(file)) {
        files.Add(file);
      }
    }

    // 3. Paths matching remove_files that no other package owns.
    List<string> directories = [];
    HashSet<string> directorySet = new(PathComparer);
    foreach (PathGlob glob in entry.RemoveFiles) {
      foreach ((string path, bool isDirectory) in EnumerateMatches(root, glob, view)) {
        if (isDirectory) {
          if (!WorkspaceLayout.IsLayoutDirectory(path) && directorySet.Add(path)) {
            directories.Add(path);
          }
        } else if (!ownedByOthers.Contains(path) && fileSet.Add(path)) {
          files.Add(path);
        }
      }
    }

    if (context.DryRun) {
      foreach (string file in files) {
        await _ui.PrintInfo($"Would delete {file}");
      }
      foreach (string directory in directories) {
        await _ui.PrintInfo($"Would delete {directory}/ (if empty after deletion)");
      }
    }

    List<PlannedStep> steps = [];
    if (!context.IgnoreScripts && entry.Hooks.PreUninstall.Count > 0) {
      steps.Add(new ScriptStep("pre_uninstall", entry.Hooks.PreUninstall));
    }

    foreach (string file in files) {
      string full = ToFullPath(root, file);
      view.DeleteFile(full);
      steps.Add(new DeleteFileStep(full));
    }

    // Directory patterns delete whole directories, except what other packages own. Deepest
    // first, so that a matched directory containing matched directories empties bottom-up.
    foreach (string directory in directories.OrderByDescending(d => d.Count(c => c == '/'))) {
      string full = ToFullPath(root, directory);
      if (view.DirectoryExists(full) && view.IsEmpty(full)) {
        view.DeleteDirectory(full);
        steps.Add(new DeleteDirectoryStep(full));
      }
    }

    // 4. Directories that became empty because of the deletions above.
    foreach (string path in files.Concat(directories)) {
      for (string? dir = _fs.Path.GetDirectoryName(ToFullPath(root, path));
          dir is not null && IsInside(root, dir);
          dir = _fs.Path.GetDirectoryName(dir)) {
        string relative = _fs.Path.GetRelativePath(root, dir)
            .Replace(_fs.Path.DirectorySeparatorChar, '/');
        if (!view.DirectoryExists(dir) || !view.IsEmpty(dir)
            || WorkspaceLayout.IsLayoutDirectory(relative)) {
          break;
        }

        view.DeleteDirectory(dir);
        steps.Add(new DeleteDirectoryStep(dir));
      }
    }

    if (!context.IgnoreScripts && entry.Hooks.PostUninstall.Count > 0) {
      steps.Add(new ScriptStep("post_uninstall", entry.Hooks.PostUninstall));
    }

    return new PackagePlan(package.Identity, root, [], steps);
  }

  public async Task Apply(
      PackagePlan plan, WorkspaceTransaction transaction, CancellationToken cancellationToken) {
    foreach (PlannedStep step in plan.Steps) {
      cancellationToken.ThrowIfCancellationRequested();
      switch (step) {
        case ScriptStep script:
          await RunScripts(plan.Name, script.Stage, script.Commands, plan.Root);
          break;
        case WriteStep write:
          try {
            await using Stream content = await write.Source.OpenRead(write.Key);
            await transaction.WriteFile(write.FullPath, content, cancellationToken);
          }
          catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new McmodInstallException(
                $"{plan.Name}: cannot write {write.Target}: {ex.Message}", ex);
          }

          break;
        case DeleteFileStep delete:
          try {
            // A hook command may have deleted it.
            if (_fs.File.Exists(delete.FullPath)) {
              transaction.DeleteFile(delete.FullPath);
            }
          }
          catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new McmodInstallException(
                $"{plan.Name}: cannot delete {delete.FullPath}: {ex.Message}", ex);
          }

          break;
        case DeleteDirectoryStep directory:
          transaction.DeleteDirectoryOnCommit(directory.FullPath);
          break;
        default:
          throw new ArgumentException($"Unknown step {step}.", nameof(plan));
      }
    }
  }

  /// The entry a lock entry records (protocol/workspace.md §7.1).
  public static ToothEntry LockedEntry(LockedPackage package) {
    try {
      return ToothParser.ParseEntry(package.Entry, package.IsLegacy);
    }
    catch (FormatException ex) {
      throw new McmodInstallException(
          $"{package.Identity}: the entry recorded in the workspace file is invalid: "
          + ex.Message, ex);
    }
  }

  /// Where asset i is mounted in the source of a package.
  private static string Mount(int i) => i.ToString(CultureInfo.InvariantCulture);

  /// Plans the writes of steps 2 and 3 of installing a package (protocol/workspace.md §4).
  private async Task<List<PlannedWrite>> Plan(
      Candidate candidate,
      MountSource source,
      string root,
      InstallContext context,
      WorkspaceView view) {
    Package package = candidate.Package;
    string name = package.Identity;
    ToothEntry entry = package.Entry;
    ModManifest? modManifest = await candidate.GetModManifest();
    string? modDirectory = modManifest is null
        ? null
        : $"{WorkspaceLayout.ModsDirectory}/{modManifest.Name}/";

    // Target (workspace-relative) -> source and key. Later writes of the same target win.
    List<string> order = [];
    Dictionary<string, (string Target, ISource Source, string Key)> targets = new(PathComparer);
    void Add(string target, ISource from, string key) {
      if (!IsSafeRelativePath(target)
          // Windows drops trailing dots and spaces, which would alias another path.
          || target.Split('/').Any(s => s.EndsWith('.') || s.EndsWith(' '))) {
        throw new McmodInstallException(
            $"{name}: '{target}' is not a valid path inside the workspace.");
      }
      target = WorkspaceLayout.CanonicalPath(target);
      if (WorkspaceLayout.IsLipPath(target)) {
        throw new McmodInstallException(
            $"{name}: '{target}' is inside {LockStore.LipDirectory}, which is reserved for lip.");
      }
      if (!targets.ContainsKey(target)) {
        order.Add(target);
      }
      targets[target] = (target, from, key);
    }

    // Step 2.
    List<string> keys = [.. source.Keys];
    if (entry.Legacy) {
      List<AssetPlacement> placements = [];
      for (int i = 0; i < entry.Assets.Count; i++) {
        placements.AddRange(entry.Assets[i].Placements.Select(p => new AssetPlacement(
            Mount(i), p.LegacyDirectory ?? false, p.Src, p.Dest)));
      }

      foreach ((string key, string target) in ToothPlacements.Expand(placements, keys)) {
        Add(target, source, key);
      }
    } else {
      for (int i = 0; i < entry.Assets.Count; i++) {
        EntryAsset asset = entry.Assets[i];
        string prefix = Mount(i) + "/";
        List<(string Path, string Key)> files = [
          .. keys
              .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
              .Select(k => (k[prefix.Length..], k))
              // The tooth file of the package is never written.
              .Where(f => asset.Type != AssetType.Self || f.Item1 != ToothFormat.ToothFileName),
        ];

        if (asset.Type == AssetType.Mcmod) {
          foreach ((string path, string key) in files) {
            Add($"{modDirectory}{path}", source, key);
          }

          continue;
        }

        foreach (EntryPlacement placement in asset.Placements) {
          PlanPlacement(name, i, placement, files, (target, key) => Add(target, source, key));
        }
      }
    }

    // Step 3.
    if (modManifest is not null) {
      byte[]? written = ModManifestFile(package, modManifest, context.Members ?? []);
      if (written is not null) {
        Add(modDirectory + ToothFormat.ModManifestFileName, new BytesSource(written), "");
      }
    }

    Dictionary<string, string> owners = new(PathComparer);
    HashSet<string> ownedBySelf = new(PathComparer);
    foreach (LockedPackage locked in context.Lock.Packages) {
      bool self = PackageNames.AreEqual(locked.Identity, name);
      foreach (string file in locked.Files.Select(NormalizeRelative)) {
        if (self) {
          ownedBySelf.Add(file);
        } else {
          owners.TryAdd(file, locked.Identity);
        }
      }
    }

    List<string> conflicts = [.. order
        .Where(owners.ContainsKey)
        .Select(t => $"{t} (owned by {owners[t]})")];
    if (conflicts.Count > 0) {
      throw new McmodInstallException(
          $"{name}: files already owned by other packages: " + string.Join(", ", conflicts));
    }

    List<PlannedWrite> plan = [];
    HashSet<string> checkedDirectories = new(PathComparer);
    foreach (string normalized in order) {
      (string target, ISource from, string key) = targets[normalized];
      string full = ToFullPath(root, target);
      if (!IsInside(root, full)) {
        throw new McmodInstallException($"{name}: '{target}' escapes the workspace.");
      }
      if (view.DirectoryExists(full)) {
        throw new McmodInstallException($"{name}: '{target}' is an existing directory.");
      }
      for (string? parent = _fs.Path.GetDirectoryName(full);
          parent is not null && IsInside(root, parent) && checkedDirectories.Add(parent);
          parent = _fs.Path.GetDirectoryName(parent)) {
        if (view.FileExists(parent)) {
          throw new McmodInstallException(
              $"{name}: cannot write '{target}': a parent path is an existing file.");
        }
      }

      bool keep = view.FileExists(full) && MatchesAny(entry.PreserveFiles, target);
      plan.Add(new PlannedWrite(
          target, from, key, full, keep, keep && ownedBySelf.Contains(target)));
    }
    return plan;
  }

  /// Plans the writes of one placement of a "self" or "zip" asset (protocol/workspace.md §4 step
  /// 2). files are the (path in the asset, key) pairs of the asset.
  private static void PlanPlacement(
      string name,
      int asset,
      EntryPlacement placement,
      IReadOnlyList<(string Path, string Key)> files,
      Action<string, string> add) {
    switch (placement.Form) {
      case PlacementForm.File: {
          (string Path, string Key)? file = files
              .Cast<(string Path, string Key)?>()
              .FirstOrDefault(f => f!.Value.Path == placement.Src);
          if (file is null) {
            throw new McmodInstallException(
                $"{name}: the file '{placement.Src}' is not in asset {asset}.");
          }

          add(placement.Dest, file.Value.Key);
          break;
        }

      case PlacementForm.Directory: {
          List<(string Path, string Key)> matched = [
            .. files.Where(f => f.Path.StartsWith(placement.Src, StringComparison.Ordinal)),
          ];
          if (matched.Count == 0) {
            throw new McmodInstallException(
                $"{name}: asset {asset} has no file below '{placement.Src}'.");
          }

          foreach ((string path, string key) in matched) {
            add(placement.Dest + path[placement.Src.Length..], key);
          }

          break;
        }

      case PlacementForm.Glob: {
          PathGlob glob = new(placement.Src);
          string fixedPrefix = PathGlob.FixedPrefix(placement.Src);
          foreach ((string path, string key) in files.Where(f => glob.IsMatch(f.Path))) {
            add(placement.Dest + path[fixedPrefix.Length..], key);
          }

          break;
        }
    }
  }

  /// The dependencies of the mod manifest of a mod (protocol/workspace.md §4 step 3): one
  /// {"name": N} for each dependency of the package, in order, that a mod with mod name N among
  /// the members satisfies.
  public static JsonArray ModDependencies(Package package, IReadOnlyList<InstalledMember> members) {
    JsonArray dependencies = [];
    foreach (ToothDependency dependency in package.Entry.Dependencies) {
      string identity = Package.GetIdentity(dependency.Source.Target, dependency.Source.Variant);
      InstalledMember? satisfier = members.FirstOrDefault(m =>
          PackageNames.AreEqual(m.Identity, identity) && dependency.Range.IsSatisfiedBy(m.Version));
      if (satisfier?.ModName is string modName) {
        dependencies.Add(new JsonObject { ["name"] = modName });
      }
    }

    return dependencies;
  }

  /// The manifest.json that step 3 writes into the mod directory (protocol/workspace.md §4): the
  /// mod manifest with "dependencies" set to ModDependencies; null when the mod manifest has
  /// "dependencies", which must then equal them. Throws McmodInstallException otherwise.
  public static byte[]? ModManifestFile(
      Package package, ModManifest manifest, IReadOnlyList<InstalledMember> members) {
    JsonArray dependencies = ModDependencies(package, members);
    if (manifest.Json.TryGetProperty("dependencies", out JsonElement declared)) {
      if (!JsonNode.DeepEquals(JsonNode.Parse(declared.GetRawText()), dependencies)) {
        throw new McmodInstallException(
            $"{package.Identity}: the \"dependencies\" of its mod manifest are "
            + $"{declared.GetRawText()}, but its dependencies are satisfied by the mods "
            + $"{dependencies.ToJsonString()}.");
      }

      return null;
    }

    JsonObject file = JsonNode.Parse(manifest.Json.GetRawText())!.AsObject();
    file["dependencies"] = dependencies;
    return Encoding.UTF8.GetBytes(file.ToJsonString(_manifestOptions));
  }

  private static List<string> OwnedFiles(List<PlannedWrite> plan) =>
      [.. plan.Where(w => !w.Keep || w.StaysOwned).Select(w => w.Target)];

  private async Task RunScripts(
      string name,
      string stage,
      IReadOnlyList<string> commands,
      string root) {
    foreach (string command in commands) {
      try {
        await _commandRunner.Run(command, root);
      }
      catch (Exception ex) {
        throw new McmodInstallException(
            $"{name}: {stage} command '{command}' failed: {ex.Message}", ex);
      }
    }
  }

  /// Workspace paths matching the glob, as (workspace-relative path, is directory); everything
  /// below a matched directory is included. Only the glob's literal leading directory is
  /// walked, only as deep as the pattern can reach, and never through links.
  private IEnumerable<(string Path, bool IsDirectory)> EnumerateMatches(
      string root,
      PathGlob glob,
      WorkspaceView view) {
    string[] segments = glob.Pattern.TrimEnd('/').Split('/');
    if (segments.Any(s => s is "." or ".." || s.Contains(':'))) {
      // Never look outside the workspace.
      yield break;
    }

    int literal = 0;
    while (literal < segments.Length && !PathGlob.HasMetacharacters(segments[literal])) {
      literal++;
    }

    if (literal == segments.Length && !glob.MatchesDirectories) {
      // A literal file path.
      string file = WorkspaceLayout.CanonicalPath(glob.Pattern);
      if (!WorkspaceLayout.IsLipPath(file) && view.FileExists(ToFullPath(root, file))) {
        yield return (file, false);
      }
      yield break;
    }

    string start = WorkspaceLayout.CanonicalPath(
        string.Concat(segments[..literal].Select(s => s + "/")));
    string startFull = ToFullPath(root, start);
    if ((start != "" && !IsInside(root, startFull)) || !view.DirectoryExists(startFull)
        || WorkspaceLayout.IsLipPath(start)) {
      yield break;
    }

    int? maxDepth = segments[literal..].Contains("**") ? null : segments.Length - literal;
    if (literal == segments.Length) {
      // A literal directory path. mods and plugins are never matched, so a pattern naming them
      // removes nothing below them either.
      if (WorkspaceLayout.IsLayoutDirectory(start.TrimEnd('/'))) {
        yield break;
      }
      yield return (start.TrimEnd('/'), true);
      foreach ((string, bool) entry in Walk(startFull, all: true, depth: 1)) {
        yield return entry;
      }
      yield break;
    }

    foreach ((string, bool) entry in Walk(startFull, all: false, depth: 1)) {
      yield return entry;
    }

    IEnumerable<(string Path, bool IsDirectory)> Walk(string directory, bool all, int depth) {
      foreach (WorkspaceView.Entry info in view.Enumerate(directory)) {
        string path = _fs.Path.GetRelativePath(root, info.FullName)
            .Replace(_fs.Path.DirectorySeparatorChar, '/');
        if (WorkspaceLayout.IsLipPath(path)) {
          continue;
        }
        string suffix = info.IsDirectory ? "/" : "";
        bool matched = all
            || (!(info.IsDirectory && WorkspaceLayout.IsLayoutDirectory(path))
                && WorkspaceLayout.GlobForms(path).Any(f => glob.IsMatch(f + suffix)));
        if (matched) {
          yield return (WorkspaceLayout.CanonicalPath(path), info.IsDirectory);
        }
        if (info.IsDirectory && !info.IsLink
            && (matched || maxDepth is null || depth < maxDepth)) {
          foreach ((string, bool) entry in Walk(info.FullName, matched, depth + 1)) {
            yield return entry;
          }
        }
      }
    }
  }

  /// Whether a glob matches a canonical workspace-relative path, in any of its forms.
  private static bool MatchesAny(IReadOnlyList<PathGlob> globs, string path) =>
      WorkspaceLayout.GlobForms(path).Any(f => globs.Any(g => g.IsMatch(f)));

  private string ToFullPath(string root, string relative) =>
      relative.TrimEnd('/') == ""
          ? root
          : _fs.Path.GetFullPath(_fs.Path.Combine(
              root,
              relative.TrimEnd('/').Replace('/', _fs.Path.DirectorySeparatorChar)));

  /// Whether path is strictly below directory.
  private bool IsInside(string directory, string path) {
    string prefix = _fs.Path.TrimEndingDirectorySeparator(directory)
        + _fs.Path.DirectorySeparatorChar;
    return path.StartsWith(prefix, PathComparison);
  }

  private static string NormalizeRelative(string path) =>
      WorkspaceLayout.CanonicalPath(path.Replace('\\', '/'));

  /// A non-empty '/'-separated relative path without empty, "." or ".." segments, backslashes
  /// or drive letters.
  private static bool IsSafeRelativePath(string path) =>
      path != ""
      && !path.Contains('\\')
      && !path.Contains(':')
      && path.Split('/').All(s => s != "" && s != "." && s != "..");

  private record PlannedWrite(
      string Target,
      ISource Source,
      string SourceKey,
      string FullPath,
      bool Keep,
      bool StaysOwned);

  /// A single generated file, with the key "".
  private sealed class BytesSource(byte[] content) : ISource {
    public IEnumerable<string> Keys => [""];

    public Task<Stream> OpenRead(string key) =>
        Task.FromResult<Stream>(new MemoryStream(content, writable: false));
  }
}
