using System.Runtime.InteropServices;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Daemon;

/// Carries out a command for a client: the arguments, with Invocation.Current the client's, and
/// the consoles of its standard output and standard error; or the interface, on terminal, the
/// terminal of the client. Returns the exit code.
public delegate Task<int> CarryOutCommand(
    string[] arguments, Start start, Invocation invocation, ClientTerminal terminal);

/// The daemon (protocol/lip-daemon.md): listens on the endpoint, and carries out the command of
/// each client that connects, side by side, until it has had no connection for idleTimeout, or is
/// asked to exit while it carries out no command.
///
/// Once it is closing, it listens no more and refuses to start commands, so that a client that
/// comes later starts a new daemon; it exits when the commands it carries out have ended.
///
/// While runsServers says that it runs a managed server (protocol/lip-run.md, "Managed servers"),
/// it is not idle, it is not asked to exit, and it does not exit.
public sealed class DaemonHost(
    DaemonEndpoint endpoint,
    string version,
    string executable,
    TimeSpan idleTimeout,
    CarryOutCommand run,
    Func<bool>? runsServers = null) {
  private readonly Func<bool> _runsServers = runsServers ?? (() => false);
  // lip was updated: the daemon closes once it runs no managed server.
  private bool _replaced;
  private readonly object _gate = new();
  // Cancelled when the daemon exits, once nothing more is to be said to a client.
  private readonly CancellationTokenSource _exit = new();
  private readonly List<Task> _commands = [];
  private int _connections;
  private DateTime _idleSince = DateTime.UtcNow;
  private bool _closing;
  private IDaemonListener? _listener;

  /// Listens until the daemon exits. Throws EndpointInUseException when another daemon has the
  /// endpoint.
  public async Task Run() {
    _listener = endpoint.Listen();
    using Timer idle = new(
        _ => CheckIdle(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    try {
      await Accept();
    }
    finally {
      lock (_gate) {
        Close();
      }
    }

    // What is said to the client that asked the daemon to exit is said before it does.
    try {
      await Task.Delay(Timeout.Infinite, _exit.Token);
    }
    catch (OperationCanceledException) {
    }

    // The commands that run, and the managed servers that they started, end before the daemon
    // does; no command starts any more.
    while (true) {
      Task[] commands;
      lock (_gate) {
        commands = [.. _commands];
      }

      if (commands.Length == 0) {
        if (!_runsServers()) {
          return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        continue;
      }

      await Task.WhenAll(commands);
    }
  }

  private async Task Accept() {
    while (true) {
      IDaemonListener? listener;
      lock (_gate) {
        listener = _listener;
      }

      if (listener is null) {
        return;
      }

      Stream connection;
      try {
        connection = await listener.Accept(CancellationToken.None);
      }
      catch (Exception ex) {
        lock (_gate) {
          if (_listener is null) {
            return;
          }
        }

        RunTrace.Write("daemon", $"Accepting a connection failed: {ex.Message}");
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        continue;
      }

      lock (_gate) {
        _connections++;
      }

      _ = Task.Run(() => Serve(connection));
    }
  }

  /// Stops listening and refuses commands from now on. Called with _gate held.
  private void Close() {
    _closing = true;
    _listener?.Dispose();
    _listener = null;
  }

  /// Closes when no command and no managed server runs; false when one runs.
  private bool TryClose() {
    lock (_gate) {
      if (_commands.Count > 0 || _runsServers()) {
        return false;
      }

      Close();
      return true;
    }
  }

  private void CheckIdle() {
    bool serving = _runsServers();
    lock (_gate) {
      if (_closing) {
        return;
      }

      if (serving) {
        // Idle from when the last managed server stopped running.
        _idleSince = DateTime.UtcNow;
        return;
      }

      if (_replaced) {
        // A command that runs may still start a managed server, which clients must reach.
        if (_commands.Count == 0) {
          RunTrace.Write("daemon", "lip was updated, and nothing runs: exiting");
          Close();
          _exit.Cancel();
        }

        return;
      }

      if (_connections == 0 && DateTime.UtcNow - _idleSince >= idleTimeout) {
        RunTrace.Write("daemon", "No connection for a while: exiting");
        Close();
        _exit.Cancel();
      }
    }
  }

  private async Task Serve(Stream connection) {
    FrameWriter writer = new(connection);
    try {
      if (await Frames.Read(connection, CancellationToken.None) is not { Type: FrameType.Hello }) {
        return;
      }

      int running;
      lock (_gate) {
        running = _commands.Count;
      }

      writer.Send(
          FrameType.Welcome,
          new Welcome(version, executable, Environment.ProcessId, running).Encode());
      switch (await Frames.Read(connection, CancellationToken.None)) {
        case { Type: FrameType.Shutdown }:
          bool closes = TryClose();
          try {
            writer.Send(FrameType.ShutdownReply, Messages.Boolean(closes));
          }
          finally {
            if (closes) {
              _exit.Cancel();
            }
          }

          break;
        case { Type: FrameType.Start } frame:
          await Carry(connection, writer, Start.Decode(frame.Payload));
          break;
      }
    }
    catch (Exception ex)
        when (ex is IOException or InvalidDataException or ObjectDisposedException) {
      RunTrace.Write("daemon", $"A connection failed: {ex.Message}");
    }
    finally {
      writer.Close();
      await connection.DisposeAsync();
      lock (_gate) {
        _connections--;
        _idleSince = DateTime.UtcNow;
      }
    }
  }

  /// Carries out the command of a client (protocol/lip-daemon.md, "Carrying out a command").
  private async Task Carry(Stream connection, FrameWriter writer, Start start) {
    TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_gate) {
      if (_closing) {
        // The client starts a new daemon.
        writer.Send(FrameType.Refused, []);
        return;
      }

      _commands.Add(done.Task);
    }

    Dictionary<string, string> environment = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    foreach ((string name, string value) in start.Environment) {
      environment[name] = value;
    }

    ConnectionInput input = new(writer);
    ClientTerminal terminal = new(writer, start.Output.Width, start.Output.Height);
    ConnectionOutputStream output = new(writer, Messages.StandardOutput);
    ConnectionOutputStream error = new(writer, Messages.StandardError);
    Invocation invocation = new(start.WorkingDirectory, environment) {
      ProcessId = start.ProcessId,
      IsInputRedirected = start.IsInputRedirected,
      IsOutputRedirected = start.IsOutputRedirected,
      IsErrorRedirected = start.IsErrorRedirected,
      OpenStandardInput = () => input,
      OpenStandardOutput = () => output,
      OpenStandardError = () => error,
      OutWriter = new ConnectionTextWriter(writer, Messages.StandardOutput),
      ErrorWriter = new ConnectionTextWriter(writer, Messages.StandardError),
    };

    try {
      Task reading = Task.Run(
          () => Read(connection, writer, input, terminal, invocation, done.Task));
      int code;
      try {
        code = await Task.Run(async () => {
          Invocation.Use(invocation);
          return await run([.. start.Arguments], start, invocation, terminal);
        });
      }
      catch (Exception ex) {
        invocation.Error.WriteLine($"The daemon of lip failed to carry out the command: {ex}");
        code = 1;
      }

      writer.Send(FrameType.Exit, Messages.Int32(code));
      done.TrySetResult();
      await reading;
    }
    finally {
      done.TrySetResult();
      // The interface reads no more keys.
      terminal.End();
      lock (_gate) {
        _commands.Remove(done.Task);
        if (invocation.LipReplaced && !_closing) {
          if (_runsServers() || _commands.Count > 0) {
            RunTrace.Write(
                "daemon", "lip was updated: exiting once no command and no managed server runs");
            _replaced = true;
          } else {
            RunTrace.Write("daemon", "lip was updated: exiting once no command runs");
            Close();
          }
        }

        if (_closing && _commands.Count == 0) {
          _exit.Cancel();
        }
      }
    }
  }

  /// Passes on the standard input, the keys, the size of the terminal and the signals of the
  /// client until it is gone or the command is done; a client that goes first abandons the
  /// command.
  private static async Task Read(
      Stream connection,
      FrameWriter writer,
      ConnectionInput input,
      ClientTerminal terminal,
      Invocation invocation,
      Task done) {
    // What the signals make lip do is traced for the client.
    Invocation.Use(invocation);
    try {
      while (true) {
        Task<Frame?> next = Frames.Read(connection, CancellationToken.None);
        if (await Task.WhenAny(next, done) == done) {
          return;
        }

        switch (await next) {
          case null:
            throw new EndOfStreamException();
          case { Type: FrameType.Input } frame:
            input.Receive(frame.Payload);
            break;
          case { Type: FrameType.Key } frame:
            terminal.Key(KeyMessage.Decode(frame.Payload));
            break;
          case { Type: FrameType.Resize } frame:
            (int width, int height) = ResizeMessage.Decode(frame.Payload);
            terminal.Resize(width, height);
            break;
          case { Type: FrameType.Signal } frame:
            PosixSignal signal = (PosixSignal)Messages.ReadInt32(frame.Payload);
            if (!invocation.Signal(signal)) {
              // The signal ends lip at once (protocol/lip-daemon.md, "Carrying out a command").
              writer.Send(FrameType.Terminate, Messages.Int32((int)signal));
              writer.Close();
              input.End();
              terminal.End();
              invocation.Abandon();
              return;
            }

            break;
        }
      }
    }
    catch (Exception ex)
        when (ex is IOException or InvalidDataException or ObjectDisposedException) {
      if (!done.IsCompleted) {
        RunTrace.Write("daemon", "The client is gone: the command is abandoned");
        writer.Close();
        input.End();
        terminal.End();
        invocation.Abandon();
      }
    }
  }
}
