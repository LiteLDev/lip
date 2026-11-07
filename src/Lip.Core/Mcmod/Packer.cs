using System.IO.Abstractions;
using System.IO.Compression;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod;

/// lip pack (protocol/cli.md §5.11): writes a ZIP archive of a directory.
public static class Packer {
  /// Writes a ZIP archive to output, replacing it: every regular file below directory at its
  /// path relative to it, except output itself and everything below .git/ and .lip/ at its root.
  /// Files are written in ordinal order of their paths; links are not followed, special files
  /// (FIFOs, sockets, device nodes) are left out, and an
  /// inaccessible directory fails the command rather than being left out.
  public static async Task WriteArchive(IFileSystem fs, string directory, string output) {
    string root = fs.Path.GetFullPath(directory);
    if (!fs.Directory.Exists(root)) {
      throw new InvalidOperationException(fs.File.Exists(root)
          ? $"Cannot pack {root}: it is a file, not a directory. Pass the directory of the package."
          : $"Cannot pack {root}: there is no such directory. Check the path.");
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
    try {
      foreach (string file in fs.Directory.EnumerateFiles(root, "*", options)) {
        string full = fs.Path.GetFullPath(file);
        string entry = fs.Path.GetRelativePath(root, full).Replace('\\', '/');
        if (string.Equals(full, outputPath, comparison)
            || entry.StartsWith(".git/", StringComparison.Ordinal)
            || entry.StartsWith(".lip/", StringComparison.Ordinal)
            || UnixFileType.IsSpecialFile(full)) {
          continue;
        }

        files.Add((entry, full));
      }
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Cannot pack {root}: could not list the files in it. lip packs every file below the"
          + " directory, so it must be able to read every directory below it.", ex);
    }

    files.Sort((a, b) => string.CompareOrdinal(a.Entry, b.Entry));
    RunTrace.Write("pack", $"Packing {files.Count} files of {root} into {outputPath}");

    Stream stream;
    try {
      stream = fs.File.Create(outputPath);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not create the archive {outputPath}. Check that its directory exists and that"
          + " no other program has it open.", ex);
    }

    Exception? entryError = null;
    try {
      // Disposing the archive writes its central directory, which can fail too (e.g. disk full).
      using (stream)
      using (ZipArchive zip = new(stream, ZipArchiveMode.Create)) {
        foreach ((string entry, string path) in files) {
          try {
            await using Stream content = fs.File.OpenRead(path);
            await using Stream target = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
            await content.CopyToAsync(target);
          }
          catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
            entryError = FileSystemExtensions.WrapFileError(
                $"Could not add {path} to the archive {outputPath}.", ex);
            throw entryError;
          }
        }
      }
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex) && ex != entryError) {
      throw FileSystemExtensions.WrapFileError($"Could not write the archive {outputPath}.", ex);
    }
    RunTrace.Write("pack", $"Wrote {outputPath}");
  }
}
