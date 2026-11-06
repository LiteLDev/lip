using System.IO.Abstractions;
using System.IO.Compression;

namespace Lip.Core.Mcmod;

/// lip pack (protocol/cli.md §5.11): writes a ZIP archive of a directory.
public static class Packer {
  /// Writes a ZIP archive to output, replacing it: every regular file below directory at its
  /// path relative to it, except output itself and everything below .git/ and .lip/ at its root.
  /// Files are written in ordinal order of their paths; links are not followed, and an
  /// inaccessible directory fails the command rather than being left out.
  public static async Task WriteArchive(IFileSystem fs, string directory, string output) {
    string root = fs.Path.GetFullPath(directory);
    if (!fs.Directory.Exists(root)) {
      throw new InvalidOperationException($"{root} is not a directory.");
    }

    string outputPath = fs.Path.GetFullPath(output);
    StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    List<(string Entry, string Path)> files = [];
    EnumerationOptions options = new() {
      RecurseSubdirectories = true,
      IgnoreInaccessible = false,
      AttributesToSkip = FileAttributes.ReparsePoint,
    };
    foreach (string file in fs.Directory.EnumerateFiles(root, "*", options)) {
      string full = fs.Path.GetFullPath(file);
      string entry = fs.Path.GetRelativePath(root, full).Replace('\\', '/');
      if (string.Equals(full, outputPath, comparison)
          || entry.StartsWith(".git/", StringComparison.Ordinal)
          || entry.StartsWith(".lip/", StringComparison.Ordinal)) {
        continue;
      }

      files.Add((entry, full));
    }

    files.Sort((a, b) => string.CompareOrdinal(a.Entry, b.Entry));

    using Stream stream = fs.File.Create(outputPath);
    using ZipArchive zip = new(stream, ZipArchiveMode.Create);
    foreach ((string entry, string path) in files) {
      await using Stream content = fs.File.OpenRead(path);
      await using Stream target = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
      await content.CopyToAsync(target);
    }
  }
}
