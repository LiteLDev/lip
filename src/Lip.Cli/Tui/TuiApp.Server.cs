using System.Globalization;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Tui;

/// The Server tab: lip run (protocol/lip-tui.md, "Server"). The output of the server is shown
/// as it is written; the lines the user enters go to its standard input.
public sealed partial class TuiApp {
  private readonly AnsiLog _serverLog = new();
  private readonly TextField _serverLine = new();
  private readonly List<string> _serverHistory = [];
  private QueueInputStream? _serverInput;
  private volatile Action? _serverStop;
  private bool _serverRunning;
  private bool _serverStopping;
  private bool _quitOnceStopped;
  private long _serverStartedAt;
  private int? _serverExitCode;
  private int _serverScroll;
  private int _serverHistoryAt;

  private (string, string)[] ServerHints() =>
      _serverRunning
          ? [
            ("Enter", "send to the server"), ("↑/↓", "earlier lines"), ("Esc", "erase"),
            ("Ctrl+C", "stop the server"), ("PgUp/PgDn", "scroll"), ("Tab", "next tab"),
          ]
          : [("Enter", "start the server"), ("↑/↓", "scroll"), ("Tab", "next tab"), ("q", "quit")];

  private void StartServer() {
    if (_serverRunning) {
      return;
    }

    QueueInputStream input = new();
    _serverInput = input;
    _serverRunning = true;
    _serverStopping = false;
    _serverStartedAt = Environment.TickCount64;
    _serverExitCode = null;
    _serverScroll = 0;
    _serverLog.AddLine(new StyledLine("── starting the server ──", Theme.Muted));
    PipedProgramStarter starter = new(bytes => Post(() => _serverLog.Append(bytes)));
    StopListener listen = stop => {
      IDisposable signals = ServerSession.ListenForSignals(stop);
      _serverStop = stop;
      // The user may have asked to stop before the server could be.
      Post(() => {
        if (_serverStopping) {
          stop();
        }
      });
      return signals;
    };
    _ = Task.Run(async () => {
      try {
        int exitCode = await _client.Run(input, listen, starter);
        Post(() => ServerExited(input, exitCode, null));
      }
      catch (Exception ex) {
        Post(() => ServerExited(input, null, ex));
      }
    });
  }

  private void ServerExited(QueueInputStream input, int? exitCode, Exception? failure) {
    input.Complete();
    _serverInput = null;
    _serverStop = null;
    _serverRunning = false;
    _serverStopping = false;
    _serverExitCode = exitCode;
    _serverLog.AddLine(new StyledLine(
        exitCode is int code
            ? string.Create(
                CultureInfo.InvariantCulture, $"── the server exited with code {code} ──")
            : "── the server did not start ──",
        Theme.Muted));
    if (_quitOnceStopped) {
      _quit = true;
    } else if (failure is not null) {
      ShowFailure("Run the server", failure, cancelled: false, []);
    }
  }

  /// Asks the server to stop: it reads the line "stop" (protocol/lip-run.md, "Stopping").
  private void StopServer() {
    if (!_serverRunning || _serverStopping) {
      return;
    }

    _serverStopping = true;
    _serverLog.AddLine(new StyledLine("── asking the server to stop ──", Theme.Muted));
    _serverStop?.Invoke();
  }

  /// Quits the interface: at once without a server, otherwise once the server it asks to stop
  /// has exited.
  private void QuitOnceServerStopped() {
    if (!_serverRunning) {
      _quit = true;
      return;
    }

    _quitOnceStopped = true;
    _tab = ServerTab;
    StopServer();
  }

  private void DrawServer(Rect body) {
    string state;
    if (_serverRunning) {
      TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64 - _serverStartedAt);
      state = (_serverStopping ? "stopping" : "running")
          + string.Create(CultureInfo.InvariantCulture, $" · up {up:hh\\:mm\\:ss}");
    } else {
      state = _serverExitCode is int code
          ? string.Create(CultureInfo.InvariantCulture, $"exited with code {code}")
          : "not running";
    }

    Rect area = Panel(
        _screen, body, $"{WorkspaceProgram.ServerFileName} — {state}", focused: true);
    int rows = area.Height - 2;
    if (rows < 1) {
      return;
    }

    // The rows of the last lines, as many as the scrolling needs.
    List<StyledLine> shown = [];
    int wanted = rows + _serverScroll;
    for (int i = _serverLog.Lines.Count - 1; i >= 0 && shown.Count < wanted; i--) {
      StyledLine line = _serverLog.Lines[i];
      if (i == _serverLog.Lines.Count - 1 && line.Spans.Count == 0) {
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
    if (_serverRunning) {
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

    if (!_serverRunning) {
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
        _serverInput?.Send(line + "\n");
        _serverLog.AddLine(new StyledLine().Add("> ", Theme.Accent).Add(line, Theme.Strong));
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
