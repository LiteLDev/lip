using System.IO.Abstractions;
using System.IO.Compression;

namespace Lip.Core.Mcmod;

/// Checks that a file is a valid ZIP archive in the sense of spec §2: decided by content only,
/// entry paths '/'-separated, not absolute, without '..' segments or '\', and only regular file
/// and directory entries.
public static class ZipValidator {
  private const int _unixFileTypeMask = 0xF000;
  private const int _unixRegularFile = 0x8000;
  private const int _unixDirectory = 0x4000;

  /// Throws ArchiveUnavailableException naming the archive key when the file is not valid.
  public static void Validate(IFileInfo file, string archiveKey) {
    using Stream stream = file.OpenRead();

    ZipArchive zip;
    try {
      zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
    }
    catch (InvalidDataException ex) {
      throw new ArchiveUnavailableException($"{archiveKey} is not a ZIP archive.", ex);
    }

    using (zip) {
      foreach (ZipArchiveEntry entry in zip.Entries) {
        if (GetInvalidReason(entry) is string reason) {
          throw new ArchiveUnavailableException(
              $"{archiveKey} is not a valid archive: entry '{entry.FullName}' {reason}.");
        }
      }
    }
  }

  private static string? GetInvalidReason(ZipArchiveEntry entry) {
    string path = entry.FullName;

    if (path == "") {
      return "has an empty path";
    }
    if (path.Contains('\\')) {
      return "contains '\\'";
    }
    if (path.StartsWith('/')) {
      return "starts with '/'";
    }
    // Absolute on Windows, like a leading '/': Path.Combine would discard the target directory.
    if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') {
      return "starts with a drive letter";
    }
    if (path.Split('/').Contains("..")) {
      return "contains a '..' segment";
    }

    // The upper 16 bits hold the Unix mode when the entry was made on Unix; they are 0 otherwise.
    int fileType = (entry.ExternalAttributes >> 16) & _unixFileTypeMask;
    if (fileType is not (0 or _unixRegularFile or _unixDirectory)) {
      return "is neither a regular file nor a directory (e.g. a symbolic link)";
    }

    return null;
  }
}
