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

/// Starts a process whose standard output and standard error lip reads, for the interface
/// (protocol/lip-tui.md, "Server"): every chunk read from either goes to output, one at a time.
/// On Windows the process gets a console of its own, without a window, so that nothing it does
/// to its console reaches the terminal of lip.
public sealed class PipedProgramStarter(Action<byte[]> output) : IProgramStarter {
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

    foreach ((string name, string value) in start.EnvironmentOverrides) {
      info.Environment[name] = value;
    }

    Process process = Process.Start(info)
        ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
    return new StartedProcess(process, output);
  }

  private sealed class StartedProcess : IStartedProgram {
    private readonly Process _process;
    private readonly Action<byte[]> _output;
    private readonly object _gate = new();
    private readonly Task _pumps;

    public StartedProcess(Process process, Action<byte[]> output) {
      _process = process;
      _output = output;
      _pumps = Task.WhenAll(
          Task.Run(() => Pump(process.StandardOutput.BaseStream)),
          Task.Run(() => Pump(process.StandardError.BaseStream)));
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public async Task<int> WaitForExit() {
      await _process.WaitForExitAsync();
      // What the server wrote last is shown before its exit is.
      await _pumps;
      return _process.ExitCode;
    }

    public void Dispose() => _process.Dispose();

    private async Task Pump(Stream stream) {
      byte[] buffer = new byte[4096];
      try {
        while (true) {
          int read = await stream.ReadAsync(buffer);
          if (read == 0) {
            return;
          }

          lock (_gate) {
            _output(buffer[..read]);
          }
        }
      }
      catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
        RunTrace.Write("run", $"Cannot read the output of the server: {ex.Message}");
      }
    }
  }
}

/// Listens for lip being asked to stop (protocol/lip-run.md, "Stopping"): calls stop each time,
/// until the returned registration is disposed.
public delegate IDisposable StopListener(Action stop);

/// Runs the server for lip run once it is started (protocol/lip-run.md, "Standard streams" and
/// "Stopping").
public static class ServerSession {
  /// The line the server reads when lip is first asked to stop.
  private static readonly byte[] _stopLine = Encoding.ASCII.GetBytes("stop\n");

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

  /// Starts the server and forwards the bytes read from input to its standard input, in order,
  /// without ending it when input ends; writes the line "stop" to it the first time lip is asked
  /// to stop; and returns the exit code of the server once it exits. Throws when the server
  /// cannot be started. Stop requests are listened for from before the server starts, so that
  /// none ends lip while the server runs; one that comes before it starts is passed on once it
  /// has. inputIsConsole: input is an interactive Windows console, whose input never ends.
  public static async Task<int> Run(
      IProgramStarter starter,
      ProgramStartInfo start,
      Stream input,
      bool inputIsConsole,
      StopListener listen) {
    SemaphoreSlim gate = new(1, 1);
    IStartedProgram? server = null;
    int stopRequested = 0;
    void StopOnce() {
      if (Volatile.Read(ref server) is { } started
          && Interlocked.CompareExchange(ref stopRequested, 2, 1) == 1) {
        RunTrace.Write("run", "Writing the line 'stop' to the server");
        _ = Write(started, gate, _stopLine, _stopLine.Length);
      }
    }

    using IDisposable stopRegistration = listen(() => {
      Interlocked.CompareExchange(ref stopRequested, 1, 0);
      StopOnce();
    });

    RunTrace.Write("run", $"Starting {start.FileName} {string.Join(' ', start.Arguments)}"
        + $" in {start.WorkingDirectory}"
        + string.Concat(start.EnvironmentOverrides.Select(v => $", {v.Key}={v.Value}")));
    using IStartedProgram program = starter.Start(start);
    Volatile.Write(ref server, program);
    StopOnce();

    // Reading the input of lip may block until the process exits; nothing waits for it.
    _ = Task.Run(() => Forward(program, input, inputIsConsole, gate));
    int exitCode = await program.WaitForExit();
    RunTrace.Write("run", $"The server exited with code {exitCode}");
    return exitCode;
  }

  private static async Task Forward(
      IStartedProgram server, Stream input, bool inputIsConsole, SemaphoreSlim gate) {
    byte[] buffer = new byte[4096];
    try {
      while (true) {
        int read = await input.ReadAsync(buffer);
        if (read == 0) {
          if (inputIsConsole) {
            // A console read that a console control signal (Ctrl+C, Ctrl+Break) interrupts, or
            // that reads a Ctrl+Z line, reads nothing, though the console has not ended; the next
            // read waits for the user to type again.
            continue;
          }
          break;
        }

        if (!await Write(server, gate, buffer, read)) {
          return;
        }
      }

      // The standard input of the server stays open.
      RunTrace.Write("run", "The standard input of lip ended");
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
      RunTrace.Write("run", $"Cannot read the standard input of lip: {ex.Message}");
    }
  }

  /// Writes the bytes to the server, after those written before; false when the server no longer
  /// reads them.
  private static async Task<bool> Write(
      IStartedProgram server, SemaphoreSlim gate, byte[] bytes, int count) {
    await gate.WaitAsync();
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
      gate.Release();
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
