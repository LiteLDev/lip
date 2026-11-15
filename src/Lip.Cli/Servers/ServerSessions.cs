using System.Collections;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Lip.Cli.Tui;
using Lip.Cli.Workspaces;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;

namespace Lip.Cli.Servers;

/// The managed servers of the daemon (protocol/lip-run.md, "Managed servers"), at most one per
/// workspace, which lip run --managed and the interfaces start and attach to; or, when lip itself
/// carries out the interface (protocol/lip-daemon.md, "Fallback"), the servers that the interface
/// runs (protocol/lip-tui.md, "Server"). They belong to the process, not to a client or an
/// interface.
public sealed class ServerSessions(RestartPolicy? restart = null) {
  private readonly object _gate = new();
  private readonly Dictionary<string, WorkspaceServer> _servers =
      new(WorkspaceList.PathComparer);

  /// The servers of this process.
  public static ServerSessions Shared { get; } = new();

  /// Raised, on any thread, when a server, or what it wrote, changes.
  public event Action<WorkspaceServer>? Changed;

  /// Whether a server runs.
  public bool AnyRunning {
    get {
      lock (_gate) {
        return _servers.Values.Any(s => s.Running);
      }
    }
  }

  /// The server of the workspace, the canonical form of an absolute path
  /// (protocol/lip-tui.md, "Workspaces").
  public WorkspaceServer For(string workspace) {
    lock (_gate) {
      if (!_servers.TryGetValue(workspace, out WorkspaceServer? server)) {
        server = new WorkspaceServer(
            workspace, restart ?? RestartPolicy.Default, changed => Changed?.Invoke(changed));
        _servers[workspace] = server;
      }

      return server;
    }
  }

  /// The server of the workspace, or null when none ever ran in it.
  public WorkspaceServer? Find(string workspace) {
    lock (_gate) {
      return _servers.GetValueOrDefault(workspace);
    }
  }

  /// The servers that run.
  public IReadOnlyList<WorkspaceServer> Running() {
    lock (_gate) {
      return [.. _servers.Values.Where(s => s.Running)];
    }
  }
}

/// The server of a workspace that lip run runs for no client (protocol/lip-run.md, "Managed
/// servers"): what it wrote, its standard input, and whether it runs. Lock Gate to read Log.
public sealed class WorkspaceServer {
  /// How many bytes of what the server wrote a client that attaches gets first, at most.
  private const int BacklogLimit = 64 * 1024;

  private readonly RestartPolicy _restart;
  private readonly Action<WorkspaceServer> _changed;
  private readonly List<Action> _stopListeners = [];
  private readonly List<ServerAttachment> _attachments = [];
  private readonly Queue<(byte[] Bytes, bool Error)> _backlog = new();
  private int _backlogSize;
  private QueueInputStream? _input;
  private volatile bool _running;
  private volatile bool _starting;
  private volatile bool _stopping;
  private volatile bool _restarting;

  internal WorkspaceServer(
      string workspace, RestartPolicy restart, Action<WorkspaceServer> changed) {
    Workspace = workspace;
    _restart = restart;
    _changed = changed;
  }

  public string Workspace { get; }

  /// Held while Log is read or written.
  public object Gate { get; } = new();

  /// What the server wrote, and what lip said about it.
  public AnsiLog Log { get; } = new();

  /// Whether lip run runs for the workspace: from when it is started until it ends.
  public bool Running => _running;

  /// Whether lip run runs and has not started its server yet: it takes the locks, recovers,
  /// and prepares the system first (protocol/lip-run.md, steps 1 to 6).
  public bool Starting => _starting;

  /// Whether lip was asked to stop since lip run started.
  public bool Stopping => _stopping;

  /// Whether lip waits to restart the server (protocol/lip-run.md, "Restarting").
  public bool Restarting => _restarting;

  /// When the server that runs started, as Environment.TickCount64.
  public long StartedAt { get; private set; }

  /// The exit code of the server that ran last, or null.
  public int? ExitCode { get; private set; }

  /// The last lines of Log, at most count of them.
  public List<StyledLine> LastLines(int count) {
    lock (Gate) {
      return [.. Log.Lines.TakeLast(count)];
    }
  }

  /// Runs lip run for the workspace unless it runs already, with the client that createClient
  /// makes from the user interaction of the server, with the environment variables of starter,
  /// and attaches attachment to it when it is given. Returns null when lip run runs already;
  /// otherwise a task that completes once the server started, or fails with the failure of lip
  /// run before it did. listenForStop listens for what else asks lip to stop: for the servers of
  /// an interface that lip itself carries out, the signals of this process.
  public Task? Start(
      Func<IUserInteraction, LipClient> createClient,
      Invocation starter,
      ServerAttachment? attachment = null,
      StopListener? listenForStop = null) {
    QueueInputStream input = new();
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (Gate) {
      if (_running) {
        return null;
      }

      _running = true;
      _starting = true;
      _stopping = false;
      _restarting = false;
      _input = input;
      StartedAt = Environment.TickCount64;
      ExitCode = null;
      _backlog.Clear();
      _backlogSize = 0;
      if (attachment is not null) {
        _attachments.Add(attachment);
      }

      Log.AddLine(new StyledLine("── starting the server ──", LogStyle.Muted));
    }

    _changed(this);
    Invocation invocation = InvocationFor(starter);
    PipedProgramStarter programs = new(bytes => Output(bytes, error: false),
        bytes => Output(bytes, error: true));
    ServerRunOptions options = new() {
      Restart = _restart,
      Started = () => {
        lock (Gate) {
          _starting = false;
          _restarting = false;
          StartedAt = Environment.TickCount64;
        }

        started.TrySetResult();
        _changed(this);
      },
      Restarting = code => {
        lock (Gate) {
          _restarting = true;
          ExitCode = code;
        }

        _changed(this);
      },
    };
    StopListener listen = stop => {
      IDisposable? other = listenForStop?.Invoke(stop);
      bool stopping;
      lock (Gate) {
        _stopListeners.Add(stop);
        stopping = _stopping;
      }

      // Asked to stop before lip run listened.
      if (stopping) {
        stop();
      }

      return new Unlisten(this, stop, other);
    };
    _ = Task.Run(async () => {
      Invocation.Use(invocation);
      int? exitCode = null;
      Exception? failure = null;
      try {
        LipClient client = createClient(new ServerInteraction(this));
        exitCode = await client.Run(input, listen, programs, options);
      }
      catch (Exception ex) {
        failure = ex;
      }

      List<ServerAttachment> attached;
      lock (Gate) {
        input.Complete();
        _input = null;
        ExitCode = exitCode ?? ExitCode;
        if (failure is not null) {
          Log.AddLine(new StyledLine($"── lip run failed: {failure.Message} ──", LogStyle.Bad));
        }

        Log.AddLine(new StyledLine(
            exitCode is int code
                ? string.Create(
                    CultureInfo.InvariantCulture, $"── the server exited with code {code} ──")
                : "── the server is not running ──",
            LogStyle.Muted));
        _stopping = false;
        _restarting = false;
        _starting = false;
        _running = false;
        attached = [.. _attachments];
        _attachments.Clear();
      }

      foreach (ServerAttachment ended in attached) {
        ended.End(exitCode, failure);
      }

      if (failure is not null) {
        started.TrySetException(failure);
      } else {
        started.TrySetResult();
      }

      _changed(this);
    });
    return started.Task;
  }

  /// Attaches attachment, which first gets what the server wrote lately when replay is set;
  /// false when lip run does not run.
  public bool Attach(ServerAttachment attachment, bool replay) {
    lock (Gate) {
      if (!_running) {
        return false;
      }

      if (replay) {
        foreach ((byte[] bytes, bool error) in _backlog) {
          attachment.Write(bytes, error);
        }
      }

      _attachments.Add(attachment);
      return true;
    }
  }

  /// Detaches attachment, which gets nothing more.
  public void Detach(ServerAttachment attachment) {
    lock (Gate) {
      _attachments.Remove(attachment);
    }

    attachment.End(null, null);
  }

  /// Gives the server the line, ended by LF, on its standard input.
  public void Send(string line) {
    lock (Gate) {
      if (_input is not { } input) {
        return;
      }

      input.Send(line + "\n");
      Log.AddLine(new StyledLine().Add("> ", LogStyle.Accent).Add(line, LogStyle.Strong));
    }

    _changed(this);
  }

  /// Gives the server the bytes on its standard input.
  public void Send(byte[] bytes) {
    lock (Gate) {
      _input?.Send(bytes);
    }
  }

  /// Asks lip to stop (protocol/lip-run.md, "Stopping"): the server reads the line "stop".
  public void Stop() {
    Action[] listeners;
    lock (Gate) {
      if (!_running || _stopping) {
        return;
      }

      _stopping = true;
      listeners = [.. _stopListeners];
      Log.AddLine(new StyledLine("── asking the server to stop ──", LogStyle.Muted));
    }

    foreach (Action stop in listeners) {
      stop();
    }

    _changed(this);
  }

  /// A message of lip about the server, which the interfaces show and the clients attached get on
  /// their standard error.
  internal void Message(string message, Style style) {
    byte[] bytes = Encoding.UTF8.GetBytes(message + Environment.NewLine);
    lock (Gate) {
      Log.AddLine(new StyledLine(message, style));
      Keep(bytes, error: true);
    }

    _changed(this);
  }

  private void Output(byte[] bytes, bool error) {
    lock (Gate) {
      Log.Append(bytes);
      Keep(bytes, error);
    }

    _changed(this);
  }

  /// Passes on what the server wrote to the clients attached, and keeps the last of it for those
  /// that attach later. Called with Gate held.
  private void Keep(byte[] bytes, bool error) {
    foreach (ServerAttachment attachment in _attachments) {
      attachment.Write(bytes, error);
    }

    _backlog.Enqueue((bytes, error));
    _backlogSize += bytes.Length;
    while (_backlogSize > BacklogLimit && _backlog.Count > 1) {
      _backlogSize -= _backlog.Dequeue().Bytes.Length;
    }
  }

  /// The invocation that the server runs for: the environment variables of starter, and no
  /// standard streams of a client.
  private Invocation InvocationFor(Invocation starter) {
    Dictionary<string, string> environment = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    if (starter.EnvironmentVariables is { } variables) {
      foreach ((string name, string value) in variables) {
        environment[name] = value;
      }
    } else {
      foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
        environment[(string)entry.Key] = (string?)entry.Value ?? "";
      }
    }

    return new Invocation(Workspace, environment) {
      ProcessId = starter.ProcessId,
      IsInputRedirected = true,
      IsOutputRedirected = true,
      IsErrorRedirected = true,
      OpenStandardInput = () => Stream.Null,
      OpenStandardOutput = () => Stream.Null,
      OpenStandardError = () => Stream.Null,
      OutWriter = TextWriter.Null,
      ErrorWriter = TextWriter.Null,
    };
  }

  private sealed class Unlisten(WorkspaceServer server, Action stop, IDisposable? other)
      : IDisposable {
    public void Dispose() {
      lock (server.Gate) {
        server._stopListeners.Remove(stop);
      }

      other?.Dispose();
    }
  }

  /// The messages of lip run about the server; it cannot ask the user anything.
  private sealed class ServerInteraction(WorkspaceServer server) : IUserInteraction {
    public Task PrintInfo(string message) => Print(message, LogStyle.Muted);

    public Task PrintSuccess(string message) => Print(message, LogStyle.Good);

    public Task PrintWarning(string message) => Print(message, LogStyle.Warning);

    public Task PrintError(string message) => Print(message, LogStyle.Bad);

    public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
      server.Message(message, LogStyle.Muted);
      await action(new Progress<double>());
    }

    public Task<string> Ask(string question) =>
        throw new CannotAskException(
            $"lip needs {question}, but cannot ask for it while it runs the server.");

    private Task Print(string message, Style style) {
      RunTrace.Write("run", message);
      server.Message(message, style);
      return Task.CompletedTask;
    }
  }
}

/// The styles of the lines that lip adds to what a server wrote.
internal static class LogStyle {
  public static readonly Style Muted = Style.Of(Color.BrightBlack);
  public static readonly Style Accent = Style.Of(Color.Cyan, bold: true);
  public static readonly Style Strong = Style.Of(bold: true);
  public static readonly Style Good = Style.Of(Color.Green);
  public static readonly Style Warning = Style.Of(Color.Yellow);
  public static readonly Style Bad = Style.Of(Color.Red, bold: true);
}

/// A client attached to a managed server (protocol/lip-run.md, "Attaching"): what the server
/// writes is passed on to write, in order, on a task of its own, so that a slow client does not
/// hold the server up.
public sealed class ServerAttachment {
  private readonly Channel<(byte[] Bytes, bool Error)> _queue =
      Channel.CreateUnbounded<(byte[] Bytes, bool Error)>(
          new UnboundedChannelOptions { SingleReader = true });
  private readonly TaskCompletionSource<int?> _ended =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
  private Exception? _failure;
  private int? _exitCode;
  private int _endedOnce;

  /// write gets each chunk, and whether it was written to standard error.
  public ServerAttachment(Action<byte[], bool> write) {
    _ = Task.Run(async () => {
      await foreach ((byte[] bytes, bool error) in _queue.Reader.ReadAllAsync()) {
        try {
          write(bytes, error);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
          RunTrace.Write("run", $"Cannot pass on the output of the server: {ex.Message}");
        }
      }

      if (_failure is not null) {
        _ended.TrySetException(_failure);
      } else {
        _ended.TrySetResult(_exitCode);
      }
    });
  }

  /// Completes, once everything is passed on, when lip run of the server ends: with the exit code
  /// of the server that ran last, or the failure of lip run; or with null when detached.
  public Task<int?> Ended => _ended.Task;

  internal void Write(byte[] bytes, bool error) => _queue.Writer.TryWrite((bytes, error));

  internal void End(int? exitCode, Exception? failure) {
    if (Interlocked.Exchange(ref _endedOnce, 1) == 1) {
      return;
    }

    _exitCode = exitCode;
    _failure = failure;
    _queue.Writer.TryComplete();
  }
}
