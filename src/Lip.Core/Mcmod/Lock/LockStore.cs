using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when a workspace lock exists but cannot be read.
public class LockFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// Reads and writes the workspace lock (spec §8).
///
/// The lock (format 4) lives inside the workspace, in &lt;root&gt;/.lip/lock.json, so that it
/// follows the workspace when the workspace is moved, renamed or copied.
///
/// When that lock is absent, a v3 &lt;root&gt;/tooth_lock.json written by lip v0.x is migrated. The
/// next Save writes .lip/lock.json and deletes tooth_lock.json.
///
/// v3 recorded the absolute paths of the files a package placed (relative to the working
/// directory, which was the workspace root). Migration turns paths under the workspace root into
/// workspace-relative '/'-separated paths, under mods for those under plugins (spec §7). When
/// the workspace was moved after install, its paths are under the old root: the old root is
/// inferred from where the recorded files are now (see MovedRoot), and paths under it are rebased
/// onto the workspace. Other paths outside the root (a v3 placement that escaped the root) are
/// kept verbatim as absolute paths; lip never deletes them, as they are not inside the workspace.
///
/// Migrated entries have kind "tooth" and record the tooth.json (spec §9), like the entries of
/// tooth.json packages lip installs itself: they are converted to format 4 when used.
///
/// v3 keyed packages by (tooth path, variant); v4 keys them by identity, which for a tooth.json
/// package is the tooth path followed by "#label" for a labelled variant (spec §9.1), so the
/// variants of one tooth migrate to entries of distinct identities.
public class LockStore(IFileSystem fs, string? dataDirectory = null) : ILockStore {
  /// The directory of lip's own files in the workspace root, and the lock in it.
  public const string LipDirectory = ".lip";
  public const string FileName = "lock.json";

  /// The v3 lock lip v0.x kept in the workspace root.
  public const string LegacyFileName = "tooth_lock.json";

  /// Kind of packages migrated from a v3 lock (IPackage.Kind).
  public const string ToothKind = Compat.ToothCompat.KindName;

  private static readonly JsonSerializerOptions _readOptions = new() {
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    // Hand-edited locks may put the archive "kind" after the other properties.
    AllowOutOfOrderMetadataProperties = true,
  };

  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
  };

  private readonly IFileSystem _fs = fs;
  private readonly string _dataDirectory = dataDirectory ?? LipDirectories.Data;

  /// The path of the lock of a workspace.
  public string GetLockPath(string workspaceRoot) =>
      _fs.Path.Join(NormalizeRoot(workspaceRoot), LipDirectory, FileName);

  /// The data directory path, without extension, of lip's files about the process changing a
  /// workspace (its journal and its inter-process lock). They stay with the path: a workspace
  /// moved or copied does not take an interrupted operation of the original with it.
  public string GetStatePath(string workspaceRoot) =>
      _fs.Path.Join(_dataDirectory, "workspaces", GetWorkspaceHash(workspaceRoot));

  public async Task<LockFile> Load(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);

    string path = GetLockPath(root);
    if (_fs.File.Exists(path)) {
      return Parse(await _fs.File.ReadAllBytesAsync(path), path);
    }

    string legacyPath = _fs.Path.Combine(root, LegacyFileName);
    if (_fs.File.Exists(legacyPath)) {
      return Migrate(await _fs.File.ReadAllBytesAsync(legacyPath), legacyPath, root);
    }

    return new LockFile();
  }

  public async Task Save(string workspaceRoot, LockFile lockFile) {
    await Stage(workspaceRoot, lockFile);
    CommitStaged(workspaceRoot);
  }

  public async Task Stage(string workspaceRoot, LockFile lockFile) {
    try {
      if (lockFile.FormatVersion != ManifestFormat.Version
          || lockFile.FormatUuid != ManifestFormat.Uuid) {
        throw new LockFormatException(
            $"format {lockFile.FormatVersion}/{lockFile.FormatUuid} is not"
            + $" {ManifestFormat.Version}/{ManifestFormat.Uuid}.");
      }

      Validate(lockFile);
    }
    catch (LockFormatException ex) {
      throw new ArgumentException($"Cannot save an invalid lock: {ex.Message}", nameof(lockFile));
    }

    string root = NormalizeRoot(workspaceRoot);
    string path = GetLockPath(root);
    CreateLipDirectory(root);
    string tempPath = StagedPath(path);

    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(lockFile, _writeOptions);
    try {
      // A leftover of an interrupted save; on Windows a hidden file cannot be overwritten.
      if (_fs.File.Exists(tempPath)) {
        _fs.File.Delete(tempPath);
      }

      await _fs.File.WriteAllBytesAsync(tempPath, bytes);
      _fs.SetHidden(tempPath, true);
    }
    catch {
      if (_fs.File.Exists(tempPath)) {
        _fs.File.Delete(tempPath);
      }

      throw;
    }
  }

  public void CommitStaged(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);
    string path = GetLockPath(root);
    string tempPath = StagedPath(path);
    if (!_fs.File.Exists(tempPath)) {
      return;
    }

    // Migrating: the lock was read from tooth_lock.json, which is deleted below. Once
    // .lip/lock.json exists, a file of that name is not lip's.
    bool migrating = !_fs.File.Exists(path);
    _fs.File.Move(tempPath, path, overwrite: true);

    // The lock is in place; what follows must not fail the operation.
    try {
      _fs.SetHidden(path, false);
      string legacyPath = _fs.Path.Combine(root, LegacyFileName);
      if (migrating && _fs.File.Exists(legacyPath)) {
        _fs.File.Delete(legacyPath);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // A hidden lock or a leftover tooth_lock.json is read correctly.
    }
  }

  public void DiscardStaged(string workspaceRoot) {
    string tempPath = StagedPath(GetLockPath(NormalizeRoot(workspaceRoot)));
    if (_fs.File.Exists(tempPath)) {
      _fs.File.Delete(tempPath);
    }
  }

  /// Creates &lt;root&gt;/.lip, hidden like on other platforms, where its name hides it.
  private void CreateLipDirectory(string root) {
    string directory = _fs.Path.Join(root, LipDirectory);
    if (_fs.Directory.Exists(directory)) {
      return;
    }

    _fs.Directory.CreateDirectory(directory);
    try {
      _fs.SetHidden(directory, true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // A visible .lip works the same.
    }
  }

  /// The hidden temporary file a lock is staged in, next to the lock.
  private string StagedPath(string lockPath) =>
      _fs.Path.Join(
          _fs.Path.GetDirectoryName(lockPath)!, $".{_fs.Path.GetFileName(lockPath)}.tmp");

  /// The lowercase hex SHA-256 of the normalised absolute workspace path: the full path without
  /// a trailing separator, lower-cased on Windows, where paths are case-insensitive.
  public string GetWorkspaceHash(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);
    if (OperatingSystem.IsWindows()) {
      root = root.ToLowerInvariant();
    }

    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
  }

  private string NormalizeRoot(string workspaceRoot) =>
      _fs.Path.TrimEndingDirectorySeparator(_fs.Path.GetFullPath(workspaceRoot));

  private static LockFile Parse(byte[] bytes, string path) {
    try {
      using JsonDocument document = JsonDocument.Parse(bytes);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException("the top level is not an object.");
      }

      if (!root.TryGetProperty(ManifestFormat.FormatVersionKey, out JsonElement version)
          || version.ValueKind != JsonValueKind.Number
          || !version.TryGetInt32(out int v)
          || v != ManifestFormat.Version) {
        throw new LockFormatException(
            $"unsupported {ManifestFormat.FormatVersionKey}"
            + $" {Describe(root, ManifestFormat.FormatVersionKey)},"
            + $" expected {ManifestFormat.Version}.");
      }

      if (!root.TryGetProperty(ManifestFormat.FormatUuidKey, out JsonElement uuid)
          || uuid.ValueKind != JsonValueKind.String
          || uuid.GetString() != ManifestFormat.Uuid) {
        throw new LockFormatException(
            $"unsupported {ManifestFormat.FormatUuidKey}"
            + $" {Describe(root, ManifestFormat.FormatUuidKey)},"
            + $" expected \"{ManifestFormat.Uuid}\".");
      }

      LockFile lockFile = root.Deserialize<LockFile>(_readOptions)
          ?? throw new LockFormatException("the lock is null.");
      Validate(lockFile);
      return lockFile;
    }
    catch (Exception ex) when (ex is JsonException or LockFormatException or ArgumentException
        or NotSupportedException or InvalidOperationException) {
      throw new LockFormatException($"Cannot read lock '{path}': {ex.Message}", ex);
    }
  }

  private static void Validate(LockFile lockFile) {
    if (lockFile.Packages is null || lockFile.FormatUuid is null) {
      throw new LockFormatException("packages or formatUuid is null.");
    }

    if (lockFile.Platform is not null
        && !McmodPlatformExtensions.TryParse(lockFile.Platform, out _)) {
      throw new LockFormatException(
          $"invalid platform \"{lockFile.Platform}\", expected \"server\" or \"client\".");
    }

    if (lockFile.Rid is "") {
      throw new LockFormatException("rid is empty.");
    }

    for (int i = 0; i < lockFile.Packages.Count; i++) {
      LockedPackage package = lockFile.Packages[i]
          ?? throw new LockFormatException($"packages[{i}] is null.");
      if (package.Chain.Any(h => h is null) || package.Files.Any(f => f is null)) {
        throw new LockFormatException($"packages[{i}] ({package.Identity}) has a null entry.");
      }

      if (package.Manifest.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException(
            $"packages[{i}] ({package.Identity}) has a manifest that is not an object.");
      }
    }
  }

  private LockFile Migrate(byte[] bytes, string path, string workspaceRoot) {
    WorkspaceState state;
    try {
      state = JsonSerializer.Deserialize<WorkspaceState>(bytes)
          ?? throw new LockFormatException("the lock is null.");
      if (state.Packages is null || state.Packages.Any(p => p is null)) {
        throw new LockFormatException("packages is null or has a null entry.");
      }
    }
    catch (Exception ex) when (ex is JsonException or LockFormatException or ArgumentException
        or FormatException or NotSupportedException or InvalidOperationException) {
      throw new LockFormatException($"Cannot read legacy lock '{path}': {ex.Message}", ex);
    }

    string root = _fs.Path.GetFullPath(workspaceRoot);
    string? movedRoot = MovedRoot(
        [.. state.Packages.SelectMany(p => p.Files ?? []).Where(f => IsOutside(f, root))], root);

    return new LockFile {
      Packages = [.. state.Packages.Select(p => MigratePackage(p, root, movedRoot))],
    };
  }

  /// The root a moved v3 workspace had: of the directories containing the recorded paths outside
  /// the workspace, the one under which most of them exist at the same relative path in the
  /// workspace (on a tie, the one containing most of them). Null when none does.
  private string? MovedRoot(IReadOnlyList<string> outside, string root) {
    List<string> files = [.. outside
        .Where(_fs.Path.IsPathRooted)
        .Select(f => _fs.Path.GetFullPath(f))
        .Distinct(WorkspaceLayout.PathComparer)];
    HashSet<string> candidates = new(WorkspaceLayout.PathComparer);
    foreach (string file in files) {
      for (string? dir = _fs.Path.GetDirectoryName(file);
          dir is not null;
          dir = _fs.Path.GetDirectoryName(dir)) {
        candidates.Add(dir);
      }
    }

    string? best = null;
    (int Found, int Contained) bestScore = (0, 0);
    foreach (string candidate in candidates) {
      List<string> contained = [.. files.Where(f => IsUnder(candidate, f))];
      int found = contained.Count(f => _fs.File.Exists(
          _fs.Path.Join(root, _fs.Path.GetRelativePath(candidate, f))));
      if (found > bestScore.Found
          || (found == bestScore.Found && found > 0 && contained.Count > bestScore.Contained)) {
        best = candidate;
        bestScore = (found, contained.Count);
      }
    }

    return best;
  }

  private LockedPackage MigratePackage(
      WorkspaceStatePackage package, string root, string? movedRoot) {
    string path = package.Manifest.Path;
    string version = package.Manifest.Version.ToString();

    return new LockedPackage {
      Identity = Compat.ToothCompat.GetIdentity(path, package.Variant),
      Version = version,
      Kind = ToothKind,
      Explicit = package.IsExplicit,
      Chain = [new LockedHop(new PackageId(path, package.Variant).ToString(), null)],
      ArchiveKey = $"{path}@{version}",
      Archive = null,
      Files = [
        .. (package.Files ?? [])
            .Select(f => WorkspaceLayout.CanonicalPath(MigrateFilePath(f, root, movedRoot)))
            .Distinct(WorkspaceLayout.PathComparer),
      ],
      Manifest = JsonSerializer.SerializeToElement(package.Manifest),
    };
  }

  private string MigrateFilePath(string file, string root, string? movedRoot) {
    if (IsOutside(file, root)) {
      return movedRoot is not null && _fs.Path.IsPathRooted(file)
          && IsUnder(movedRoot, _fs.Path.GetFullPath(file))
          ? _fs.Path.GetRelativePath(movedRoot, _fs.Path.GetFullPath(file)).Replace('\\', '/')
          : file;
    }

    return _fs.Path.GetRelativePath(root, _fs.Path.GetFullPath(_fs.Path.Combine(root, file)))
        .Replace('\\', '/');
  }

  /// Whether a v3 path names no file inside the workspace root. A Windows absolute path read on
  /// another OS (a lock copied across machines) is foreign: it is outside rather than mistaken
  /// for a relative path.
  private bool IsOutside(string file, string root) {
    if (!_fs.Path.IsPathRooted(file) && IsWindowsAbsolute(file)) {
      return true;
    }

    string full = _fs.Path.GetFullPath(_fs.Path.Combine(root, file));
    string relative = _fs.Path.GetRelativePath(root, full);
    return relative == "." || relative == ".." || _fs.Path.IsPathRooted(relative)
        || relative.StartsWith(".." + _fs.Path.DirectorySeparatorChar)
        || relative.StartsWith("../");
  }

  /// Whether path is strictly below directory.
  private bool IsUnder(string directory, string path) =>
      path.StartsWith(
          _fs.Path.TrimEndingDirectorySeparator(directory) + _fs.Path.DirectorySeparatorChar,
          OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

  private static bool IsWindowsAbsolute(string path) =>
      path.StartsWith(@"\\")
      || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'
          && path[2] is '\\' or '/');

  private static string Describe(JsonElement root, string key) =>
      root.TryGetProperty(key, out JsonElement value) ? value.GetRawText() : "(missing)";
}
