using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Compat;

/// Migrates the tooth.json of a package repository, of format 1, 2 or 3, to a lip.toml (lip
/// migrate, protocol/compatibility.md §5), by ToothMigrator.
///
/// - When lip.toml exists, or tooth.json is missing or is not one that protocol/compatibility.md
///   §2 reads (PackageDocument.ParseToothJson), it is a failure, and nothing is changed.
/// - Questions are asked through IUserInteraction.Ask before anything is written, and a result
///   that is not a valid package file is a failure.
/// - lip.toml is written atomically, through lip.toml.tmp, then tooth.json is deleted.
///
/// The notes are ToothMigrator's and those of ToothMigrator.DroppedFields.
public static class RepositoryMigrator {
  public static async Task<IReadOnlyList<string>> Migrate(
      IFileSystem fs, string directory, IUserInteraction ui) {
    string packageFilePath = fs.Path.Join(directory, PackageFormat.FileName);
    string toothPath = fs.Path.Join(directory, ToothCompat.ToothFileName);
    RunTrace.Write("migrate", $"migrating {toothPath} to {packageFilePath}");
    if (fs.File.Exists(packageFilePath) || fs.Directory.Exists(packageFilePath)) {
      throw new InvalidOperationException(
          $"{packageFilePath} already exists, so this package repository already uses "
          + $"{PackageFormat.FileName}: there is nothing to migrate.");
    }

    if (!fs.File.Exists(toothPath)) {
      throw new InvalidOperationException(
          $"There is no {ToothCompat.ToothFileName} in {directory}. Run 'lip migrate' in the root "
          + "directory of the package repository, the one that holds "
          + $"{ToothCompat.ToothFileName}.");
    }

    PackageDocument document;
    try {
      document = PackageDocument.ParseToothJson(await fs.File.ReadAllBytesAsync(toothPath));
    }
    catch (PackageFormatException ex) {
      throw new InvalidOperationException(
          $"Cannot read {ToothCompat.ToothFileName} in {directory}, so it cannot be migrated; "
          + "nothing was changed. Fix the problem below, then run 'lip migrate' again.", ex);
    }

    RunTrace.Write("migrate", $"{toothPath} has {ToothCompat.FormatVersionKey} "
        + $"{ToothCompat.FormatVersion(document.Json)}, id {document.Id}, "
        + $"version {document.Version}");

    ToothMigrator.Result result = await ToothMigrator.Migrate(document.Legacy!, ui);
    List<string> notes = [
      .. result.Notes, .. ToothMigrator.DroppedFields(document.Json, document.Legacy!),
    ];
    RunTrace.Write("migrate", $"converted to {PackageFormat.FileName} with {notes.Count} notes");

    byte[] bytes;
    try {
      bytes = ToothMigrator.Serialize(result.Document);
      using JsonDocument written = PackageFileParser.ParseToml(bytes);
      _ = PackageFileParser.Parse(written.RootElement);
    }
    catch (PackageFormatException ex) {
      throw new InvalidOperationException(
          $"Cannot migrate {ToothCompat.ToothFileName} in {directory}: the converted file would "
          + $"not be a valid {PackageFormat.FileName}, so nothing was changed. The value named "
          + $"below is a path in the converted {PackageFormat.FileName}, not in your "
          + $"{ToothCompat.ToothFileName}: "
          + "find the value it was converted from among the variants and placements of your "
          + "file, fix it, and run 'lip migrate' again. If that value looks right, this is likely "
          + "a lip bug: please report it at https://github.com/LiteLDev/lip/issues with the file "
          + "attached.",
          ex);
    }

    // Written atomically (protocol/README.md): a lip.toml created meanwhile is not replaced, and
    // tooth.json is deleted only once lip.toml is in place.
    string tempPath = packageFilePath + ".tmp";
    try {
      RunTrace.Write("migrate", $"writing {tempPath}");
      using (Stream stream = fs.File.Open(tempPath, FileMode.Create, FileAccess.Write)) {
        await stream.WriteAsync(bytes);
      }

      RunTrace.Write("migrate", $"renaming {tempPath} to {packageFilePath}");
      fs.File.Move(tempPath, packageFilePath, overwrite: false);
    }
    catch (IOException ex) {
      throw new IOException(
          $"Cannot write the migrated {PackageFormat.FileName} to {packageFilePath}; "
          + $"{ToothCompat.ToothFileName} was not changed.", ex);
    }
    catch (UnauthorizedAccessException ex) {
      throw new UnauthorizedAccessException(
          $"lip is not allowed to write the migrated {PackageFormat.FileName} to "
          + $"{packageFilePath}; {ToothCompat.ToothFileName} was not changed.", ex);
    }
    finally {
      if (fs.File.Exists(tempPath)) {
        RunTrace.Write("migrate", $"deleting {tempPath}");
        fs.File.Delete(tempPath);
      }
    }

    RunTrace.Write("migrate", $"wrote {packageFilePath}; deleting {toothPath}");
    try {
      FileAttributes attributes = fs.File.GetAttributes(toothPath);
      if (attributes.HasFlag(FileAttributes.ReadOnly)) {
        fs.File.SetAttributes(toothPath, attributes & ~FileAttributes.ReadOnly);
      }

      fs.File.Delete(toothPath);
    }
    catch (IOException ex) {
      throw new IOException(
          $"Wrote {packageFilePath}, but cannot delete {toothPath}. Delete "
          + $"{ToothCompat.ToothFileName} yourself: lip reads {PackageFormat.FileName} and "
          + $"ignores {ToothCompat.ToothFileName} next to it.", ex);
    }
    catch (UnauthorizedAccessException ex) {
      throw new UnauthorizedAccessException(
          $"Wrote {packageFilePath}, but lip is not allowed to delete {toothPath}. Delete "
          + $"{ToothCompat.ToothFileName} yourself: lip reads {PackageFormat.FileName} and "
          + $"ignores {ToothCompat.ToothFileName} next to it.", ex);
    }

    return notes;
  }
}
