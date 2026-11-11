using System.IO.Abstractions;
using System.IO.Compression;

namespace Lip.Core.Mcmod;

/// Checks that a file is a valid ZIP archive in the sense of protocol/lip-package.md, "Terms":
/// decided by content only, entry paths '/'-separated, not absolute, without '..' segments or '\',
/// and only regular file and directory entries.
public static class ZipValidator {
  private const int _unixFileTypeMask = 0xF000;
  private const int _unixRegularFile = 0x8000;
  private const int _unixDirectory = 0x4000;

  /// Throws ArchiveUnavailableException when the file is not valid. subject names the file in
  /// the message the way the user knows it, as the subject of a sentence, e.g. "The file
  /// downloaded from https://example.com/a.zip".
  public static void Validate(IFileInfo file, string subject) {
    using Stream stream = file.OpenRead();
    Validate(stream, subject);
  }

  /// Validates the ZIP archive in a seekable stream, which is left open.
  public static void Validate(Stream stream, string subject) {
    ZipArchive zip;
    try {
      zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
    }
    catch (InvalidDataException ex) {
      throw new ArchiveUnavailableException(
          $"{subject} is not a ZIP archive{Guess(stream)}. lip reads packages and assets only as"
          + " ZIP files (not as .tar.gz, .7z or .rar).", ex);
    }

    using (zip) {
      foreach (ZipArchiveEntry entry in zip.Entries) {
        if (GetInvalidReason(entry) is string reason) {
          throw new ArchiveUnavailableException(
              $"{subject} is not a valid archive: its entry '{entry.FullName}' {reason}, which"
              + " lip does not install for safety. Ask the package author to create the archive"
              + " with only regular files and directories at relative '/'-separated paths.");
        }
      }
    }
  }

  /// What the content is instead, when it is recognisable: e.g. a web page, which servers and
  /// proxies send instead of a file they do not have.
  private static string Guess(Stream stream) {
    try {
      stream.Position = 0;
      Span<byte> head = stackalloc byte[4];
      int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
      head = head[..read];
      if (read == 0) {
        return " (it is empty)";
      }
      if (head[0] == (byte)'<') {
        return " (it is a web page, which servers send when the file is missing or access is"
            + " denied)";
      }
      if (read >= 2 && head[0] == 0x1F && head[1] == 0x8B) {
        return " (it is a gzip file)";
      }
      if (read >= 2 && head[0] == (byte)'7' && head[1] == (byte)'z') {
        return " (it is a 7z archive)";
      }
      if (read >= 4 && head[0] == (byte)'R' && head[1] == (byte)'a' && head[2] == (byte)'r'
          && head[3] == (byte)'!') {
        return " (it is a RAR archive)";
      }
      return "";
    }
    catch (Exception ex) when (ex is IOException or NotSupportedException) {
      return "";
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
