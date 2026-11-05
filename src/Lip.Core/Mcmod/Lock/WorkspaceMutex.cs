using System.IO.Abstractions;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when another process is changing the same workspace.
public class WorkspaceBusyException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// The workspace lock (protocol/workspace.md §7.2), held while an install, uninstall or update
/// changes a workspace, so that two processes never interleave their changes, their journals or
/// their workspace files.
///
/// It is the file &lt;root&gt;/.lip/lock, opened without sharing: on Windows no other process can
/// open it, elsewhere .NET takes an exclusive flock on it. The file is never deleted, so every
/// process locks the same file. A process that is killed releases the lock with its handles.
public sealed class WorkspaceMutex : IDisposable {
  private FileSystemStream? _stream;

  private WorkspaceMutex(FileSystemStream stream) {
    _stream = stream;
  }

  /// Takes the lock, or throws WorkspaceBusyException at once when another process holds it.
  /// The directory of the lock file must exist.
  public static WorkspaceMutex Acquire(IFileSystem fs, string path, string workspaceRoot) {
    try {
      return new WorkspaceMutex(fs.FileStream.New(
          path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
    }
    catch (IOException ex) when (IsLocked(ex)) {
      throw new WorkspaceBusyException(
          $"Another process is changing the workspace '{workspaceRoot}'. Wait for it to"
          + " finish, then try again.",
          ex);
    }
  }

  /// A sharing or lock violation on Windows; elsewhere the failed flock.
  private static bool IsLocked(IOException ex) =>
      ex is not FileNotFoundException and not DirectoryNotFoundException
      && (!OperatingSystem.IsWindows() || (ex.HResult & 0xFFFF) is 32 or 33);

  public void Dispose() {
    _stream?.Dispose();
    _stream = null;
  }
}
