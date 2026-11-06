using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Compat;

/// Migrates the tooth.json of a package repository, of format 1, 2 or 3, to a lip.yaml (lip
/// migrate, protocol/compatibility.md §5), by ToothMigrator.
///
/// - When lip.yaml exists, or tooth.json is missing or is not one that protocol/compatibility.md
///   §2 reads (PackageDocument.ParseToothJson), it is a failure, and nothing is changed.
/// - Questions are asked through IUserInteraction.Ask before anything is written, and a result
///   that is not a valid package file is a failure.
/// - lip.yaml is written atomically, through lip.yaml.tmp, then tooth.json is deleted.
///
/// The notes are ToothMigrator's and those of ToothMigrator.DroppedFields.
public static class RepositoryMigrator {
  public static async Task<IReadOnlyList<string>> Migrate(
      IFileSystem fs, string directory, IUserInteraction ui) {
    string packageFilePath = fs.Path.Join(directory, PackageFormat.FileName);
    string toothPath = fs.Path.Join(directory, ToothCompat.ToothFileName);
    if (fs.File.Exists(packageFilePath) || fs.Directory.Exists(packageFilePath)) {
      throw new InvalidOperationException(
          $"{PackageFormat.FileName} already exists in {directory}: there is nothing to migrate.");
    }

    if (!fs.File.Exists(toothPath)) {
      throw new InvalidOperationException(
          $"There is no {ToothCompat.ToothFileName} in {directory}. Run lip migrate in the root "
          + "directory of the package repository.");
    }

    PackageDocument document;
    try {
      document = PackageDocument.ParseToothJson(await fs.File.ReadAllBytesAsync(toothPath));
    }
    catch (PackageFormatException ex) {
      throw new InvalidOperationException(
          $"Cannot read {ToothCompat.ToothFileName}: {ex.Message}", ex);
    }

    ToothMigrator.Result result = await ToothMigrator.Migrate(document.Legacy!, ui);
    List<string> notes = [
      .. result.Notes, .. ToothMigrator.DroppedFields(document.Json, document.Legacy!),
    ];

    byte[] bytes = ToothMigrator.Serialize(result.Document);
    try {
      using JsonDocument written = PackageFileParser.ParseYaml(bytes);
      _ = PackageFileParser.Parse(written.RootElement);
    }
    catch (PackageFormatException ex) {
      throw new InvalidOperationException(
          $"Cannot migrate {ToothCompat.ToothFileName}: the result is not a valid "
          + $"{PackageFormat.FileName}: {ex.Message}", ex);
    }

    // Written atomically (protocol/README.md): a lip.yaml created meanwhile is not replaced, and
    // tooth.json is deleted only once lip.yaml is in place.
    string tempPath = packageFilePath + ".tmp";
    try {
      using (Stream stream = fs.File.Open(tempPath, FileMode.Create, FileAccess.Write)) {
        await stream.WriteAsync(bytes);
      }

      fs.File.Move(tempPath, packageFilePath, overwrite: false);
    }
    finally {
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }
    }

    FileAttributes attributes = fs.File.GetAttributes(toothPath);
    if (attributes.HasFlag(FileAttributes.ReadOnly)) {
      fs.File.SetAttributes(toothPath, attributes & ~FileAttributes.ReadOnly);
    }

    fs.File.Delete(toothPath);
    return notes;
  }
}
