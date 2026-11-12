using System.Collections.Concurrent;
using System.Text;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui;

/// The interface (protocol/lip-tui.md): the commands of lip, the catalogue and the server, in
/// one full-screen program. Everything but the reading of keys and the work of the commands
/// happens on the thread that calls Run, which other threads reach through Post.
public sealed partial class TuiApp {
  private const int PackagesTab = 0;
  private const int BrowseTab = 1;
  private const int ServerTab = 2;
  private const int SettingsTab = 3;
  private const int DevelopTab = 4;

  private static readonly string[] _tabs = ["Packages", "Browse", "Server", "Settings", "Develop"];

  private readonly ITerminal _terminal;
  private readonly LipClient _client;
  private readonly ModCatalogClient _catalogClient;
  private readonly string _version;
  private readonly string _workspace;
  private readonly Func<Exception, string[], bool, Failure> _describeFailure;
  private readonly BlockingCollection<Action> _events = new();
  private readonly List<Dialog> _dialogs = [];
  private ScreenBuffer _screen = new(1, 1);
  private string[] _shown = [];
  private volatile string _screenText = "";
  private int _tab;
  private bool _quit;
  private StyledLine? _status;

  /// createClient makes the client of the interface from its user interaction and from what
  /// opens the stream that a hook command writes its output to. describeFailure works out why
  /// a command failed and writes the failure report: ErrorReporter.Describe when not given.
  public TuiApp(
      ITerminal terminal,
      Func<IUserInteraction, Func<Stream>, LipClient> createClient,
      ModCatalogClient catalogClient,
      string version,
      string workspace,
      Func<Exception, string[], bool, Failure>? describeFailure = null) {
    _describeFailure = describeFailure ?? ErrorReporter.Describe;
    _terminal = terminal;
    _catalogClient = catalogClient;
    _version = version;
    _workspace = workspace;
    _client = createClient(
        new Interaction(this),
        () => new LineSinkStream((line, transient) =>
            Post(() => AddHookLine(new StyledLine("│ " + line, Theme.Muted), transient))));
  }

  /// What the screen shows, as plain text.
  public string ScreenText => _screenText;

  /// Runs the interface until the user quits it, and returns the exit code of lip.
  public int Run() {
    Thread reader = new(ReadKeys) { IsBackground = true, Name = "lip keys" };
    reader.Start();
    using Timer timer = new(_ => Post(Tick), null, 250, 250);
    LoadPackages();
    Render();
    while (!_quit) {
      Action? next = _events.Take();
      do {
        next();
      } while (!_quit && _events.TryTake(out next));

      if (!_quit) {
        Render();
      }
    }

    return 0;
  }

  /// Runs the action on the thread of the interface.
  public void Post(Action action) => _events.Add(action);

  private void ReadKeys() {
    try {
      while (true) {
        ConsoleKeyInfo key = _terminal.ReadKey();
        Post(() => OnKey(key));
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException
        or OperationCanceledException or ObjectDisposedException) {
      // The terminal is gone.
      Post(QuitOnceServerStopped);
    }
  }

  private void Tick() {
    BrowseTick();
  }

  private Rect Body => new(0, 1, _screen.Width, _screen.Height - 2);

  private void Render() {
    int width = _terminal.Width;
    int height = _terminal.Height;
    StringBuilder output = new();
    if (width != _screen.Width || height != _screen.Height || _shown.Length == 0) {
      _screen = new ScreenBuffer(width, height);
      _shown = new string[_screen.Height];
      output.Append("\x1b[0m\x1b[2J");
    }

    _screen.Clear();
    if (_screen.Width < 60 || _screen.Height < 16) {
      _screen.WriteFit(0, 0, _screen.Width, "The terminal is too small for lip.", Theme.Warning);
    } else {
      DrawChrome();
      DrawTab();
      foreach (Dialog dialog in _dialogs) {
        dialog.Draw(_screen);
      }
    }

    for (int y = 0; y < _screen.Height; y++) {
      string row = _screen.RowAnsi(y);
      if (row != _shown[y]) {
        output.Append(System.Globalization.CultureInfo.InvariantCulture, $"\x1b[{y + 1};1H")
            .Append(row);
        _shown[y] = row;
      }
    }

    _screenText = _screen.Text;
    if (output.Length > 0) {
      _terminal.Write(output.ToString());
    }
  }

  private void DrawChrome() {
    int x = _screen.Write(1, 0, $"lip {_version}", Theme.Strong);
    x = _screen.Write(x, 0, " │ ", Theme.Border);
    for (int i = 0; i < _tabs.Length; i++) {
      Style style = i == _tab ? Theme.Accent with { Underline = true } : Theme.Muted;
      x = _screen.Write(x, 0, $"{i + 1} {_tabs[i]}", style);
      if (i == ServerTab && _serverRunning) {
        x = _screen.Write(x, 0, " ●", Theme.Good);
      }

      x += 2;
    }

    int room = _screen.Width - x - 1;
    if (room > 8) {
      string path = TextWidth.Fit(_workspace, room);
      _screen.Write(_screen.Width - 1 - TextWidth.Of(path), 0, path, Theme.Muted);
    }

    int y = _screen.Height - 1;
    x = 1;
    foreach ((string key, string text) in _dialogs.Count > 0 ? _dialogs[^1].Hints : TabHints()) {
      x = _screen.Write(x, y, key, Theme.Strong);
      x = _screen.Write(x + 1, y, text, Theme.Muted) + 2;
    }

    if (_status is { } status && _dialogs.Count == 0) {
      string text = TextWidth.Fit(status.PlainText, Math.Max(0, _screen.Width - x - 2));
      _screen.Write(
          _screen.Width - 1 - TextWidth.Of(text), y, text,
          status.Spans.Count > 0 ? status.Spans[0].Style : default);
    }
  }

  private (string Key, string Text)[] TabHints() => _tab switch {
    PackagesTab => PackagesHints,
    BrowseTab => BrowseHints(),
    ServerTab => ServerHints(),
    SettingsTab => SettingsHints,
    _ => DevelopHints,
  };

  private void DrawTab() {
    switch (_tab) {
      case PackagesTab:
        DrawPackages(Body);
        break;
      case BrowseTab:
        DrawBrowse(Body);
        break;
      case ServerTab:
        DrawServer(Body);
        break;
      case SettingsTab:
        DrawSettings(Body);
        break;
      default:
        DrawDevelop(Body);
        break;
    }
  }

  private void OnKey(ConsoleKeyInfo key) {
    if (_dialogs.Count > 0) {
      _dialogs[^1].OnKey(key);
      return;
    }

    _status = null;
    bool control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
    if (key.Key == ConsoleKey.Tab) {
      int step = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? _tabs.Length - 1 : 1;
      SwitchTab((_tab + step) % _tabs.Length);
      return;
    }

    if (control && key.Key == ConsoleKey.Q) {
      RequestQuit();
      return;
    }

    // Keys that a text being typed does not take.
    bool typing = (_tab == BrowseTab && _searching) || (_tab == ServerTab && _serverRunning);
    if (!typing && !control) {
      if (key.KeyChar is >= '1' and <= '5') {
        SwitchTab(key.KeyChar - '1');
        return;
      }

      if (key.KeyChar == 'q') {
        RequestQuit();
        return;
      }
    }

    if (control && key.Key == ConsoleKey.C && !(_tab == ServerTab && _serverRunning)) {
      RequestQuit();
      return;
    }

    switch (_tab) {
      case PackagesTab:
        PackagesKey(key);
        break;
      case BrowseTab:
        BrowseKey(key);
        break;
      case ServerTab:
        ServerKey(key);
        break;
      case SettingsTab:
        SettingsKey(key);
        break;
      default:
        DevelopKey(key);
        break;
    }
  }

  private void SwitchTab(int tab) {
    _tab = tab;
    _searching = false;
    switch (tab) {
      case PackagesTab:
        LoadPackages();
        break;
      case BrowseTab:
        if (_catalog is null) {
          LoadCatalog();
        }
        break;
      case SettingsTab:
        LoadConfig();
        break;
    }
  }

  private void RequestQuit() {
    if (!_serverRunning) {
      _quit = true;
      return;
    }

    Push(new MessageDialog(
        this, "Quit lip",
        [new StyledLine("The server is running. Quitting stops it first.")],
        ["Stop and quit", "Cancel"],
        choice => {
          if (choice == 0) {
            QuitOnceServerStopped();
          }
        }));
  }

  private void Push(Dialog dialog) => _dialogs.Add(dialog);

  private void Close(Dialog dialog) => _dialogs.Remove(dialog);

  /// A message of a command: part of the log of the operation that runs, otherwise the status.
  private void AddLog(StyledLine line) {
    if (_operation is { } operation) {
      operation.Log.Add(line);
    } else {
      _status = line;
    }
  }

  /// A line that a hook command wrote. A transient one, such as a progress display, gives way
  /// to the line after it.
  private void AddHookLine(StyledLine line, bool transient) {
    if (_operation is not { } operation) {
      return;
    }

    if (operation.TransientLine is { } last && operation.Log.Count > 0
        && ReferenceEquals(operation.Log[^1], last)) {
      operation.Log[^1] = line;
    } else {
      operation.Log.Add(line);
    }

    operation.TransientLine = transient ? line : null;
  }

  /// The first row of a list to show so that the selected row is among the rows shown.
  private static int ScrollTo(int selected, int top, int rows) {
    if (selected < top) {
      return selected;
    }

    return selected >= top + rows ? selected - rows + 1 : Math.Max(0, top);
  }

  /// Moves a selection by a key; false when the key does not move it.
  private static bool MoveSelection(ConsoleKeyInfo key, ref int selected, int count, int page) {
    int moved = key.Key switch {
      ConsoleKey.UpArrow => selected - 1,
      ConsoleKey.DownArrow => selected + 1,
      ConsoleKey.PageUp => selected - page,
      ConsoleKey.PageDown => selected + page,
      ConsoleKey.Home => 0,
      ConsoleKey.End => count - 1,
      _ => int.MinValue,
    };
    if (moved == int.MinValue) {
      return false;
    }

    selected = Math.Clamp(moved, 0, Math.Max(0, count - 1));
    return true;
  }

  /// A box with a title, filled; returns the rectangle inside it.
  private static Rect Panel(ScreenBuffer screen, Rect rect, string title, bool focused = false) {
    screen.Fill(rect, default);
    screen.Box(
        rect, title, focused ? Theme.Accent with { Bold = false } : Theme.Border,
        focused ? Theme.Accent : Theme.Strong);
    return rect.Inner;
  }

  /// The messages and questions of the commands, shown in the interface.
  private sealed class Interaction(TuiApp app) : IUserInteraction {
    public Task PrintInfo(string message) => Print("info", message, "", default);

    public Task PrintSuccess(string message) => Print("success", message, "✓ ", Theme.Good);

    public Task PrintWarning(string message) => Print("warning", message, "⚠ ", Theme.Warning);

    public Task PrintError(string message) => Print("error", message, "✗ ", Theme.Bad);

    public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
      RunTrace.Write("progress", message);
      ProgressItem item = new(message);
      app.Post(() => app._operation?.Progress.Add(item));
      await action(new Reporter(app, item));
      app.Post(() => item.Done = true);
    }

    public Task<string> Ask(string question) {
      RunTrace.Write("ask", question);
      TaskCompletionSource<string> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
      app.Post(() => app.Push(new PromptDialog(
          app, "lip asks", [new PromptField($"Enter {question}")],
          values => answer.SetResult(values[0]),
          () => {
            app._operation?.Cancel.Cancel();
            answer.SetException(new OperationCanceledException("The question was not answered."));
          })));
      return answer.Task;
    }

    private Task Print(string category, string message, string mark, Style style) {
      RunTrace.Write(category, message);
      app.Post(() => {
        if (category == "warning" && app._operation is { } operation) {
          operation.Warned = true;
        }

        foreach (string line in message.Split('\n')) {
          app.AddLog(new StyledLine(mark + line.TrimEnd('\r'), style));
          mark = "  ";
        }
      });
      return Task.CompletedTask;
    }

    private sealed class Reporter(TuiApp app, ProgressItem item) : IProgress<double> {
      private int _reported = -1;

      public void Report(double value) {
        // Whole percents only: a download reports far more often than the screen changes.
        int percent = (int)Math.Clamp(value, 0, 100);
        if (Interlocked.Exchange(ref _reported, percent) != percent) {
          app.Post(() => item.Percent = percent);
        }
      }
    }
  }
}
