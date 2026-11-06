using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when a workspace file exists but cannot be read.
public class LockFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// Reads and writes the workspace file (protocol/workspace.md §7.1, workspace.schema.yaml), called
/// the lock in the code. It is a YAML file (protocol/README.md), read and written through
/// YamlJson.
///
/// The workspace file lives inside the workspace, in &lt;root&gt;/.lip/workspace.yaml, so that it
/// follows the workspace when the workspace is moved, renamed or copied. Next to it in .lip are
/// the workspace lock (WorkspaceMutex) and the journal (WorkspaceTransaction). A workspace without
/// the workspace file has no installed packages, unless it has a tooth_lock.json
/// (ToothLockMigration), which the first workspace file written replaces (CommitStaged).
public partial class LockStore(IFileSystem fs) {
  /// The directory of lip's own files in the workspace root, and the files in it.
  public const string LipDirectory = ".lip";
  public const string FileName = "workspace.yaml";
  public const string StagedFileName = "workspace.yaml.tmp";
  public const string MutexFileName = "lock";
  public const string JournalFileName = "journal";

  private static readonly JsonSerializerOptions _readOptions = new() {
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
  };


  private readonly IFileSystem _fs = fs;

  /// The path of the workspace lock file (WorkspaceMutex), &lt;root&gt;/.lip/lock.
  public string GetMutexPath(string workspaceRoot) => LipPath(workspaceRoot, MutexFileName);

  /// The path of the journal (WorkspaceTransaction), &lt;root&gt;/.lip/journal.
  public string GetJournalPath(string workspaceRoot) => LipPath(workspaceRoot, JournalFileName);

  private string LipPath(string workspaceRoot, string name) =>
      _fs.Path.Join(NormalizeRoot(workspaceRoot), LipDirectory, name);

  /// Returns null when none exists. Throws LockFormatException when the lock exists but cannot
  /// be read.
  public async Task<LockFile?> Load(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);

    string path = LipPath(root, FileName);
    if (_fs.File.Exists(path)) {
      return Parse(await _fs.File.ReadAllBytesAsync(path), path);
    }

    return null;
  }

  /// Writes the lock: Stage, then CommitStaged.
  public async Task Save(string workspaceRoot, LockFile lockFile) {
    await Stage(workspaceRoot, lockFile);
    CommitStaged(workspaceRoot);
  }

  /// Writes the lock to &lt;root&gt;/.lip/workspace.yaml.tmp, replacing an earlier staged lock.
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

    byte[] bytes = YamlJson.SerializeToUtf8Bytes(JsonSerializer.SerializeToElement(lockFile));
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

  /// Moves the staged lock into place in one rename, the point at which an operation commits.
  /// Does nothing when no lock is staged. Throws only when the lock was not moved.
  ///
  /// The first workspace file of a workspace whose workspace file was derived from its
  /// tooth_lock.json replaces it: tooth_lock.json is deleted once the workspace file is in place
  /// (protocol/compatibility.md §3).
  public void CommitStaged(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);
    string path = LipPath(root, FileName);
    string tempPath = LipPath(root, StagedFileName);
    if (!_fs.File.Exists(tempPath)) {
      return;
    }

    bool migrated = !_fs.File.Exists(path) && ToothLockMigration.Read(_fs, root) is not null;
    _fs.File.Move(tempPath, path, overwrite: true);
    if (!migrated) {
      return;
    }

    // The workspace file is in place; what follows must not fail the operation.
    try {
      _fs.File.Delete(_fs.Path.Join(root, ToothLockMigration.FileName));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // A tooth_lock.json is not read once the workspace file exists.
    }
  }

  /// Deletes the staged lock, if any.
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
      using JsonDocument document = YamlJson.Parse(bytes);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException("the top level is not an object.");
      }

      if (PackageFileParser.Format(root) != PackageFormat.Format) {
        throw new LockFormatException(
            $"{PackageFormat.FormatKey} is not {PackageFormat.Format}.");
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
        or NotSupportedException or InvalidOperationException or YamlFormatException) {
      throw new LockFormatException($"Cannot read the workspace file '{path}': {ex.Message}", ex);
    }
  }

  /// Checks what workspace.schema.yaml requires beyond what deserializing checks.
  private static void Validate(LockFile lockFile) {
    if (lockFile.Packages is null) {
      throw new LockFormatException("packages is null.");
    }

    if (lockFile.Format != PackageFormat.Format) {
      throw new LockFormatException($"{PackageFormat.FormatKey} is not {PackageFormat.Format}.");
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
        _ = PackageFileParser.Parse(package.Manifest);
      }
      catch (PackageFormatException ex) {
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
