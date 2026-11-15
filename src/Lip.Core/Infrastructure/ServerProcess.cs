using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lip.Core.Infrastructure;

/// A program that lip started and whose standard input it writes; its standard output and
/// standard error are those of lip.
public interface IStartedProgram : IDisposable {
  /// The standard input of the program.
  Stream StandardInput { get; }

  /// Completes with the exit code of the program when it exits.
  Task<int> WaitForExit();
}

/// Starts programs for lip run (protocol/lip-run.md).
public interface IProgramStarter {
  /// Starts the program, or throws when it cannot be started.
  IStartedProgram Start(ProgramStartInfo start);
}

/// Starts a process that inherits the standard output and the standard error of lip.
public sealed class ProcessProgramStarter : IProgramStarter {
  public IStartedProgram Start(ProgramStartInfo start) {
    ProcessStartInfo info = new(start.FileName) {
      UseShellExecute = false,
      WorkingDirectory = start.WorkingDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = false,
      RedirectStandardError = false,
    };
    if (start.VerbatimArguments) {
      info.Arguments = start.JoinedArguments;
    } else {
      foreach (string argument in start.Arguments) {
        info.ArgumentList.Add(argument);
      }
    }

    Invocation.Current.ApplyEnvironment(info.Environment);
    foreach ((string name, string value) in start.EnvironmentOverrides) {
      info.Environment[name] = value;
    }

    Process process = Process.Start(info)
        ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
    return new StartedProcess(process);
  }

  private sealed class StartedProcess(Process process) : IStartedProgram {
    public Stream StandardInput => process.StandardInput.BaseStream;

    public async Task<int> WaitForExit() {
      await process.WaitForExitAsync();
      return process.ExitCode;
    }

    public void Dispose() => process.Dispose();
  }
}

/// Starts a process whose standard output and standard error lip reads: every chunk read from its
/// standard output goes to output, and from its standard error to error, one at a time. For the
/// interface (protocol/lip-tui.md, "Server"), both go to one output; for the daemon
/// (protocol/lip-daemon.md, "Carrying out a command"), to those of the client. On Windows the
/// process gets a console of its own, without a window, so that nothing it does to its console
/// reaches the terminal of lip.
public sealed class PipedProgramStarter(Action<byte[]> output, Action<byte[]> error)
    : IProgramStarter {
  public PipedProgramStarter(Action<byte[]> output) : this(output, output) {
  }

  /// The starter of the programs of the current invocation: one that they inherit the standard
  /// streams of lip from, or that passes on what they write.
  public static IProgramStarter ForCurrent() {
    Invocation invocation = Invocation.Current;
    if (invocation.InheritsStandardStreams) {
      return new ProcessProgramStarter();
    }

    Stream stdout = invocation.OpenStandardOutput();
    Stream stderr = invocation.OpenStandardError();
    return new PipedProgramStarter(
        bytes => {
          stdout.Write(bytes);
          stdout.Flush();
        },
        bytes => {
          stderr.Write(bytes);
          stderr.Flush();
        });
  }

  public IStartedProgram Start(ProgramStartInfo start) {
    ProcessStartInfo info = new(start.FileName) {
      UseShellExecute = false,
      CreateNoWindow = true,
      WorkingDirectory = start.WorkingDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    if (start.VerbatimArguments) {
      info.Arguments = start.JoinedArguments;
    } else {
      foreach (string argument in start.Arguments) {
        info.ArgumentList.Add(argument);
      }
    }

    Invocation.Current.ApplyEnvironment(info.Environment);
    foreach ((string name, string value) in start.EnvironmentOverrides) {
      info.Environment[name] = value;
    }

    Process process = Process.Start(info)
        ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
    return new StartedProcess(process, output, error);
  }

  private sealed class StartedProcess : IStartedProgram {
    private readonly Process _process;
    private readonly object _gate = new();
    private readonly Task _pumps;

    public StartedProcess(Process process, Action<byte[]> output, Action<byte[]> error) {
      _process = process;
      _pumps = Task.WhenAll(
          Task.Run(() => Pump(process.StandardOutput.BaseStream, output)),
          Task.Run(() => Pump(process.StandardError.BaseStream, error)));
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public async Task<int> WaitForExit() {
      await _process.WaitForExitAsync();
      // What the server wrote last is shown before its exit is.
      await _pumps;
      return _process.ExitCode;
    }

    public void Dispose() => _process.Dispose();

    private async Task Pump(Stream stream, Action<byte[]> sink) {
      byte[] buffer = new byte[4096];
      try {
        while (true) {
          int read = await stream.ReadAsync(buffer);
          if (read == 0) {
            return;
          }

          lock (_gate) {
            sink(buffer[..read]);
          }
        }
      }
      catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
        RunTrace.Write("run", $"Cannot pass on the output of the server: {ex.Message}");
      }
    }
  }
}

/// Listens for lip being asked to stop (protocol/lip-run.md, "Stopping"): calls stop each time,
/// until the returned registration is disposed.
public delegate IDisposable StopListener(Action stop);

/// Runs the servers of one lip run (protocol/lip-run.md, "Standard streams" and "Stopping"):
/// one at a time, more than one when the server is restarted ("Restarting"). The bytes read from
/// input are forwarded to the standard input of the server that runs, in order, and those read
/// while none runs to the next one; input ending does not end the standard input of a server. The
/// first time lip is asked to stop, the server that runs, or the next one once it starts, reads
/// the line "stop". Stop requests are listened for from when the session is created until it is
/// disposed, so that none ends lip meanwhile.
public sealed class ServerSession : IDisposable {
  /// The line the server reads when lip is first asked to stop.
  private static readonly byte[] _stopLine = Encoding.ASCII.GetBytes("stop\n");

  private readonly IProgramStarter _starter;
  private readonly Stream _input;
  private readonly bool _inputIsConsole;
  private readonly object _sync = new();
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly TaskCompletionSource _stopRequested =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly CancellationTokenSource _ended = new();
  private readonly IDisposable _stopRegistration;
  private IStartedProgram? _server;
  private TaskCompletionSource _serverChanged =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
  private bool _stopWritten;
  private bool _forwarding;

  /// inputIsConsole: input is an interactive Windows console, whose input never ends.
  public ServerSession(
      IProgramStarter starter, Stream input, bool inputIsConsole, StopListener listen) {
    _starter = starter;
    _input = input;
    _inputIsConsole = inputIsConsole;
    _stopRegistration = listen(() => {
      _stopRequested.TrySetResult();
      StopOnce();
    });
  }

  /// Whether lip was asked to stop since the session was created.
  public bool StopRequested => _stopRequested.Task.IsCompleted;

  /// Completes when lip is first asked to stop.
  public Task Stopped => _stopRequested.Task;

  /// Listens for SIGINT, SIGTERM and SIGHUP, and on Windows for every console control signal
  /// (.NET maps CTRL_C_EVENT to SIGINT, CTRL_BREAK_EVENT to SIGQUIT, CTRL_CLOSE_EVENT to SIGHUP,
  /// CTRL_LOGOFF_EVENT and CTRL_SHUTDOWN_EVENT to SIGTERM), and cancels the termination of lip that
  /// they would cause. Console.CancelKeyPress shares the cancellation and never resets it.
  public static IDisposable ListenForSignals(Action stop) {
    List<PosixSignal> signals = [PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP];
    if (OperatingSystem.IsWindows()) {
      signals.Add(PosixSignal.SIGQUIT);
    }

    List<PosixSignalRegistration> registrations = [
      .. signals.Select(signal => PosixSignalRegistration.Create(signal, context => {
        context.Cancel = true;
        RunTrace.Write("run", $"Asked to stop by {context.Signal}");
        stop();
      })),
    ];
    return new Registrations(registrations);
  }

  /// Runs one server in a session of its own, and returns its exit code once it exits. Throws
  /// when it cannot be started.
  public static async Task<int> Run(
      IProgramStarter starter,
      ProgramStartInfo start,
      Stream input,
      bool inputIsConsole,
      StopListener listen) {
    using ServerSession session = new(starter, input, inputIsConsole, listen);
    return await session.RunServer(start);
  }

  /// Starts the server, calls started once it has, and returns its exit code once it exits.
  /// Throws when it cannot be started.
  public async Task<int> RunServer(ProgramStartInfo start, Action? started = null) {
    RunTrace.Write("run", $"Starting {start.FileName} {string.Join(' ', start.Arguments)}"
        + $" in {start.WorkingDirectory}"
        + string.Concat(start.EnvironmentOverrides.Select(v => $", {v.Key}={v.Value}")));
    using IStartedProgram program = _starter.Start(start);
    SetServer(program);
    StopOnce();
    started?.Invoke();
    bool forward;
    lock (_sync) {
      forward = !_forwarding;
      _forwarding = true;
    }

    if (forward) {
      // Reading the input of lip may block until the process exits; nothing waits for it.
      _ = Task.Run(Forward);
    }

    try {
      int exitCode = await program.WaitForExit();
      RunTrace.Write("run", $"The server exited with code {exitCode}");
      return exitCode;
    }
    finally {
      SetServer(null);
    }
  }

  public void Dispose() {
    _stopRegistration.Dispose();
    _ended.Cancel();
    SetServer(null);
  }

  private void SetServer(IStartedProgram? server) {
    TaskCompletionSource changed;
    lock (_sync) {
      _server = server;
      changed = _serverChanged;
      _serverChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    changed.TrySetResult();
  }

  /// Writes the line "stop" to the server that runs, when lip was asked to stop and it was not
  /// written yet.
  private void StopOnce() {
    IStartedProgram server;
    lock (_sync) {
      if (!StopRequested || _stopWritten || _server is null) {
        return;
      }

      _stopWritten = true;
      server = _server;
    }

    RunTrace.Write("run", "Writing the line 'stop' to the server");
    _ = Write(server, _stopLine, _stopLine.Length);
  }

  private async Task Forward() {
    byte[] buffer = new byte[4096];
    try {
      while (!_ended.IsCancellationRequested) {
        int read = await _input.ReadAsync(buffer);
        if (read == 0) {
          if (_inputIsConsole) {
            // A console read that a console control signal (Ctrl+C, Ctrl+Break) interrupts, or
            // that reads a Ctrl+Z line, reads nothing, though the console has not ended; the next
            // read waits for the user to type again.
            continue;
          }

          // The standard input of the server stays open.
          RunTrace.Write("run", "The standard input of lip ended");
          return;
        }

        await Deliver(buffer, read);
      }
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
      RunTrace.Write("run", $"Cannot read the standard input of lip: {ex.Message}");
    }
  }

  /// Writes the bytes to the server that runs, or to the next one when none runs or it exits
  /// before it reads them; drops them once the session ends.
  private async Task Deliver(byte[] bytes, int count) {
    while (true) {
      IStartedProgram? server;
      Task changed;
      lock (_sync) {
        if (_ended.IsCancellationRequested) {
          return;
        }

        server = _server;
        changed = _serverChanged.Task;
      }

      if (server is not null && await Write(server, bytes, count)) {
        return;
      }

      await changed;
    }
  }

  /// Writes the bytes to the server, after those written before; false when the server no longer
  /// reads them.
  private async Task<bool> Write(IStartedProgram server, byte[] bytes, int count) {
    await _gate.WaitAsync();
    try {
      await server.StandardInput.WriteAsync(bytes.AsMemory(0, count));
      await server.StandardInput.FlushAsync();
      return true;
    }
    catch (Exception ex)
        when (ex is IOException or ObjectDisposedException or InvalidOperationException) {
      // The server exited.
      RunTrace.Write("run", $"Cannot write to the server: {ex.Message}");
      return false;
    }
    finally {
      _gate.Release();
    }
  }

  private sealed class Registrations(List<PosixSignalRegistration> registrations) : IDisposable {
    public void Dispose() {
      foreach (PosixSignalRegistration registration in registrations) {
        registration.Dispose();
      }
    }
  }
}
