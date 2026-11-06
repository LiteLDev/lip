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
/// written whole into the mod directory. The mod directory is named by the mod
/// name, the "name" of the manifest.json of the mcmod asset (Candidate.GetModManifest); lip
/// writes that manifest.json as it is, like any other file.
///
/// Ownership: a file is owned by a package when the package wrote it. A file that already exists
/// and matches preserve is kept and not written, so it is not owned by the package.
///
/// The mods directory is never deleted, and remove_files never matches it (so a directory
/// pattern naming it removes nothing). .lip, where lip keeps the lock, is never written, deleted
/// or matched.
public partial class McmodInstaller(
    IFileSystem fileSystem,
    ICommandRunner commandRunner,
    IUserInteraction userInteraction) {
  private readonly IFileSystem _fs = fileSystem;
  private readonly ICommandRunner _commandRunner = commandRunner;
  private readonly IUserInteraction _ui = userInteraction;

  /// How workspace paths are compared: ignoring case on Windows, ordinal elsewhere.
  public static StringComparer PathComparer => WorkspaceLayout.PathComparer;

  private static readonly StringComparison PathComparison =
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

  /// Plans installing the package into the workspace as the view shows it, and adds the planned
  /// writes to the view. Fetches the archive and the assets (a dry run reports the plan too).
  /// Throws McmodInstallException when the package cannot be installed, e.g. when a file is
  /// owned by another installed package.
  public async Task<PackagePlan> PlanInstall(
      Candidate candidate, InstallContext context, WorkspaceView view) {
    Package package = candidate.Package;
    PackageEntry entry = package.Entry;
    string name = package.Identity;
    string label = Label(package);
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    RunTrace.Write("install", $"Planning to install {label} from {candidate.Source}"
        + $" into {root} ({candidate.Assets.Count} assets)");

    // Every asset is obtained before anything is written or run.
    List<(string Mount, ISource Source)> mounts = [];
    for (int i = 0; i < candidate.Assets.Count; i++) {
      try {
        mounts.Add((Mount(i), await candidate.Assets[i].Open()));
      }
      catch (ArchiveUnavailableException ex) {
        throw new McmodInstallException(
            $"Cannot install {label}: lip could not obtain asset {i}"
            + $" ({DescribeAsset(entry, i)}) of the package, so nothing was changed.",
            ex);
      }
    }

    MountSource source = new(mounts);
    List<PlannedWrite> plan = await Plan(candidate, source, root, context, view);
    List<PlannedWrite> writes = [.. plan.Where(w => !w.Keep)];
    foreach (PlannedWrite write in writes) {
      view.AddFile(write.FullPath);
    }

    foreach (PlannedWrite kept in plan.Where(w => w.Keep)) {
      RunTrace.Write("install", $"{name}: keeping the existing {kept.Target} (preserve_files)");
    }

    RunTrace.Write("install", $"{name}: {writes.Count} files to write");

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
      catch (Exception ex) when (ex is not OperationCanceledException) {
        throw new McmodInstallException(
            $"Cannot install {label}: lip could not get the files of the package, so"
            + " nothing was changed.",
            ex);
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

  /// Plans uninstalling the package from the workspace as the view shows it, and adds the
  /// planned deletions to the view. A dry run reports the plan.
  public async Task<PackagePlan> PlanUninstall(
      LockedPackage package, InstallContext context, WorkspaceView view) {
    PackageEntry entry = LockedEntry(package, context.Environment);
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    RunTrace.Write("install", $"Planning to uninstall {package.Identity} {package.Version}"
        + $" from {root} ({package.Files.Count} owned files recorded)");

    // The paths to delete are planned against the workspace before pre_uninstall runs; a
    // DeleteMatchesStep deletes what matches only after hook commands ran.
    HashSet<string> ownedByOthers = new(
        context.Lock.Packages
            .Where(p => !PackageNames.AreEqual(p.Identity, package.Identity))
            .SelectMany(p => p.Files)
            .Select(NormalizeRelative),
        PathComparer);

    // 2. Owned files not matching preserve_files.
    List<string> files = [];
    HashSet<string> fileSet = new(PathComparer);
    List<string> ownedToDelete = [];
    foreach (string file in package.Files.Select(NormalizeRelative)) {
      if (!IsSafeRelativePath(file) || WorkspaceLayout.IsLipPath(file)
          || ownedByOthers.Contains(file)) {
        RunTrace.Write("install", $"{package.Identity}: not deleting {file}: it is not a safe"
            + " workspace path, is inside .lip, or another package owns it");
        continue;
      }
      if (MatchesAny(entry.PreserveFiles, file)) {
        RunTrace.Write("install", $"{package.Identity}: keeping {file} (preserve_files)");
        continue;
      }
      ownedToDelete.Add(file);
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
          if (!WorkspaceLayout.IsModsDirectory(path) && directorySet.Add(path)) {
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
            || WorkspaceLayout.IsModsDirectory(relative)) {
          break;
        }

        view.DeleteDirectory(dir);
        steps.Add(new DeleteDirectoryStep(dir));
      }
    }

    // Without hook commands, the workspace is as planned when the deletions run.
    if (!context.IgnoreScripts) {
      steps.Add(new DeleteMatchesStep(ownedToDelete, entry.RemoveFiles, ownedByOthers));
    }

    if (!context.IgnoreScripts && entry.Hooks.PostUninstall.Count > 0) {
      steps.Add(new ScriptStep("post_uninstall", entry.Hooks.PostUninstall));
    }

    RunTrace.Write("install", $"{package.Identity}: {files.Count} files and"
        + $" {steps.OfType<DeleteDirectoryStep>().Count()} directories to delete");
    return new PackagePlan(package.Identity, root, [], steps);
  }

  /// Runs the steps of a plan, making every file change through the transaction. Throws
  /// McmodInstallException when a step fails; the caller rolls the transaction back.
  public async Task Apply(
      PackagePlan plan, WorkspaceTransaction transaction, CancellationToken cancellationToken) {
    RunTrace.Write("install", $"Applying the plan of {plan.Name} ({plan.Steps.Count} steps)");
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
                $"Cannot install {plan.Name}: lip cannot write '{write.Target}'.", ex);
          }

          break;
        case DeleteFileStep delete:
          try {
            // A hook command may have deleted it.
            if (_fs.File.Exists(delete.FullPath)) {
              transaction.DeleteFile(delete.FullPath);
            } else {
              RunTrace.Write("install", $"{plan.Name}: {delete.FullPath} is already gone");
            }
          }
          catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new McmodInstallException(
                $"Cannot uninstall {plan.Name}: lip cannot delete"
                + $" '{WorkspaceErrors.Show(_fs, plan.Root, delete.FullPath)}'.",
                ex);
          }

          break;
        case DeleteDirectoryStep directory:
          try {
            transaction.DeleteDirectoryOnCommit(directory.FullPath);
          }
          catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new McmodInstallException(
                $"Cannot uninstall {plan.Name}: lip cannot record that it deletes the directory"
                + $" '{WorkspaceErrors.Show(_fs, plan.Root, directory.FullPath)}'.",
                ex);
          }

          break;
        case DeleteMatchesStep matches:
          DeleteMatches(plan, matches, transaction);
          break;
        default:
          throw new ArgumentException(
              $"lip planned a step it does not know ({step}). This is a bug in lip.",
              nameof(plan));
      }
    }
  }

  /// Deletes what uninstallation steps 2–4 match in the workspace as it is now (protocol/workspace.md
  /// §6 rule 4), beyond what the plan already deleted.
  private void DeleteMatches(
      PackagePlan plan, DeleteMatchesStep step, WorkspaceTransaction transaction) {
    WorkspaceView now = new(_fs);
    List<string> files = [.. step.OwnedFiles];
    List<string> directories = [];
    foreach (PathGlob glob in step.Purge) {
      foreach ((string path, bool isDirectory) in EnumerateMatches(plan.Root, glob, now)) {
        if (isDirectory) {
          if (!WorkspaceLayout.IsModsDirectory(path)) {
            directories.Add(path);
          }
        } else if (!step.OwnedByOthers.Contains(path)) {
          files.Add(path);
        }
      }
    }

    List<string> deleted = [];
    foreach (string file in files.Distinct(PathComparer)) {
      string full = ToFullPath(plan.Root, file);
      if (!_fs.File.Exists(full)) {
        continue;
      }

      RunTrace.Write("install", $"{plan.Name}: deleting {file}, which matches after hook commands");
      try {
        transaction.DeleteFile(full);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        throw new McmodInstallException(
            $"Cannot uninstall {plan.Name}: lip cannot delete"
            + $" '{WorkspaceErrors.Show(_fs, plan.Root, full)}'.",
            ex);
      }

      deleted.Add(file);
    }

    // Matched directories, and the directories these deletions leave empty, are deleted when the
    // operation commits if they are empty then.
    HashSet<string> scheduled = new(PathComparer);
    foreach (string directory in directories) {
      Schedule(ToFullPath(plan.Root, directory));
    }
    foreach (string path in deleted.Concat(directories)) {
      for (string? dir = _fs.Path.GetDirectoryName(ToFullPath(plan.Root, path));
          dir is not null && IsInside(plan.Root, dir);
          dir = _fs.Path.GetDirectoryName(dir)) {
        string relative = _fs.Path.GetRelativePath(plan.Root, dir)
            .Replace(_fs.Path.DirectorySeparatorChar, '/');
        if (WorkspaceLayout.IsModsDirectory(relative)) {
          break;
        }
        Schedule(dir);
      }
    }

    void Schedule(string full) {
      if (!scheduled.Add(full) || !_fs.Directory.Exists(full)) {
        return;
      }
      try {
        transaction.DeleteDirectoryOnCommit(full);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        throw new McmodInstallException(
            $"Cannot uninstall {plan.Name}: lip cannot record that it deletes the directory"
            + $" '{WorkspaceErrors.Show(_fs, plan.Root, full)}'.",
            ex);
      }
    }
  }

  /// The entry of an installed package (protocol/workspace.md §7.1): the entry of its variant in
  /// its recorded package file for the environment.
  public static PackageEntry LockedEntry(LockedPackage package, LipEnvironment environment) {
    PackageFile manifest;
    try {
      manifest = PackageFileParser.Parse(package.Manifest);
    }
    catch (FormatException ex) {
      throw new McmodInstallException(
          $"The package file that the workspace file ({LockStore.LipDirectory}/"
          + $"{LockStore.FileName}) records for {package.Identity} {package.Version} is invalid."
          + " If you edited the workspace file by hand, undo the change.",
          ex);
    }

    string variant = package.Variant == ""
        ? "the default variant"
        : $"the variant \"{package.Variant}\"";
    return manifest.SelectEntry(package.Variant, environment)
        ?? throw new McmodInstallException(
            $"The package file that the workspace file ({LockStore.LipDirectory}/"
            + $"{LockStore.FileName}) records for {package.Identity} {package.Version} has no"
            + $" entry of {variant} for the platform {environment.Platform.ToSpecString()} and"
            + $" the runtime {environment.Runtime}. If you passed --rid, pass the runtime the"
            + " workspace was installed for instead.");
  }

  /// A package for messages: its identity and version.
  private static string Label(Package package) => $"{package.Identity} {package.Version}";

  /// An asset of an entry for messages: its type and where it comes from.
  private static string DescribeAsset(PackageEntry entry, int i) {
    if (i >= entry.Assets.Count) {
      return "unknown";
    }

    EntryAsset asset = entry.Assets[i];
    string type = asset.Type.ToString().ToLowerInvariant();
    return asset.Type == AssetType.Self || asset.Urls.Count == 0
        ? $"type \"{type}\", the archive of the package itself"
        : $"type \"{type}\", from {string.Join(" or ", asset.Urls)}";
  }

  /// Where asset i is mounted in the source of a package.
  private static string Mount(int i) => i.ToString(CultureInfo.InvariantCulture);

  /// Plans the writes of step 2 of installing a package (protocol/workspace.md §4).
  private async Task<List<PlannedWrite>> Plan(
      Candidate candidate,
      MountSource source,
      string root,
      InstallContext context,
      WorkspaceView view) {
    Package package = candidate.Package;
    string label = Label(package);
    PackageEntry entry = package.Entry;
    ModManifest? modManifest = await candidate.GetModManifest();
    string? modDirectory = modManifest is null
        ? null
        : $"{WorkspaceLayout.ModsDirectory}/{modManifest.Name}/";
    if (modDirectory is not null) {
      RunTrace.Write("install", $"{label}: the mod directory is {modDirectory}");
    }

    // Target (workspace-relative) -> source and key. Later writes of the same target win.
    List<string> order = [];
    Dictionary<string, (string Target, ISource Source, string Key)> targets = new(PathComparer);
    void Add(string target, ISource from, string key) {
      // "a/./b" is "a/b". Windows drops trailing dots and spaces, which would alias another
      // path, e.g. one in .lip or owned by another package, and takes ':' for an alternate data
      // stream.
      string[] segments = IsSafeRelativePath(target)
          ? [.. target.Split('/').Where(s => s != ".")]
          : [];
      if (segments.Length == 0) {
        throw new McmodInstallException(
            $"Cannot install {label}: its package file places a file at '{target}', which is not a"
            + " valid path inside the workspace (a relative path with '/' separators and without"
            + " '..'). Report this to the author of the package.");
      }
      if (OperatingSystem.IsWindows()
          && segments.Any(s => s.EndsWith('.') || s.EndsWith(' ') || s.Contains(':'))) {
        throw new McmodInstallException(
            $"Cannot install {label}: its package file places a file at '{target}', which is not a"
            + " valid path on Windows (a name in it ends with '.' or a space, or contains ':')."
            + " Report this to the author of the package.");
      }
      target = string.Join('/', segments);
      if (WorkspaceLayout.IsLipPath(target)) {
        throw new McmodInstallException(
            $"Cannot install {label}: its package file places a file at '{target}', inside"
            + $" {LockStore.LipDirectory}, which is reserved for lip. Report this to the author"
            + " of the package.");
      }
      if (!targets.ContainsKey(target)) {
        order.Add(target);
      }
      targets[target] = (target, from, key);
    }

    // Step 2.
    List<string> keys = [.. source.Keys];
    for (int i = 0; i < entry.Assets.Count; i++) {
      EntryAsset asset = entry.Assets[i];
      string prefix = Mount(i) + "/";
      List<(string Path, string Key)> files = [
        .. keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => (k[prefix.Length..], k)),
      ];

      // The package file of the package is never written (protocol/workspace.md §4 step 2): its
      // lip.yaml, or, for an archive without one, the tooth.json it is read from.
      if (asset.Type == AssetType.Self) {
        string packageFile = files.Any(f => f.Path == PackageFormat.FileName)
            ? PackageFormat.FileName
            : ToothCompat.ToothFileName;
        files.RemoveAll(f => f.Path == packageFile);
      }

      if (asset.Type == AssetType.Mcmod) {
        foreach ((string path, string key) in files) {
          Add($"{modDirectory}{path}", source, key);
        }

        continue;
      }

      foreach (EntryPlacement placement in asset.Placements) {
        PlanPlacement(label, i, placement, files, (target, key) => Add(target, source, key));
      }
    }

    Dictionary<string, string> owners = new(PathComparer);
    foreach (LockedPackage locked in context.Lock.Packages
        .Where(p => !PackageNames.AreEqual(p.Identity, package.Identity))) {
      foreach (string file in locked.Files.Select(NormalizeRelative)) {
        owners.TryAdd(file, locked.Identity);
      }
    }

    List<string> conflicts = [.. order
        .Where(owners.ContainsKey)
        .Select(t => $"{t} (owned by {owners[t]})")];
    if (conflicts.Count > 0) {
      RunTrace.Write("install", $"{label}: conflicts with installed packages: "
          + string.Join(", ", conflicts));
      throw new McmodInstallException(
          $"Cannot install {label}: it would overwrite files that belong to other installed"
          + $" packages: {string.Join(", ", conflicts)}. Uninstall those packages first, or ask"
          + " the authors of the packages to resolve the conflict.");
    }

    List<PlannedWrite> plan = [];
    HashSet<string> checkedDirectories = new(PathComparer);
    foreach (string normalized in order) {
      (string target, ISource from, string key) = targets[normalized];
      string full = ToFullPath(root, target);
      if (!IsInside(root, full)) {
        throw new McmodInstallException(
            $"Cannot install {label}: its package file places a file at '{target}', which is"
            + " outside the workspace. Report this to the author of the package.");
      }
      if (view.DirectoryExists(full)) {
        throw new McmodInstallException(
            $"Cannot install {label}: it writes the file '{target}', but a directory of that"
            + " name is in the way. Move or delete that directory, then try again.");
      }
      for (string? parent = _fs.Path.GetDirectoryName(full);
          parent is not null && IsInside(root, parent) && checkedDirectories.Add(parent);
          parent = _fs.Path.GetDirectoryName(parent)) {
        if (view.FileExists(parent)) {
          throw new McmodInstallException(
              $"Cannot install {label}: it writes the file '{target}', but"
              + $" '{WorkspaceErrors.Show(_fs, root, parent)}' is a file, not a directory. Move"
              + " or delete that file, then try again.");
        }
      }

      bool keep = view.FileExists(full) && MatchesAny(entry.PreserveFiles, target);
      plan.Add(new PlannedWrite(target, from, key, full, keep));
    }
    return plan;
  }

  /// Plans the writes of one placement of a "self" or "zip" asset (protocol/workspace.md §4 step
  /// 2). files are the (path in the asset, key) pairs of the asset.
  private static void PlanPlacement(
      string label,
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
                $"Cannot install {label}: its package file places the file '{placement.Src}' of"
                + $" asset {asset}, but the asset has no such file. Report this to the author of"
                + " the package.");
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
                $"Cannot install {label}: its package file places the directory '{placement.Src}'"
                + $" of asset {asset}, but the asset has no file below it. Report this to the"
                + " author of the package.");
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

  private static List<string> OwnedFiles(List<PlannedWrite> plan) =>
      [.. plan.Where(w => !w.Keep).Select(w => w.Target)];

  private async Task RunScripts(
      string name,
      string stage,
      IReadOnlyList<string> commands,
      string root) {
    foreach (string command in commands) {
      RunTrace.Write("install", $"{name}: running the {stage} command '{command}'");
      try {
        await _commandRunner.Run(command, root);
      }
      catch (Exception ex) {
        throw new McmodInstallException(
            $"In the {stage} hook of {name}, the command '{command}' failed. lip undoes the file"
            + " changes of this operation, but not what hook commands changed.",
            ex);
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
    if (segments.Any(s => s is "." or "..") || StartsWithDrive(glob.Pattern)) {
      // No workspace-relative path has such a segment; never look outside the workspace.
      yield break;
    }

    int literal = 0;
    while (literal < segments.Length && !PathGlob.HasMetacharacters(segments[literal])) {
      literal++;
    }

    if (literal == segments.Length && !glob.MatchesDirectories) {
      // A literal file path.
      string file = glob.Pattern;
      if (!WorkspaceLayout.IsLipPath(file) && view.FileExists(ToFullPath(root, file))) {
        yield return (file, false);
      }
      yield break;
    }

    string start = string.Concat(segments[..literal].Select(s => s + "/"));
    string startFull = ToFullPath(root, start);
    if ((start != "" && !IsInside(root, startFull)) || !view.DirectoryExists(startFull)
        || WorkspaceLayout.IsLipPath(start)) {
      yield break;
    }

    int? maxDepth = segments[literal..].Contains("**") ? null : segments.Length - literal;
    if (literal == segments.Length) {
      // A literal directory path. mods is never matched, so a pattern naming it removes nothing
      // below it either.
      if (WorkspaceLayout.IsModsDirectory(start)) {
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
            || (!(info.IsDirectory && WorkspaceLayout.IsModsDirectory(path))
                && glob.IsMatch(path + suffix));
        if (matched) {
          yield return (path, info.IsDirectory);
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

  /// Whether a glob matches a workspace-relative path.
  private static bool MatchesAny(IReadOnlyList<PathGlob> globs, string path) =>
      globs.Any(g => g.IsMatch(path));

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

  /// A recorded path, "a/./b" being "a/b" as when it was recorded.
  private static string NormalizeRelative(string path) =>
      string.Join('/', path.Replace('\\', '/').Split('/').Where(s => s != "."));

  /// A non-empty '/'-separated relative path as lip.schema.yaml #/definitions/path allows it:
  /// without empty or ".." segments, backslashes, or a leading ASCII letter and ':'.
  private static bool IsSafeRelativePath(string path) =>
      path != ""
      && !path.Contains('\\')
      && !StartsWithDrive(path)
      && path.Split('/').All(s => s is not ("" or ".."));

  /// Whether a path starts with an ASCII letter and ':', a drive on Windows.
  private static bool StartsWithDrive(string path) =>
      path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

  private record PlannedWrite(
      string Target,
      ISource Source,
      string SourceKey,
      string FullPath,
      bool Keep);
}
