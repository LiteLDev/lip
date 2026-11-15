using System.Globalization;
using Lip.Cli.Servers;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Lip.Core.Services;

namespace Lip.Cli.Tui.Pages.Server;

/// The Server page: the server of the selected workspace (protocol/lip-tui.md, "Server"). A
/// line says whether it runs; below it a button starts or stops it and a toggle sets whether
/// it is restarted after a failure; then what the server wrote, and a line that the server
/// reads when the user enters it.
public sealed class ServerPage(AppContext context) : Page(context) {
  /// The areas of the page that take the keys, from top to bottom.
  private enum Area {
    Controls,
    Log,
    Input,
  }

  private readonly TextField _input = new();
  private readonly List<string> _history = [];
  private Area _focus;

  /// The line of the history that the input line shows, or their count while it shows the
  /// line being typed, which is kept as the draft meanwhile.
  private int _historyAt;
  private string _draft = "";

  /// Which of the controls is chosen: 0 for the button, 1 for the toggle.
  private int _control;

  /// Scrolled back as far as it goes: Draw makes it the row count of the output.
  private const int ScrollLimit = int.MaxValue / 4;

  /// How many rows the output is scrolled back from its end; Draw keeps it within what the
  /// server wrote.
  private int _scroll;

  /// Whether the output showed its first row when it was last drawn, and how many rows it
  /// showed.
  private bool _atTop = true;
  private int _logRows = 1;

  /// The last line of the output that was a row when it was last drawn: the rows written
  /// after it are scrolled past, so that what the user scrolled back to stays where it is.
  private StyledLine? _anchor;

  /// The workspace that the page shows.
  private string? _workspace;

  /// Whether the workspace has a game, and so a server to run; null until it is read.
  private bool? _hasGame;

  /// Whether the server is restarted after a failure; null until it is read. What was read
  /// before the user switched it, or of another workspace, has an older version and is
  /// dropped; the writes go one after the other.
  private bool? _restart;
  private int _restartVersion;
  private Task _restartSaved = Task.CompletedTask;
  private bool _wasRunning;

  public override string Title => ServerText.Title;

  public override StyledLine? Badge =>
      Context.Server is not { Running: true } server ? null
          : new StyledLine("●", server.Restarting || server.Stopping ? Theme.Warning : Theme.Good);

  public override IReadOnlyList<Hint> Hints {
    get {
      if (Missing) {
        return _hasGame is null ? [] : [CommonText.HintSelect];
      }

      List<Hint> hints = [];
      switch (_focus) {
        case Area.Controls:
          hints.Add(CommonText.HintSwitch);
          hints.Add(_control == 0 ? CommonText.HintSelect : CommonText.HintToggle);
          break;
        case Area.Log:
          hints.Add(CommonText.HintScroll);
          hints.Add(CommonText.HintPage);
          break;
        default:
          hints.Add(ServerText.HintSend);
          if (_history.Count > 0) {
            hints.Add(ServerText.HintHistory);
          }

          break;
      }

      hints.Add(CommonText.HintNext);
      if (CanStop) {
        hints.Add(CommonText.HintStop);
      }

      return hints;
    }
  }

  /// Whether the page shows that there is no server, or that it does not know yet, instead of
  /// the server: the workspace has no game, and nothing runs in it.
  private bool Missing => _hasGame != true && !Context.ServerRunning;

  /// Whether lip can be asked to stop for the server now.
  private bool CanStop => Context.Server is { Running: true, Stopping: false };

  public override void OnShown() {
    Arrive();
    Load();
  }

  public override void OnWorkspaceChanged() {
    // An operation changed the selected workspace: it may have a server now.
    if (_workspace is not null && Context.Workspaces.Same(_workspace, Context.Workspace)) {
      if (Context.CurrentPage == PageId.Server) {
        Load();
      }

      return;
    }

    _workspace = Context.Workspace;
    _hasGame = null;
    _restart = null;
    _restartVersion++;
    _input.Set("");
    _draft = "";
    _historyAt = _history.Count;
    if (Context.CurrentPage == PageId.Server) {
      Arrive();
      Load();
    }
  }

  /// The page shows the server of the selected workspace from its end, with the focus on the
  /// input line when it runs.
  private void Arrive() {
    _workspace = Context.Workspace;
    _wasRunning = Context.ServerRunning;
    Focus(_wasRunning ? Area.Input : Area.Controls);
  }

  public override void OnRequest(string request) {
    if (request == PageRequest.StartServer) {
      Start();
    }
  }

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    Follow();
    if (area.Width < 1 || area.Height < 1) {
      return;
    }

    if (Missing) {
      DrawMissing(screen, area, focused, loading: _hasGame is null);
      return;
    }

    WorkspaceServer? server = Context.Server;
    bool running = server?.Running == true;
    DrawState(screen, area, server);
    DrawControls(screen, area, focused, server);

    // The input line and the rule above it are there only while a server reads the line.
    int logTop = area.Y + 3;
    int logRows = area.Bottom - logTop - (running ? 2 : 0);
    Rule(screen, area, area.Y + 2, ServerText.Output, focused && _focus == Area.Log);
    DrawLog(screen, new Rect(area.X, logTop, area.Width, logRows), server);
    if (running) {
      int y = area.Bottom - 1;
      Rule(screen, area, y - 1, "", focused && _focus == Area.Input);
      screen.Write(area.X, y, "> ", Theme.Accent, area.Right);
      _input.Draw(
          screen, area.X + 2, y, area.Width - 2, focused && _focus == Area.Input,
          ServerText.InputExample);
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    Follow();
    if (Missing) {
      if (_hasGame == false && key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar) {
        Context.Navigate(PageId.Workspaces, PageRequest.NewServer);
        return true;
      }

      return false;
    }

    if (Ui.IsInterrupt(key)) {
      // Ctrl+C stops the server that runs; otherwise the shell asks whether to quit.
      if (!CanStop) {
        return false;
      }

      Context.Server?.Stop();
      return true;
    }

    if (key.Key == ConsoleKey.Tab) {
      Area next = _focus + (Ui.IsBackTab(key) ? -1 : 1);
      if (next < Area.Controls || next > LastArea) {
        return false;
      }

      Focus(next);
      return true;
    }

    return _focus switch {
      Area.Controls => ControlsKey(key),
      Area.Log => LogKey(key),
      _ => InputKey(key),
    };
  }

  /// The last area that takes the keys: the input line while the server runs.
  private Area LastArea => Context.ServerRunning ? Area.Input : Area.Log;

  /// Gives an area the focus. The output follows its end unless it has the focus.
  private void Focus(Area area) {
    _focus = area;
    if (area != Area.Log) {
      _scroll = 0;
    }
  }

  /// Follows the server: once it does not run, the focus leaves the input line, which is
  /// gone. A server that someone else starts does not move the focus.
  private void Follow() {
    bool running = Context.ServerRunning;
    if (running == _wasRunning) {
      return;
    }

    _wasRunning = running;
    if (!running && _focus == Area.Input) {
      Focus(Area.Controls);
      _control = 0;
    }
  }

  /// Reads whether the workspace has a server to run, and whether it is restarted.
  private void Load() {
    LipClient client = Context.Client;
    Context.Background(
        async () => (await client.ListPackages()).Any(IsGame),
        hasGame => _hasGame = hasGame,
        // A workspace that cannot be read says why when its server is started.
        _ => _hasGame = true);
    int version = _restartVersion;
    Task saved = _restartSaved;
    Context.Background(
        async () => {
          await Settled(saved);
          return await client.ConfigGet(ConfigService.RestartKey, workspace: true);
        },
        value => {
          if (version == _restartVersion) {
            _restart = value == ConfigService.RestartOnFailure;
          }
        },
        _ => {
          if (version == _restartVersion) {
            _restart = null;
          }
        });
  }

  /// Completes once the task has ended, however it ended.
  private static async Task Settled(Task task) {
    try {
      await task;
    }
    catch (Exception) {
      // Whoever started the task shows why it failed.
    }
  }

  /// Whether an installed package is the game: a package of the server source, whatever its
  /// variant.
  private static bool IsGame(InstalledPackage package) {
    int variant = package.Identity.IndexOf('#', StringComparison.Ordinal);
    return BedrockServer.IsRepository(
        variant < 0 ? package.Identity : package.Identity[..variant]);
  }

  /// Starts the server of the selected workspace unless it runs.
  private void Start() {
    if (Context.ServerRunning) {
      return;
    }

    Context.StartServer(ServerText.StartTitle);
    _wasRunning = Context.ServerRunning;
    if (_wasRunning) {
      Focus(Area.Input);
    }
  }

  /// Sets restart of the workspace configuration to the other value.
  private void ToggleRestart() {
    if (_restart is not bool restart) {
      return;
    }

    LipClient client = Context.Client;
    _restart = !restart;
    int version = ++_restartVersion;
    Task before = _restartSaved;
    string value = restart ? ConfigService.RestartNever : ConfigService.RestartOnFailure;
    Task save = _restartSaved = Task.Run(async () => {
      await Settled(before);
      await client.ConfigSet(ConfigService.RestartKey, value, workspace: true);
    });
    Context.Background(
        async () => {
          await save;
          return true;
        },
        _ => {
        },
        exception => {
          if (version == _restartVersion) {
            _restart = restart;
          }

          Context.ShowFailure(ServerText.AutoRestart, exception);
        });
  }

  #region Keys

  private bool ControlsKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.LeftArrow when _control > 0:
        _control = 0;
        return true;
      case ConsoleKey.RightArrow:
        _control = 1;
        return true;
      case ConsoleKey.DownArrow:
        Focus(Area.Log);
        return true;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        if (_control == 1) {
          ToggleRestart();
        } else if (Context.ServerRunning) {
          Context.Server?.Stop();
        } else {
          Start();
        }

        return true;
      default:
        return false;
    }
  }

  private bool LogKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.UpArrow:
        if (_atTop) {
          Focus(Area.Controls);
        } else {
          _scroll++;
        }

        return true;
      case ConsoleKey.DownArrow:
        if (_scroll > 0) {
          _scroll--;
        } else if (Context.ServerRunning) {
          Focus(Area.Input);
        }

        return true;
      case ConsoleKey.PageUp:
        _scroll = Math.Min(ScrollLimit, _scroll + _logRows);
        return true;
      case ConsoleKey.PageDown:
        _scroll = Math.Max(0, _scroll - _logRows);
        return true;
      case ConsoleKey.Home:
        _scroll = ScrollLimit;
        return true;
      case ConsoleKey.End:
        _scroll = 0;
        return true;
      default:
        return false;
    }
  }

  private bool InputKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Enter:
        string line = _input.Value;
        Context.Server?.Send(line);
        if (line.Length > 0) {
          _history.Remove(line);
          _history.Add(line);
        }

        _historyAt = _history.Count;
        _draft = "";
        _input.Set("");
        return true;
      case ConsoleKey.UpArrow or ConsoleKey.DownArrow:
        int at = Math.Clamp(
            _historyAt + (key.Key == ConsoleKey.UpArrow ? -1 : 1), 0, _history.Count);
        if (at != _historyAt) {
          if (_historyAt == _history.Count) {
            _draft = _input.Value;
          }

          _historyAt = at;
          _input.Set(at < _history.Count ? _history[at] : _draft);
        }

        return true;
      default:
        return _input.OnKey(key);
    }
  }

  #endregion

  #region Drawing

  /// What is shown instead of a server: that the workspace is being read, or that it has no
  /// server, with the row that leads to creating one.
  private static void DrawMissing(ScreenBuffer screen, Rect area, bool focused, bool loading) {
    if (loading) {
      screen.WriteFit(area.X, area.Y, area.Width, CommonText.Loading, Theme.Muted);
      return;
    }

    screen.WriteFit(area.X, area.Y, area.Width, ServerText.NoServer, Theme.Muted);
    if (area.Height >= 3) {
      screen.Write(
          area.X, area.Y + 2, $"[ {ServerText.NewServer} ]",
          focused ? Theme.Selected : Theme.Strong, area.Right);
    }
  }

  /// The line that says whether the server runs, and since when or how it ended.
  private static void DrawState(ScreenBuffer screen, Rect area, WorkspaceServer? server) {
    (string text, Style style) = State(server);
    screen.WriteFit(area.X, area.Y, area.Width, text, style);
  }

  private static (string Text, Style Style) State(WorkspaceServer? server) {
    if (server is not { Running: true }) {
      return server?.ExitCode is int code
          ? ("○ " + ServerText.ExitedWith(code), code == 0 ? Theme.Muted : Theme.Warning)
          : ("○ " + CommonText.ServerStopped, Theme.Muted);
    }

    if (server.Restarting) {
      string restarting = "● " + CommonText.ServerRestarting;
      return (
          server.ExitCode is int exited
              ? $"{restarting} · {ServerText.ExitedWith(exited)}"
              : restarting,
          Theme.Warning);
    }

    if (server.Stopping) {
      return ("● " + CommonText.ServerStopping, Theme.Warning);
    }

    TimeSpan up = TimeSpan.FromMilliseconds(
        Math.Max(0, Environment.TickCount64 - server.StartedAt));
    string time = string.Create(
        CultureInfo.InvariantCulture,
        $"{(long)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}");
    return ($"● {CommonText.ServerRunning} · {time}", Theme.Good);
  }

  /// The button that starts or stops the server, the toggle, and what the toggle does while
  /// it is chosen.
  private void DrawControls(ScreenBuffer screen, Rect area, bool focused, WorkspaceServer? server) {
    int y = area.Y + 1;
    bool chosen = focused && _focus == Area.Controls;
    bool running = server?.Running == true;
    Style button = running && server!.Stopping ? Theme.Muted
        : _control != 0 ? default
        : chosen ? Theme.Selected
        : Theme.Strong;
    int x = screen.Write(
        area.X, y, $"[ {(running ? ServerText.Stop : ServerText.Start)} ]", button, area.Right);
    Style toggle = _restart is null ? Theme.Muted
        : _control != 1 ? default
        : chosen ? Theme.Selected
        : Theme.Strong;
    x = screen.Write(
        x + 2, y, $"[{(_restart == true ? "x" : " ")}] {ServerText.AutoRestart}", toggle,
        area.Right);
    if (chosen && _control == 1 && area.Right - x > 2) {
      screen.WriteFit(x + 2, y, area.Right - x - 2, ServerText.AutoRestartDetail, Theme.Muted);
    }
  }

  /// A line across the page, with a title when one is given; it stands out when what is below
  /// it has the focus.
  private static void Rule(ScreenBuffer screen, Rect area, int y, string title, bool focused) {
    Style line = focused ? Theme.Accent with { Bold = false } : Theme.Border;
    screen.Write(area.X, y, new string('─', area.Width), line, area.Right);
    if (title.Length > 0) {
      screen.Write(area.X + 1, y, $" {title} ", focused ? Theme.Accent : Theme.Strong, area.Right);
    }
  }

  /// The last rows of what the server wrote, or the rows that the user scrolled back to.
  private void DrawLog(ScreenBuffer screen, Rect area, WorkspaceServer? server) {
    _logRows = Math.Max(1, area.Height);
    _atTop = true;
    if (area.Height < 1) {
      return;
    }

    // A line takes one row at least, so the rows to show are among the last lines, as many
    // as there are rows wanted; one line more tells whether rows are left above them, and
    // another makes up for the line being written, which is no row while it is empty. The
    // lines are copied, and wrapped once the server may write again. The column at the
    // right edge is that of the scroll marks.
    int width = Math.Max(1, area.Width - 1);
    List<StyledLine> lines = server is null ? [] : Tail(server, area.Height, width);
    int wanted = area.Height + _scroll;
    List<StyledLine> rows = [];
    for (int i = lines.Count - 1; i >= 0 && rows.Count <= wanted; i--) {
      if (i == lines.Count - 1 && lines[i].Spans.Count == 0) {
        continue;
      }

      List<StyledLine> wrapped = TextWidth.Wrap(lines[i], width);
      wrapped.Reverse();
      rows.AddRange(wrapped);
    }

    if (rows.Count == 0) {
      _scroll = 0;
      screen.WriteFit(area.X, area.Y, area.Width, ServerText.NoOutput, Theme.Muted);
      return;
    }

    // With no more rows than wanted, these are all the rows there are.
    _scroll = Math.Min(_scroll, Math.Max(0, rows.Count - area.Height));
    _atTop = rows.Count <= _scroll + area.Height;
    // Rows that do not fill the area start at its top.
    int shown = Math.Min(area.Height, rows.Count);
    for (int row = 0; row < shown; row++) {
      screen.Write(area.X, area.Y + row, rows[_scroll + shown - 1 - row], area.X + width);
    }

    Ui.ScrollMarks(screen, area, !_atTop, _scroll > 0);
  }

  /// The last lines of what the server wrote, as many as height rows scrolled back may show
  /// and two more. While the output is scrolled back, it is first scrolled back further by
  /// the rows written since it was last drawn. The line being written is a copy, since the
  /// server goes on writing it.
  private List<StyledLine> Tail(WorkspaceServer server, int height, int width) {
    lock (server.Gate) {
      List<StyledLine> all = server.Log.Lines;
      int last = all.Count - 1;
      if (last >= 0 && all[last].Spans.Count == 0) {
        last--;
      }

      if (_scroll > 0 && _anchor is not null) {
        int at = all.LastIndexOf(_anchor);
        for (int i = at + 1; at >= 0 && i <= last && _scroll < ScrollLimit; i++) {
          _scroll += TextWidth.Wrap(all[i], width).Count;
        }
      }

      _anchor = last >= 0 ? all[last] : null;
      int count = Math.Min(all.Count, height + _scroll + 2);
      List<StyledLine> lines = all.GetRange(all.Count - count, count);
      if (count > 0) {
        StyledLine written = new();
        foreach (Span span in lines[^1].Spans) {
          written.Add(span.Text, span.Style);
        }

        lines[^1] = written;
      }

      return lines;
    }
  }

  #endregion
}
