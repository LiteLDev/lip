using System.IO.Abstractions;

namespace Lip.Core.Infrastructure;

public static class FileSystemExtensions {
  public static FileSystemStream CreateFileWithDirectory(this IFileSystem fs, string path) {
    string? dirPath = fs.Path.GetDirectoryName(path);

    try {
      if (!string.IsNullOrEmpty(dirPath)) {
        fs.Directory.CreateDirectory(dirPath);
      }

      return fs.File.Create(path);
    }
    catch (Exception ex) when (IsFileError(ex)) {
      throw WrapFileError($"Could not create the file {path}.", ex);
    }
  }

  /// Sets or clears the hidden attribute of a file on Windows. Elsewhere a file is hidden by its
  /// name starting with ".", so this does nothing.
  public static void SetHidden(this IFileSystem fs, string path, bool hidden) {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    try {
      FileAttributes attributes = fs.File.GetAttributes(path);
      FileAttributes updated = hidden
          ? attributes | FileAttributes.Hidden
          : attributes & ~FileAttributes.Hidden;
      if (updated != attributes) {
        fs.File.SetAttributes(path, updated == 0 ? FileAttributes.Normal : updated);
      }
    }
    catch (Exception ex) when (IsFileError(ex)) {
      throw WrapFileError(
          $"Could not {(hidden ? "hide" : "unhide")} {path} (set its hidden attribute).", ex);
    }
  }

  /// Whether an exception is a failure of the file system: a file that cannot be found, read,
  /// written or accessed.
  internal static bool IsFileError(Exception ex) =>
      ex is IOException or UnauthorizedAccessException;

  /// A file system failure with a message saying what lip was doing, of the same kind as the
  /// failure (UnauthorizedAccessException or IOException), which is kept as the inner exception.
  internal static Exception WrapFileError(string message, Exception inner) => inner switch {
    UnauthorizedAccessException => new UnauthorizedAccessException(message, inner),
    _ => new IOException(message, inner),
  };
}
