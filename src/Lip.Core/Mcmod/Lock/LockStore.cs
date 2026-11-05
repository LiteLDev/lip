using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when a workspace file exists but cannot be read.
public class LockFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// Reads and writes the workspace file (protocol/workspace.md §7.1, workspace.schema.json), called
/// the lock in the code.
///
/// The workspace file lives inside the workspace, in &lt;root&gt;/.lip/workspace.json, so that it
/// follows the workspace when the workspace is moved, renamed or copied. Next to it in .lip are
/// the workspace lock (WorkspaceMutex) and the journal (WorkspaceTransaction).
///
/// Workspaces of earlier lip versions (protocol/workspace.md §7.6) are not taken over: a
/// .lip/workspace.json without format_version 4, a .lip/lock.json or a tooth_lock.json at the root
/// cannot be read, so that lip never writes over the files the packages they record own.
public partial class LockStore(IFileSystem fs) : ILockStore {
  /// The directory of lip's own files in the workspace root, and the files in it.
  public const string LipDirectory = ".lip";
  public const string FileName = "workspace.json";
  public const string StagedFileName = "workspace.json.new";
  public const string MutexFileName = "lock";
  public const string JournalFileName = "journal";

  /// The workspace file of lip v0.36, in .lip.
  public const string V036FileName = "lock.json";

  /// The workspace file lip v0.x kept in the workspace root.
  public const string LegacyFileName = "tooth_lock.json";

  private static readonly JsonSerializerOptions _readOptions = new() {
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
  };

  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
  };

  private readonly IFileSystem _fs = fs;

  /// The path of the lock of a workspace.
  public string GetLockPath(string workspaceRoot) => LipPath(workspaceRoot, FileName);

  public string GetMutexPath(string workspaceRoot) => LipPath(workspaceRoot, MutexFileName);

  public string GetJournalPath(string workspaceRoot) => LipPath(workspaceRoot, JournalFileName);

  private string LipPath(string workspaceRoot, string name) =>
      _fs.Path.Join(NormalizeRoot(workspaceRoot), LipDirectory, name);

  public async Task<LockFile?> Load(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);

    string path = LipPath(root, FileName);
    if (_fs.File.Exists(path)) {
      return Parse(await _fs.File.ReadAllBytesAsync(path), path);
    }

    foreach (string earlier in (string[])[
        LipPath(root, V036FileName), _fs.Path.Join(root, LegacyFileName)]) {
      if (_fs.File.Exists(earlier)) {
        throw new LockFormatException(
            $"'{earlier}' is the workspace file of an earlier lip version, which this lip cannot "
            + "take over. Uninstall its packages with that lip version, or delete it to start "
            + "over (lip then knows nothing about the files they installed).");
      }
    }

    return null;
  }

  public async Task Save(string workspaceRoot, LockFile lockFile) {
    await Stage(workspaceRoot, lockFile);
    CommitStaged(workspaceRoot);
  }

  public async Task Stage(string workspaceRoot, LockFile lockFile) {
    try {
      Validate(lockFile);
    }
    catch (LockFormatException ex) {
      throw new ArgumentException($"Cannot save an invalid lock: {ex.Message}", nameof(lockFile));
    }

    string root = NormalizeRoot(workspaceRoot);
    CreateLipDirectory(root);
    string tempPath = LipPath(root, StagedFileName);

    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(lockFile, _writeOptions);
    try {
      await _fs.File.WriteAllBytesAsync(tempPath, bytes);
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
    string tempPath = LipPath(root, StagedFileName);
    if (_fs.File.Exists(tempPath)) {
      _fs.File.Move(tempPath, LipPath(root, FileName), overwrite: true);
    }
  }

  public void DiscardStaged(string workspaceRoot) {
    string tempPath = LipPath(NormalizeRoot(workspaceRoot), StagedFileName);
    if (_fs.File.Exists(tempPath)) {
      _fs.File.Delete(tempPath);
    }
  }

  /// Creates &lt;root&gt;/.lip, hidden like on other platforms, where its name hides it.
  public void CreateLipDirectory(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);
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

  private string NormalizeRoot(string workspaceRoot) =>
      _fs.Path.TrimEndingDirectorySeparator(_fs.Path.GetFullPath(workspaceRoot));

  private static LockFile Parse(byte[] bytes, string path) {
    try {
      using JsonDocument document = JsonDocument.Parse(bytes);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException("the top level is not an object.");
      }

      if (ToothParser.FormatVersion(root) != ToothFormat.FormatVersion) {
        throw new LockFormatException(
            $"it is not of {ToothFormat.FormatVersionKey} {ToothFormat.FormatVersion}, but of an "
            + "earlier lip version, which this lip cannot take over. Uninstall its packages with "
            + "that lip version, or delete it to start over (lip then knows nothing about the "
            + "files they installed).");
      }

      // "commit" is omitted, not null, for a package that is not from a remote repository.
      if (root.TryGetProperty("packages", out JsonElement packages)
          && packages.ValueKind == JsonValueKind.Array
          && packages.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.Object
              && p.TryGetProperty("commit", out JsonElement commit)
              && commit.ValueKind != JsonValueKind.String)) {
        throw new LockFormatException("a package has a commit that is not a string.");
      }

      LockFile lockFile = root.Deserialize<LockFile>(_readOptions)
          ?? throw new LockFormatException("the lock is null.");
      Validate(lockFile);
      return lockFile;
    }
    catch (Exception ex) when (ex is JsonException or LockFormatException or ArgumentException
        or NotSupportedException or InvalidOperationException) {
      throw new LockFormatException($"Cannot read the workspace file '{path}': {ex.Message}", ex);
    }
  }

  /// Checks what workspace.schema.json requires beyond what deserializing checks.
  private static void Validate(LockFile lockFile) {
    if (lockFile.Packages is null) {
      throw new LockFormatException("packages is null.");
    }

    if (lockFile.FormatVersion != ToothFormat.FormatVersion) {
      throw new LockFormatException(
          $"{ToothFormat.FormatVersionKey} is not {ToothFormat.FormatVersion}.");
    }

    if (lockFile.Platform is null
        || !McmodPlatformExtensions.TryParse(lockFile.Platform, out _)) {
      throw new LockFormatException(
          $"invalid platform \"{lockFile.Platform}\", expected \"server\" or \"client\".");
    }

    if (lockFile.Runtime is null or "") {
      throw new LockFormatException("runtime is missing or empty.");
    }

    for (int i = 0; i < lockFile.Packages.Count; i++) {
      LockedPackage package = lockFile.Packages[i]
          ?? throw new LockFormatException($"packages[{i}] is null.");
      string at = $"packages[{i}]";
      if (package.Files is null || package.Files.Any(f => f is null)) {
        throw new LockFormatException($"{at} has a null entry.");
      }

      if (package.Variant != "" && !Ref.IsValidVariant(package.Variant)) {
        throw new LockFormatException($"{at} has the invalid variant \"{package.Variant}\".");
      }

      if (package.Manifest.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException($"{at} has a manifest that is not an object.");
      }

      try {
        _ = ToothParser.Parse(package.Manifest);
      }
      catch (ToothFormatException ex) {
        throw new LockFormatException($"{at} has an invalid manifest: {ex.Message}", ex);
      }

      if (package.Commit is not null && !CommitRegex().IsMatch(package.Commit)) {
        throw new LockFormatException($"{at} has the invalid commit \"{package.Commit}\".");
      }
    }
  }

  [GeneratedRegex(@"^[0-9a-f]{40}\z")]
  private static partial Regex CommitRegex();
}
