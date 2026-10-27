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
/// The lock (format 4) lives outside the workspace, in the per-user data directory:
/// &lt;Data&gt;/workspaces/&lt;hash&gt;.json, where hash is the lowercase hex SHA-256 of the
/// normalised absolute workspace path (see GetWorkspaceHash). It records that path in
/// "workspace", and Load fails when it does not match the workspace.
///
/// When that lock is absent, a lock inside the workspace is read instead: a format 4
/// &lt;root&gt;/lip_lock.json written by earlier lip versions, else a v3 &lt;root&gt;/tooth_lock.json,
/// which is migrated. The next Save writes the lock to the data directory and deletes both
/// workspace files.
///
/// v3 recorded the absolute paths of the files a package placed (relative to the working
/// directory, which was the workspace root). Migration turns paths under the workspace root into
/// workspace-relative '/'-separated paths. Paths outside the root (a workspace moved after
/// install, or a v3 placement that escaped the root) are kept verbatim as absolute paths, so
/// uninstalling behaves exactly as v3 would have.
///
/// v3 keyed packages by (tooth path, variant); v4 keys them by identity (the tooth path). A v3
/// lock holding two variants of one tooth migrates to two entries with the same identity; the
/// caller decides how to reconcile them.
public class LockStore(IFileSystem fs, string? dataDirectory = null) : ILockStore {
  /// The format 4 lock earlier lip versions kept in the workspace root.
  public const string WorkspaceFileName = "lip_lock.json";
  public const string LegacyFileName = "tooth_lock.json";

  /// Kind of packages migrated from a v3 lock (IPackage.Kind).
  public const string ToothKind = "tooth";

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
      _fs.Path.Join(_dataDirectory, "workspaces", $"{GetWorkspaceHash(workspaceRoot)}.json");

  /// Owned files under plugins are rewritten to their canonical path under mods (spec §7), so
  /// that locks written before plugins became a link to mods compare correctly.
  public async Task<LockFile> Load(string workspaceRoot) =>
      CanonicalizeFiles(await LoadUnchanged(workspaceRoot));

  private async Task<LockFile> LoadUnchanged(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);

    string path = GetLockPath(root);
    if (_fs.File.Exists(path)) {
      LockFile lockFile = Parse(await _fs.File.ReadAllBytesAsync(path), path);
      if (lockFile.Workspace is not string recorded || !SameWorkspace(recorded, root)) {
        throw new LockFormatException(
            $"Cannot read lock '{path}': it belongs to the workspace "
            + $"'{lockFile.Workspace ?? "(missing)"}', not '{root}'.");
      }

      return lockFile;
    }

    string workspacePath = _fs.Path.Combine(root, WorkspaceFileName);
    if (_fs.File.Exists(workspacePath)) {
      return Parse(await _fs.File.ReadAllBytesAsync(workspacePath), workspacePath);
    }

    string legacyPath = _fs.Path.Combine(root, LegacyFileName);
    if (_fs.File.Exists(legacyPath)) {
      return Migrate(await _fs.File.ReadAllBytesAsync(legacyPath), legacyPath, root);
    }

    return new LockFile();
  }

  public async Task Save(string workspaceRoot, LockFile lockFile) {
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
    // Migrating: the lock was read from the workspace, whose files are deleted below. Once the
    // lock is in the data directory, files of those names in the workspace are not lip's.
    bool migrating = !_fs.File.Exists(path);

    string directory = _fs.Path.GetDirectoryName(path)!;
    _fs.Directory.CreateDirectory(directory);
    string tempPath = _fs.Path.Join(directory, $".{_fs.Path.GetFileName(path)}.tmp");

    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
        lockFile with { Workspace = root }, _writeOptions);
    try {
      // A leftover of an interrupted save; on Windows a hidden file cannot be overwritten.
      if (_fs.File.Exists(tempPath)) {
        _fs.File.Delete(tempPath);
      }

      await _fs.File.WriteAllBytesAsync(tempPath, bytes);
      _fs.SetHidden(tempPath, true);
      _fs.File.Move(tempPath, path, overwrite: true);
      _fs.SetHidden(path, false);
    }
    finally {
      if (_fs.File.Exists(tempPath)) {
        _fs.File.Delete(tempPath);
      }
    }

    if (migrating) {
      foreach (string name in (string[])[WorkspaceFileName, LegacyFileName]) {
        string workspacePath = _fs.Path.Combine(root, name);
        if (_fs.File.Exists(workspacePath)) {
          _fs.File.Delete(workspacePath);
        }
      }
    }
  }

  private static LockFile CanonicalizeFiles(LockFile lockFile) => lockFile with {
    Packages = [
      .. lockFile.Packages.Select(p => p with {
        Files = [
          .. p.Files.Select(WorkspaceLayout.CanonicalPath).Distinct(WorkspaceLayout.PathComparer),
        ],
      }),
    ],
  };

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

  private bool SameWorkspace(string recorded, string root) =>
      string.Equals(
          NormalizeRoot(recorded),
          root,
          OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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

    return new LockFile {
      Packages = [.. state.Packages.Select(p => MigratePackage(p, root))],
    };
  }

  private LockedPackage MigratePackage(WorkspaceStatePackage package, string root) {
    string path = package.Manifest.Path;
    string version = package.Manifest.Version.ToString();

    return new LockedPackage {
      Identity = path,
      Version = version,
      Kind = ToothKind,
      Explicit = package.IsExplicit,
      Chain = [new LockedHop(new PackageId(path, package.Variant).ToString(), null)],
      ArchiveKey = $"{path}@{version}",
      Archive = null,
      Files = [.. (package.Files ?? []).Select(f => MigrateFilePath(f, root))],
      Manifest = JsonSerializer.SerializeToElement(package.Manifest),
    };
  }

  private string MigrateFilePath(string file, string root) {
    // A Windows absolute path read on another OS (a lock copied across machines) is foreign:
    // keep it verbatim rather than mistaking it for a relative path.
    if (!_fs.Path.IsPathRooted(file) && IsWindowsAbsolute(file)) {
      return file;
    }

    string full = _fs.Path.GetFullPath(_fs.Path.Combine(root, file));
    string relative = _fs.Path.GetRelativePath(root, full);
    if (relative == "." || relative == ".." || _fs.Path.IsPathRooted(relative)
        || relative.StartsWith(".." + _fs.Path.DirectorySeparatorChar)
        || relative.StartsWith("../")) {
      return file;
    }

    return relative.Replace('\\', '/');
  }

  private static bool IsWindowsAbsolute(string path) =>
      path.StartsWith(@"\\")
      || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'
          && path[2] is '\\' or '/');

  private static string Describe(JsonElement root, string key) =>
      root.TryGetProperty(key, out JsonElement value) ? value.GetRawText() : "(missing)";
}
