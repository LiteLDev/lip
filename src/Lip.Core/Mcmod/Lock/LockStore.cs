using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when a state file exists but cannot be read.
public class LockFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// Reads and writes the state file (protocol/workspace.md §7.1, state.schema.json), called the
/// lock in the code. It is a JSON file (protocol/README.md), read and written through JsonFile.
///
/// The state file lives inside the workspace, in &lt;root&gt;/.lip/state.json, so that it
/// follows the workspace when the workspace is moved, renamed or copied. Next to it in .lip are
/// the workspace lock (WorkspaceMutex) and the journal (WorkspaceTransaction). A workspace without
/// the state file has no installed packages, unless it has a tooth_lock.json
/// (ToothLockMigration), which the first state file written replaces (CommitStaged).
public partial class LockStore(IFileSystem fs) {
  /// The directory of lip's own files in the workspace root, and the files in it.
  public const string LipDirectory = ".lip";
  public const string FileName = "state.json";
  public const string StagedFileName = "state.json.tmp";
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
    if (!_fs.File.Exists(path)) {
      RunTrace.Write("workspace", $"No state file at {path}: no packages are installed");
      return null;
    }

    byte[] bytes;
    try {
      bytes = await _fs.File.ReadAllBytesAsync(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw WorkspaceErrors.Wrap(ex, $"Cannot read the state file '{path}'.");
    }

    LockFile lockFile = Parse(bytes, path);
    RunTrace.Write("workspace", $"Read the state file {path}: side {lockFile.Side},"
        + $" platform {lockFile.Platform}, {lockFile.Packages.Count} packages");
    return lockFile;
  }

  /// Writes the lock: Stage, then CommitStaged.
  public async Task Save(string workspaceRoot, LockFile lockFile) {
    await Stage(workspaceRoot, lockFile);
    CommitStaged(workspaceRoot);
  }

  /// Writes the lock to &lt;root&gt;/.lip/state.json.tmp, replacing an earlier staged lock.
  public async Task Stage(string workspaceRoot, LockFile lockFile) {
    try {
      Validate(lockFile);
    }
    catch (LockFormatException ex) {
      throw new ArgumentException(
          "lip was about to write an invalid state file, and did not write it. This is a bug"
          + " in lip.",
          nameof(lockFile),
          ex);
    }

    string root = NormalizeRoot(workspaceRoot);
    CreateLipDirectory(root);
    string tempPath = LipPath(root, StagedFileName);

    byte[] bytes = JsonFile.SerializeToUtf8Bytes(JsonSerializer.SerializeToElement(lockFile));
    try {
      await _fs.File.WriteAllBytesAsync(tempPath, bytes);
      RunTrace.Write("workspace", $"Wrote the new state file {tempPath}"
          + $" ({lockFile.Packages.Count} packages)");
    }
    catch (Exception ex) {
      try {
        if (_fs.File.Exists(tempPath)) {
          _fs.File.Delete(tempPath);
        }
      }
      catch (Exception delete) when (delete is IOException or UnauthorizedAccessException) {
        // A staged state file is never read, and the next save replaces it.
        RunTrace.Write("workspace", $"Cannot delete {tempPath}: {delete.Message}");
      }

      if (ex is IOException or UnauthorizedAccessException) {
        throw WorkspaceErrors.Wrap(ex, $"Cannot write the new state file '{tempPath}'.");
      }

      throw;
    }
  }

  /// Moves the staged lock into place in one rename, the point at which an operation commits.
  /// Does nothing when no lock is staged. Throws only when the lock was not moved.
  ///
  /// The first state file of a workspace whose state file was derived from its
  /// tooth_lock.json replaces it: tooth_lock.json is deleted once the state file is in place
  /// (protocol/compatibility.md §3).
  public void CommitStaged(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);
    string path = LipPath(root, FileName);
    string tempPath = LipPath(root, StagedFileName);
    if (!_fs.File.Exists(tempPath)) {
      return;
    }

    bool migrated = !_fs.File.Exists(path) && ToothLockMigration.Read(_fs, root) is not null;
    try {
      _fs.File.Move(tempPath, path, overwrite: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw WorkspaceErrors.Wrap(
          ex, $"Cannot replace the state file '{path}' with the new one '{tempPath}'.");
    }

    RunTrace.Write("workspace", $"Moved {tempPath} to {path}");
    if (!migrated) {
      return;
    }

    // The state file is in place; what follows must not fail the operation.
    string toothLock = _fs.Path.Join(root, ToothLockMigration.FileName);
    try {
      _fs.File.Delete(toothLock);
      RunTrace.Write("workspace", $"Deleted {toothLock}, which the state file replaces");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // A tooth_lock.json is not read once the state file exists.
      RunTrace.Write("workspace", $"Cannot delete {toothLock}: {ex.Message}");
    }
  }

  /// Deletes the staged lock, if any.
  public void DiscardStaged(string workspaceRoot) {
    string tempPath = LipPath(NormalizeRoot(workspaceRoot), StagedFileName);
    if (!_fs.File.Exists(tempPath)) {
      return;
    }

    try {
      _fs.File.Delete(tempPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw WorkspaceErrors.Wrap(ex, $"Cannot delete the unused new state file '{tempPath}'.");
    }

    RunTrace.Write("workspace", $"Deleted {tempPath}");
  }

  /// Creates &lt;root&gt;/.lip, hidden like on other platforms, where its name hides it.
  public void CreateLipDirectory(string workspaceRoot) {
    string root = NormalizeRoot(workspaceRoot);
    string directory = _fs.Path.Join(root, LipDirectory);
    if (_fs.Directory.Exists(directory)) {
      return;
    }

    try {
      _fs.Directory.CreateDirectory(directory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw WorkspaceErrors.Wrap(
          ex,
          $"Cannot create the directory '{directory}', where lip keeps the state file. Check"
          + " that you may write to the workspace.");
    }

    RunTrace.Write("workspace", $"Created {directory}");
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
    bool otherFormat = false;
    try {
      using JsonDocument document = JsonFile.Parse(bytes);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException("The file is not a JSON object.");
      }

      int? format = PackageFileParser.Format(root);
      if (format != PackageFormat.Format) {
        otherFormat = format is not null;
        throw new LockFormatException(
            $"\"{PackageFormat.FormatKey}\" is "
            + (format is null ? "missing or not a number" : $"{format}")
            + $", but this version of lip reads only {PackageFormat.Format}. If another"
            + " version of lip wrote the file, use that version.");
      }

      // Before the required keys are read, so that a state file of lip before v0.40 gets the
      // rename instead of a missing "side".
      if (PackageFileParser.UsesOldEnvironmentKeys(root)) {
        otherFormat = true;
        throw new LockFormatException(
            $"The file {PackageFileParser.OldEnvironmentKeysReason}.");
      }

      // "commit" is omitted, not null, for a package that is not from a remote repository.
      if (root.TryGetProperty("packages", out JsonElement packages)
          && packages.ValueKind == JsonValueKind.Array) {
        int i = 0;
        foreach (JsonElement package in packages.EnumerateArray()) {
          if (package.ValueKind == JsonValueKind.Object
              && package.TryGetProperty("commit", out JsonElement commit)
              && commit.ValueKind != JsonValueKind.String) {
            throw new LockFormatException(
                $"packages[{i}].commit is {commit.ValueKind.ToString().ToLowerInvariant()}, but it"
                + " must be a string of 40 lowercase hexadecimal digits, or be left out.");
          }

          i++;
        }
      }

      LockFile lockFile = root.Deserialize<LockFile>(_readOptions)
          ?? throw new LockFormatException(
              "The file is null instead of a JSON object.");
      Validate(lockFile);
      return lockFile;
    }
    catch (Exception ex) when (ex is JsonException or LockFormatException or ArgumentException
        or NotSupportedException or InvalidOperationException) {
      RunTrace.Write("workspace", $"Cannot read the state file {path}: {ex.Message}");
      throw new LockFormatException(otherFormat
          ? $"The state file '{path}' was written by a different version of lip, so this lip"
            + " cannot tell which packages are installed."
          : $"The state file '{path}' is invalid, so lip cannot tell which packages are"
            + " installed. If you edited it by hand, undo the change; if you have a copy of the"
            + " file, restore it.",
          ex);
    }
  }

  /// Checks what state.schema.json requires beyond what deserializing checks.
  private static void Validate(LockFile lockFile) {
    if (lockFile.Packages is null) {
      throw new LockFormatException("\"packages\" is null, but it must be an array.");
    }

    if (lockFile.Format != PackageFormat.Format) {
      throw new LockFormatException(
          $"\"{PackageFormat.FormatKey}\" is {lockFile.Format}, but it must be"
          + $" {PackageFormat.Format}.");
    }

    if (lockFile.Side is null || !McmodSideExtensions.TryParse(lockFile.Side, out _)) {
      throw new LockFormatException(
          $"\"side\" is \"{lockFile.Side}\", but it must be \"server\" or \"client\".");
    }

    if (lockFile.Platform is null or "") {
      throw new LockFormatException(
          "\"platform\" is missing or empty, but it must be a runtime identifier such as"
          + " \"win-x64\".");
    }

    for (int i = 0; i < lockFile.Packages.Count; i++) {
      LockedPackage package = lockFile.Packages[i]
          ?? throw new LockFormatException($"packages[{i}] is null, but it must be an object.");
      string at = $"packages[{i}]";
      if (package.Files is null) {
        throw new LockFormatException($"{at}.files is null, but it must be an array of paths.");
      }

      int nullFile = package.Files.FindIndex(f => f is null);
      if (nullFile >= 0) {
        throw new LockFormatException(
            $"{at}.files[{nullFile}] is null, but it must be a workspace-relative path.");
      }

      if (package.Variant != "" && !Ref.IsValidVariant(package.Variant)) {
        throw new LockFormatException(
            $"{at}.variant is \"{package.Variant}\", which is not a valid variant name.");
      }

      if (package.Manifest.ValueKind != JsonValueKind.Object) {
        throw new LockFormatException(
            $"{at}.manifest is {package.Manifest.ValueKind.ToString().ToLowerInvariant()}, but it"
            + " must be the package file of the package, a mapping.");
      }

      try {
        _ = PackageFileParser.Parse(package.Manifest);
      }
      catch (PackageFormatException ex) {
        throw new LockFormatException(
            $"{at}.manifest, the package file of {DescribePackage(package)}, is not a valid"
            + " package file.",
            ex);
      }

      if (package.Commit is not null && !CommitRegex().IsMatch(package.Commit)) {
        throw new LockFormatException(
            $"{at}.commit is \"{package.Commit}\", but it must be 40 lowercase hexadecimal"
            + " digits.");
      }
    }
  }

  /// The identity and version of a package for messages, as far as its manifest tells.
  private static string DescribePackage(LockedPackage package) =>
      package.Id == "" ? "a package" : $"{package.Identity} {package.Version}".TrimEnd();

  [GeneratedRegex(@"^[0-9a-f]{40}\z")]
  private static partial Regex CommitRegex();
}
