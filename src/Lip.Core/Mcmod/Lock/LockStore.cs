using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Entities;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when a workspace lock exists but cannot be read.
public class LockFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// Reads and writes the workspace lock (spec §8).
///
/// The lock lives at &lt;root&gt;/lip_lock.json (format 4). When it is absent, a v3
/// &lt;root&gt;/tooth_lock.json is migrated on Load; the next Save writes lip_lock.json and deletes
/// tooth_lock.json.
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
public class LockStore(IFileSystem fs) : ILockStore {
  public const string FileName = "lip_lock.json";
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

  public async Task<LockFile> Load(string workspaceRoot) {
    string path = _fs.Path.Combine(workspaceRoot, FileName);
    if (_fs.File.Exists(path)) {
      return Parse(await _fs.File.ReadAllBytesAsync(path), path);
    }

    string legacyPath = _fs.Path.Combine(workspaceRoot, LegacyFileName);
    if (_fs.File.Exists(legacyPath)) {
      return Migrate(await _fs.File.ReadAllBytesAsync(legacyPath), legacyPath, workspaceRoot);
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

    _fs.Directory.CreateDirectory(workspaceRoot);
    string path = _fs.Path.Combine(workspaceRoot, FileName);
    string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(lockFile, _writeOptions);
    try {
      await _fs.File.WriteAllBytesAsync(tempPath, bytes);
      _fs.File.Move(tempPath, path, overwrite: true);
    }
    finally {
      if (_fs.File.Exists(tempPath)) {
        _fs.File.Delete(tempPath);
      }
    }

    string legacyPath = _fs.Path.Combine(workspaceRoot, LegacyFileName);
    if (_fs.File.Exists(legacyPath)) {
      _fs.File.Delete(legacyPath);
    }
  }

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
