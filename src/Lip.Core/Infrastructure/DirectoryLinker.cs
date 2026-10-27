using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lip.Core.Infrastructure;

/// Creates and reads directory links, hiding how a platform links directories.
public interface IDirectoryLinker {
  /// Creates a link at linkPath to the directory targetPath, which must exist. linkPath must not
  /// exist.
  void CreateLink(string linkPath, string targetPath);

  /// The target of the link at path as stored in the link (absolute, or relative to the link's
  /// directory), or null when path is not a link.
  string? GetLinkTarget(string path);
}

/// Links directories with a directory junction on Windows, which needs neither administrator
/// rights nor developer mode (unlike a symbolic link), and with a relative symbolic link
/// elsewhere.
public class DirectoryLinker(IFileSystem fileSystem) : IDirectoryLinker {
  private readonly IFileSystem _fs = fileSystem;

  public void CreateLink(string linkPath, string targetPath) {
    if (OperatingSystem.IsWindows()) {
      CreateJunction(_fs.Path.GetFullPath(linkPath), _fs.Path.GetFullPath(targetPath));
      return;
    }

    string relative = _fs.Path.GetRelativePath(
        _fs.Path.GetDirectoryName(_fs.Path.GetFullPath(linkPath))!,
        _fs.Path.GetFullPath(targetPath));
    _fs.Directory.CreateSymbolicLink(linkPath, relative);
  }

  /// FileSystemInfo.LinkTarget reads both symbolic links and junctions, and is null for a
  /// missing path.
  public string? GetLinkTarget(string path) => _fs.DirectoryInfo.New(path).LinkTarget;

  // .NET has no API for junctions. They are created here with FSCTL_SET_REPARSE_POINT rather
  // than "cmd /c mklink /J": no child process, no quoting of paths through cmd, and failures
  // come back as Win32 error codes instead of localized console output.

  private const uint GenericWrite = 0x40000000;
  private const uint OpenExisting = 3;
  private const uint FileFlagBackupSemantics = 0x02000000;
  private const uint FileFlagOpenReparsePoint = 0x00200000;
  private const uint FsctlSetReparsePoint = 0x000900A4;
  private const uint IoReparseTagMountPoint = 0xA0000003;

  [SupportedOSPlatform("windows")]
  private void CreateJunction(string linkPath, string targetPath) {
    // The substitute name is an NT path; the print name is what tools display.
    byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + targetPath);
    byte[] print = Encoding.Unicode.GetBytes(targetPath);

    // REPARSE_DATA_BUFFER: tag, data length, reserved, then MountPointReparseBuffer: substitute
    // name offset and length, print name offset and length (in bytes, into the path buffer),
    // and the path buffer holding both names, each NUL-terminated.
    int pathBufferLength = substitute.Length + 2 + print.Length + 2;
    int dataLength = 8 + pathBufferLength;
    byte[] buffer = new byte[8 + dataLength];
    Span<byte> span = buffer;
    BinaryPrimitives.WriteUInt32LittleEndian(span, IoReparseTagMountPoint);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)dataLength);
    BinaryPrimitives.WriteUInt16LittleEndian(span[8..], 0);
    BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)substitute.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)(substitute.Length + 2));
    BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)print.Length);
    substitute.CopyTo(span[16..]);
    print.CopyTo(span[(16 + substitute.Length + 2)..]);

    // A junction is an empty directory carrying the reparse point. Junctions can only target
    // local volumes; elsewhere (a network share, FAT32) setting the reparse point fails.
    _fs.Directory.CreateDirectory(linkPath);
    try {
      using SafeFileHandle handle = CreateFileW(
          linkPath,
          GenericWrite,
          0,
          IntPtr.Zero,
          OpenExisting,
          FileFlagBackupSemantics | FileFlagOpenReparsePoint,
          IntPtr.Zero);
      if (handle.IsInvalid) {
        throw new Win32Exception(Marshal.GetLastWin32Error());
      }

      if (!DeviceIoControl(
          handle,
          FsctlSetReparsePoint,
          buffer,
          buffer.Length,
          IntPtr.Zero,
          0,
          out _,
          IntPtr.Zero)) {
        throw new Win32Exception(Marshal.GetLastWin32Error());
      }
    }
    catch (Exception ex) {
      try {
        _fs.Directory.Delete(linkPath);
      }
      catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) {
        // The original error matters more; an empty directory is left behind.
      }

      throw new IOException(
          $"Cannot create a junction '{linkPath}' to '{targetPath}': {ex.Message}", ex);
    }
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern SafeFileHandle CreateFileW(
      string fileName,
      uint desiredAccess,
      uint shareMode,
      IntPtr securityAttributes,
      uint creationDisposition,
      uint flagsAndAttributes,
      IntPtr templateFile);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DeviceIoControl(
      SafeFileHandle device,
      uint ioControlCode,
      byte[] inBuffer,
      int inBufferSize,
      IntPtr outBuffer,
      int outBufferSize,
      out int bytesReturned,
      IntPtr overlapped);
}
