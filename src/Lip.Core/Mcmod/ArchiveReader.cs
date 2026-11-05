using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Migration.PackageManifests;
using Semver;

namespace Lip.Core.Mcmod;

/// The tooth file of a package (protocol/package.md §1): of format 4 (File), or of format 1, 2 or
/// 3 read as format 3 (Legacy). Json is the file as written.
public record ToothDocument(int FormatVersion, ToothFile? File, PackageManifest? Legacy, JsonElement Json) {
  /// The tooth path.
  public string Tooth => File?.Tooth ?? Legacy!.Path;

  /// The version the tooth file declares.
  public SemVersion Version => File?.Version ?? Legacy!.Version;

  public bool IsLegacy => File is null;

  /// The tooth file of format 4 the tooth file is read as for an environment (protocol/package.md
  /// §5). Throws ToothFormatException when a tooth file of format 3 cannot be read so.
  public ToothFile ReadFor(LipEnvironment environment) =>
      File ?? ToothCompat.Derive(Legacy!, environment);

  /// Reads a tooth file. Throws ToothFormatException when it is not the tooth file of a package.
  public static ToothDocument Parse(byte[] utf8Json) {
    using JsonDocument json = ToothParser.ParseJson(utf8Json);
    JsonElement root = json.RootElement;
    int? formatVersion = ToothParser.FormatVersion(root);
    switch (formatVersion) {
      case ToothFormat.FormatVersion:
        return new ToothDocument(formatVersion.Value, ToothParser.Parse(root), null, root.Clone());
      case 1 or 2 or 3:
        PackageManifest legacy;
        try {
          legacy = PackageManifestMigration.Migrate(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
          // The migration and the format 3 model throw many kinds of exceptions on bad input.
          throw new ToothFormatException(
              $"Invalid tooth.json of format {formatVersion}: {ex.Message}", ex);
        }

        return new ToothDocument(formatVersion.Value, null, legacy, root.Clone());
      default:
        throw new ToothFormatException(
            $"Invalid tooth.json: {ToothFormat.FormatVersionKey} must be 1, 2, 3 or "
            + $"{ToothFormat.FormatVersion}.");
    }
  }
}

/// Reads the tooth file at the root of an archive (protocol/package.md §1).
public interface IArchiveReader {
  /// Throws ArchiveUnavailableException when the archive has no tooth file or it is not valid,
  /// i.e. when the archive is not a package.
  public Task<ToothDocument> Read(ArchiveHandle archive);
}

public class ArchiveReader : IArchiveReader {
  public async Task<ToothDocument> Read(ArchiveHandle archive) {
    if (!archive.Source.Keys.Contains(ToothFormat.ToothFileName)) {
      throw new ArchiveUnavailableException(
          $"{archive.Key} has no {ToothFormat.ToothFileName}.");
    }

    try {
      using Stream stream = await archive.Source.OpenRead(ToothFormat.ToothFileName);
      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      return ToothDocument.Parse(memory.ToArray());
    }
    catch (Exception ex) when (ex is not ArchiveUnavailableException
        and not OperationCanceledException) {
      throw new ArchiveUnavailableException(
          $"Invalid {ToothFormat.ToothFileName} in {archive.Key}: {ex.Message}", ex);
    }
  }
}
