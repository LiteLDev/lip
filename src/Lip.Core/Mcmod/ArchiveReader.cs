using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Semver;

namespace Lip.Core.Mcmod;

/// What makes an archive a package (protocol/package.md §1): its package file (File), or, for an
/// archive without one, its tooth.json of format 1, 2 or 3 read as format 3 (Legacy,
/// protocol/compatibility.md §2). Json is the file as written.
public record PackageDocument(PackageFile? File, PackageManifest? Legacy, JsonElement Json) {
  /// The package id.
  public string Id => File?.Id ?? Legacy!.Path;

  /// The version the file declares.
  public SemVersion Version => File?.Version ?? Legacy!.Version;

  /// Whether the package is read from a tooth.json.
  public bool IsLegacy => File is null;

  /// The package file the package is read as for an environment: its own, or the one
  /// protocol/compatibility.md §2 derives from its tooth.json, with version, when given, as the
  /// version of the package (its tag version). Throws PackageFormatException when a tooth.json
  /// cannot be read so.
  public PackageFile ReadFor(LipEnvironment environment, SemVersion? version = null) =>
      File ?? ToothCompat.Derive(Legacy!, environment, version);

  /// Reads lip.yaml. Throws PackageFormatException when it is not a valid package file.
  public static PackageDocument ParsePackageFile(byte[] yaml) {
    using JsonDocument json = PackageFileParser.ParseYaml(yaml);
    return new PackageDocument(
        PackageFileParser.Parse(json.RootElement), null, json.RootElement.Clone());
  }

  /// Reads a tooth.json (protocol/compatibility.md §2). Throws PackageFormatException when it is
  /// not a JSON object with format_version 1, 2 or 3 that reads as format 3.
  public static PackageDocument ParseToothJson(byte[] utf8Json) {
    using JsonDocument json = PackageFileParser.ParseJson(utf8Json, ToothCompat.ToothFileName);
    return new PackageDocument(
        null, ToothCompat.ReadToothJson(json.RootElement), json.RootElement.Clone());
  }
}

/// Reads the file that makes an archive a package (protocol/package.md §1): lip.yaml at its root,
/// or, when there is none, tooth.json (protocol/compatibility.md §2).
public interface IArchiveReader {
  /// Throws ArchiveUnavailableException when the archive is not a package: it has neither file,
  /// or the file it is read from is not valid.
  public Task<PackageDocument> Read(ArchiveHandle archive);
}

public class ArchiveReader : IArchiveReader {
  public async Task<PackageDocument> Read(ArchiveHandle archive) {
    string name = Describe(archive);
    List<string> keys = [.. archive.Source.Keys];
    bool hasPackageFile = keys.Contains(PackageFormat.FileName);
    string fileName = hasPackageFile ? PackageFormat.FileName : ToothCompat.ToothFileName;
    if (!hasPackageFile && !keys.Contains(ToothCompat.ToothFileName)) {
      RunTrace.Write("archive", $"{name} has neither {PackageFormat.FileName} nor"
          + $" {ToothCompat.ToothFileName} at its root");
      // Only a package file one folder deep, as in GitHub's "Download ZIP" archives, suggests the
      // whole package is in a folder; deeper ones are usually examples or vendored packages.
      string? nested = keys.FirstOrDefault(
          k => (k.EndsWith($"/{PackageFormat.FileName}", StringComparison.Ordinal)
                  || k.EndsWith($"/{ToothCompat.ToothFileName}", StringComparison.Ordinal))
              && k.Count(c => c == '/') == 1);
      throw new ArchiveUnavailableException(
          $"{name} is not a lip package: it has no {PackageFormat.FileName} at its root."
          + (nested is null
              ? ""
              : $" There is one at '{nested}': the files of a package must be at the root of"
                + " the archive, not in a folder."));
    }

    byte[] content;
    try {
      using Stream stream = await archive.Source.OpenRead(fileName);
      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      content = memory.ToArray();
    }
    catch (Exception ex) when (ex is not ArchiveUnavailableException
        and not OperationCanceledException) {
      throw new ArchiveUnavailableException(
          $"Could not read the {fileName} of {name}.", ex);
    }

    try {
      PackageDocument document = hasPackageFile
          ? PackageDocument.ParsePackageFile(content)
          : PackageDocument.ParseToothJson(content);
      RunTrace.Write("archive", $"Read the {fileName} of {name}: {document.Id} {document.Version}");
      return document;
    }
    catch (Exception ex) when (ex is not ArchiveUnavailableException
        and not OperationCanceledException) {
      bool local = archive.Key.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
      string advice = ex is NewerPackageFormatException
          ? ""
          : local
              ? $" Fix the {fileName} in {name}."
              : " Ask the package author to fix it.";
      throw new ArchiveUnavailableException(
          $"The {fileName} of {name} is not valid.{advice}", ex);
    }
  }

  /// The archive as the user knows it: the path of a local file or directory, else its key.
  private static string Describe(ArchiveHandle archive) =>
      archive.Key.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
          && Ref.TryParse(archive.Key, allowFile: true, out Ref? file, out _)
          ? file.LocalPath
          : archive.Key;
}
