using System.IO.Abstractions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Lock;

/// Thrown when another process runs the server of the workspace.
public class ServerRunningException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// The server lock (protocol/lip-workspace.md, "Server lock"), held by the process that runs the
/// server of a workspace for as long as lip run runs it, so that at most one server of the
/// workspace runs. It is the file &lt;root&gt;/.lip/server.lock, opened without sharing, as the
/// workspace lock is (WorkspaceMutex).
public sealed class ServerLock : IDisposable {
  // How many more times Acquire tries when the lock file is in use, and how long it waits
  // before each: IsHeld has the file open for a moment, which must not look like a server.
  private const int Retries = 3;
  private const int RetryMilliseconds = 20;

  private FileSystemStream? _stream;

  private ServerLock(FileSystemStream stream) {
    _stream = stream;
  }

  /// Takes the lock, or throws ServerRunningException when another process holds it: without
  /// waiting for it to be released, but for the moment that a process asking whether the lock
  /// is held (IsHeld) has the file open. The directory of the lock file must exist.
  public static ServerLock Acquire(IFileSystem fs, string path, string workspaceRoot) {
    try {
      for (int attempt = 0; ; attempt++) {
        try {
          ServerLock serverLock = new(fs.FileStream.New(
              path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
          RunTrace.Write("lock", $"Acquired the server lock {path}");
          return serverLock;
        }
        catch (IOException ex) when (WorkspaceMutex.IsLocked(ex) && attempt < Retries) {
          Thread.Sleep(RetryMilliseconds);
        }
      }
    }
    catch (IOException ex) when (WorkspaceMutex.IsLocked(ex)) {
      RunTrace.Write("lock", $"The server lock {path} is held by another process: {ex.Message}");
      throw new ServerRunningException(
          $"A server of the workspace '{workspaceRoot}' is already running: another lip run, the"
          + " interface of lip or the daemon of lip runs it, and holds the server lock"
          + $" '{path}'. Stop that server first, or attach to it with 'lip run --managed' when it"
          + " is a managed server.");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("lock", $"Cannot open the server lock {path}: {ex.Message}");
      throw WorkspaceErrors.Wrap(
          ex,
          $"Cannot open the server lock file '{path}', which lip holds while it runs the server.");
    }
  }

  /// Whether a process, this one included, holds the server lock of a workspace: whether its
  /// server runs. Takes no lock and creates no file; the lock file is open for a moment only,
  /// which Acquire waits out. False when that cannot be told, such as without a lock file.
  public static bool IsHeld(IFileSystem fs, string workspaceRoot) {
    string path = new LockStore(fs).GetServerLockPath(workspaceRoot);
    try {
      if (!fs.File.Exists(path)) {
        return false;
      }

      // Opening the file fails while it is open without sharing, or exclusively locked.
      using FileSystemStream stream = fs.FileStream.New(
          path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
          bufferSize: 1);
      return false;
    }
    catch (IOException ex) when (ex.GetType() == typeof(IOException)
        && WorkspaceMutex.IsLocked(ex)) {
      // What makes Acquire say that the server runs: neither a path that is too long nor a
      // drive that is gone, which are IOExceptions of their own types.
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException) {
      return false;
    }
  }

  public void Dispose() {
    if (_stream is not null) {
      RunTrace.Write("lock", $"Released the server lock {_stream.Name}");
    }

    _stream?.Dispose();
    _stream = null;
  }
}
