using System.Collections;
using System.Runtime.InteropServices;

namespace Lip.Core.Infrastructure;

/// The invocation of lip that a command is carried out for: its working directory, environment
/// variables, standard streams, process ID and signals. It is the process itself, unless the
/// daemon carries the command out for a client (protocol/lip-daemon.md, "Carrying out a
/// command"), in which case everything here is the client's. Current flows with the asynchronous
/// work of the command, so that the commands of several clients run side by side in one process.
public sealed class Invocation {
  private static readonly AsyncLocal<Invocation?> s_current = new();

  private readonly object _gate = new();
  private readonly List<Action> _stopListeners = [];
  private readonly CancellationTokenSource _cancellation = new();
  private readonly string? _workingDirectory;
  private readonly IReadOnlyDictionary<string, string>? _environment;
  private int _openTransactions;
  private bool _abandoned;

  /// A client's invocation (protocol/lip-daemon.md): workingDirectory is absolute, and environment
  /// holds every environment variable of the client.
  public Invocation(string workingDirectory, IReadOnlyDictionary<string, string> environment) {
    _workingDirectory = workingDirectory;
    _environment = environment;
  }

  private Invocation() {
  }

  /// The invocation of this process.
  public static Invocation Process { get; } = new() {
    ProcessId = Environment.ProcessId,
    IsInputRedirected = Console.IsInputRedirected,
    IsOutputRedirected = Console.IsOutputRedirected,
    IsErrorRedirected = Console.IsErrorRedirected,
    InputIsConsole = OperatingSystem.IsWindows() && !Console.IsInputRedirected,
    OpenStandardInput = Console.OpenStandardInput,
    OpenStandardOutput = Console.OpenStandardOutput,
    OpenStandardError = Console.OpenStandardError,
    InheritsStandardStreams = true,
  };

  /// The invocation that the running command is carried out for.
  public static Invocation Current => s_current.Value ?? Process;

  /// Set when the command replaced the executable file of lip, or started an installer that does
  /// (protocol/lip-self-update.md): the daemon then exits (protocol/lip-daemon.md, "Exiting").
  public bool LipReplaced { get; set; }

  /// Makes invocation the current one for the rest of the calling asynchronous flow.
  public static void Use(Invocation invocation) => s_current.Value = invocation;

  /// The absolute working directory, the workspace of the command (protocol/lip.md, "Working
  /// directory").
  public string WorkingDirectory => _workingDirectory ?? Environment.CurrentDirectory;

  /// The environment variables, or null when they are those of this process.
  public IReadOnlyDictionary<string, string>? EnvironmentVariables => _environment;

  public int ProcessId { get; init; }

  public bool IsInputRedirected { get; init; }

  public bool IsOutputRedirected { get; init; }

  public bool IsErrorRedirected { get; init; }

  /// Standard input is an interactive Windows console, a read of which may return nothing though
  /// it has not ended.
  public bool InputIsConsole { get; init; }

  /// The programs that lip runs write to the standard output and the standard error of this
  /// process itself; otherwise lip passes on what they write.
  public bool InheritsStandardStreams { get; init; }

  public required Func<Stream> OpenStandardInput { get; init; }

  public required Func<Stream> OpenStandardOutput { get; init; }

  public required Func<Stream> OpenStandardError { get; init; }

  /// Standard output as text.
  public TextWriter Out => OutWriter ?? Console.Out;

  /// Standard error as text.
  public TextWriter Error => ErrorWriter ?? Console.Error;

  public TextWriter? OutWriter { private get; init; }

  public TextWriter? ErrorWriter { private get; init; }

  /// What lip did for this invocation (RunTrace).
  public TraceLog Trace { get; } = new();

  /// Cancelled when the user interrupts a workspace operation that changes the workspace, or when
  /// the command is abandoned (protocol/lip-daemon.md).
  public CancellationToken Cancellation => _cancellation.Token;

  public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

  /// Whether an operation of this invocation changes a workspace (WorkspaceTransaction).
  public bool AnyTransactionOpen => Volatile.Read(ref _openTransactions) > 0;

  internal void TransactionOpened() => Interlocked.Increment(ref _openTransactions);

  internal void TransactionClosed() => Interlocked.Decrement(ref _openTransactions);

  public string? GetEnvironmentVariable(string name) {
    if (_environment is null) {
      return Environment.GetEnvironmentVariable(name);
    }

    if (_environment.TryGetValue(name, out string? value)) {
      return value;
    }

    // Windows compares the names of environment variables case-insensitively.
    return OperatingSystem.IsWindows()
        ? _environment.FirstOrDefault(
            v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase)).Value
        : null;
  }


  /// Gives a program the environment variables of this invocation (protocol/lip-workspace.md,
  /// "Running programs"); environment holds those of this process.
  public void ApplyEnvironment(IDictionary<string, string?> environment) {
    if (_environment is null) {
      return;
    }

    environment.Clear();
    foreach ((string name, string value) in _environment) {
      environment[name] = value;
    }
  }

  /// The changes that give a program, which would get the environment variables of this process,
  /// those of this invocation instead: a null value removes a variable.
  public IReadOnlyDictionary<string, string?> EnvironmentChanges() {
    Dictionary<string, string?> changes = new(StringComparer.Ordinal);
    if (_environment is null) {
      return changes;
    }

    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
      changes[(string)entry.Key] = null;
    }

    foreach ((string name, string value) in _environment) {
      if (OperatingSystem.IsWindows()) {
        foreach (string same in changes.Keys
            .Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToList()) {
          changes.Remove(same);
        }
      }

      changes[name] = value;
    }

    return changes;
  }

  /// Listens for lip being asked to stop (protocol/lip-run.md, "Stopping"): calls stop each time,
  /// until the returned registration is disposed. A StopListener.
  /// When the command was abandoned before, stop is called at once.
  public IDisposable ListenForStop(Action stop) {
    bool abandoned;
    lock (_gate) {
      _stopListeners.Add(stop);
      abandoned = _abandoned;
    }

    if (abandoned) {
      stop();
    }

    return new StopRegistration(this, stop);
  }

  /// lip receives the signal: SIGINT, SIGQUIT, SIGTERM or SIGHUP, or a console control signal as
  /// .NET maps it to one of these. Returns true when lip goes on, false when the signal ends lip at
  /// once (protocol/lip.md, "Workspace operations"; protocol/lip-run.md, "Stopping").
  public bool Signal(PosixSignal signal) {
    Action[] listeners;
    lock (_gate) {
      listeners = [.. _stopListeners];
    }

    // .NET maps Ctrl+Break to SIGQUIT on Windows, where it is a console control signal.
    if (listeners.Length > 0 && (signal != PosixSignal.SIGQUIT || OperatingSystem.IsWindows())) {
      RunTrace.Write("run", $"Asked to stop by {signal}");
      foreach (Action stop in listeners) {
        stop();
      }

      return true;
    }

    if (signal is PosixSignal.SIGINT or PosixSignal.SIGQUIT && AnyTransactionOpen) {
      lock (_gate) {
        if (_cancellation.IsCancellationRequested) {
          return false;
        }

        Error.WriteLine("Cancelling... (press Ctrl+C again to exit at once)");
      }

      _cancellation.Cancel();
      return true;
    }

    return false;
  }

  /// The command is abandoned (protocol/lip-daemon.md, "Carrying out a command"): the server of
  /// lip run is asked to stop, and anything else is cancelled.
  public void Abandon() {
    Action[] listeners;
    lock (_gate) {
      _abandoned = true;
      listeners = [.. _stopListeners];
    }

    if (listeners.Length > 0) {
      RunTrace.Write("run", "Asked to stop: the client ended");
      foreach (Action stop in listeners) {
        stop();
      }

      return;
    }

    _cancellation.Cancel();
  }

  private sealed class StopRegistration(Invocation invocation, Action stop) : IDisposable {
    public void Dispose() {
      lock (invocation._gate) {
        invocation._stopListeners.Remove(stop);
      }
    }
  }
}
