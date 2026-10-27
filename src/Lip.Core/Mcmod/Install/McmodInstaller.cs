using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Install;

/// Thrown when installing or uninstalling a package fails (spec §7). Failures detected while
/// planning leave the workspace untouched.
public class McmodInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Installs and uninstalls manifest.json packages: mods and file packages (spec §7).
///
/// Ownership: a file is owned by a package when the package wrote it. A file that already
/// exists and matches preserveFiles is kept and not written, so it is not owned by the package,
/// unless the lock already records it as owned by a package of the same identity (reinstall or
/// update while the old entry is still in the lock), in which case ownership carries over.
///
/// Paths are recorded and compared in their canonical form (WorkspaceLayout.CanonicalPath):
/// a file package placing a file under plugins/ writes and owns it under mods/. Globs relative to
/// the workspace root match both forms. The mods and plugins directories are never deleted.
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

  public bool CanHandle(string kind) => kind == McmodPackage.KindName;

  public async Task<IReadOnlyList<string>> Install(Candidate candidate, InstallContext context) {
    if (candidate.Package is not McmodPackage package) {
      throw new ArgumentException(
          $"Expected a {McmodPackage.KindName} package, got '{candidate.Package.Kind}'.",
          nameof(candidate));
    }

    ConcreteDocument document = package.Document;
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    string baseDir = BaseDirectory(document, context.Environment);
    ISource source = candidate.Archive.Source;

    // 1. Plan: every check happens before anything is written or run.
    List<PlannedWrite> plan = Plan(document, baseDir, source, root, context.Lock);

    if (context.DryRun) {
      foreach (PlannedWrite write in plan) {
        await _ui.PrintInfo(write.Keep
            ? $"Would keep {write.Target} (preserved)"
            : $"Would write {write.Target}");
      }
      return OwnedFiles(plan);
    }

    // Fetch content before running anything, so that a fetch failure changes nothing.
    List<PlannedWrite> writes = [.. plan.Where(w => !w.Keep)];
    try {
      await source.Prefetch(writes.Select(w => w.SourceKey).Distinct());
    }
    catch (Exception ex) {
      throw new McmodInstallException(
          $"{document.Name}: cannot fetch the archive content: {ex.Message}", ex);
    }

    // 2. preInstall.
    if (!context.IgnoreScripts) {
      await RunScripts(document.Name, "preInstall", document.Scripts.PreInstall, root);
    }

    // 3. Write files, rolling back on failure.
    WriteTransaction transaction = new(_fs, writes.Select(w => w.FullPath));
    try {
      foreach (PlannedWrite write in writes) {
        await using Stream content = await source.OpenRead(write.SourceKey);
        await transaction.Write(write.FullPath, content);
      }

      // 4. postInstall.
      if (!context.IgnoreScripts) {
        await RunScripts(document.Name, "postInstall", document.Scripts.PostInstall, root);
      }
    }
    catch (Exception ex) {
      IReadOnlyList<string> errors = transaction.Rollback();
      foreach (string error in errors) {
        await _ui.PrintWarning($"Rollback: {error}");
      }
      if (ex is McmodInstallException) {
        throw;
      }
      throw new McmodInstallException(
          $"Failed to install {document.Name}; written files were rolled back: {ex.Message}", ex);
    }
    transaction.Commit();

    return OwnedFiles(plan);
  }

  public async Task Uninstall(LockedPackage package, InstallContext context) {
    ConcreteDocument document = ParseLockedManifest(package);
    string root = _fs.Path.GetFullPath(context.WorkspaceRoot);
    string baseDir = BaseDirectory(document, context.Environment);
    string stopDir = document.IsFilePackage
        ? root
        : ToFullPath(root, context.Environment.Platform.ModsDirectory());

    // 1. preUninstall.
    if (!context.DryRun && !context.IgnoreScripts) {
      await RunScripts(document.Name, "preUninstall", document.Scripts.PreUninstall, root);
    }

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
      if (!IsSafeRelativePath(file) || ownedByOthers.Contains(file)) {
        continue;
      }
      if (MatchesAny(document.PreserveFiles, file, baseDir)) {
        continue;
      }
      if (_fs.File.Exists(ToFullPath(root, file)) && fileSet.Add(file)) {
        files.Add(file);
      }
    }

    // 3. Paths matching removeFiles that no other package owns.
    List<string> directories = [];
    HashSet<string> directorySet = new(PathComparer);
    foreach (PathGlob glob in document.RemoveFiles) {
      foreach ((string path, bool isDirectory) in EnumerateMatches(root, baseDir, glob)) {
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
      return;
    }

    foreach (string file in files) {
      _fs.File.Delete(ToFullPath(root, file));
    }

    // Directory patterns delete whole directories, except what other packages own. Deepest
    // first, so that a matched directory containing matched directories empties bottom-up.
    foreach (string directory in directories.OrderByDescending(d => d.Count(c => c == '/'))) {
      string full = ToFullPath(root, directory);
      if (_fs.Directory.Exists(full) && IsEmpty(full)) {
        _fs.Directory.Delete(full);
      }
    }

    // 4. Directories that became empty because of the deletions above.
    foreach (string path in files.Concat(directories)) {
      PruneEmptyAncestors(root, ToFullPath(root, path), stopDir);
    }

    // 5. postUninstall.
    if (!context.IgnoreScripts) {
      await RunScripts(document.Name, "postUninstall", document.Scripts.PostUninstall, root);
    }
  }

  /// The directory globs are relative to, workspace-relative with a trailing '/' ("" for the
  /// workspace root).
  private static string BaseDirectory(ConcreteDocument document, LipEnvironment environment) {
    if (!NamePattern().IsMatch(document.Name)) {
      // The parser enforces this (spec §4.2); the name becomes a path segment here.
      throw new McmodInstallException($"Invalid package name '{document.Name}'.");
    }
    return document.IsFilePackage
        ? ""
        : $"{environment.Platform.ModsDirectory()}/{document.Name}/";
  }

  [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
  private static partial Regex NamePattern();

  private List<PlannedWrite> Plan(
      ConcreteDocument document,
      string baseDir,
      ISource source,
      string root,
      LockFile lockFile) {
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
      if (!targets.ContainsKey(target)) {
        order.Add(target);
      }
      targets[target] = (target, key);
    }

    List<string> keys = [.. source.Keys];
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
          if (!keys.Contains(placement.Src, StringComparer.Ordinal)) {
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
      if (_fs.Directory.Exists(full)) {
        throw new McmodInstallException($"{document.Name}: '{target}' is an existing directory.");
      }
      for (string? parent = _fs.Path.GetDirectoryName(full);
          parent is not null && IsInside(root, parent) && checkedDirectories.Add(parent);
          parent = _fs.Path.GetDirectoryName(parent)) {
        if (_fs.File.Exists(parent)) {
          throw new McmodInstallException(
              $"{document.Name}: cannot write '{target}': a parent path is an existing file.");
        }
      }

      bool keep = _fs.File.Exists(full) && MatchesAny(document.PreserveFiles, target, baseDir);
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

  private ConcreteDocument ParseLockedManifest(LockedPackage package) {
    ManifestDocument document;
    try {
      document = _parser.Parse(Encoding.UTF8.GetBytes(package.Manifest.GetRawText()));
    }
    catch (Exception ex) {
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
      PathGlob glob) {
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
      if (_fs.File.Exists(ToFullPath(root, file))) {
        yield return (file, false);
      }
      yield break;
    }

    string start = WorkspaceLayout.CanonicalPath(
        baseDir + string.Concat(segments[..literal].Select(s => s + "/")));
    string startFull = ToFullPath(root, start);
    if ((start != "" && !IsInside(root, startFull)) || !_fs.Directory.Exists(startFull)) {
      yield break;
    }

    IDirectoryInfo startInfo = _fs.DirectoryInfo.New(startFull);
    int? maxDepth = segments[literal..].Contains("**") ? null : segments.Length - literal;
    if (literal == segments.Length) {
      // A literal directory path.
      yield return (start.TrimEnd('/'), true);
      foreach ((string, bool) entry in Walk(startInfo, all: true, depth: 1)) {
        yield return entry;
      }
      yield break;
    }

    foreach ((string, bool) entry in Walk(startInfo, all: false, depth: 1)) {
      yield return entry;
    }

    IEnumerable<(string Path, bool IsDirectory)> Walk(
        IDirectoryInfo directory,
        bool all,
        int depth) {
      foreach (IFileSystemInfo info in directory.EnumerateFileSystemInfos()) {
        bool isDirectory = info is IDirectoryInfo;
        bool isLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        string path = _fs.Path.GetRelativePath(root, info.FullName)
            .Replace(_fs.Path.DirectorySeparatorChar, '/');
        string suffix = isDirectory ? "/" : "";
        bool matched = all || GlobForms(path, baseDir).Any(f => glob.IsMatch(f + suffix));
        if (matched) {
          yield return (WorkspaceLayout.CanonicalPath(path), isDirectory);
        }
        if (isDirectory && !isLink && (matched || maxDepth is null || depth < maxDepth)) {
          foreach ((string, bool) entry in Walk((IDirectoryInfo)info, matched, depth + 1)) {
            yield return entry;
          }
        }
      }
    }
  }
  /// Deletes the empty directories above a deleted path, up to stopDir (exclusive), never mods
  /// or plugins.
  private void PruneEmptyAncestors(string root, string deletedPath, string stopDir) {
    for (string? dir = _fs.Path.GetDirectoryName(deletedPath);
        dir is not null && IsInside(stopDir, dir);
        dir = _fs.Path.GetDirectoryName(dir)) {
      string relative = _fs.Path.GetRelativePath(root, dir)
          .Replace(_fs.Path.DirectorySeparatorChar, '/');
      if (!_fs.Directory.Exists(dir) || !IsEmpty(dir)
          || WorkspaceLayout.IsLayoutDirectory(relative)) {
        return;
      }
      _fs.Directory.Delete(dir);
    }
  }

  private bool IsEmpty(string directory) =>
      !_fs.Directory.EnumerateFileSystemEntries(directory).Any();

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

  /// Writes files and remembers how to undo them: overwritten files are first renamed to a
  /// backup next to them, created directories are recorded. Backups never take the name of a
  /// planned write.
  private sealed class WriteTransaction(IFileSystem fs, IEnumerable<string> planned) {
    private readonly HashSet<string> _planned = new(planned, PathComparer);
    private readonly List<string> _written = [];
    private readonly List<(string Target, string Backup, bool WasHidden)> _backups = [];
    private readonly List<string> _createdDirectories = [];

    public async Task Write(string path, Stream content) {
      string directory = fs.Path.GetDirectoryName(path)!;
      List<string> missing = [];
      for (string? d = directory; d is not null && !fs.Directory.Exists(d);
          d = fs.Path.GetDirectoryName(d)) {
        missing.Add(d);
      }
      fs.Directory.CreateDirectory(directory);
      _createdDirectories.AddRange(missing);

      if (fs.File.Exists(path)) {
        // A hidden sibling: same volume (a cheap rename), and never in a directory that gets
        // cleaned.
        string backup = BackupPath(path);
        bool wasHidden = fs.File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
        fs.File.Move(path, backup);
        fs.SetHidden(backup, true);
        _backups.Add((path, backup, wasHidden));
      }

      _written.Add(path);
      await using FileSystemStream output = fs.File.Create(path);
      await content.CopyToAsync(output);
    }

    /// ".{name}.bak" in the directory of the file, or ".{name}.bak1", ".{name}.bak2", ... when
    /// that name is taken.
    private string BackupPath(string path) {
      string directory = fs.Path.GetDirectoryName(path)!;
      string name = $".{fs.Path.GetFileName(path)}.bak";
      string backup = fs.Path.Join(directory, name);
      for (int i = 1;
          fs.File.Exists(backup) || fs.Directory.Exists(backup) || _planned.Contains(backup);
          i++) {
        backup = fs.Path.Join(directory, $"{name}{i}");
      }
      return backup;
    }

    public void Commit() {
      foreach ((_, string backup, _) in _backups) {
        try {
          fs.File.Delete(backup);
        }
        catch (IOException) {
          // A leftover backup is harmless.
        }
      }
    }

    /// Best effort; returns what could not be undone.
    public IReadOnlyList<string> Rollback() {
      List<string> errors = [];
      void Try(Action action, string what) {
        try {
          action();
        }
        catch (Exception ex) {
          errors.Add($"{what}: {ex.Message}");
        }
      }

      foreach (string path in Enumerable.Reverse(_written)) {
        Try(() => {
          if (fs.File.Exists(path)) {
            fs.File.Delete(path);
          }
        }, $"cannot delete {path}");
      }
      foreach ((string target, string backup, bool wasHidden) in Enumerable.Reverse(_backups)) {
        Try(() => {
          fs.File.Move(backup, target);
          fs.SetHidden(target, wasHidden);
        }, $"cannot restore {target} from {backup}");
      }
      // Children before parents.
      foreach (string directory in _createdDirectories.OrderByDescending(d => d.Length)) {
        Try(() => {
          if (fs.Directory.Exists(directory)
              && !fs.Directory.EnumerateFileSystemEntries(directory).Any()) {
            fs.Directory.Delete(directory);
          }
        }, $"cannot delete {directory}");
      }
      return errors;
    }
  }
}
