using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lip.Core.Mcmod;

/// Packing (spec §8, lip pack in specs/cli.md §5.11): turns a meta document into the package
/// document of one of its targets, and writes it into a ZIP archive with the package's files.
public static class Packer {
  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n",
  };

  /// The keys of a target that select it rather than override the template.
  private static readonly HashSet<string> _targetKeys = ["variant", "platform", "runtime", "source"];

  /// The package document of a manifest.json for a variant and an environment: Pack(M, T, p, r)
  /// for a meta document M and the target T the variant and environment select (spec §5.2 step
  /// 3), or a package document itself (variant ""). Throws InvalidOperationException when the
  /// document is not of format bf45b1, no single target is selected, or the result is not a valid
  /// package document.
  public static JsonObject Pack(byte[] manifest, string variant, LipEnvironment environment) {
    DocumentParser parser = new();
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

    switch (parser.Parse(manifest)) {
      case PackageDocument:
        if (variant != "") {
          throw new InvalidOperationException(
              $"{ManifestFormat.ManifestFileName} is a package document; it has no variant"
              + $" \"{variant}\".");
        }

        return root;

      case MetaDocument meta:
        int index = SelectTarget(meta, variant, environment);
        JsonObject target = (JsonObject)root["targets"]!.AsArray()[index]!;
        JsonObject packed = (JsonObject)root.DeepClone();
        packed.Remove("targets");
        foreach ((string key, JsonNode? value) in target) {
          if (_targetKeys.Contains(key)) {
            continue;
          }

          if (value is null) {
            packed.Remove(key);
          } else {
            packed[key] = value.DeepClone();
          }
        }

        if (target.ContainsKey("platform")) {
          packed["platform"] = environment.Platform.ToSpecString();
        }

        if (target.ContainsKey("runtime")) {
          packed["runtime"] = environment.Runtime;
        }

        try {
          if (parser.Parse(Serialize(packed)) is not PackageDocument) {
            throw new ManifestFormatException("the result is not a package document.");
          }
        }
        catch (ManifestFormatException ex) {
          throw new InvalidOperationException(
              $"Packing target {index} gives an invalid package document: {ex.Message}", ex);
        }

        return packed;

      default:
        throw new InvalidOperationException("Unknown document type.");
    }
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

  /// The index of the target a variant and an environment select (spec §5.2 step 3).
  private static int SelectTarget(MetaDocument meta, string variant, LipEnvironment environment) {
    List<int> matching = [
      .. meta.Targets
          .Select((target, index) => (target, index))
          .Where(t => t.target.Variant == variant && t.target.Matches(environment))
          .Select(t => t.index),
    ];
    if (matching.Count == 0) {
      throw new InvalidOperationException(
          $"No target for variant \"{variant}\" matches ({environment.Platform.ToSpecString()},"
          + $" {environment.Runtime}).");
    }

    int most = matching.Max(i => meta.Targets[i].SelectorCount);
    List<int> best = [.. matching.Where(i => meta.Targets[i].SelectorCount == most)];
    if (best.Count > 1) {
      throw new InvalidOperationException(
          $"Targets {string.Join(", ", best)} match ({environment.Platform.ToSpecString()},"
          + $" {environment.Runtime}) equally well.");
    }

    return best[0];
  }

  private static async Task WriteEntry(ZipArchive zip, string name, Stream content) {
    ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
    await using Stream target = entry.Open();
    await content.CopyToAsync(target);
  }
}
