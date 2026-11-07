using System.Runtime.InteropServices;

namespace Lip.Core.Infrastructure;

/// The type of a file on Unix, which .NET does not expose: directory listings return FIFOs,
/// sockets and device nodes along with regular files, and FileAttributes tells them apart only
/// from directories and links.
public static class UnixFileType {
  // The file type bits of the mode System.Native reports, the same on every Unix.
  private const int _typeMask = 0xF000;
  private const int _regularFile = 0x8000;
  private const int _directory = 0x4000;
  private const int _symbolicLink = 0xA000;

  /// Whether the file at path, not following links, exists and is neither a regular file nor a
  /// directory nor a link (e.g. a FIFO, socket or device node). Always false on Windows, and
  /// when the type cannot be read, so that reading the file reports why.
  public static bool IsSpecialFile(string path) {
    if (OperatingSystem.IsWindows() || LStat(path, out FileStatus status) != 0) {
      return false;
    }

    return (status.Mode & _typeMask) is not (_regularFile or _directory or _symbolicLink);
  }

  // The lstat of the System.Native library of the .NET runtime, whose FileStatus has the same
  // layout on every Unix and begins with the fields below; Size leaves room for the rest of it.
  [DllImport("libSystem.Native", EntryPoint = "SystemNative_LStat")]
  private static extern int LStat(
      [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out FileStatus output);

  [StructLayout(LayoutKind.Explicit, Size = 256)]
  private readonly struct FileStatus {
    [FieldOffset(4)]
    public readonly int Mode;
  }
}
