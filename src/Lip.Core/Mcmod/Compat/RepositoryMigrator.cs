using System.IO.Abstractions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Compat;

/// Rewrites the tooth.json of a package repository, of format 1, 2 or 3, as a tooth.json of format
/// 4 (lip migrate, protocol/cli.md §5.8), by ToothMigrator.
///
/// - A repository without tooth.json, or whose tooth.json is already of format 4 or cannot be
///   read (ToothDocument.Parse), is a failure, and nothing is changed.
/// - Questions are asked through IUserInteraction.Ask before anything is written, and a result
///   that is not a valid tooth.json of format 4 is a failure.
/// - tooth.json is written, indented by two spaces, through a temporary file and a rename.
///
/// The notes are ToothMigrator's and those of ToothMigrator.DroppedFields.
public static class RepositoryMigrator {
  public static async Task<IReadOnlyList<string>> Migrate(
      IFileSystem fs, string directory, IUserInteraction ui) {
    string toothPath = fs.Path.Join(directory, ToothFormat.ToothFileName);
    if (!fs.File.Exists(toothPath)) {
      throw new InvalidOperationException(
          $"There is no {ToothFormat.ToothFileName} in {directory}. Run lip migrate in the root "
          + "directory of the package repository.");
    }

    ToothDocument document;
    try {
      document = ToothDocument.Parse(await fs.File.ReadAllBytesAsync(toothPath));
    }
    catch (ToothFormatException ex) {
      throw new InvalidOperationException(
          $"Cannot read {ToothFormat.ToothFileName}: {ex.Message}", ex);
    }

    if (!document.IsLegacy) {
      throw new InvalidOperationException(
          $"{ToothFormat.ToothFileName} is already of {ToothFormat.FormatVersionKey} "
          + $"{ToothFormat.FormatVersion}: there is nothing to migrate.");
    }

    ToothMigrator.Result result = await ToothMigrator.Migrate(document.Legacy!, ui);
    List<string> notes = [
      .. result.Notes, .. ToothMigrator.DroppedFields(document.Json, document.Legacy!),
    ];

    byte[] bytes = ToothMigrator.Serialize(result.Document);
    try {
      _ = ToothParser.Parse(bytes);
    }
    catch (ToothFormatException ex) {
      throw new InvalidOperationException(
          $"Cannot migrate {ToothFormat.ToothFileName}: the result is not a valid tooth.json of "
          + $"format {ToothFormat.FormatVersion}: {ex.Message}", ex);
    }

    // A temporary file of a new name, renamed over tooth.json, leaves it unchanged when writing
    // fails. It takes the attributes of tooth.json, so a hidden one stays hidden.
    string tempPath = fs.Path.Join(directory, $".tooth.json.{Guid.NewGuid():N}.tmp");
    try {
      using (Stream stream = fs.File.Open(tempPath, FileMode.CreateNew, FileAccess.Write)) {
        await stream.WriteAsync(bytes);
      }

      fs.File.SetAttributes(
          tempPath, fs.File.GetAttributes(toothPath) & ~FileAttributes.ReadOnly);
      fs.File.Move(tempPath, toothPath, overwrite: true);
    }
    finally {
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }
    }

    return notes;
  }
}
