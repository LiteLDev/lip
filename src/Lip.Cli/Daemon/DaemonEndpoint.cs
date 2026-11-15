using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Daemon;

/// Thrown when the endpoint cannot be created because another daemon has it.
public sealed class EndpointInUseException(string message, Exception? inner = null)
    : IOException(message, inner);

/// Where the daemon of the user is reached (protocol/lip-daemon.md, "Endpoint"): on Windows a
/// named pipe that only the user can open, elsewhere a Unix domain socket in the daemon
/// directory. Name is the name of the pipe, or the path of the socket.
public sealed class DaemonEndpoint(string name) {
  /// In a Debug build only, the variable that names another endpoint, so that tests run their own
  /// daemons; a Release build, which is what lip is released as, has no such hook.
  public const string OverrideVariable = "LIP_DAEMON_ENDPOINT";

  private const int UserOnlyDirectory = 0b111_000_000;

  public string Name { get; } = name;

  /// The endpoint of the user who runs this process.
  public static DaemonEndpoint ForCurrentUser() {
#if DEBUG
    if (Invocation.Current.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } name) {
      return new DaemonEndpoint(name);
    }
#endif
    return OperatingSystem.IsWindows()
        ? new DaemonEndpoint($"lip-{WindowsIdentity.GetCurrent().User!.Value}")
        : new DaemonEndpoint(Path.Join(LipDirectories.Daemon, "daemon.sock"));
  }

  /// A connection to the daemon, or null when no daemon listens. Throws
  /// UnauthorizedAccessException when the named pipe is not owned by the user.
  public async Task<Stream?> TryConnect(CancellationToken cancellationToken = default) {
    if (OperatingSystem.IsWindows()) {
      NamedPipeClientStream pipe = new(
          ".", Name, PipeDirection.InOut,
          PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      try {
        await pipe.ConnectAsync(200, cancellationToken);
        return pipe;
      }
      catch (Exception ex) when (ex is TimeoutException or IOException) {
        await pipe.DisposeAsync();
        return null;
      }
      catch {
        await pipe.DisposeAsync();
        throw;
      }
    }

    if (!File.Exists(Name)) {
      return null;
    }

    Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    try {
      await socket.ConnectAsync(new UnixDomainSocketEndPoint(Name), cancellationToken);
      return new NetworkStream(socket, ownsSocket: true);
    }
    catch (SocketException) {
      socket.Dispose();
      return null;
    }
  }

  /// Creates the endpoint and listens on it. Throws EndpointInUseException when another daemon
  /// has it.
  public IDaemonListener Listen() =>
      OperatingSystem.IsWindows() ? new PipeListener(Name) : SocketListener.Create(Name);

  /// Creates the daemon directory of a socket, accessible to the user only.
  private static void CreateUserDirectory(string directory) {
    if (OperatingSystem.IsWindows()) {
      return;
    }

    Directory.CreateDirectory(directory, (UnixFileMode)UserOnlyDirectory);
    File.SetUnixFileMode(directory, (UnixFileMode)UserOnlyDirectory);
  }

  /// Named pipe instances: one always waits for a client, so that the pipe exists as long as the
  /// daemon runs and a second daemon cannot create it.
  private sealed class PipeListener : IDaemonListener {
    private readonly string _name;
    private readonly object _gate = new();
    // The instance that waits for the next client; null once disposed, or when creating it failed.
    private NamedPipeServerStream? _next;
    private bool _disposed;

    public PipeListener(string name) {
      _name = name;
      try {
        _next = Create(first: true);
      }
      catch (UnauthorizedAccessException ex) {
        throw new EndpointInUseException($"Another daemon listens on \\\\.\\pipe\\{name}.", ex);
      }
      catch (IOException ex) {
        throw new EndpointInUseException($"Another daemon listens on \\\\.\\pipe\\{name}.", ex);
      }
    }

    public async Task<Stream> Accept(CancellationToken cancellationToken) {
      NamedPipeServerStream waiting;
      lock (_gate) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        waiting = _next ??= Create(first: false);
      }

      await waiting.WaitForConnectionAsync(cancellationToken);
      lock (_gate) {
        _next = null;
        if (_disposed) {
          waiting.Dispose();
          throw new ObjectDisposedException(nameof(PipeListener));
        }

        // The connected instance keeps the pipe while the next one is created.
        try {
          _next = Create(first: false);
        }
        catch (IOException) {
          // Created again by the next Accept.
        }
      }

      return waiting;
    }

    public void Dispose() {
      lock (_gate) {
        _disposed = true;
        _next?.Dispose();
        _next = null;
      }
    }

    private NamedPipeServerStream Create(bool first) =>
        new(
            _name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
                | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));
  }

  /// A Unix domain socket. A lock on daemon.lock beside it, held while the daemon runs, tells a
  /// socket that a daemon listens on from one that a daemon left behind.
  [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
  private sealed class SocketListener(Socket socket, string path, FileStream lockFile)
      : IDaemonListener {
    public static SocketListener Create(string path) {
      string directory = Path.GetDirectoryName(path)!;
      CreateUserDirectory(directory);
      FileStream lockFile;
      try {
        lockFile = new FileStream(
            Path.Join(directory, "daemon.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None);
      }
      catch (IOException ex) {
        throw new EndpointInUseException($"Another daemon listens on {path}.", ex);
      }

      Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
      try {
        // A socket left by a daemon that ended without deleting it.
        File.Delete(path);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        socket.Listen();
        return new SocketListener(socket, path, lockFile);
      }
      catch {
        socket.Dispose();
        lockFile.Dispose();
        throw;
      }
    }

    public async Task<Stream> Accept(CancellationToken cancellationToken) =>
        new NetworkStream(await socket.AcceptAsync(cancellationToken), ownsSocket: true);

    public void Dispose() {
      socket.Dispose();
      try {
        File.Delete(path);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      }

      lockFile.Dispose();
    }
  }
}

/// An endpoint that a daemon listens on. Disposing it stops listening, and deletes the socket.
public interface IDaemonListener : IDisposable {
  Task<Stream> Accept(CancellationToken cancellationToken);
}
