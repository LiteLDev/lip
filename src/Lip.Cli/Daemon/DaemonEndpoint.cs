using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Daemon;

/// Thrown when the endpoint cannot be created because another daemon has it.
public sealed class EndpointInUseException(string message, Exception? inner = null)
    : IOException(message, inner);

/// Thrown when a daemon has the endpoint but took no connection within the time waited for it.
public sealed class DaemonBusyException(TimeSpan waited) : IOException(
    $"the daemon of lip that runs took no connection within {waited.TotalSeconds:0.#} seconds");

/// Where the daemon of the user is reached (protocol/lip-daemon.md, "Endpoint"): on Windows a
/// named pipe that only the user can open, elsewhere a Unix domain socket in the daemon
/// directory. Name is the name of the pipe, or the path of the socket.
public sealed class DaemonEndpoint(string name) {
  /// In a Debug build only, the variable that names another endpoint, so that tests run their own
  /// daemons; a Release build, which is what lip is released as, has no such hook.
  public const string OverrideVariable = "LIP_DAEMON_ENDPOINT";

  /// How long a daemon that listens but takes no connection is waited for.
  public static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(10);

  private const int UserOnlyDirectory = 0b111_000_000;

  // Windows error codes of WaitNamedPipe: no instance of the pipe exists; none became free.
  private const int ErrorFileNotFound = 2;
  private const int ErrorSemTimeout = 121;

  // How long one wait for a busy daemon lasts before the time and the cancellation are checked.
  private static readonly TimeSpan _busyPoll = TimeSpan.FromMilliseconds(100);
  // How long one attempt to connect to the socket may take.
  private static readonly TimeSpan _connectAttempt = TimeSpan.FromSeconds(1);
  // How long a socket that refuses connections is tried, where a full one refuses them too.
  private static readonly TimeSpan _refusedGrace = TimeSpan.FromMilliseconds(300);
  // How long a daemon that starts waits for one that exits to let go of the daemon directory.
  private static readonly TimeSpan _exitingDaemon = TimeSpan.FromSeconds(2);

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

  /// A connection to the daemon, or null when no daemon listens. A daemon that listens but takes
  /// no connection yet, because every instance of its named pipe is taken or its socket has no
  /// room for one more, is waited for; DaemonBusyException when it takes none within BusyTimeout.
  /// Throws UnauthorizedAccessException when the named pipe is not owned by the user.
  public Task<Stream?> TryConnect(CancellationToken cancellationToken = default) =>
      TryConnect(BusyTimeout, cancellationToken);

  /// TryConnect that waits for a daemon that takes no connection yet for busyTimeout.
  public Task<Stream?> TryConnect(TimeSpan busyTimeout, CancellationToken cancellationToken) =>
      OperatingSystem.IsWindows()
          ? ConnectToPipe(busyTimeout, cancellationToken)
          : ConnectToSocket(busyTimeout, cancellationToken);

  /// Connects on a thread of its own, since connecting to a named pipe and waiting for one
  /// block: the threads that the tasks of the process share stay free, as for a daemon in
  /// this process.
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private Task<Stream?> ConnectToPipe(TimeSpan busyTimeout, CancellationToken cancellationToken) {
    TaskCompletionSource<Stream?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Thread thread = new(() => {
      try {
        result.SetResult(ConnectToPipeNow(busyTimeout, cancellationToken));
      }
      catch (OperationCanceledException) {
        result.SetCanceled(cancellationToken);
      }
      catch (Exception ex) {
        result.SetException(ex);
      }
    }) {
      IsBackground = true,
      Name = "lip daemon connection",
    };
    thread.Start();
    return result.Task;
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private NamedPipeClientStream? ConnectToPipeNow(
      TimeSpan busyTimeout, CancellationToken cancellationToken) {
    Stopwatch watch = Stopwatch.StartNew();
    string path = $@"\\.\pipe\{Name}";
    // Whether the last wait found a free instance, which the next attempt is for whatever the
    // time; and how many attempts in a row failed although one was free.
    bool free = false;
    int lost = 0;
    while (true) {
      cancellationToken.ThrowIfCancellationRequested();
      NamedPipeClientStream pipe = new(
          ".", Name, PipeDirection.InOut,
          PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      try {
        // One attempt: it fails alike when the pipe does not exist and when none of its
        // instances is free, which WaitNamedPipe then tells apart.
        pipe.Connect(0);
        return pipe;
      }
      catch (Exception ex) when (ex is TimeoutException or IOException) {
        pipe.Dispose();
      }
      catch {
        pipe.Dispose();
        throw;
      }

      if (free && watch.Elapsed >= busyTimeout) {
        throw new DaemonBusyException(busyTimeout);
      }

      if (free && ++lost > 1) {
        // An instance is free each time, yet connecting fails: not as fast as it can be tried.
        Thread.Sleep(TimeSpan.FromMilliseconds(20));
      }

      if (WaitNamedPipeW(path, (uint)_busyPoll.TotalMilliseconds)) {
        // An instance is free, until another client takes it: it is tried for, even when the
        // time has passed.
        free = true;
        continue;
      }

      int error = Marshal.GetLastWin32Error();
      switch (error) {
        case ErrorFileNotFound:
          return null;
        case ErrorSemTimeout:
          free = false;
          lost = 0;
          if (watch.Elapsed >= busyTimeout) {
            throw new DaemonBusyException(busyTimeout);
          }

          break;
        default:
          throw new IOException(
              $"Waiting for {path} failed: {new Win32Exception(error).Message}");
      }
    }
  }

  private async Task<Stream?> ConnectToSocket(
      TimeSpan busyTimeout, CancellationToken cancellationToken) {
    Stopwatch watch = Stopwatch.StartNew();
    while (true) {
      if (!File.Exists(Name)) {
        return null;
      }

      bool refused = false;
      Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
      try {
        using CancellationTokenSource attempt =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_connectAttempt);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(Name), attempt.Token);
        return new NetworkStream(socket, ownsSocket: true);
      }
      catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused
          && !OperatingSystem.IsLinux()) {
        // Nothing listens on the socket, or, as systems other than Linux say it, the socket
        // has no room for one more connection: a daemon accepts some within a moment.
        socket.Dispose();
        refused = true;
      }
      catch (SocketException ex) when (ex.SocketErrorCode
          is not (SocketError.WouldBlock or SocketError.TryAgain or SocketError.TimedOut)) {
        // Nothing listens on the socket: a daemon left it behind, or deleted it just now.
        socket.Dispose();
        return null;
      }
      catch (SocketException) {
        // The socket has no room for one more connection until the daemon accepts some.
        socket.Dispose();
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
        socket.Dispose();
      }
      catch {
        socket.Dispose();
        throw;
      }

      if (refused) {
        if (watch.Elapsed >= _refusedGrace) {
          return null;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        continue;
      }

      if (watch.Elapsed >= busyTimeout) {
        throw new DaemonBusyException(busyTimeout);
      }

      await Task.Delay(_busyPoll, cancellationToken);
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
    // The instances that clients connected to, until they are closed.
    private readonly List<NamedPipeServerStream> _taken = [];
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
        // An instance is added only to a pipe that this daemon still holds an instance of:
        // otherwise the pipe may be gone, and another daemon may have created it since.
        _taken.RemoveAll(taken => !IsOpen(taken));
        waiting = _next ??= Create(first: _taken.Count == 0);
      }

      await waiting.WaitForConnectionAsync(cancellationToken);
      lock (_gate) {
        _next = null;
        if (_disposed) {
          waiting.Dispose();
          throw new ObjectDisposedException(nameof(PipeListener));
        }

        // The connected instance keeps the pipe while the next one is created.
        _taken.Add(waiting);
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

    private static bool IsOpen(NamedPipeServerStream instance) {
      try {
        return !instance.SafePipeHandle.IsClosed;
      }
      catch (ObjectDisposedException) {
        return false;
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
      Stopwatch watch = Stopwatch.StartNew();
      while (true) {
        try {
          lockFile = new FileStream(
              Path.Join(directory, "daemon.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
              FileShare.None);
          break;
        }
        catch (IOException ex) {
          // A daemon that exits deletes its socket, then lets go of the lock: it is waited for,
          // so that the client that started this daemon finds one.
          if (File.Exists(path) || watch.Elapsed >= _exitingDaemon) {
            throw new EndpointInUseException($"Another daemon listens on {path}.", ex);
          }

          Thread.Sleep(TimeSpan.FromMilliseconds(20));
        }
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
      // The socket is deleted first, so that a client finds a daemon that listens or no socket;
      // the lock is let go of last, so that the next daemon deletes no socket but its own.
      try {
        File.Delete(path);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      }

      socket.Dispose();
      lockFile.Dispose();
    }
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool WaitNamedPipeW(string name, uint timeout);
}

/// An endpoint that a daemon listens on. Disposing it stops listening, and deletes the socket.
public interface IDaemonListener : IDisposable {
  Task<Stream> Accept(CancellationToken cancellationToken);
}
