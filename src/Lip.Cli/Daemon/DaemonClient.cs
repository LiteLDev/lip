using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lip.Cli.Daemon;

/// The standard streams of a client: what the daemon reads and writes for its command.
/// InputIsConsole: Input is an interactive Windows console, a read of which may return nothing
/// though it has not ended.
public sealed record ClientStreams(
    Stream Input,
    bool InputIsConsole,
    Stream Output,
    Stream Error,
    TextWriter OutputText,
    TextWriter ErrorText);

/// How a command that a client asked the daemon for ended: with an exit code; ended by a signal,
/// which ends the client at once; or not carried out by a daemon, for the reason given
/// (protocol/lip-daemon.md, "Fallback").
public sealed record ClientOutcome(int? ExitCode, PosixSignal? Signal, string? Fallback);

/// A client of the daemon (protocol/lip-daemon.md): connects to the endpoint, starting a daemon
/// when none listens and replacing one of another version or executable, and has the daemon carry
/// out its command. startDaemon starts a daemon, returning why it could not, or null.
public sealed class DaemonClient(
    DaemonEndpoint endpoint,
    string version,
    string executable,
    Func<string?> startDaemon) {
  /// How long a started daemon may take to listen, and an old one to exit.
  private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(10);

  private FrameWriter? _writer;
  // Why Connect found no daemon.
  private string? _fallback;

  /// Whether the version and the executable of a daemon are those of this client.
  public bool IsSame(Welcome welcome) =>
      welcome.Version == version
      && string.Equals(
          welcome.Executable, executable,
          OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

  /// Passes on a signal that the client received to the command. False when no command runs yet,
  /// so that the signal ends the client as it would end lip.
  public bool Signal(PosixSignal signal) =>
      Volatile.Read(ref _writer) is { } writer
      && writer.Send(FrameType.Signal, Messages.Int32((int)signal));

  /// Has the daemon carry out the command (protocol/lip-daemon.md, "Carrying out a command").
  public async Task<ClientOutcome> Run(Start start, ClientStreams streams) {
    // A daemon that is exiting refuses the command, and the next attempt starts a new one.
    for (int attempt = 0; attempt < 3; attempt++) {
      Stream? connection;
      try {
        connection = await Connect();
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
          or InvalidDataException or TimeoutException) {
        return new ClientOutcome(null, null, ex.Message);
      }

      if (connection is null) {
        return new ClientOutcome(null, null, _fallback);
      }

      await using (connection) {
        if (await Carry(connection, start, streams) is { } outcome) {
          return outcome;
        }
      }
    }

    return new ClientOutcome(null, null, "the daemon keeps exiting");
  }

  /// A connection to a daemon of this version and executable, after Hello; null with _fallback
  /// set when there is none.
  private async Task<Stream?> Connect() {
    bool started = false;
    for (int attempt = 0; attempt < 3; attempt++) {
      Stream? connection = await endpoint.TryConnect();
      if (connection is null) {
        if (started) {
          _fallback = "the daemon that lip started does not listen";
          return null;
        }

        if (startDaemon() is { } error) {
          _fallback = error;
          return null;
        }

        started = true;
        connection = await ConnectWithin(_startTimeout);
        if (connection is null) {
          _fallback = "the daemon that lip started did not listen within"
              + $" {_startTimeout.TotalSeconds:0} seconds";
          return null;
        }
      }

      Welcome welcome;
      try {
        welcome = await Greet(connection);
      }
      catch {
        await connection.DisposeAsync();
        throw;
      }

      if (IsSame(welcome)) {
        return connection;
      }

      // A daemon of another version, such as the one that ran before lip updated itself.
      bool exits;
      await using (connection) {
        exits = await AskToExit(connection);
      }

      if (!exits) {
        _fallback = $"the daemon of lip {welcome.Version} that runs carries out a command";
        return null;
      }

      await WaitForExit(welcome.ProcessId);
      started = false;
    }

    _fallback = "the daemon keeps being replaced";
    return null;
  }

  /// Sends Hello and reads the Welcome of the daemon.
  public async Task<Welcome> Greet(Stream connection) {
    Frames.Write(connection, FrameType.Hello, new Hello(version, executable).Encode());
    return await Frames.Read(connection, CancellationToken.None)
        is { Type: FrameType.Welcome } frame
        ? Welcome.Decode(frame.Payload)
        : throw new InvalidDataException("The daemon did not answer as a daemon of lip.");
  }

  /// Asks the daemon, after Greet, to exit; whether it does.
  public static async Task<bool> AskToExit(Stream connection) {
    Frames.Write(connection, FrameType.Shutdown, []);
    return await Frames.Read(connection, CancellationToken.None)
        is { Type: FrameType.ShutdownReply } frame
        && Messages.ReadBoolean(frame.Payload);
  }

  /// Waits until the process has exited, or the start timeout has passed. A daemon in this
  /// process, as in tests, has stopped listening already.
  public static async Task WaitForExit(int processId) {
    if (processId == Environment.ProcessId) {
      return;
    }

    try {
      using Process process = Process.GetProcessById(processId);
      using CancellationTokenSource timeout = new(_startTimeout);
      await process.WaitForExitAsync(timeout.Token);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
        or OperationCanceledException or System.ComponentModel.Win32Exception) {
      // It has exited already, or cannot be waited for: connecting tells.
    }
  }

  private async Task<Stream?> ConnectWithin(TimeSpan timeout) {
    Stopwatch watch = Stopwatch.StartNew();
    TimeSpan delay = TimeSpan.FromMilliseconds(20);
    while (true) {
      if (await endpoint.TryConnect() is { } connection) {
        return connection;
      }

      if (watch.Elapsed >= timeout) {
        return null;
      }

      await Task.Delay(delay);
      delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 200));
    }
  }

  /// The outcome of the command, or null when the daemon refused it because it exits.
  private async Task<ClientOutcome?> Carry(
      Stream connection, Start start, ClientStreams streams) {
    FrameWriter writer = new(connection);
    if (!writer.Send(FrameType.Start, start.Encode())) {
      return new ClientOutcome(null, null, "the daemon closed the connection");
    }

    Volatile.Write(ref _writer, writer);
    InputPump input = new(streams, writer);
    try {
      while (true) {
        Frame? next;
        try {
          next = await Frames.Read(connection, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) {
          next = null;
        }

        switch (next) {
          case null:
            await streams.ErrorText.WriteLineAsync(
                "lip: the daemon ended before the command did.");
            await streams.ErrorText.FlushAsync();
            return new ClientOutcome(1, null, null);
          case { Type: FrameType.Text } frame:
            TextWriter text = frame.Payload[0] == Messages.StandardOutput
                ? streams.OutputText
                : streams.ErrorText;
            Write(() => {
              text.Write(System.Text.Encoding.UTF8.GetString(frame.Payload.AsSpan(1)));
              text.Flush();
            });
            break;
          case { Type: FrameType.Output } frame:
            Stream stream = frame.Payload[0] == Messages.StandardOutput
                ? streams.Output
                : streams.Error;
            Write(() => {
              stream.Write(frame.Payload.AsSpan(1));
              stream.Flush();
            });
            break;
          case { Type: FrameType.InputWanted } frame:
            input.Want(Messages.ReadInt32(frame.Payload));
            break;
          case { Type: FrameType.Exit } frame:
            return new ClientOutcome(Messages.ReadInt32(frame.Payload), null, null);
          case { Type: FrameType.Refused }:
            return null;
          case { Type: FrameType.Terminate } frame:
            return new ClientOutcome(null, (PosixSignal)Messages.ReadInt32(frame.Payload), null);
        }
      }
    }
    finally {
      Volatile.Write(ref _writer, null);
      writer.Close();
    }
  }

  // A standard stream that cannot be written to, such as a closed pipe, loses what is written.
  private static void Write(Action write) {
    try {
      write();
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
    }
  }

  /// Reads the standard input of the client on a thread of its own, only as much as the daemon
  /// wants and only when it wants it.
  private sealed class InputPump {
    private readonly ClientStreams _streams;
    private readonly FrameWriter _writer;
    private readonly SemaphoreSlim _wanted = new(0);
    private readonly object _gate = new();
    private int _count;
    private Thread? _thread;

    public InputPump(ClientStreams streams, FrameWriter writer) {
      _streams = streams;
      _writer = writer;
    }

    public void Want(int count) {
      lock (_gate) {
        _count = Math.Clamp(count, 1, 64 * 1024);
        _thread ??= new Thread(Pump) { IsBackground = true, Name = "lip standard input" };
        if (!_thread.IsAlive && _thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted)) {
          _thread.Start();
        }
      }

      _wanted.Release();
    }

    private void Pump() {
      bool ended = false;
      while (true) {
        _wanted.Wait();
        int count;
        lock (_gate) {
          count = _count;
        }

        byte[] buffer = new byte[count];
        int read = 0;
        while (!ended) {
          try {
            read = _streams.Input.Read(buffer);
          }
          catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
            read = 0;
          }

          // A console read that a console control signal interrupts reads nothing, though the
          // console has not ended.
          if (read > 0 || !_streams.InputIsConsole) {
            break;
          }
        }

        ended |= read == 0;
        if (!_writer.Send(FrameType.Input, buffer.AsSpan(0, read))) {
          return;
        }
      }
    }
  }
}
