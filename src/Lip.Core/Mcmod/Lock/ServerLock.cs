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
  private FileSystemStream? _stream;

  private ServerLock(FileSystemStream stream) {
    _stream = stream;
  }

  /// Takes the lock, or throws ServerRunningException at once when another process holds it.
  /// The directory of the lock file must exist.
  public static ServerLock Acquire(IFileSystem fs, string path, string workspaceRoot) {
    try {
      ServerLock serverLock = new(fs.FileStream.New(
          path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
      RunTrace.Write("lock", $"Acquired the server lock {path}");
      return serverLock;
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

  public void Dispose() {
    if (_stream is not null) {
      RunTrace.Write("lock", $"Released the server lock {_stream.Name}");
    }

    _stream?.Dispose();
    _stream = null;
  }
}
