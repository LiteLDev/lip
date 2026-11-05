using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lip.Core.Mcmod;

/// Packing (lip pack in specs/cli.md §5.11): checks that a manifest.json is a package document,
/// and writes it into a ZIP archive with the package's files.
public static class Packer {
  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n",
  };

  /// The package document of a manifest.json. Throws ManifestFormatException when it is not a
  /// valid document, and InvalidOperationException when it has no format or is a meta document.
  public static JsonObject Pack(byte[] manifest) {
    JsonObject root;
    using (JsonDocument json = DocumentParser.ParseJson(manifest)) {
      root = JsonObject.Create(json.RootElement.Clone()) ?? throw new InvalidOperationException(
          $"{ManifestFormat.ManifestFileName} is not a JSON object.");
    }

    if (!root.ContainsKey(ManifestFormat.FormatKey)) {
      throw new InvalidOperationException(
          $"{ManifestFormat.ManifestFileName} has no \"{ManifestFormat.FormatKey}\" key; run lip"
          + " migrate first.");
    }

    if (new DocumentParser().Parse(manifest) is not PackageDocument) {
      throw new InvalidOperationException(
          $"{ManifestFormat.ManifestFileName} is a meta package; only a concrete package can be"
          + " packed.");
    }

    return root;
  }

  /// The UTF-8 bytes of a document as lip writes it: indented by two spaces, with a final line
  /// break.
  public static byte[] Serialize(JsonObject document) =>
      Encoding.UTF8.GetBytes(document.ToJsonString(_writeOptions) + "\n");

  /// Writes a ZIP archive to output, replacing it: the document as manifest.json at its root,
  /// and every regular file below directory at its path relative to it, except manifest.json at
  /// its root, everything below .git/ and .lip/ at its root, and output itself.
  public static async Task WriteArchive(
      IFileSystem fs, string directory, string output, JsonObject document) {
    string root = fs.Path.GetFullPath(directory);
    string outputPath = fs.Path.GetFullPath(output);
    StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    List<(string Entry, string Path)> files = [];
    EnumerationOptions options = new() {
      RecurseSubdirectories = true,
      IgnoreInaccessible = true,
      AttributesToSkip = FileAttributes.ReparsePoint,
    };
    foreach (string file in fs.Directory.EnumerateFiles(root, "*", options)) {
      string full = fs.Path.GetFullPath(file);
      string entry = fs.Path.GetRelativePath(root, full).Replace('\\', '/');
      if (string.Equals(full, outputPath, comparison)
          || entry == ManifestFormat.ManifestFileName
          || entry.StartsWith(".git/", StringComparison.Ordinal)
          || entry.StartsWith(".lip/", StringComparison.Ordinal)) {
        continue;
      }

      files.Add((entry, full));
    }

    files.Sort((a, b) => string.CompareOrdinal(a.Entry, b.Entry));

    string tempPath = outputPath + ".tmp";
    try {
      using (Stream stream = fs.File.Create(tempPath))
      using (ZipArchive zip = new(stream, ZipArchiveMode.Create)) {
        await WriteEntry(zip, ManifestFormat.ManifestFileName, new MemoryStream(Serialize(document)));
        foreach ((string entry, string path) in files) {
          await using Stream content = fs.File.OpenRead(path);
          await WriteEntry(zip, entry, content);
        }
      }

      fs.File.Move(tempPath, outputPath, overwrite: true);
    }
    finally {
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }
    }
  }

  private static async Task WriteEntry(ZipArchive zip, string name, Stream content) {
    ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
    await using Stream target = entry.Open();
    await content.CopyToAsync(target);
  }
}
