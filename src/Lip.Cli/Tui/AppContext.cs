using System.IO.Abstractions;
using Lip.Cli.Servers;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui;

/// The services of the interface for its pages and its dialogs: the selected workspace and its
/// client, navigation, dialogs, operations, and the parts of lip that tests replace. Every
/// member is for the thread of the interface, but Post, SelfUpdate and DaemonStatus, which
/// are for any thread.
public sealed class AppContext {
  private readonly App _app;
  private readonly AppOptions _options;

  internal AppContext(App app, AppOptions options) {
    _app = app;
    _options = options;
    ServerSetup = options.ServerSetup ?? ClientServerSetup.Instance;
  }

  #region The workspace

  /// The client of the selected workspace: every command of lip. It is another object after
  /// another workspace was selected.
  public LipClient Client => _app.ClientOfWorkspace;

  /// The selected workspace, the canonical form of an absolute path (protocol/lip-tui.md,
  /// "Workspaces").
  public string Workspace => _app.SelectedWorkspace;

  /// The workspace list.
  public WorkspaceList Workspaces => _app.WorkspaceList;

  /// The file system of the workspaces.
  public IFileSystem FileSystem => _app.WorkspaceList.FileSystem;

  /// The canonical form of a path, absolute or relative to the selected workspace.
  public string FullPath(string path) => _app.FullPath(path);

  /// Selects the workspace, once the page shown has no unsaved changes; then calls switched.
  /// Every page gets OnWorkspaceChanged.
  public void SwitchWorkspace(string workspace, Action? switched = null) =>
      _app.SwitchWorkspace(workspace, switched);

  /// Adds the directory at path, absolute or relative to the selected workspace, to the
  /// workspace list, and selects it as SwitchWorkspace does. False, with why in the language of
  /// the interface, when it is not a directory or the list cannot be written.
  public bool AddWorkspace(string path, out string? error, Action? switched = null) =>
      _app.AddWorkspace(path, out error, switched);

  /// Tells every page that what it read of the selected workspace is stale: for a page that
  /// changed the workspace by other means than PlanAndRun.
  public void NotifyWorkspaceChanged() => _app.WorkspaceChanged();

  /// A client for another directory than the selected workspace, whose messages and questions
  /// are shown in the interface too.
  public LipClient ClientFor(string directory) => _app.ClientFor(directory);

  #endregion

  #region The parts of lip

  /// The version of lip.
  public string Version => _options.Version;

  /// Reads the catalogue (protocol/lip-tui.md, "Catalogue").
  public ModCatalogClient Catalog => _options.Catalog;

  /// Checks that GitHub is reached, or null when the interface does not check
  /// (protocol/lip-tui.md, "GitHub reachability").
  public GithubProbe? GithubProbe => _options.GithubProbe;

  /// The versions of the game and of the loader, and lip setup (protocol/lip-tui.md, "New
  /// server").
  public IServerSetup ServerSetup { get; }

  /// lip self-update (protocol/lip-self-update.md) with a client: only checks for a newer
  /// release when checkOnly is set. For any thread: take the client from Client on the thread
  /// of the interface, before the operation or the background work that calls this.
  public Task<SelfUpdateResult> SelfUpdate(LipClient client, bool checkOnly) =>
      _options.SelfUpdate is { } update
          ? update(client, checkOnly)
          : client.SelfUpdate(SemVersion.Parse(Version), checkOnly);

  /// What lip daemon status finds: null when no daemon listens. For any thread.
  public Task<DaemonState?> DaemonStatus(CancellationToken cancellationToken = default) =>
      _options.DaemonStatus is { } status
          ? status(cancellationToken)
          : Task.FromResult<DaemonState?>(null);

  /// Opens an http or https URL in the browser of the user; a failure, or anything else than
  /// such a URL, is said in the bottom bar. A URL may come from the catalogue, which is not to
  /// be trusted with starting a program.
  public void OpenUrl(string url) {
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        || uri.Scheme is not ("http" or "https")) {
      Toast(url, ToastKind.Bad);
    } else if (_options.OpenUrl is { } open) {
      open(uri.AbsoluteUri);
    } else if (!App.OpenInBrowser(uri.AbsoluteUri, out string? error)) {
      Toast(error!, ToastKind.Bad);
    }
  }

  #endregion

  #region The server

  /// The servers of the process, of every workspace (protocol/lip-tui.md, "Server").
  public ServerSessions Servers => _app.ServerSessions;

  /// The server of the selected workspace, or null when none ever ran in it.
  public WorkspaceServer? Server => _app.ServerSessions.Find(Workspace);

  /// Whether the server of the selected workspace runs: a server of this process (Server), or
  /// one that another lip runs, which holds the server lock of the workspace
  /// (protocol/lip-workspace.md, "Server lock"). The interface asks about the lock about once
  /// a second, in the background, so this is cheap, and at most that old. While it is true the
  /// interface carries out no workspace operation, and the files of the server are not to be
  /// saved (protocol/lip-tui.md, "Commands", "Configuration").
  public bool ServerRunning => IsServerRunning(Workspace);

  /// As ServerRunning, for any workspace of the workspace list.
  public bool IsServerRunning(string workspace) => _app.IsServerRunning(workspace);

  /// Whether another lip runs the server of the selected workspace: a plain lip run, or
  /// another daemon, which holds the server lock of the workspace, while this process has no
  /// server that runs for it. As old as ServerRunning. The interface knows that such a server
  /// runs and no more: Server is null or stopped for it, and it is stopped where it was
  /// started.
  public bool ServerRunsElsewhere => _app.ServerRunsElsewhere(Workspace);

  /// Whether the server of a workspace runs right now: as IsServerRunning, but looking at the
  /// server lock itself. Not for the thread of the interface: for the work of Background or
  /// RunOperation, right before it does what must not be done while the server runs.
  public bool ServerRunsNow(string workspace) => _app.ServerRunsNow(workspace);

  /// When the server of the selected workspace runs, tells the user in a dialog with the title
  /// to stop it first (CommonText.StopServerFirst), and returns true: for what must not be
  /// done meanwhile, as in "if (Context.RefuseWhileServerRuns(title)) { return; }". PlanAndRun
  /// does this itself.
  public bool RefuseWhileServerRuns(string title) => _app.RefuseWhileServerRuns(title);

  /// Whether the servers that the interface starts are managed servers, which keep running
  /// when it quits.
  public bool ManagedServers => _app.Managed;

  /// Starts the server of the selected workspace unless it runs, as protocol/lip-tui.md,
  /// "Server" defines. A failure before the server starts is shown in a dialog with the title.
  public void StartServer(string title) => _app.StartServer(title);

  #endregion

  #region Navigation and dialogs

  /// Shows the page and gives the content area the focus, once the page shown has no unsaved
  /// changes. With a request, one of PageRequest, the page then gets OnRequest.
  public void Navigate(PageId page, string? request = null) => _app.Navigate(page, request);

  /// The page shown.
  public PageId CurrentPage => _app.CurrentPage;

  /// Whether the content area, and not the sidebar, has the focus.
  public bool ContentFocused => _app.ContentFocused;

  /// Gives the content area the focus.
  public void FocusContent() => _app.Focus(content: true);

  /// Gives the sidebar the focus.
  public void FocusSidebar() => _app.Focus(content: false);

  /// Opens the workspace picker, which the workspace of the top bar opens too: the workspace
  /// list, from which the user selects, adds or removes a workspace.
  public void ChooseWorkspace() => _app.ChooseWorkspace();

  /// Opens the dialog above everything else; it takes every key until it closes.
  public void Show(Dialog dialog) => _app.Show(dialog);

  /// Closes the dialog.
  public void Close(Dialog dialog) => _app.Close(dialog);

  /// Whether nothing is open above the page: no dialog, and no operation. Something that asks
  /// the user without being asked to, such as the GitHub question, waits for this.
  public bool Idle => _app.Idle;

  /// Asks a question with two buttons; confirmed is called when the user chooses the first.
  public void Confirm(
      string title, string question, string confirm, Action confirmed, string? cancel = null) =>
      Show(new ConfirmDialog(
          title, [new StyledLine(question)], [confirm, cancel ?? CommonText.Cancel],
          choice => {
            if (choice == 0) {
              confirmed();
            }
          }));

  /// Shows a message with one button.
  public void Message(string title, string message, Style style = default) =>
      Show(new ConfirmDialog(title, [new StyledLine(message, style)], [CommonText.Close]));

  /// Shows the action menu of a row.
  public void ShowMenu(string title, params MenuItem[] items) => Show(new Menu(title, items));

  /// Shows a message at the right of the bottom bar for a few seconds.
  public void Toast(string text, ToastKind kind = ToastKind.Info) => _app.Toast(text, kind);

  /// Asks to quit, as Esc in the sidebar does.
  public void RequestQuit() => _app.RequestQuit();

  /// Quits without asking, unless a server runs: then asks as RequestQuit does.
  public void Quit() => _app.Quit();

  /// Has message reach the terminal of the user once the interface has ended
  /// (AppOptions.Farewell).
  public void Farewell(string message) => _options.Farewell?.Invoke(message);

  #endregion

  #region Operations

  /// Runs the action on the thread of the interface, which then draws the screen again. For
  /// any thread.
  public void Post(Action action) => _app.Post(action);

  /// Runs a command in the background, showing its progress in a dialog; then calls succeeded
  /// with what the command told the user, or shows why it failed. The user may cancel it with
  /// Esc when cancellable is set. One command runs at a time: false, and nothing runs, while
  /// another does.
  public bool RunOperation(
      string title,
      Func<CancellationToken, Task> action,
      Action<Operation>? succeeded = null,
      bool cancellable = false) =>
      _app.RunOperation(title, action, succeeded, cancellable);

  /// As RunOperation, for a command with a result, which succeeded gets.
  public bool RunOperation<T>(
      string title,
      Func<CancellationToken, Task<T>> action,
      Action<T> succeeded,
      bool cancellable = false) {
    T? result = default;
    return _app.RunOperation(
        title, async token => result = await action(token), _ => succeeded(result!), cancellable);
  }

  /// Does work in the background without a dialog, such as reading what a page shows; then
  /// calls done with its result, or failed, on the thread of the interface. Without failed, the
  /// failure is shown in the bottom bar.
  ///
  /// What the work reads is taken to be of the selected workspace: neither done nor failed is
  /// called when another workspace was selected meanwhile. Set anyWorkspace for work that is
  /// of no workspace, such as reading the workspace list, the catalogue, the user
  /// configuration or the daemon: its callbacks are then called whatever is selected.
  public void Background<T>(
      Func<Task<T>> work,
      Action<T> done,
      Action<Exception>? failed = null,
      bool anyWorkspace = false) =>
      _app.Background(work, done, failed, anyWorkspace);

  /// A workspace operation, as every one of them goes: its dry run, whose messages are the
  /// plan; [Apply] [Cancel], or only [Close] when the dry run says that nothing would change;
  /// the license of the game when a setup would ask for it; its progress; its result. After
  /// it succeeded every page gets OnWorkspaceChanged, and done is called. Nothing is carried
  /// out while the server of the selected workspace runs: the user is told to stop it first.
  ///
  /// operation is called for the dry run, and again once the user applied the plan
  /// (OperationRun). Pass run.Session on to the command: what the dry run read of the network,
  /// the command after it does not read again. session is the session to use, for a flow that
  /// read something before, such as the versions that the user chose from; a new one when not
  /// given. notApplied is called when the operation is not carried out: the user cancelled
  /// the plan or declined the license, or the dry run or the operation failed or was
  /// cancelled, once the user closed the dialog that says so; not when the plan had nothing
  /// to apply. It is for a flow that did something before the operation, which the user is
  /// then told about.
  public void PlanAndRun(
      string title,
      Func<OperationRun, Task<OperationResult>> operation,
      Action? done = null,
      OperationSession? session = null,
      Action? notApplied = null) =>
      _app.PlanAndRun(title, operation, done, session, notApplied);

  /// Shows what a command told the user, once it succeeded, ending with done.
  public void ShowResult(string title, Operation operation, string? done = null) =>
      _app.ShowResult(title, operation, done ?? CommonText.Done);

  /// Shows why something failed, with what to do about it; the failure report is written below
  /// the selected workspace.
  public void ShowFailure(
      string title,
      Exception exception,
      bool cancelled = false,
      IReadOnlyList<StyledLine>? log = null) =>
      _app.ShowFailure(title, exception, cancelled, log ?? []);

  #endregion

  #region Language

  /// The interface language (protocol/lip-tui.md, "Language").
  public Language Language => _app.Language;

  /// The value of the configuration key language, or null when it has none and the interface
  /// follows the client language.
  public Language? LanguageSetting => _app.LanguageSetting;

  /// The language of this interface, for L.Bind: a thread that the interface did not start,
  /// such as that of a test, reads the text of the interface in its language once bound.
  public LanguageScope LanguageScope => _app.LanguageScope;

  /// The client language, or null when there is none.
  public Language? ClientLanguage => _options.ClientLanguage;

  /// Sets the configuration key language, or removes it when setting is null; the interface is
  /// drawn in the language that follows at once.
  public void SetLanguage(Language? setting) => _app.SetLanguage(setting);

  #endregion
}
