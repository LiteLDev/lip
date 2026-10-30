using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Install;

/// Thrown when installing or uninstalling a package fails (spec §7). Failures detected while
/// planning leave the workspace untouched.
public class McmodInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Installs and uninstalls manifest.json packages: mods and file packages (spec §7), and the
/// file packages tooth.json archives are converted to (spec §9; lock kind "tooth", whose recorded
/// manifest is the tooth.json, converted again to uninstall).
///
/// Ownership: a file is owned by a package when the package wrote it. A file that already
/// exists and matches preserveFiles is kept and not written, so it is not owned by the package,
/// unless the lock already records it as owned by a package of the same identity (reinstall or
/// update while the old entry is still in the lock), in which case ownership carries over.
///
/// Paths are recorded and compared in their canonical form (WorkspaceLayout.CanonicalPath):
/// a file package placing a file under plugins/ writes and owns it under mods/. Globs relative to
/// the workspace root match both forms. The mods and plugins directories are never deleted, and
/// removeFiles never matches them (so a directory pattern naming them removes nothing). .lip,
/// where lip keeps the lock, is never written, deleted or matched.
public partial class McmodInstaller(
    IFileSystem fileSystem,
    ICommandRunner commandRunner,
    IDocumentParser parser,
    IUserInteraction userInteraction) : IMcmodInstaller {
  private const string RootManifest = "manifest.json";

  private readonly IFileSystem _fs = fileSystem;
  private readonly ICommandRunner _commandRunner = commandRunner;
  private readonly IDocumentParser _parser = parser;
  private readonly IUserInteraction _ui = userInteraction;

  /// How workspace paths are compared: ignoring case on Windows, ordinal elsewhere.
  public static StringComparer PathComparer => WorkspaceLayout.PathComparer;

  private static readonly StringComparison PathComparison =
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

  public bool CanHandle(string kind) => kind is McmodPackage.KindName or ToothCompat.KindName;

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
    if (candidate.Package is not McmodPackage package) {
      throw new ArgumentException(
          $"Expected a {McmodPackage.KindName} package, got '{candidate.Package.Kind}'.",
          nameof(candidate));
    }

    ConcreteDocument document = package.Document;
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    string baseDir = BaseDirectory(document, context.Environment);
    ISource source = candidate.Archive.Source;

    // Every check happens before anything is written or run.
    List<PlannedWrite> plan = Plan(document, baseDir, source, root, context.Lock, view);
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
        await source.Prefetch(writes.Select(w => w.SourceKey).Distinct());
      }
      catch (Exception ex) {
        throw new McmodInstallException(
            $"{document.Name}: cannot fetch the archive content: {ex.Message}", ex);
      }
    }

    // preInstall, the writes, postInstall.
    List<PlannedStep> steps = [];
    if (!context.IgnoreScripts && document.Scripts.PreInstall.Count > 0) {
      steps.Add(new ScriptStep("preInstall", document.Scripts.PreInstall));
    }

    steps.AddRange(writes.Select(w => new WriteStep(w.Target, w.FullPath, source, w.SourceKey)));
    if (!context.IgnoreScripts && document.Scripts.PostInstall.Count > 0) {
      steps.Add(new ScriptStep("postInstall", document.Scripts.PostInstall));
    }

    return new PackagePlan(document.Name, root, OwnedFiles(plan), steps);
  }

  public async Task<PackagePlan> PlanUninstall(
      LockedPackage package, InstallContext context, WorkspaceView view) {
    ConcreteDocument document = await LockedDocument(package, context.Environment);
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    string baseDir = BaseDirectory(document, context.Environment);
    string stopDir = document.IsFilePackage
        ? root
        : ToFullPath(root, context.Environment.Platform.ModsDirectory());

    // The paths to delete are those of the workspace before preUninstall runs.
    HashSet<string> ownedByOthers = new(
        context.Lock.Packages
            .Where(p => !PackageNames.AreEqual(p.Identity, package.Identity))
            .SelectMany(p => p.Files)
            .Select(NormalizeRelative),
        PathComparer);

    // 2. Owned files not matching preserveFiles.
    List<string> files = [];
    HashSet<string> fileSet = new(PathComparer);
    foreach (string file in package.Files.Select(NormalizeRelative)) {
      if (!IsSafeRelativePath(file) || WorkspaceLayout.IsLipPath(file)
          || ownedByOthers.Contains(file)) {
        continue;
      }
      if (MatchesAny(document.PreserveFiles, file, baseDir)) {
        continue;
      }
      if (view.FileExists(ToFullPath(root, file)) && fileSet.Add(file)) {
        files.Add(file);
      }
    }

    // Paths matching removeFiles that no other package owns.
    List<string> directories = [];
    HashSet<string> directorySet = new(PathComparer);
    foreach (PathGlob glob in document.RemoveFiles) {
      foreach ((string path, bool isDirectory) in EnumerateMatches(root, baseDir, glob, view)) {
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
    if (!context.IgnoreScripts && document.Scripts.PreUninstall.Count > 0) {
      steps.Add(new ScriptStep("preUninstall", document.Scripts.PreUninstall));
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

    // Directories that became empty because of the deletions above.
    foreach (string path in files.Concat(directories)) {
      for (string? dir = _fs.Path.GetDirectoryName(ToFullPath(root, path));
          dir is not null && IsInside(stopDir, dir);
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

    if (!context.IgnoreScripts && document.Scripts.PostUninstall.Count > 0) {
      steps.Add(new ScriptStep("postUninstall", document.Scripts.PostUninstall));
    }

    return new PackagePlan(document.Name, root, [], steps);
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
            // A script may have deleted it.
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

  /// The directory globs are relative to, workspace-relative with a trailing '/' ("" for the
  /// workspace root).
  private static string BaseDirectory(ConcreteDocument document, LipEnvironment environment) {
    if (document.IsFilePackage) {
      // The name of a file package converted from a tooth.json is its tooth path (with
      // "#label" for a labelled variant), never a path segment.
      return "";
    }
    if (!NamePattern().IsMatch(document.Name)) {
      // The parser enforces this (spec §4.2); the name becomes a path segment here.
      throw new McmodInstallException($"Invalid package name '{document.Name}'.");
    }
    return $"{environment.Platform.ModsDirectory()}/{document.Name}/";
  }

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
  private static partial Regex NamePattern();

  private List<PlannedWrite> Plan(
      ConcreteDocument document,
      string baseDir,
      ISource source,
      string root,
      LockFile lockFile,
      WorkspaceView view) {
    // Target (workspace-relative) -> source key. Later entries overwrite earlier ones.
    List<string> order = [];
    Dictionary<string, (string Target, string Key)> targets = new(PathComparer);
    void Add(string target, string key) {
      if (!IsSafeRelativePath(target)
          // Windows drops trailing dots and spaces, which would alias another path.
          || target.Split('/').Any(s => s.EndsWith('.') || s.EndsWith(' '))) {
        throw new McmodInstallException(
            $"{document.Name}: '{target}' is not a valid path inside the workspace.");
      }
      target = WorkspaceLayout.CanonicalPath(target);
      if (WorkspaceLayout.IsLipPath(target)) {
        throw new McmodInstallException(
            $"{document.Name}: '{target}' is inside {LockStore.LipDirectory}, which is reserved"
            + " for lip.");
      }
      if (!targets.ContainsKey(target)) {
        order.Add(target);
      }
      targets[target] = (target, key);
    }

    List<string> keys = [.. source.Keys];
    HashSet<string> keySet = new(keys, StringComparer.Ordinal);
    // In key order: a later placement of the same target wins.
    document = ToothPlacements.Expand(document, keys);
    if (!document.IsFilePackage) {
      if (document.Placements.Count > 0) {
        throw new McmodInstallException(
            $"{document.Name}: placements are only allowed in file packages.");
      }
      foreach (string key in keys) {
        Add(baseDir + key, key);
      }
    } else {
      foreach (Placement placement in document.Placements) {
        if (placement.IsDirectory) {
          List<string> matched = [.. keys.Where(
              k => k.StartsWith(placement.Src, StringComparison.Ordinal))];
          if (placement.Src != "" && matched.Count == 0) {
            throw new McmodInstallException(
                $"{document.Name}: placement source '{placement.Src}' is not in the archive.");
          }
          foreach (string key in matched.Where(k => k != RootManifest)) {
            Add(placement.Dest + key[placement.Src.Length..], key);
          }
        } else {
          if (!keySet.Contains(placement.Src)) {
            throw new McmodInstallException(
                $"{document.Name}: placement source '{placement.Src}' is not in the archive.");
          }
          if (placement.Src != RootManifest) {
            Add(placement.Dest, placement.Src);
          }
        }
      }
    }

    Dictionary<string, string> owners = new(PathComparer);
    HashSet<string> ownedBySelf = new(PathComparer);
    foreach (LockedPackage locked in lockFile.Packages) {
      bool self = PackageNames.AreEqual(locked.Identity, document.Name);
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
          $"{document.Name}: files already owned by other packages: "
          + string.Join(", ", conflicts));
    }

    List<PlannedWrite> plan = [];
    HashSet<string> checkedDirectories = new(PathComparer);
    foreach (string normalized in order) {
      (string target, string key) = targets[normalized];
      string full = ToFullPath(root, target);
      if (!IsInside(root, full)) {
        throw new McmodInstallException($"{document.Name}: '{target}' escapes the workspace.");
      }
      if (view.DirectoryExists(full)) {
        throw new McmodInstallException($"{document.Name}: '{target}' is an existing directory.");
      }
      for (string? parent = _fs.Path.GetDirectoryName(full);
          parent is not null && IsInside(root, parent) && checkedDirectories.Add(parent);
          parent = _fs.Path.GetDirectoryName(parent)) {
        if (view.FileExists(parent)) {
          throw new McmodInstallException(
              $"{document.Name}: cannot write '{target}': a parent path is an existing file.");
        }
      }

      bool keep = view.FileExists(full) && MatchesAny(document.PreserveFiles, target, baseDir);
      plan.Add(new PlannedWrite(target, key, full, keep, keep && ownedBySelf.Contains(target)));
    }
    return plan;
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

  private async Task<ConcreteDocument> LockedDocument(
      LockedPackage package, LipEnvironment environment) {
    ManifestDocument? document;
    try {
      document = package.Kind == ToothCompat.KindName
          ? (await ToothCompat.FromLocked(package, environment))?.Package.Document
              ?? throw new McmodInstallException(
                  $"{package.Identity}: the tooth.json recorded in the lock has no variant "
                  + $"for the locked ref in {environment.Rid}.")
          : _parser.Parse(Encoding.UTF8.GetBytes(package.Manifest.GetRawText()));
    }
    catch (Exception ex) when (ex is not McmodInstallException) {
      throw new McmodInstallException(
          $"{package.Identity}: the manifest recorded in the lock is invalid: {ex.Message}", ex);
    }
    return document as ConcreteDocument ?? throw new McmodInstallException(
        $"{package.Identity}: the manifest recorded in the lock is not a concrete document.");
  }

  /// Workspace paths matching the glob, as (workspace-relative path, is directory); everything
  /// below a matched directory is included. Only the glob's literal leading directory is
  /// walked, only as deep as the pattern can reach, and never through links.
  private IEnumerable<(string Path, bool IsDirectory)> EnumerateMatches(
      string root,
      string baseDir,
      PathGlob glob,
      WorkspaceView view) {
    string[] segments = glob.Pattern.TrimEnd('/').Split('/');
    if (segments.Any(s => s is "." or ".." || s.Contains(':'))) {
      // Never look outside the base directory.
      yield break;
    }

    int literal = 0;
    while (literal < segments.Length && !segments[literal].Contains('*')) {
      literal++;
    }

    if (literal == segments.Length && !glob.MatchesDirectories) {
      // A literal file path.
      string file = WorkspaceLayout.CanonicalPath(baseDir + glob.Pattern);
      if (!WorkspaceLayout.IsLipPath(file) && view.FileExists(ToFullPath(root, file))) {
        yield return (file, false);
      }
      yield break;
    }

    string start = WorkspaceLayout.CanonicalPath(
        baseDir + string.Concat(segments[..literal].Select(s => s + "/")));
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
                && GlobForms(path, baseDir).Any(f => glob.IsMatch(f + suffix)));
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

  /// Whether a glob of the package matches a canonical workspace-relative path.
  private static bool MatchesAny(IReadOnlyList<PathGlob> globs, string path, string baseDir) =>
      GlobForms(path, baseDir).Any(f => globs.Any(g => g.IsMatch(f)));

  /// The forms of a canonical workspace-relative path that globs of the package are matched
  /// against: relative to the base directory, and, when that is the workspace root, also
  /// through plugins. None when the path is outside the base directory.
  private static IEnumerable<string> GlobForms(string path, string baseDir) =>
      baseDir == "" ? WorkspaceLayout.GlobForms(path)
      : path.StartsWith(baseDir, PathComparison) ? [path[baseDir.Length..]]
      : [];

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
      string SourceKey,
      string FullPath,
      bool Keep,
      bool StaysOwned);
}
