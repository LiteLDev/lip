using System.IO.Abstractions;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when another lip process is changing the same workspace.
public class WorkspaceBusyException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// An exclusive lock of one workspace across processes, held while an install, uninstall or
/// update runs, so that two lip processes never interleave their changes, their journals or
/// their lock files.
///
/// It is a file next to the workspace lock (&lt;Data&gt;/workspaces/&lt;hash&gt;.lock) opened without
/// sharing: on Windows no other process can open it, elsewhere .NET takes an advisory lock
/// (flock) on it. The file is deleted when the lock is released. A process that is killed
/// releases the lock with its handles; the file it leaves is taken over by the next process.
public sealed class WorkspaceMutex : IDisposable {
  private readonly IFileSystem _fs;
  private readonly string _path;
  private FileSystemStream? _stream;

  private WorkspaceMutex(IFileSystem fs, string path, FileSystemStream stream) {
    _fs = fs;
    _path = path;
    _stream = stream;
  }

  /// Takes the lock, or throws WorkspaceBusyException at once when another process holds it.
  public static WorkspaceMutex Acquire(IFileSystem fs, string path, string workspaceRoot) {
    fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
    // A process releasing the lock elsewhere than on Windows deletes the file, then closes it: a
    // process that opened the file just before then holds a lock on a deleted file. Such a lock
    // is given up for one on the file at the path.
    for (int attempt = 0; ; attempt++) {
      FileSystemStream stream;
      try {
        stream = fs.FileStream.New(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
      }
      catch (IOException ex) when (IsLocked(ex)) {
        throw new WorkspaceBusyException(
            $"Another lip process is changing the workspace '{workspaceRoot}'. Wait for it to"
            + " finish, then try again.",
            ex);
      }

      if (fs.File.Exists(path) || attempt >= 3) {
        return new WorkspaceMutex(fs, path, stream);
      }

      stream.Dispose();
    }
  }

  /// A sharing or lock violation on Windows; elsewhere the failed flock.
  private static bool IsLocked(IOException ex) =>
      ex is not FileNotFoundException and not DirectoryNotFoundException
      && (!OperatingSystem.IsWindows() || (ex.HResult & 0xFFFF) is 32 or 33);

  public void Dispose() {
    if (_stream is null) {
      return;
    }

    if (!OperatingSystem.IsWindows()) {
      try {
        _fs.File.Delete(_path);
      }
      catch (IOException) {
        // Left for the next process to take over.
      }
    }

    _stream.Dispose();
    _stream = null;
  }
}
