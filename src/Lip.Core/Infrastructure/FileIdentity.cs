using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lip.Core.Infrastructure;

/// The identity of a file: the device (volume) holding it and its number on that device. Two
/// paths name the same file exactly when their identities are equal, however they reach it
/// (links, junctions, case, 8.3 short names, subst drives), which comparing paths cannot tell.
public readonly record struct FileIdentity(ulong Device, UInt128 File) {
  /// The identity of the file at path, following links, or null when it cannot be read (e.g.
  /// the file does not exist).
  public static FileIdentity? Of(string path) =>
      OperatingSystem.IsWindows() ? OfWindows(path) : OfUnix(path);

  private static FileIdentity? OfUnix(string path) =>
      Stat(path, out FileStatus status) == 0
          ? new FileIdentity((ulong)status.Dev, (ulong)status.Ino)
          : null;

  private static FileIdentity? OfWindows(string path) {
    // No access is needed to read the identity, and sharing everything keeps it readable while
    // other programs have the file open.
    using SafeFileHandle handle = CreateFileW(
        path, 0, _fileShareAll, 0, _openExisting, _fileFlagBackupSemantics, 0);
    if (handle.IsInvalid) {
      return null;
    }

    // FILE_ID_INFO holds the 128-bit numbers of ReFS; file systems without it (e.g. FAT) only
    // give the 64-bit file index.
    if (GetFileInformationByHandleEx(
            handle, _fileIdInfo, out FileIdInfo id, Marshal.SizeOf<FileIdInfo>())) {
      return new FileIdentity(id.VolumeSerialNumber, new UInt128(id.FileIdHigh, id.FileIdLow));
    }

    return GetFileInformationByHandle(handle, out ByHandleFileInformation info)
        ? new FileIdentity(
            info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow)
        : null;
  }

  // The stat of the System.Native library of the .NET runtime, whose FileStatus has the same
  // layout on every Unix: Flags, Mode, Uid, Gid (4 bytes each), then the 8-byte Size, ATime,
  // ATimeNsec, MTime, MTimeNsec, CTime, CTimeNsec, BirthTime, BirthTimeNsec, Dev, RDev and Ino.
  [DllImport("libSystem.Native", EntryPoint = "SystemNative_Stat")]
  private static extern int Stat(
      [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out FileStatus output);

  [StructLayout(LayoutKind.Explicit, Size = 256)]
  private readonly struct FileStatus {
    [FieldOffset(88)]
    public readonly long Dev;

    [FieldOffset(104)]
    public readonly long Ino;
  }

  private const uint _fileShareAll = 0x7;
  private const uint _openExisting = 3;
  private const uint _fileFlagBackupSemantics = 0x02000000;
  private const int _fileIdInfo = 18;

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern SafeFileHandle CreateFileW(
      string path, uint access, uint share, nint security, uint disposition, uint flags,
      nint template);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetFileInformationByHandleEx(
      SafeFileHandle handle, int informationClass, out FileIdInfo information, int size);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetFileInformationByHandle(
      SafeFileHandle handle, out ByHandleFileInformation information);

  [StructLayout(LayoutKind.Sequential)]
  private readonly struct FileIdInfo {
    public readonly ulong VolumeSerialNumber;
    public readonly ulong FileIdLow;
    public readonly ulong FileIdHigh;
  }

  // FILETIME is two 4-byte halves, so the times are 4-byte aligned.
  [StructLayout(LayoutKind.Sequential, Pack = 4)]
  private readonly struct ByHandleFileInformation {
    public readonly uint FileAttributes;
    public readonly long CreationTime;
    public readonly long LastAccessTime;
    public readonly long LastWriteTime;
    public readonly uint VolumeSerialNumber;
    public readonly uint FileSizeHigh;
    public readonly uint FileSizeLow;
    public readonly uint NumberOfLinks;
    public readonly uint FileIndexHigh;
    public readonly uint FileIndexLow;
  }
}
