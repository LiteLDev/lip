using System.IO.Abstractions;

namespace Lip.Core.Infrastructure;

public static class FileSystemExtensions {
  public static FileSystemStream CreateFileWithDirectory(this IFileSystem fs, string path) {
    string? dirPath = fs.Path.GetDirectoryName(path);

    if (!string.IsNullOrEmpty(dirPath)) {
      fs.Directory.CreateDirectory(dirPath);
    }

    return fs.File.Create(path);
  }

  /// Sets or clears the hidden attribute of a file on Windows. Elsewhere a file is hidden by its
  /// name starting with ".", so this does nothing.
  public static void SetHidden(this IFileSystem fs, string path, bool hidden) {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    FileAttributes attributes = fs.File.GetAttributes(path);
    FileAttributes updated = hidden
        ? attributes | FileAttributes.Hidden
        : attributes & ~FileAttributes.Hidden;
    if (updated != attributes) {
      fs.File.SetAttributes(path, updated == 0 ? FileAttributes.Normal : updated);
    }
  }
}
