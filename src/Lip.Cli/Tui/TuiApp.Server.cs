using System.Globalization;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Tui;

/// The Server tab: lip run (protocol/lip-tui.md, "Server"). The output of the server is shown
/// as it is written; the lines the user enters go to its standard input.
public sealed partial class TuiApp {
  private readonly TextField _serverLine = new();
  private readonly List<string> _serverHistory = [];
  private bool _quitOnceStopped;
  private int _serverScroll;
  private int _serverHistoryAt;

  /// The server of the selected workspace.
  private WorkspaceServer Server => _servers.For(_workspace);

  private bool ServerRunning => _servers.Find(_workspace)?.Running == true;

  private (string, string)[] ServerHints() =>
      ServerRunning
          ? [
            ("Enter", "send to the server"), ("↑/↓", "earlier lines"), ("Esc", "erase"),
            ("Ctrl+C", "stop the server"), ("PgUp/PgDn", "scroll"), ("Tab", "next tab"),
          ]
          : [("Enter", "start the server"), ("↑/↓", "scroll"), ("Tab", "next tab"), ("q", "quit")];

  /// Starts the server of the selected workspace: its managed server, or, when lip itself
  /// carries out the interface, a server that this process runs (protocol/lip-tui.md, "Server").
  private void StartServer() {
    _serverScroll = 0;
    string workspace = _workspace;
    Task? started = Server.Start(
        interaction => _createClient(interaction, () => Stream.Null, workspace),
        _invocation,
        listenForStop: _managed ? null : ServerSession.ListenForSignals);
    started?.ContinueWith(
        task => Post(() => {
          if (!_quitOnceStopped) {
            ShowFailure(
                "Run the server", task.Exception!.InnerException!, cancelled: false, []);
          }
        }),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
  }

  /// Something about a server changed: the screen is drawn again, and the interface quits once
  /// the servers it asked to stop have exited.
  private void ServersChanged() {
    if (_quitOnceStopped && _servers.Running().Count == 0) {
      _quit = true;
    }
  }

  /// Asks the server of the selected workspace to stop: it reads the line "stop"
  /// (protocol/lip-run.md, "Stopping").
  private void StopServer() => Server.Stop();

  /// Quits the interface: at once when its servers are managed servers or none runs, otherwise
  /// once the servers of this process, which it asks to stop, have exited (protocol/lip-tui.md,
  /// "Server").
  private void QuitOnceServerStopped() {
    IReadOnlyList<WorkspaceServer> running = _managed ? [] : _servers.Running();
    if (running.Count == 0) {
      _quit = true;
      return;
    }

    _quitOnceStopped = true;
    _tab = ServerTab;
    foreach (WorkspaceServer server in running) {
      server.Stop();
    }
  }

  private void DrawServer(Rect body) {
    WorkspaceServer? server = _servers.Find(_workspace);
    bool running = server?.Running == true;
    string state;
    if (running && server!.Restarting) {
      state = string.Create(
          CultureInfo.InvariantCulture,
          $"exited with code {server.ExitCode} · restarting");
    } else if (running) {
      TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64 - server!.StartedAt);
      state = (server.Stopping ? "stopping" : "running")
          + string.Create(CultureInfo.InvariantCulture, $" · up {up:hh\\:mm\\:ss}");
    } else {
      state = server?.ExitCode is int code
          ? string.Create(CultureInfo.InvariantCulture, $"exited with code {code}")
          : "not running";
    }

    Rect area = Panel(
        _screen, body, $"Server — {state}", focused: true);
    int rows = area.Height - 2;
    if (rows < 1) {
      return;
    }

    // The rows of the last lines, as many as the scrolling needs.
    List<StyledLine> shown = [];
    int wanted = rows + _serverScroll;
    // A line takes one row at least: the lines that may be shown are copied, and wrapped once
    // the server may write again.
    List<StyledLine> lines = server?.LastLines(wanted + 1) ?? [];
    for (int i = lines.Count - 1; i >= 0 && shown.Count < wanted; i--) {
      StyledLine line = lines[i];
      if (i == lines.Count - 1 && line.Spans.Count == 0) {
        continue;
      }

      List<StyledLine> wrapped = TextWidth.Wrap(line, area.Width);
      wrapped.Reverse();
      shown.AddRange(wrapped);
    }

    _serverScroll = Math.Clamp(_serverScroll, 0, Math.Max(0, shown.Count - rows));
    for (int row = 0; row < rows; row++) {
      int index = _serverScroll + rows - 1 - row;
      if (index < shown.Count) {
        _screen.Write(area.X, area.Y + row, shown[index], area.Right);
      }
    }

    int y = area.Bottom - 1;
    _screen.Write(area.X, y - 1, new string('─', area.Width), Theme.Border);
    if (running) {
      _screen.Write(area.X, y, "> ", Theme.Accent);
      _serverLine.Draw(_screen, area.X + 2, y, area.Width - 2, focused: true);
    } else {
      _screen.WriteFit(
          area.X, y, area.Width, "Press Enter to start the server of this workspace.",
          Theme.Muted);
    }

    if (_serverScroll > 0) {
      string note = $" scrolled back {_serverScroll} rows ";
      _screen.Write(area.Right - note.Length, y - 1, note, Theme.Warning);
    }
  }

  private void ServerKey(ConsoleKeyInfo key) {
    int page = Math.Max(1, Body.Height - 5);
    switch (key.Key) {
      case ConsoleKey.PageUp:
        _serverScroll += page;
        return;
      case ConsoleKey.PageDown:
        _serverScroll = Math.Max(0, _serverScroll - page);
        return;
      case ConsoleKey.Escape when _serverScroll > 0:
        // Back to what the server wrote last.
        _serverScroll = 0;
        return;
    }

    if (!ServerRunning) {
      switch (key.Key) {
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          StartServer();
          break;
        case ConsoleKey.UpArrow:
          _serverScroll++;
          break;
        case ConsoleKey.DownArrow:
          _serverScroll = Math.Max(0, _serverScroll - 1);
          break;
        case ConsoleKey.Home:
          _serverScroll = int.MaxValue / 2;
          break;
        case ConsoleKey.End:
          _serverScroll = 0;
          break;
        default:
          if (key.KeyChar == 's') {
            StartServer();
          }
          break;
      }

      return;
    }

    if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) {
      StopServer();
      return;
    }

    switch (key.Key) {
      case ConsoleKey.Escape:
        _serverLine.Set("");
        _serverHistoryAt = _serverHistory.Count;
        return;
      case ConsoleKey.Enter:
        string line = _serverLine.Value;
        Server.Send(line);
        if (line.Length > 0) {
          _serverHistory.Remove(line);
          _serverHistory.Add(line);
        }

        _serverHistoryAt = _serverHistory.Count;
        _serverLine.Set("");
        _serverScroll = 0;
        return;
      case ConsoleKey.UpArrow or ConsoleKey.DownArrow:
        if (_serverHistory.Count == 0) {
          return;
        }

        _serverHistoryAt = Math.Clamp(
            _serverHistoryAt + (key.Key == ConsoleKey.UpArrow ? -1 : 1), 0, _serverHistory.Count);
        _serverLine.Set(
            _serverHistoryAt < _serverHistory.Count ? _serverHistory[_serverHistoryAt] : "");
        return;
    }

    _serverLine.OnKey(key);
  }
}
