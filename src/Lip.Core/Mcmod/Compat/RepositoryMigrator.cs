using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Migration.PackageManifests;

namespace Lip.Core.Mcmod.Compat;

/// Rewrites the tooth.json of a package repository, of format 1, 2 or 3, as a tooth.json of format
/// 4 (lip migrate, protocol/cli.md §5.8), by ToothMigrator.
///
/// - A repository without tooth.json, or whose tooth.json is already of format 4 or cannot be
///   read, is a failure, and nothing is changed.
/// - Questions are asked through IUserInteraction.Ask before anything is written.
/// - tooth.json is written, indented by two spaces, through the hidden temporary file
///   .tooth.json.tmp and a rename.
///
/// The notes are ToothMigrator's, those of ToothMigrator.DroppedFields, and a last one when the
/// written tooth.json is not valid.
public static class RepositoryMigrator {
  public const string TempFileName = ".tooth.json.tmp";

  private static readonly JsonDocumentOptions _strictJson = new() {
    AllowDuplicateProperties = false,
    AllowTrailingCommas = false,
    CommentHandling = JsonCommentHandling.Disallow,
  };

  public static async Task<IReadOnlyList<string>> Migrate(
      IFileSystem fs, string directory, IUserInteraction ui) {
    string toothPath = fs.Path.Join(directory, ToothFormat.ToothFileName);
    if (!fs.File.Exists(toothPath)) {
      throw new InvalidOperationException(
          $"There is no {ToothFormat.ToothFileName} in {directory}. Run lip migrate in the root "
          + "directory of the package repository.");
    }

    List<string> notes = [];
    ToothMigrator.Result result;
    using (JsonDocument json = await ReadJson(fs, toothPath)) {
      int? formatVersion = ToothParser.FormatVersion(json.RootElement);
      if (formatVersion == ToothFormat.FormatVersion) {
        throw new InvalidOperationException(
            $"{ToothFormat.ToothFileName} is already of {ToothFormat.FormatVersionKey} "
            + $"{ToothFormat.FormatVersion}: there is nothing to migrate.");
      }

      if (formatVersion is not (1 or 2 or 3)) {
        throw new InvalidOperationException(
            $"{ToothFormat.ToothFileName} has no {ToothFormat.FormatVersionKey} 1, 2 or 3.");
      }

      PackageManifest tooth;
      try {
        tooth = PackageManifestMigration.Migrate(json);
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        // The migration and the format 3 model throw many kinds of exceptions on bad input.
        throw new InvalidOperationException(
            $"{ToothFormat.ToothFileName} is not a valid tooth.json of format {formatVersion}: "
            + ex.Message, ex);
      }

      result = await ToothMigrator.Migrate(tooth, ui, () => RepositoryFiles(fs, directory));
      notes.AddRange(result.Notes);
      notes.AddRange(ToothMigrator.DroppedFields(json.RootElement, tooth));
    }

    byte[] bytes = ToothMigrator.Serialize(result.Document);
    if (ToothMigrator.ValidationNote(bytes) is string validation) {
      notes.Add(validation);
    }

    string tempPath = fs.Path.Join(directory, TempFileName);
    try {
      // A leftover of an interrupted migration; on Windows a hidden file cannot be overwritten.
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }

      await fs.File.WriteAllBytesAsync(tempPath, bytes);
      fs.SetHidden(tempPath, true);
      fs.File.Move(tempPath, toothPath, overwrite: true);
      fs.SetHidden(toothPath, false);
    }
    finally {
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }
    }

    return notes;
  }

  private static async Task<JsonDocument> ReadJson(IFileSystem fs, string path) {
    try {
      return JsonDocument.Parse(await fs.File.ReadAllBytesAsync(path), _strictJson);
    }
    catch (JsonException ex) {
      throw new InvalidOperationException(
          $"Cannot read {ToothFormat.ToothFileName}: {ex.Message}", ex);
    }
  }

  /// The '/'-separated paths of the files of the repository, in ordinal order, which make up
  /// the archive of the tooth.json ("self" assets): everything in the directory but .git and
  /// tooth.json itself. Links are not followed and inaccessible entries are skipped. Untracked
  /// files are included: the working tree is all lip can see.
  private static List<string> RepositoryFiles(IFileSystem fs, string directory) {
    EnumerationOptions options = new() {
      IgnoreInaccessible = true,
      AttributesToSkip = FileAttributes.ReparsePoint,
    };
    List<string> files = [];
    void Walk(string current) {
      foreach (string file in fs.Directory.EnumerateFiles(current, "*", options)) {
        files.Add(fs.Path.GetRelativePath(directory, file).Replace('\\', '/'));
      }

      foreach (string sub in fs.Directory.EnumerateDirectories(current, "*", options)) {
        if (current != directory || fs.Path.GetFileName(sub) != ".git") {
          Walk(sub);
        }
      }
    }

    try {
      Walk(directory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new InvalidOperationException(
          $"Cannot list the files of the repository: {ex.Message}", ex);
    }

    files.Remove(ToothFormat.ToothFileName);
    files.Sort(StringComparer.Ordinal);
    return files;
  }
}
