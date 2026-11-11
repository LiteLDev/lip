using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Install;

/// Thrown when installing or uninstalling a package fails (protocol/lip-workspace.md, "Installing
/// and uninstalling"). Failures detected while planning leave the workspace untouched.
public class McmodInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Thrown when a path of the workspace names more than one existing file or directory, its paths
/// being case-insensitive (protocol/lip-workspace.md, "Environment and layout"). Names: the names
/// of those files or directories, which equal each other ignoring case.
public class AmbiguousPathException(string path, IReadOnlyList<string> names)
    : McmodInstallException(
        $"'{path}' names more than one file or directory of the workspace"
        + $" ({string.Join(", ", names.Select(n => $"'{n}'"))}) to the programs that Wine runs for"
        + " a Windows platform, as they ignore case. Rename or merge them, then try again.");

/// Installs and uninstalls packages (protocol/lip-workspace.md, "Installing and uninstalling").
///
/// Installing writes the files of the assets of the entry, in order (step 2): each asset is
/// mounted at its index; "self" and "zip" assets carry out their placements, "mcmod" assets are
/// written whole into the mod directory. The mod directory is named by the mod
/// name, the "name" of the manifest.json of the mcmod asset (Candidate.GetModManifest); lip
/// writes that manifest.json as it is, like any other file.
///
/// Ownership: a file is owned by a package when the package wrote it. A file that exists when it
/// would be written and matches preserve is kept and not written, so it is not owned by the
/// package; whether it exists is decided then, after the hook commands that ran before it.
///
/// The write rules are checked twice: when planning, against the workspace as the earlier
/// changes of the operation are planned to leave it, so that a failure changes nothing
/// (protocol/lip-workspace.md, "Atomicity" rule 1); and at each write, against the workspace and
/// the owners as they are then (FileOwners), which decides.
///
/// The mods directory is never deleted, and remove_files never matches it (so a directory
/// pattern naming it removes nothing). .lip, where lip keeps the lock, is never written, deleted
/// or matched. On Windows, these checks and ownership see a path by its long names (LongName),
/// so that an 8.3 short name such as "LIP~1" cannot name .lip or a file of another package.
/// Elsewhere, on a file system that ignores case (as macOS does by default), they see each name
/// that exists as the file system spells it, so that ".LIP" or "MODS" cannot name .lip or mods,
/// nor "Mods/a.dll" a file "mods/a.dll" of another package.
///
/// When the paths of the workspace are case-insensitive (WorkspacePaths.IgnoresCase: a Windows
/// platform, and lip does not run on Windows), every path is compared and matched ignoring the
/// case of A-Z, and each name in it that exists is spelled as the existing entry whose name
/// equals it so, wherever the file system tells case apart: a file is written in place of the
/// existing file and below the existing directories its path names, and a path naming more than
/// one of them is a failure (protocol/lip-workspace.md, "Environment and layout").
public partial class McmodInstaller(
    IFileSystem fileSystem,
    ICommandRunner commandRunner,
    IUserInteraction userInteraction) {
  private readonly IFileSystem _fs = fileSystem;
  private readonly ICommandRunner _commandRunner = commandRunner;
  private readonly IUserInteraction _ui = userInteraction;

  /// Whether the file system of each workspace root ignores case (IgnoresCase), once known.
  private readonly ConcurrentDictionary<string, bool> _ignoresCase = new(StringComparer.Ordinal);

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
    WorkspacePaths paths = WorkspacePaths.For(context.Environment);
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
    List<PlannedWrite> plan = await Plan(candidate, source, root, context, view, paths);
    foreach (PlannedWrite write in plan) {
      view.AddFile(write.FullPath);
    }

    // Whether a file matching preserve is kept is decided when it is written, after the hook
    // commands before it (protocol/lip-workspace.md, "Installing and uninstalling"); the plan
    // expects the workspace as it is.
    RunTrace.Write("install", $"{name}: {plan.Count(w => !w.Keep)} files to write,"
        + $" {plan.Count(w => w.Keep)} existing files matching preserve to keep");

    if (context.DryRun) {
      foreach (PlannedWrite write in plan) {
        await _ui.PrintInfo(write.Keep
            ? $"Would keep {write.Target} (preserved)"
            : $"Would write {write.Target}");
      }
    } else {
      // Fetch content now, so that a fetch failure changes nothing. A file expected to be kept
      // is fetched too: it is written when a hook command deleted it.
      try {
        await source.Prefetch(
            plan.Where(w => w.Source == source).Select(w => w.SourceKey).Distinct());
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
    if (entry.Hooks.PreInstall.Count > 0) {
      steps.Add(new ScriptStep(
          "pre_install", entry.Hooks.PreInstall, context.Environment.Platform));
    }

    steps.AddRange(plan.Select(w => new WriteStep(
        w.Target, w.FullPath, w.Source, w.SourceKey, entry.PreserveFiles, w.Keep)));
    if (entry.Hooks.PostInstall.Count > 0) {
      steps.Add(new ScriptStep(
          "post_install", entry.Hooks.PostInstall, context.Environment.Platform));
    }

    return new PackagePlan(name, root, OwnedFiles(plan), steps, paths);
  }

  /// Plans uninstalling the package from the workspace as the view shows it, and adds the
  /// planned deletions to the view. A dry run reports the plan.
  public async Task<PackagePlan> PlanUninstall(
      LockedPackage package, InstallContext context, WorkspaceView view) {
    PackageEntry entry = LockedEntry(package, context.Environment);
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    WorkspacePaths paths = WorkspacePaths.For(context.Environment);
    RunTrace.Write("install", $"Planning to uninstall {package.Identity} {package.Version}"
        + $" from {root} ({package.Files.Count} owned files recorded)");

    // The paths to delete are planned against the workspace before pre_uninstall runs; a
    // DeleteMatchesStep deletes what matches only after hook commands ran. Each deletion is
    // checked again against the owners when it is carried out (Apply).
    HashSet<string> ownedByOthers = new(
        context.Lock.Packages
            .Where(p => !PackageNames.AreEqual(p.Identity, package.Identity))
            .SelectMany(p => p.Files)
            .Select(f => LongName(root, NormalizeRelative(f), paths) ?? NormalizeRelative(f)),
        paths.Comparer);

    // 2. Owned files not matching preserve_files.
    List<string> files = [];
    HashSet<string> fileSet = new(paths.Comparer);
    List<string> ownedToDelete = [];
    foreach (string recorded in package.Files.Select(NormalizeRelative)) {
      // A path whose long name cannot be read is not deleted: it might name .lip.
      string file = LongName(root, recorded, paths) ?? "";
      if (!IsSafeRelativePath(file) || paths.IsLipPath(file)
          || ownedByOthers.Contains(file)) {
        RunTrace.Write("install", $"{package.Identity}: not deleting {recorded}: it is not a"
            + " safe workspace path, is inside .lip, or another package owns it");
        continue;
      }
      if (MatchesAny(entry.PreserveFiles, file, paths)) {
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
    HashSet<string> directorySet = new(paths.Comparer);
    foreach (PathGlob glob in entry.RemoveFiles) {
      foreach ((string path, bool isDirectory) in EnumerateMatches(root, glob, view, paths)) {
        if (isDirectory) {
          if (!paths.IsModsDirectory(path) && directorySet.Add(path)) {
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
    if (entry.Hooks.PreUninstall.Count > 0) {
      steps.Add(new ScriptStep(
          "pre_uninstall", entry.Hooks.PreUninstall, context.Environment.Platform));
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
            || paths.IsModsDirectory(relative)) {
          break;
        }

        view.DeleteDirectory(dir);
        steps.Add(new DeleteDirectoryStep(dir));
      }
    }

    steps.Add(new DeleteMatchesStep(ownedToDelete, entry.RemoveFiles));

    if (entry.Hooks.PostUninstall.Count > 0) {
      steps.Add(new ScriptStep(
          "post_uninstall", entry.Hooks.PostUninstall, context.Environment.Platform));
    }

    RunTrace.Write("install", $"{package.Identity}: {files.Count} files and"
        + $" {steps.OfType<DeleteDirectoryStep>().Count()} directories to delete");
    return new PackagePlan(package.Identity, root, [], steps, paths);
  }

  /// Runs the steps of a plan, making every file change through the transaction, and returns the
  /// files the package owns afterwards (workspace-relative, '/'-separated; none for an
  /// uninstall). Each write is checked against the write rules as the workspace and owners are
  /// then (protocol/lip-workspace.md, "Installing and uninstalling"), and adds the file it writes
  /// to owners; a file that another package owns then is not deleted. Throws McmodInstallException
  /// when a step fails; the caller rolls the transaction back.
  public async Task<IReadOnlyList<string>> Apply(
      PackagePlan plan,
      WorkspaceTransaction transaction,
      FileOwners owners,
      CancellationToken cancellationToken) {
    RunTrace.Write("install", $"Applying the plan of {plan.Name} ({plan.Steps.Count} steps)");
    WorkspacePaths paths = plan.Paths;
    List<string> owned = [];
    HashSet<string> ownedSet = new(paths.Comparer);
    foreach (PlannedStep step in plan.Steps) {
      cancellationToken.ThrowIfCancellationRequested();
      switch (step) {
        case ScriptStep script:
          if (script.Commands.Count > 0) {
            // A hook command runs only once the journal exists (protocol/lip-workspace.md,
            // "Carrying out an operation").
            transaction.Begin();
          }

          await RunScripts(
              plan.Name, script.Stage, script.Commands, plan.Root, script.Platform);
          break;
        case WriteStep write: {
            // The write rules see the target by its long names as the workspace is now: a
            // directory that an earlier write or a hook command created can give a short name in
            // it a meaning it did not have when the plan was made (protocol/lip-workspace.md,
            // "Installing and uninstalling").
            string target = LongName(plan.Root, write.Target, paths)
                ?? throw new McmodInstallException(
                    $"Cannot install {plan.Name}: lip cannot tell which file '{write.Target}'"
                    + " names, as it cannot read the long name of a short name in it, so it"
                    + " does not write it.");
            if (paths.IsLipPath(target)) {
              throw new McmodInstallException(
                  $"Cannot install {plan.Name}: it writes '{target}', inside"
                  + $" {LockStore.LipDirectory}, which is reserved for lip. Report this to the"
                  + " author of the package.");
            }
            if (owners.OwnerOf(target) is string owner
                && !PackageNames.AreEqual(owner, plan.Name)) {
              throw new McmodInstallException(
                  $"Cannot install {plan.Name}: it writes '{target}', which belongs to {owner}."
                  + " Uninstall one of the packages first, or ask the authors of the packages to"
                  + " resolve the conflict.");
            }

            // An existing file matching preserve is kept, as it is now.
            string full = ToFullPath(plan.Root, target);
            if (MatchesAny(write.Preserve, target, paths) && _fs.File.Exists(full)) {
              RunTrace.Write("install", $"{plan.Name}: keeping the existing {target} (preserve)");
              break;
            }

            try {
              await using Stream content = await write.Source.OpenRead(write.Key);
              await transaction.WriteFile(full, content, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
              throw new McmodInstallException(
                  $"Cannot install {plan.Name}: lip cannot write '{target}'.", ex);
            }

            owners.Add(target, plan.Name);
            if (ownedSet.Add(target)) {
              owned.Add(target);
            }
            break;
          }
        case DeleteFileStep delete:
          try {
            // An earlier package of the operation may own it now, and a hook command may have
            // deleted it.
            if (OwnedByAnother(plan, owners, delete.FullPath)) {
              break;
            }
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
          DeleteMatches(plan, matches, transaction, owners);
          break;
        default:
          throw new ArgumentException(
              $"lip planned a step it does not know ({step}). This is a bug in lip.",
              nameof(plan));
      }
    }

    return owned;
  }

  /// Deletes what uninstallation steps 2–4 match in the workspace as it is now
  /// (protocol/lip-workspace.md, "Atomicity" rule 4), beyond what the plan already deleted. A file
  /// that another package owns now is kept, and so is a matched directory holding it.
  private void DeleteMatches(
      PackagePlan plan,
      DeleteMatchesStep step,
      WorkspaceTransaction transaction,
      FileOwners owners) {
    WorkspacePaths paths = plan.Paths;
    WorkspaceView now = new(_fs, paths: paths);
    List<string> files = [.. step.OwnedFiles];
    List<string> directories = [];
    foreach (PathGlob glob in step.Purge) {
      foreach ((string path, bool isDirectory) in EnumerateMatches(plan.Root, glob, now, paths)) {
        if (isDirectory) {
          if (!paths.IsModsDirectory(path)) {
            directories.Add(path);
          }
        } else {
          files.Add(path);
        }
      }
    }

    List<string> deleted = [];
    foreach (string file in files.Distinct(paths.Comparer)) {
      string full = ToFullPath(plan.Root, file);
      if (!_fs.File.Exists(full) || OwnedByAnother(plan, owners, full)) {
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
    HashSet<string> scheduled = new(paths.Comparer);
    foreach (string directory in directories) {
      Schedule(ToFullPath(plan.Root, directory));
    }
    foreach (string path in deleted.Concat(directories)) {
      for (string? dir = _fs.Path.GetDirectoryName(ToFullPath(plan.Root, path));
          dir is not null && IsInside(plan.Root, dir);
          dir = _fs.Path.GetDirectoryName(dir)) {
        string relative = _fs.Path.GetRelativePath(plan.Root, dir)
            .Replace(_fs.Path.DirectorySeparatorChar, '/');
        if (paths.IsModsDirectory(relative)) {
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

  /// Whether a package other than the one of the plan owns the file now, so that uninstalling
  /// the package keeps it (protocol/lip-workspace.md, "Installing and uninstalling").
  private bool OwnedByAnother(PackagePlan plan, FileOwners owners, string full) {
    string relative = _fs.Path.GetRelativePath(plan.Root, full)
        .Replace(_fs.Path.DirectorySeparatorChar, '/');
    if (owners.OwnerOf(relative) is not string owner || PackageNames.AreEqual(owner, plan.Name)) {
      return false;
    }

    RunTrace.Write("install", $"{plan.Name}: not deleting {relative}: {owner} owns it");
    return true;
  }

  /// The owners of the files of the installed packages (protocol/lip-workspace.md, "Installing and
  /// uninstalling"), by the long names of the files, compared as the paths of the workspace are
  /// for the environment; the first package recording a file owns it.
  public FileOwners Owners(
      string root, IEnumerable<LockedPackage> packages, LipEnvironment environment) {
    string full = _fs.Path.GetFullPath(root);
    WorkspacePaths paths = WorkspacePaths.For(environment);
    FileOwners owners = new(paths);
    foreach (LockedPackage package in packages) {
      foreach (string file in package.Files.Select(NormalizeRelative)) {
        owners.TryAdd(LongName(full, file, paths) ?? file, package.Identity);
      }
    }

    return owners;
  }

  /// The entry of an installed package (protocol/state.json.md): the entry of its variant in
  /// its recorded package file for the environment.
  public static PackageEntry LockedEntry(LockedPackage package, LipEnvironment environment) {
    PackageFile manifest;
    try {
      manifest = PackageFileParser.Parse(package.Manifest);
    }
    catch (FormatException ex) {
      throw new McmodInstallException(
          $"The package file that the state file ({LockStore.LipDirectory}/"
          + $"{LockStore.FileName}) records for {package.Identity} {package.Version} is invalid."
          + " If you edited the state file by hand, undo the change.",
          ex);
    }

    string variant = package.Variant == ""
        ? "the default variant"
        : $"the variant \"{package.Variant}\"";
    return manifest.SelectEntry(package.Variant, environment)
        ?? throw new McmodInstallException(
            $"The package file that the state file ({LockStore.LipDirectory}/"
            + $"{LockStore.FileName}) records for {package.Identity} {package.Version} has no"
            + $" entry of {variant} for the side {environment.Side.ToSpecString()} and"
            + $" the platform {environment.Platform}. If you passed --platform, pass the platform"
            + " the workspace was installed for instead.");
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

  /// Plans the writes of step 2 of installing a package (protocol/lip-workspace.md, "Installing and
  /// uninstalling").
  private async Task<List<PlannedWrite>> Plan(
      Candidate candidate,
      MountSource source,
      string root,
      InstallContext context,
      WorkspaceView view,
      WorkspacePaths paths) {
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
    Dictionary<string, (string Target, ISource Source, string Key)> targets = new(paths.Comparer);
    void Add(string target, ISource from, string key) {
      // "a/./b" is "a/b". Windows drops trailing dots and spaces, which would alias another
      // path, e.g. one in .lip or owned by another package, and takes ':' for an alternate data
      // stream; so does Wine for a Windows platform.
      string[] segments = IsSafeRelativePath(target)
          ? [.. target.Split('/').Where(s => s != ".")]
          : [];
      if (segments.Length == 0) {
        throw new McmodInstallException(
            $"Cannot install {label}: its package file places a file at '{target}', which is not a"
            + " valid path inside the workspace (a relative path with '/' separators and without"
            + " '..'). Report this to the author of the package.");
      }
      if (segments.Any(paths.IsInvalidName)) {
        throw new McmodInstallException(
            $"Cannot install {label}: its package file places a file at '{target}', which is not a"
            + " valid path on Windows (a name in it ends with '.' or a space, or contains ':')."
            + " Report this to the author of the package.");
      }
      target = LongName(root, string.Join('/', segments), paths)
          ?? throw new McmodInstallException(
              $"Cannot install {label}: lip cannot tell which file '{target}' names, as it"
              + " cannot read the long name of a short name in it.");
      if (paths.IsLipPath(target)) {
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

      // The package file of the package is never written (protocol/lip-workspace.md, "Installing
      // and uninstalling" step 2): its lip.toml, or, for an archive without one, the tooth.json it
      // is read from.
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

    Dictionary<string, string> owners = new(paths.Comparer);
    foreach (LockedPackage locked in context.Lock.Packages
        .Where(p => !PackageNames.AreEqual(p.Identity, package.Identity))) {
      foreach (string file in locked.Files.Select(NormalizeRelative)) {
        owners.TryAdd(LongName(root, file, paths) ?? file, locked.Identity);
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
    HashSet<string> checkedDirectories = new(paths.Comparer);
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

      plan.Add(new PlannedWrite(
          target, from, key, full,
          MatchesAny(entry.PreserveFiles, target, paths) && view.FileExists(full)));
    }
    return plan;
  }

  /// Plans the writes of one placement of a "self" or "zip" asset (protocol/lip-workspace.md,
  /// "Installing and uninstalling" step 2). files are the (path in the asset, key) pairs of the
  /// asset.
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
      string root,
      string platform) {
    foreach (string command in commands) {
      RunTrace.Write("install", $"{name}: running the {stage} command '{command}'");
      try {
        await _commandRunner.Run(command, root, platform);
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
      WorkspaceView view,
      WorkspacePaths paths) {
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
      if (LongName(root, glob.Pattern, paths) is string file && !paths.IsLipPath(file)
          && view.FileExists(ToFullPath(root, file))) {
        yield return (file, false);
      }
      yield break;
    }

    // The walk starts at the long name of the literal leading directory, so that the paths it
    // finds are checked by their long names; the glob is matched against them as it spells that
    // directory.
    string start = string.Concat(segments[..literal].Select(s => s + "/"));
    if (LongName(root, start, paths) is not string longStart) {
      yield break;
    }
    string startFull = ToFullPath(root, longStart);
    if ((start != "" && !IsInside(root, startFull)) || !view.DirectoryExists(startFull)
        || paths.IsLipPath(longStart)) {
      yield break;
    }

    int? maxDepth = segments[literal..].Contains("**") ? null : segments.Length - literal;
    if (literal == segments.Length) {
      // A literal directory path. mods is never matched, so a pattern naming it removes nothing
      // below it either.
      if (paths.IsModsDirectory(longStart)) {
        yield break;
      }
      yield return (longStart.TrimEnd('/'), true);
      foreach ((string, bool) entry in Walk(startFull, all: true, depth: 1)) {
        yield return entry;
      }
      yield break;
    }

    foreach ((string, bool) entry in Walk(startFull, all: false, depth: 1)) {
      yield return entry;
    }

    IEnumerable<(string Path, bool IsDirectory)> Walk(string directory, bool all, int depth) {
      List<WorkspaceView.Entry> infos = [.. view.Enumerate(directory)];
      if (paths.IgnoresCase) {
        // Entries whose names equal each other ignoring case have one path when the paths of the
        // workspace are case-insensitive, a path naming more than one existing file or
        // directory (protocol/lip-workspace.md, "Environment and layout").
        foreach (IGrouping<string, string> group in infos
            .Select(e => _fs.Path.GetFileName(e.FullName))
            .GroupBy(n => n, paths.Comparer)) {
          List<string> names = [.. group.Order(StringComparer.Ordinal)];
          if (names.Count > 1) {
            string parent = _fs.Path.GetRelativePath(root, directory)
                .Replace(_fs.Path.DirectorySeparatorChar, '/');
            throw new AmbiguousPathException(
                parent == "." ? names[0] : $"{parent}/{names[0]}", names);
          }
        }
      }

      foreach (WorkspaceView.Entry info in infos) {
        string path = _fs.Path.GetRelativePath(root, info.FullName)
            .Replace(_fs.Path.DirectorySeparatorChar, '/');
        if (paths.IsLipPath(path)) {
          continue;
        }
        // A path below the long name of the start is matched as the glob spells the start. A
        // path that the start as spelled does not lead to, as when Windows drops the trailing
        // dots of a name in it, is matched as it is.
        string spelled = longStart != start && paths.StartsWith(path, longStart)
            ? start + path[longStart.Length..]
            : path;
        string suffix = info.IsDirectory ? "/" : "";
        // A path below mods does not match only because mods does (protocol/lip-workspace.md,
        // "Installing and uninstalling").
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        bool belowMods = slash >= 0 && paths.IsModsDirectory(path[..slash]);
        bool matched = all
            || (!(info.IsDirectory && paths.IsModsDirectory(path))
                && glob.IsMatch(
                    spelled + suffix, minSegments: belowMods ? 2 : 0,
                    ignoreCase: paths.IgnoresCase));
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

  /// Whether a glob matches a workspace-relative path, ignoring case when its paths are
  /// case-insensitive.
  private static bool MatchesAny(
      IReadOnlyList<PathGlob> globs, string path, WorkspacePaths paths) =>
      globs.Any(g => g.IsMatch(path, ignoreCase: paths.IgnoresCase));

  private string ToFullPath(string root, string relative) =>
      relative.TrimEnd('/') == ""
          ? root
          : _fs.Path.GetFullPath(_fs.Path.Combine(
              root,
              relative.TrimEnd('/').Replace('/', _fs.Path.DirectorySeparatorChar)));

  /// A workspace-relative path with each 8.3 short name in it replaced by the long name it
  /// stands for, on Windows: "LIP~1/state.json" is ".lip/state.json" when ".lip" has the
  /// short name "LIP~1". Only names that exist can be short names, and every short name contains
  /// '~', so other paths are returned as they are without asking the file system, and so are
  /// paths that are not safe workspace paths. Null when a name containing '~' exists but its
  /// long name cannot be read: such a path cannot be checked, so it fails the checks.
  ///
  /// Elsewhere, on a file system that ignores case, or when the paths of the workspace are
  /// case-insensitive, each name in the path that exists is spelled as the file system has it
  /// (Spelled): ".LIP/lock" is ".lip/lock" when ".lip" exists. Throws AmbiguousPathException when
  /// the path names more than one existing file or directory.
  private string? LongName(string root, string relative, WorkspacePaths paths) {
    if (!IsSafeRelativePath(relative.TrimEnd('/'))) {
      return relative;
    }
    if (!paths.OnWindows) {
      bool fileSystemIgnoresCase = IgnoresCase(root);
      return fileSystemIgnoresCase || paths.IgnoresCase
          ? Spelled(root, relative, paths, fileSystemIgnoresCase)
          : relative;
    }
    if (!relative.Contains('~')) {
      return relative;
    }

    // The longest leading part of the path that exists.
    string[] segments = relative.TrimEnd('/').Split('/');
    int existing = segments.Length;
    string full = ToFullPath(root, relative);
    while (existing > 0 && !_fs.File.Exists(full) && !_fs.Directory.Exists(full)) {
      existing--;
      full = ToFullPath(root, string.Join('/', segments[..existing]));
    }
    if (!segments[..existing].Any(s => s.Contains('~'))) {
      return relative;
    }
    if (GetLongPath(root) is not string longRoot || GetLongPath(full) is not string longPath) {
      return null;
    }

    string head = _fs.Path.GetRelativePath(longRoot, longPath)
        .Replace(_fs.Path.DirectorySeparatorChar, '/');
    if (head == "." || head == ".." || head.StartsWith("../", StringComparison.Ordinal)
        || _fs.Path.IsPathRooted(head)) {
      return null;
    }

    return string.Join('/', [head, .. segments[existing..]])
        + (relative.EndsWith('/') ? "/" : "");
  }

  /// A workspace-relative path with each name in it that exists spelled as the file system has
  /// it: "MODS/a.dll" is "mods/a.dll" when "mods" exists. When the paths of the workspace are
  /// case-insensitive, a name is the existing name equal to it ignoring the case of A-Z, and a
  /// name equal to more than one so is a failure (protocol/lip-workspace.md, "Environment and
  /// layout"); on a file system that ignores case, it is also the name the file system finds for
  /// it. Null when a directory on the way cannot be listed: such a path cannot be checked.
  private string? Spelled(
      string root, string relative, WorkspacePaths paths, bool fileSystemIgnoresCase) {
    string[] segments = relative.TrimEnd('/').Split('/');
    string directory = root;
    for (int i = 0; i < segments.Length; i++) {
      List<string> names;
      try {
        if (!_fs.Directory.Exists(directory)) {
          break;
        }
        names = [.. _fs.Directory.EnumerateFileSystemEntries(directory)
            .Select(p => _fs.Path.GetFileName(p))];
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        RunTrace.Write("install", $"Cannot list {directory} to spell {relative}: {ex.Message}");
        return null;
      }

      string segment = segments[i];
      string? name;
      if (paths.IgnoresCase) {
        List<string> equal = [.. names.Where(n => paths.AreEqual(n, segment))];
        if (equal.Count > 1) {
          throw new AmbiguousPathException(
              string.Join('/', [.. segments[..i], segment]),
              [.. equal.Order(StringComparer.Ordinal)]);
        }
        name = equal.FirstOrDefault();
      } else {
        name = names.FirstOrDefault(n => n == segment);
      }
      if (name is null && fileSystemIgnoresCase) {
        name = names.FirstOrDefault(n => string.Equals(
            n.Normalize(), segment.Normalize(), StringComparison.OrdinalIgnoreCase));
      }
      if (name is null) {
        break;
      }
      segments[i] = name;
      directory = _fs.Path.Join(directory, name);
    }

    return string.Join('/', segments) + (relative.EndsWith('/') ? "/" : "");
  }

  /// Whether the file system of the workspace root ignores case in names, as macOS does by
  /// default: a name in the root spelled in another case finds the same entry, and no other
  /// entry has that spelling. Taken as false while the root holds no name with a letter.
  private bool IgnoresCase(string root) {
    if (_ignoresCase.TryGetValue(root, out bool known)) {
      return known;
    }

    List<string> names;
    try {
      names = [.. _fs.Directory.EnumerateFileSystemEntries(root)
          .Select(p => _fs.Path.GetFileName(p))];
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("install", $"Cannot list {root} to tell whether it ignores case: {ex.Message}");
      return false;
    }

    bool Exists(string name) {
      string path = _fs.Path.Join(root, name);
      return _fs.File.Exists(path) || _fs.Directory.Exists(path);
    }

    foreach (string name in names) {
      string swapped = string.Concat(name.Select(
          c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)));
      // A link that leads nowhere does not tell.
      if (swapped == name || !Exists(name)) {
        continue;
      }

      bool ignores = !names.Contains(swapped, StringComparer.Ordinal) && Exists(swapped);
      RunTrace.Write("install", $"The file system of {root} "
          + (ignores ? "ignores case" : "tells case apart"));
      return _ignoresCase[root] = ignores;
    }

    return false;
  }

  /// The absolute path with its 8.3 short names replaced by long names (GetLongPathNameW), or
  /// null when it cannot be found. A path of MAX_PATH characters or more is passed with the
  /// "\\?\" prefix, without which GetLongPathNameW fails on it.
  private static string? GetLongPath(string path) {
    const string Prefix = @"\\?\";
    const string UncPrefix = @"\\?\UNC\";
    string query = path.Length < 260 || path.StartsWith(Prefix, StringComparison.Ordinal)
        ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal)
            ? UncPrefix + path[2..]
            : Prefix + path;
    char[] buffer = new char[query.Length + 260];
    while (true) {
      uint length = GetLongPathNameW(query, buffer, (uint)buffer.Length);
      if (length == 0) {
        return null;
      }
      if (length < buffer.Length) {
        string result = new(buffer, 0, (int)length);
        if (ReferenceEquals(query, path)) {
          return result;
        }
        return result.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase)
            ? @"\\" + result[UncPrefix.Length..]
            : result.StartsWith(Prefix, StringComparison.Ordinal)
                ? result[Prefix.Length..]
                : result;
      }
      buffer = new char[length];
    }
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern uint GetLongPathNameW(string shortPath, char[] longPath, uint length);

  /// Whether path is strictly below directory.
  private bool IsInside(string directory, string path) {
    string prefix = _fs.Path.TrimEndingDirectorySeparator(directory)
        + _fs.Path.DirectorySeparatorChar;
    return WorkspacePaths.Host.StartsWith(path, prefix);
  }

  /// A recorded path, "a/./b" being "a/b" as when it was recorded.
  private static string NormalizeRelative(string path) =>
      string.Join('/', path.Replace('\\', '/').Split('/').Where(s => s != "."));

  /// A non-empty '/'-separated relative path as lip.schema.toml #/definitions/path allows it:
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
