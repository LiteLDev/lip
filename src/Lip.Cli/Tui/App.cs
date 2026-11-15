using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Lip.Cli.Servers;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Lip.Core.Services;

namespace Lip.Cli.Tui;

/// What the interface is made of: its terminal, and the parts of lip that it works with. Tests
/// replace any of them.
public sealed class AppOptions {
  public required ITerminal Terminal { get; init; }

  /// Makes the client of the interface for a workspace, an absolute path, from its user
  /// interaction and from what opens the stream that a hook command writes its output to.
  public required Func<IUserInteraction, Func<Stream>, string, LipClient> CreateClient {
    get; init;
  }

  /// Reads the catalogue (protocol/lip-tui.md, "Catalogue").
  public required ModCatalogClient Catalog { get; init; }

  /// The version of lip.
  public required string Version { get; init; }

  /// The working directory of lip, which is added to the workspace list and selected.
  public required string WorkingDirectory { get; init; }

  /// Works out why a command failed and writes the failure report below the workspace given
  /// last: ErrorReporter.Describe when not given.
  public Func<Exception, string[], bool, string, Failure>? DescribeFailure { get; init; }

  /// The workspace list: kept in memory when not given.
  public WorkspaceList? Workspaces { get; init; }

  /// The servers of the process: ServerSessions.Shared when not given.
  public ServerSessions? Servers { get; init; }

  /// Checks that GitHub is reached (protocol/lip-tui.md, "GitHub reachability"); the check is
  /// not made when not given.
  public GithubProbe? GithubProbe { get; init; }

  /// Sets up a server: through LipClient when not given.
  public IServerSetup? ServerSetup { get; init; }

  /// Whether the servers that the interface starts are managed servers, which quitting leaves
  /// running (protocol/lip-tui.md, "Server"): when not given, whether the daemon carries out
  /// the interface.
  public bool? ManagedServers { get; init; }

  /// The client language (protocol/lip-tui.md, "Language"), or null when there is none.
  public Language? ClientLanguage { get; init; }

  /// lip self-update with the client, only checking when the flag is set: LipClient.SelfUpdate
  /// for Version when not given.
  public Func<LipClient, bool, Task<SelfUpdateResult>>? SelfUpdate { get; init; }

  /// What lip daemon status finds, null when no daemon listens: no daemon when not given.
  public Func<CancellationToken, Task<DaemonState?>>? DaemonStatus { get; init; }

  /// Opens a URL in the browser of the user: with the shell of the system when not given.
  public Action<string>? OpenUrl { get; init; }

  /// The pages of the sidebar: PageRegistry.All when not given.
  public IReadOnlyList<PageEntry>? Pages { get; init; }
}

/// The interface (protocol/lip-tui.md): the commands of lip, the catalogue and the server, in
/// one full-screen program of a sidebar of pages, the page shown, and dialogs above it.
/// Everything but the reading of keys and the work of the commands happens on the thread that
/// calls Run, which other threads reach through Post.
public sealed partial class App {
  /// The smallest terminal that the interface is drawn on.
  public const int MinWidth = 60;
  public const int MinHeight = 16;

  // Below this width the sidebar shows icons, and the names only while it has the focus.
  private const int WideWidth = 80;
  private const int ToastMilliseconds = 6000;

  private readonly ITerminal _terminal;
  private readonly Func<IUserInteraction, Func<Stream>, string, LipClient> _createClient;
  private readonly Interaction _interaction;
  private readonly Func<Stream> _hookOutput;
  private readonly string _version;
  private readonly WorkspaceList _workspaces;
  private readonly ServerSessions _servers;
  // The invocation of lip that the interface is carried out for.
  private readonly Invocation _invocation;
  private readonly bool _managed;
  private readonly Func<Exception, string[], bool, string, Failure> _describeFailure;
  private readonly Language? _clientLanguage;
  private readonly LanguageScope _language = new();
  private readonly BlockingCollection<Action> _events = new();
  private readonly List<Dialog> _dialogs = [];
  private readonly IReadOnlyList<PageEntry> _entries;
  private readonly List<Page> _pages = [];
  // The client of the selected workspace.
  private LipClient _client;
  // The selected workspace (protocol/lip-tui.md, "Workspaces").
  private string _workspace;
  private Language? _configuredLanguage;
  private Task _languageSaved = Task.CompletedTask;
  private ScreenBuffer _screen = new(1, 1);
  private string[] _shown = [];
  private volatile string _screenText = "";
  private int _current;
  private bool _contentFocused;
  private bool _quit;
  private bool _quitOnceStopped;
  private int _tickPending;
  private int _serversPending;
  private (string Text, ToastKind Kind, long At)? _toast;
  private readonly string? _workspaceListFailure;
  private readonly HashSet<Page> _tickFailed = [];
  private long _keysHandled;
  private long _frames;

  public App(AppOptions options) {
    _terminal = options.Terminal;
    _createClient = options.CreateClient;
    _version = options.Version;
    _describeFailure = options.DescribeFailure ?? ErrorReporter.Describe;
    _workspaces = options.Workspaces
        ?? new WorkspaceList(new System.IO.Abstractions.FileSystem(), null);
    _servers = options.Servers ?? ServerSessions.Shared;
    _invocation = Invocation.Current;
    _managed = options.ManagedServers ?? _invocation != Invocation.Process;
    _clientLanguage = options.ClientLanguage;
    _language.Language = _clientLanguage ?? Language.En;
    _entries = options.Pages ?? PageRegistry.All;
    if (_entries.Count == 0) {
      throw new ArgumentException("The interface has no page.", nameof(options));
    }

    _interaction = new Interaction(this);
    _hookOutput = () => new LineSinkStream((line, transient) =>
        Post(() => AddHookLine(new StyledLine("│ " + line, Theme.Muted), transient)));

    // The working directory of lip is added to the list and selected.
    _workspace = _workspaces.Normalize(options.WorkingDirectory);
    try {
      _workspace = _workspaces.Add(_workspace);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // Said once the interface runs, in its language.
      _workspaceListFailure = ex.Message;
    }

    _client = _createClient(_interaction, _hookOutput, _workspace);
    Context = new AppContext(this, options);
  }

  /// The services of the interface, for its pages and its dialogs.
  public AppContext Context { get; }

  /// What the screen shows, as plain text.
  public string ScreenText => _screenText;

  /// How many keys the interface has handled, and how many times it has drawn the screen: for
  /// tests, which wait for the keys they pressed to show.
  public long KeysHandled => Interlocked.Read(ref _keysHandled);

  public long Frames => Interlocked.Read(ref _frames);

  /// Runs the interface until the user quits it, and returns the exit code of lip.
  public int Run() {
    L.Bind(_language);
    ReadLanguage();
    Thread reader = new(ReadKeys) { IsBackground = true, Name = "lip keys" };
    reader.Start();
    using Timer timer = new(
        _ => {
          if (Interlocked.Exchange(ref _tickPending, 1) == 0) {
            Post(Tick);
          }
        },
        null, 250, 250);
    // Only a change that this interface shows or waits for draws the screen again.
    // The servers of the other workspaces are drawn again with the next tick.
    void Changed(WorkspaceServer server) {
      if ((_quitOnceStopped || WorkspaceList.PathComparer.Equals(server.Workspace, _workspace))
          && Interlocked.Exchange(ref _serversPending, 1) == 0) {
        Post(ServersChanged);
      }
    }

    _servers.Changed += Changed;
    try {
      foreach (PageEntry entry in _entries) {
        _pages.Add(entry.Create(Context));
      }

      Guard(() => {
        foreach (Page page in _pages) {
          page.OnStart();
        }

        Current.OnShown();
      });
      if (_workspaceListFailure is { } failure) {
        Toast($"{CommonText.WorkspaceListUnwritable}: {failure}", ToastKind.Bad);
      }

      Render();
      while (!_quit) {
        Action? next = _events.Take();
        do {
          Guard(next);
        } while (!_quit && _events.TryTake(out next));

        if (!_quit) {
          Render();
        }
      }
    }
    finally {
      _servers.Changed -= Changed;
      L.Bind(null);
    }

    // The language that the user chose last is written before lip may exit.
    try {
      _languageSaved.Wait(TimeSpan.FromSeconds(5));
    }
    catch (AggregateException ex) {
      RunTrace.Write("interface", "The language was not written: " + ex);
    }

    return 0;
  }

  /// Runs the action on the thread of the interface, which then draws the screen again.
  public void Post(Action action) => _events.Add(action);

  internal LipClient ClientOfWorkspace => _client;

  internal string SelectedWorkspace => _workspace;

  internal WorkspaceList WorkspaceList => _workspaces;

  internal ServerSessions ServerSessions => _servers;

  internal bool Managed => _managed;

  internal Language Language => _language.Language;

  internal Language? LanguageSetting => _configuredLanguage;

  internal LanguageScope LanguageScope => _language;

  /// A client for a directory, whose messages and questions the interface shows.
  internal LipClient ClientFor(string directory) =>
      _createClient(_interaction, _hookOutput, _workspaces.Normalize(directory));

  /// Shows the page, with the focus in the content area.
  internal void Navigate(PageId page, string? request) {
    int index = _entries.ToList().FindIndex(e => e.Id == page);
    if (index >= 0) {
      ShowPage(index, focus: true, request);
    }
  }

  internal void Focus(bool content) => _contentFocused = content;

  internal PageId CurrentPage => _entries[_current].Id;

  internal bool ContentFocused => _contentFocused;

  private Page Current => _pages[_current];

  private bool TooSmall => _screen.Width < MinWidth || _screen.Height < MinHeight;

  private bool Narrow => _screen.Width < WideWidth;

  /// Whether nothing is open above the page: no dialog, and no operation.
  internal bool Idle => _dialogs.Count == 0 && _operation is null;

  /// Something that a page or a dialog does: when it fails, the failure is shown, and the
  /// interface goes on.
  private void Guard(Action action) {
    try {
      action();
    }
    catch (Exception ex) {
      RunTrace.Write("interface", "A part of the interface failed: " + ex);
      ShowFailure("lip", ex, cancelled: false, []);
    }
  }

  private void ReadKeys() {
    try {
      while (true) {
        ConsoleKeyInfo key = _terminal.ReadKey();
        Post(() => {
          try {
            OnKey(key);
          }
          finally {
            Interlocked.Increment(ref _keysHandled);
          }
        });
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException
        or OperationCanceledException or ObjectDisposedException) {
      // The terminal is gone.
      Post(QuitOnceServersStopped);
    }
  }

  /// The interface language (protocol/lip-tui.md, "Language"): the value of language, read
  /// without writing a file, otherwise the client language, otherwise English.
  private void ReadLanguage() {
    try {
      _configuredLanguage = Languages.Parse(_client.Language().GetAwaiter().GetResult());
    }
    catch (Exception ex) {
      RunTrace.Write("interface", "The language was not read: " + ex);
    }

    _language.Language = _configuredLanguage ?? _clientLanguage ?? Language.En;
  }

  private void Tick() {
    Volatile.Write(ref _tickPending, 0);
    if (_toast is { } toast && Environment.TickCount64 - toast.At > ToastMilliseconds) {
      _toast = null;
    }

    // A page whose tick fails says so once, and gets no more ticks.
    foreach (Page page in _pages) {
      if (_tickFailed.Contains(page)) {
        continue;
      }

      try {
        page.OnTick();
      }
      catch (Exception ex) {
        _tickFailed.Add(page);
        RunTrace.Write("interface", "The tick of a page failed: " + ex);
        ShowFailure(page.Title, ex, cancelled: false, []);
      }
    }

    if (_dialogs.Count > 0) {
      Dialog top = _dialogs[^1];
      try {
        top.OnTick();
      }
      catch (Exception ex) {
        Close(top);
        RunTrace.Write("interface", "The tick of a dialog failed: " + ex);
        ShowFailure("lip", ex, cancelled: false, []);
      }
    }
  }

  /// Something about a server changed: the screen is drawn again, and the interface quits once
  /// the servers it asked to stop have exited.
  private void ServersChanged() {
    Volatile.Write(ref _serversPending, 0);
    if (_quitOnceStopped && _servers.Running().Count == 0) {
      _quit = true;
    }
  }

  #region Keys

  private void OnKey(ConsoleKeyInfo key) {
    if (TooSmall) {
      // Nothing is shown that a key could act on; Ctrl+C still quits, without the question
      // that there is no room for, and cancels the operation that runs.
      if (Ui.IsInterrupt(key)) {
        if (_operation is { Cancellable: true } operation) {
          operation.Cancel.Cancel();
        }

        QuitOnceServersStopped();
      }

      return;
    }

    if (_dialogs.Count > 0) {
      _dialogs[^1].OnKey(key);
      return;
    }

    if (_contentFocused) {
      if (Current.OnKey(key)) {
        return;
      }

      if (key.Key is ConsoleKey.Escape or ConsoleKey.LeftArrow or ConsoleKey.Tab) {
        _contentFocused = false;
      } else if (Ui.IsInterrupt(key)) {
        RequestQuit();
      }

      return;
    }

    switch (key.Key) {
      case ConsoleKey.UpArrow:
        ShowPage((_current + _pages.Count - 1) % _pages.Count, focus: false);
        break;
      case ConsoleKey.DownArrow:
        ShowPage((_current + 1) % _pages.Count, focus: false);
        break;
      case ConsoleKey.Home or ConsoleKey.PageUp:
        ShowPage(0, focus: false);
        break;
      case ConsoleKey.End or ConsoleKey.PageDown:
        ShowPage(_pages.Count - 1, focus: false);
        break;
      case ConsoleKey.Enter or ConsoleKey.RightArrow or ConsoleKey.Tab:
        _contentFocused = true;
        break;
      case ConsoleKey.Escape:
        RequestQuit();
        break;
      default:
        if (Ui.IsInterrupt(key)) {
          RequestQuit();
        }

        break;
    }
  }

  /// Shows the page at index, once the page shown lets go; the content area takes the focus
  /// when focus is set, and the page gets the request when one is given.
  internal void ShowPage(int index, bool focus, string? request = null) {
    void Arrive() {
      _contentFocused = focus || _contentFocused;
      if (request is not null) {
        _pages[index].OnRequest(request);
      }
    }

    if (index == _current) {
      Arrive();
      return;
    }

    Leave(() => {
      Current.OnHidden();
      _current = index;
      Current.OnShown();
      Arrive();
    });
  }

  /// Carries leave out once the page shown has no unsaved changes, or the user saved or
  /// discarded them.
  private void Leave(Action leave) {
    if (Current.CanLeave) {
      leave();
    } else {
      Current.ConfirmLeave(leave);
    }
  }

  internal void RequestQuit() =>
      Leave(() => {
        bool running = _servers.Running().Count > 0;
        List<StyledLine> lines = [new StyledLine(CommonText.QuitQuestion)];
        if (running) {
          lines.Add(new StyledLine(
              _managed ? CommonText.QuitServersKeepRunning : CommonText.QuitStopsServers,
              Theme.Muted));
        }

        Show(new ConfirmDialog(
            CommonText.QuitTitle, lines, [CommonText.Quit, CommonText.Cancel],
            choice => {
              if (choice == 0) {
                QuitOnceServersStopped();
              }
            }));
      });

  /// Quits the interface: at once when its servers are managed servers or none runs, otherwise
  /// once the servers of this process, which it asks to stop, have exited (protocol/lip-tui.md,
  /// "Server").
  private void QuitOnceServersStopped() {
    IReadOnlyList<WorkspaceServer> running = _managed ? [] : _servers.Running();
    if (running.Count == 0) {
      _quit = true;
      return;
    }

    _quitOnceStopped = true;
    // The page of the server shows what it writes while it stops.
    int server = _entries.ToList().FindIndex(e => e.Id == PageId.Server);
    if (server >= 0 && server != _current) {
      Current.OnHidden();
      _current = server;
      Current.OnShown();
    }

    _contentFocused = true;
    foreach (WorkspaceServer stopped in running) {
      stopped.Stop();
    }
  }

  #endregion

  #region Dialogs

  internal void Show(Dialog dialog) {
    dialog.Owner = Context;
    _dialogs.Add(dialog);
  }

  internal void Close(Dialog dialog) => _dialogs.Remove(dialog);

  internal void Toast(string text, ToastKind kind) =>
      _toast = (text.ReplaceLineEndings(" "), kind, Environment.TickCount64);

  #endregion

  #region Workspaces

  /// Selects the workspace, once the page shown lets go: the commands carried out from now on
  /// have it as their working directory, and the pages show it.
  internal void SwitchWorkspace(string workspace, Action? switched = null) {
    if (_workspaces.Same(workspace, _workspace)) {
      switched?.Invoke();
      return;
    }

    Leave(() => {
      _workspace = _workspaces.Normalize(workspace);
      _client = _createClient(_interaction, _hookOutput, _workspace);
      WorkspaceChanged();
      Toast($"{CommonText.WorkspaceSelected}: {_workspace}", ToastKind.Info);
      switched?.Invoke();
    });
  }

  /// What the pages read of the selected workspace is stale.
  internal void WorkspaceChanged() {
    foreach (Page page in _pages) {
      page.OnWorkspaceChanged();
    }
  }

  /// The canonical form of a path, absolute or relative to the selected workspace.
  internal string FullPath(string path) {
    System.IO.Abstractions.IFileSystem fileSystem = _workspaces.FileSystem;
    return _workspaces.Normalize(
        fileSystem.Path.IsPathFullyQualified(path) ? path : fileSystem.Path.Join(_workspace, path));
  }

  /// Adds the directory at path, absolute or relative to the selected workspace, to the list,
  /// and selects it. False, with why, when it is not a directory or the list is not written.
  internal bool AddWorkspace(string path, out string? error, Action? switched = null) {
    string full = FullPath(path);
    if (!_workspaces.FileSystem.Directory.Exists(full)) {
      error = $"{CommonText.NotADirectory}: {full}";
      return false;
    }

    try {
      SwitchWorkspace(_workspaces.Add(full), switched);
      error = null;
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      error = $"{CommonText.WorkspaceListUnwritable}: {ex.Message}";
      return false;
    }
  }

  #endregion

  #region Language

  /// Sets the configuration key language, or removes it when setting is null, and shows the
  /// interface in the language that follows, at once.
  internal void SetLanguage(Language? setting) {
    _configuredLanguage = setting;
    _language.Language = setting ?? _clientLanguage ?? Language.En;
    LipClient client = _client;
    // The changes are written one at a time, in the order they were made.
    _languageSaved = _languageSaved.ContinueWith(
        async _ => {
          try {
            if (setting is { } language) {
              await client.ConfigSet(ConfigService.LanguageKey, Languages.Code(language));
            } else {
              await client.ConfigDelete(ConfigService.LanguageKey);
            }
          }
          catch (Exception ex) {
            Post(() => ShowFailure(ConfigService.LanguageKey, ex, cancelled: false, []));
          }
        },
        CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
  }

  #endregion

  #region Server

  /// Starts the server of the selected workspace unless it runs: its managed server, or, when
  /// lip itself carries out the interface, a server that this process runs
  /// (protocol/lip-tui.md, "Server"). A failure before the server starts is shown.
  internal void StartServer(string title) {
    string workspace = _workspace;
    Task? started = _servers.For(workspace).Start(
        interaction => _createClient(interaction, () => Stream.Null, workspace),
        _invocation,
        listenForStop: _managed ? null : ServerSession.ListenForSignals);
    started?.ContinueWith(
        task => Post(() => {
          if (!_quitOnceStopped) {
            ShowFailure(title, task.Exception!.InnerException!, cancelled: false, []);
          }
        }),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
  }

  #endregion

  #region Drawing

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
    if (TooSmall) {
      _screen.WriteFit(0, 0, _screen.Width, CommonText.TooSmall, Theme.Warning);
    } else {
      Rect body = new(0, 1, _screen.Width, _screen.Height - 2);
      // On a narrow terminal the sidebar shows the names of the pages only while it has the
      // focus, and icons while the page has it.
      int sidebar = Narrow && _contentFocused ? 5 : SidebarWidth();
      DrawTopBar();
      DrawSidebar(new Rect(0, body.Y, sidebar, body.Height));
      DrawContent(new Rect(sidebar, body.Y, body.Width - sidebar, body.Height));
      foreach (Dialog dialog in _dialogs.ToArray()) {
        try {
          dialog.Draw(_screen);
        }
        catch (Exception ex) {
          // A dialog that cannot be drawn is closed, and the interface goes on.
          RunTrace.Write("interface", "A dialog was not drawn: " + ex);
          Close(dialog);
          Toast(ex.Message, ToastKind.Bad);
        }
      }

      DrawBottomBar();
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
    Interlocked.Increment(ref _frames);
    if (output.Length > 0) {
      _terminal.Write(output.ToString());
    }
  }

  /// The columns of the sidebar that shows the names of the pages.
  private int SidebarWidth() =>
      Math.Min(_screen.Width / 2, _pages.Max(p => TextWidth.Of(p.Title)) + 7);

  private void DrawTopBar() {
    int x = _screen.Write(1, 0, $"lip {_version}", Theme.Strong);
    WorkspaceServer? server = _servers.Find(_workspace);
    (string Text, Style Style)? state = server is not { Running: true } ? null
        : server.Restarting ? (CommonText.ServerRestarting, Theme.Warning)
        : server.Stopping ? (CommonText.ServerStopping, Theme.Warning)
        : (CommonText.ServerRunning, Theme.Good);
    int right = _screen.Width - 1;
    if (state is { } shown) {
      right -= TextWidth.Of(shown.Text) + 2;
      _screen.Write(right, 0, "● " + shown.Text, shown.Style);
      right -= 2;
    }

    x = _screen.Write(x, 0, " · ", Theme.Border);
    _screen.Write(x, 0, FitEnd(_workspace, right - x), Theme.Muted);
  }

  /// The text cut to its last width columns, starting with an ellipsis when it was cut: the end
  /// of a path says more than its start.
  private static string FitEnd(string text, int width) {
    if (TextWidth.Of(text) <= width) {
      return text;
    }

    if (width <= 1) {
      return "";
    }

    int start = text.Length;
    int used = 1;
    foreach (Rune rune in text.EnumerateRunes().Reverse()) {
      int w = TextWidth.Of(rune);
      if (used + w > width) {
        break;
      }

      used += w;
      start -= rune.Utf16SequenceLength;
    }

    return "…" + text[start..];
  }

  private void DrawSidebar(Rect rect) {
    bool focused = !_contentFocused && _dialogs.Count == 0;
    bool names = !Narrow || !_contentFocused;
    _screen.Fill(rect, default);
    _screen.Box(
        rect, "", focused ? Theme.Accent with { Bold = false } : Theme.Border, Theme.Strong);
    Rect inner = rect.Inner;
    for (int i = 0; i < _pages.Count && i < inner.Height; i++) {
      int y = inner.Y + i;
      if (names) {
        _screen.WriteFit(inner.X, y, inner.Width, _pages[i].Title);
        if (_pages[i].Badge is { } badge) {
          _screen.Write(inner.Right - TextWidth.Of(badge.PlainText), y, badge, inner.Right);
        }
      } else {
        _screen.Write(inner.X, y, _entries[i].Icon, default, inner.Right);
      }

      if (i == _current) {
        _screen.Restyle(
            inner.X - 1, y, inner.Width + 2,
            !_contentFocused ? Theme.Selected : Theme.SelectedInactive);
      }
    }
  }

  private void DrawContent(Rect rect) {
    bool focused = _contentFocused && _dialogs.Count == 0;
    Rect inner = Ui.Panel(_screen, rect, Current.Title, focused);
    try {
      Current.Draw(_screen, inner, focused);
    }
    catch (Exception ex) {
      // A page that cannot be drawn says why, and the interface goes on.
      RunTrace.Write("interface", "A page was not drawn: " + ex);
      _screen.Fill(inner, default);
      int y = inner.Y;
      foreach (string line in TextWidth.Wrap(ex.Message, inner.Width).Take(inner.Height)) {
        _screen.Write(inner.X, y++, line, Theme.Bad, inner.Right);
      }
    }
  }

  /// The keys that work now, from what has the focus.
  private IReadOnlyList<Hint> CurrentHints() {
    if (_dialogs.Count > 0) {
      return _dialogs[^1].Hints;
    }

    if (!_contentFocused) {
      return [CommonText.HintMove, CommonText.HintOpen, CommonText.HintQuit];
    }

    IReadOnlyList<Hint> hints = Current.Hints;
    return hints.Any(h => h.Key == CommonText.HintBack.Key)
        ? hints
        : [.. hints, CommonText.HintBack];
  }

  private void DrawBottomBar() {
    int y = _screen.Height - 1;
    int limit = _screen.Width - 1;
    if (_toast is { } toast) {
      string text = TextWidth.Fit(toast.Text, Math.Max(10, _screen.Width / 2));
      limit -= TextWidth.Of(text);
      _screen.Write(limit, y, text, toast.Kind switch {
        ToastKind.Good => Theme.Good,
        ToastKind.Warning => Theme.Warning,
        ToastKind.Bad => Theme.Bad,
        _ => Theme.Accent,
      });
      limit -= 2;
    }

    int x = 1;
    foreach (Hint hint in CurrentHints()) {
      // A hint is shown whole or not at all.
      if (x + TextWidth.Of(hint.Key) + 1 + TextWidth.Of(hint.Text) > limit) {
        break;
      }

      x = _screen.Write(x, y, hint.Key, Theme.Strong);
      x = _screen.Write(x + 1, y, hint.Text, Theme.Muted) + 2;
    }
  }

  #endregion

  /// Opens a URL in the browser of the user; false, with why, when it cannot.
  internal static bool OpenInBrowser(string url, out string? error) {
    try {
      using Process? process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
      error = null;
      return true;
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
        or InvalidOperationException or PlatformNotSupportedException) {
      error = ex.Message;
      return false;
    }
  }
}
