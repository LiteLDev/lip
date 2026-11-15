using System.Globalization;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui;

/// The servers that the interfaces run (protocol/lip-tui.md, "Server"): at most one per
/// workspace. They belong to the process, the daemon or lip itself, not to an interface, so that
/// the interfaces that the daemon carries out at once share them: each shows the server of the
/// workspace it selected.
public sealed class ServerSessions {
  private readonly object _gate = new();
  private readonly Dictionary<string, WorkspaceServer> _servers =
      new(WorkspaceList.PathComparer);

  /// The servers of this process.
  public static ServerSessions Shared { get; } = new();

  /// Raised, on any thread, when the server, or what it wrote, changes.
  public event Action<WorkspaceServer>? Changed;

  /// The server of the workspace, an absolute path.
  public WorkspaceServer For(string workspace) {
    lock (_gate) {
      if (!_servers.TryGetValue(workspace, out WorkspaceServer? server)) {
        server = new WorkspaceServer(workspace, changed => Changed?.Invoke(changed));
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

  /// The servers that run and were started by owner.
  public IReadOnlyList<WorkspaceServer> RunningFor(object owner) {
    lock (_gate) {
      return [.. _servers.Values.Where(s => s.Running && ReferenceEquals(s.Owner, owner))];
    }
  }
}

/// The server of a workspace, as lip run runs it for the interface (protocol/lip-tui.md,
/// "Server"): what it wrote, the lines entered for it, and whether it runs. Lock Gate to read
/// Log.
public sealed class WorkspaceServer {
  private readonly Action<WorkspaceServer> _changed;
  private QueueInputStream? _input;
  private Action? _stop;
  private volatile bool _running;
  private volatile bool _stopping;
  private volatile object? _owner;

  internal WorkspaceServer(string workspace, Action<WorkspaceServer> changed) {
    Workspace = workspace;
    _changed = changed;
  }

  public string Workspace { get; }

  /// Held while Log is read or written.
  public object Gate { get; } = new();

  /// What the server wrote, and what lip said about it.
  public AnsiLog Log { get; } = new();

  public bool Running => _running;

  /// Whether lip was asked to stop since the server started.
  public bool Stopping => _stopping;

  /// What started the server that runs: the interface that quits once it has stopped it.
  public object? Owner => _owner;

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

  /// Runs lip run with client, whose workspace is this one, unless the server runs already.
  /// listenForStop listens for what asks lip to stop besides Stop; failed is called, on any
  /// thread, when the server could not be run.
  public bool Start(
      object owner, LipClient client, StopListener listenForStop, Action<Exception> failed) {
    QueueInputStream input = new();
    lock (Gate) {
      if (_running) {
        return false;
      }

      _running = true;
      _stopping = false;
      _owner = owner;
      _input = input;
      StartedAt = Environment.TickCount64;
      ExitCode = null;
      Log.AddLine(new StyledLine("── starting the server ──", Theme.Muted));
    }

    _changed(this);
    PipedProgramStarter starter = new(bytes => {
      lock (Gate) {
        Log.Append(bytes);
      }

      _changed(this);
    });
    StopListener listen = stop => {
      IDisposable registration = listenForStop(stop);
      bool stopping;
      lock (Gate) {
        _stop = stop;
        stopping = _stopping;
      }

      // Asked to stop before the server could be.
      if (stopping) {
        stop();
      }

      return registration;
    };
    _ = Task.Run(async () => {
      int? exitCode = null;
      Exception? failure = null;
      try {
        exitCode = await client.Run(input, listen, starter);
      }
      catch (Exception ex) {
        failure = ex;
      }

      lock (Gate) {
        input.Complete();
        _input = null;
        _stop = null;
        ExitCode = exitCode;
        Log.AddLine(new StyledLine(
            exitCode is int code
                ? string.Create(
                    CultureInfo.InvariantCulture, $"── the server exited with code {code} ──")
                : "── the server did not start ──",
            Theme.Muted));
        _owner = null;
        _stopping = false;
        _running = false;
      }

      if (failure is not null) {
        failed(failure);
      }

      _changed(this);
    });
    return true;
  }

  /// Gives the server the line, ended by LF, on its standard input.
  public void Send(string line) {
    lock (Gate) {
      if (_input is not { } input) {
        return;
      }

      input.Send(line + "\n");
      Log.AddLine(new StyledLine().Add("> ", Theme.Accent).Add(line, Theme.Strong));
    }

    _changed(this);
  }

  /// Asks lip to stop (protocol/lip-run.md, "Stopping"): the server reads the line "stop".
  public void Stop() {
    Action? stop;
    lock (Gate) {
      if (!_running || _stopping) {
        return;
      }

      _stopping = true;
      stop = _stop;
      Log.AddLine(new StyledLine("── asking the server to stop ──", Theme.Muted));
    }

    stop?.Invoke();
    _changed(this);
  }
}
