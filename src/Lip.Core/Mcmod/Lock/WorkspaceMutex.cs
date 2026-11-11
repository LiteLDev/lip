using System.IO.Abstractions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when another process is changing the same workspace.
public class WorkspaceBusyException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// The workspace lock (protocol/lip-workspace.md, "Workspace lock"), held while an install,
/// uninstall or update changes a workspace, so that two processes never interleave their changes,
/// their journals or their state files.
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
      WorkspaceMutex mutex = new(fs.FileStream.New(
          path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
      RunTrace.Write("lock", $"Acquired the workspace lock {path}");
      return mutex;
    }
    catch (IOException ex) when (IsLocked(ex)) {
      RunTrace.Write("lock", $"The workspace lock {path} is held by another process: {ex.Message}");
      throw new WorkspaceBusyException(
          $"lip cannot take the workspace lock '{path}': most likely another lip command is"
          + $" changing the workspace '{workspaceRoot}'. Wait for the other lip command to finish,"
          + " then try again. If no other lip command is running, a program may be holding the"
          + " lock file open: close it, then try again.",
          ex);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("lock", $"Cannot open the workspace lock {path}: {ex.Message}");
      throw WorkspaceErrors.Wrap(
          ex,
          $"Cannot open the workspace lock file '{path}', which lip holds while it changes the"
          + " workspace.");
    }
  }

  /// A sharing or lock violation on Windows; elsewhere the failed flock.
  private static bool IsLocked(IOException ex) =>
      ex is not FileNotFoundException and not DirectoryNotFoundException
      && (!OperatingSystem.IsWindows() || (ex.HResult & 0xFFFF) is 32 or 33);

  public void Dispose() {
    if (_stream is not null) {
      RunTrace.Write("lock", $"Released the workspace lock {_stream.Name}");
    }

    _stream?.Dispose();
    _stream = null;
  }
}
