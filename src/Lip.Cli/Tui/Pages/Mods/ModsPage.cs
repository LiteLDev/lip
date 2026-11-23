using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.Mods;

/// An action row above the list of the Mods page.
internal enum ModAction {
  SetUpServer,
  InstallFromSource,
  UpdateAll,
  GetMods,
}

/// Says that the catalogue is being read, for the details of a package. Esc gives up waiting,
/// and calls cancelled.
internal sealed class CatalogueWait(Action cancelled) : Dialog {
  public override IReadOnlyList<Hint> Hints => [CommonText.HintCancel];

  public override void Draw(ScreenBuffer screen) {
    string text = ModsText.ReadingCatalogue;
    int width = Math.Min(Ui.DialogWidth(screen), Math.Max(36, TextWidth.Of(text) + 4));
    Rect inner = Ui.DialogFrame(screen, ModsText.Details, width, 3);
    screen.WriteFit(inner.X, inner.Y, inner.Width, text, Theme.Muted);
  }

  public override void OnKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Escape) {
      Close();
      cancelled();
    }
  }
}

/// An action menu that follows the server of the selected workspace while it is open: when
/// the server starts or stops, the menu is made again where it is, with the same action
/// highlighted, so that what it offers, and what it says of what it does not offer, is true
/// when an action is chosen. make gets this dialog, which an action closes before it is
/// carried out, and whether the server runs.
internal sealed class ServerMenu(Func<ServerMenu, bool, Menu> make) : Dialog {
  // The menu as it was made last, whether the server ran then, and the keys that moved its
  // highlight since it was opened.
  private readonly List<ConsoleKeyInfo> _moves = [];
  private Menu? _menu;
  private bool _running;

  public override IReadOnlyList<Hint> Hints => Current().Hints;

  public override void Draw(ScreenBuffer screen) => Current().Draw(screen);

  public override void OnKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Escape) {
      Close();
      return;
    }

    Menu menu = Current();
    if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow) {
      _moves.Add(key);
    }

    menu.OnKey(key);
  }

  /// The menu for the server as it is now.
  private Menu Current() {
    bool running = Context.ServerRunning;
    if (_menu is null || running != _running) {
      _running = running;
      _menu = make(this, running);
      // Every action of these menus can be chosen or says why not, so ↑ and ↓ stop at each
      // of them, and the same keys lead to the same action.
      foreach (ConsoleKeyInfo move in _moves) {
        _menu.OnKey(move);
      }
    }

    return _menu;
  }
}

/// The Mods page: the installed mods of the selected workspace and what they need
/// (InstalledPackage.Mods), with lip install, lip update and lip uninstall
/// (protocol/lip-install.md, lip-update.md, lip-uninstall.md, lip-list.md). The game, the
/// loader and what only they need are not listed: the Server page sets them up. The list is in
/// the order of InstalledPackage.Listed, which no operation changes. Action rows stand above the list; Enter on a package opens its action menu,
/// which acts on the checked packages when there are any. While the server of the workspace
/// runs, what changes the workspace is shown disabled, and a line says so; a menu that is open
/// when the server starts or stops is made again.
public sealed class ModsPage : Page {
  // The rows that the details area takes: a rule, a title, and two rows for a sentence, or
  // for a sentence and the source of a package.
  private const int DetailRows = 4;
  private readonly ListView<ModAction> _actions;
  private readonly ListView<InstalledPackage> _list;
  // The areas that take the keys: 0 the action rows, 1 the list, when it has rows.
  private readonly FocusRing _focus = new(1);
  // The workspace that the list was read of.
  private string? _workspace;
  private bool _loaded;
  // Whether the workspace has a game, as last read.
  private bool _hasGame;
  private string? _error;
  // How many times the list was read.
  private int _reads;
  // The columns of the name and of the version of a row, as last drawn.
  private int _nameWidth = 1;
  private int _versionWidth = 1;
  // The dialog that waits for the catalogue, for the details of a package; null when none
  // does.
  private Dialog? _waiting;

  public ModsPage(AppContext context) : base(context) {
    _actions = new ListView<ModAction>(
        action => new StyledLine(Label(action), Disabled(action) ? Theme.Muted : default));
    // Only what both lip update and lip uninstall take can be checked: not a dependency, which
    // cannot be uninstalled.
    _list = new ListView<InstalledPackage>(Row) {
      MultiSelect = true,
      CanCheck = CanCheck,
    };
    Refresh();
  }

  public override string Title => ModsText.Title;

  public override IReadOnlyList<Hint> Hints =>
      _focus.Index == 1
          ? [
            CommonText.HintMove,
            .. _list.HasCurrent && CanCheck(_list.Current) ? [CommonText.HintCheck] : (Hint[])[],
            CommonText.HintActions,
            CommonText.HintNext,
            .. _list.PageHints,
          ]
          : [
            CommonText.HintMove,
            .. _actions.HasCurrent && !Disabled(_actions.Current)
                ? [CommonText.HintSelect] : (Hint[])[],
            .. _focus.Count > 1 ? [CommonText.HintNext] : (Hint[])[],
          ];

  public override void OnShown() {
    Sync();
    Load();
  }

  public override void OnWorkspaceChanged() {
    if (Context.CurrentPage == PageId.Mods) {
      Sync();
      Load();
    } else {
      // Another page changed the workspace: what was read is read again when this one is shown.
      _workspace = null;
    }
  }

  public override void OnRequest(string request) {
    if (request == PageRequest.InstallFromSource
        && !Context.RefuseWhileServerRuns(ModsText.InstallTitle)) {
      PromptInstall();
    }
  }

  /// Tab from the sidebar goes to the action rows, and Shift+Tab to the list when it has rows.
  public override void OnTabIn(bool backward) =>
      _focus.Index = backward ? _focus.Count - 1 : 0;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (area.Width < 1 || area.Height < 1) {
      return;
    }

    // While the server runs, the first row says that nothing can be changed.
    if (Context.ServerRunning && area.Height > _actions.Items.Count + 1) {
      screen.WriteFit(area.X, area.Y, area.Width, ModsText.ServerRuns, Theme.Warning);
      area = area.Shrink(top: 1);
    }

    bool onList = _focus.Index == 1;
    Rect actions = new(
        area.X, area.Y, area.Width, Math.Min(_actions.Items.Count, area.Height));
    _actions.Draw(screen, actions, focused && !onList);

    int top = actions.Bottom + 1;
    int details = area.Bottom - top >= DetailRows + 3 ? DetailRows : 0;
    DrawList(
        screen, new Rect(area.X, top, area.Width, Math.Max(0, area.Bottom - top - details)),
        focused && onList);
    if (details > 0) {
      DrawDetails(screen, new Rect(area.X, area.Bottom - details, area.Width, details));
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    if (_focus.OnKey(key)) {
      return true;
    }

    if (_focus.Index == 0) {
      if (key.Key == ConsoleKey.Enter) {
        if (_actions.HasCurrent && !Disabled(_actions.Current)) {
          Run(_actions.Current);
        }

        return true;
      }

      if (_actions.OnKey(key)) {
        return true;
      }

      // ↓ on the last action row goes on to the first package.
      if (key.Key == ConsoleKey.DownArrow && _focus.Next()) {
        _list.Selected = 0;
        return true;
      }

      return false;
    }

    if (key.Key == ConsoleKey.Enter) {
      if (_list.HasCurrent) {
        ShowActions();
      }

      return true;
    }

    if (_list.OnKey(key)) {
      return true;
    }

    // ↑ on the first package goes back to the last action row.
    if (key.Key == ConsoleKey.UpArrow && _focus.Previous()) {
      _actions.Selected = _actions.Items.Count - 1;
      return true;
    }

    return false;
  }

  #region Reading

  /// Drops what was read of another workspace than the selected one.
  private void Sync() {
    if (_workspace == Context.Workspace) {
      return;
    }

    _workspace = Context.Workspace;
    _loaded = false;
    _error = null;
    _list.Checked.Clear();
    _list.Items = [];
    _list.Selected = 0;
    _actions.Selected = 0;
    Refresh();
  }

  /// Reads the installed packages (lip list) in the background.
  private void Load() {
    LipClient client = Context.Client;
    // Of several reads, only the one that started last counts.
    int read = ++_reads;
    Context.Background(
        () => client.ListPackages(),
        packages => {
          if (read != _reads) {
            return;
          }

          bool first = !_loaded;
          _loaded = true;
          _error = null;
          // The mods that the user installed, then their dependencies, each by identity: a package stays where it is whatever was installed or uninstalled,
          // and the highlight stays on the package it was on while that is still installed.
          string? highlighted = _list.HasCurrent ? _list.Current.Identity : null;
          _hasGame = packages.Any(p => p.IsGame);
          _list.Items = InstalledPackage.Listed(InstalledPackage.Mods(packages));
          if (highlighted is not null) {
            _list.Select(p => p.Identity == highlighted);
          }

          Refresh();
          if (first) {
            // With mods, the list is what the page is about; without, the first action row,
            // which is what to do first.
            _actions.Selected = 0;
            if (_list.Items.Any(CanCheck)) {
              _focus.Index = 1;
            }
          }
        },
        exception => {
          if (read != _reads) {
            return;
          }

          _loaded = true;
          _error = exception.Message;
          _hasGame = false;
          _list.Items = [];
          Refresh();
        });
  }

  /// The action rows and the focus areas that fit what the list holds.
  private void Refresh() {
    bool read = _loaded && _error is null;
    // The mods that the user installed: what lip update without operands updates.
    bool mods = _list.Items.Any(CanCheck);
    List<ModAction> actions = [];
    if (read && !_hasGame) {
      // A workspace without a game needs one before any mod.
      actions.Add(ModAction.SetUpServer);
    }

    actions.Add(ModAction.InstallFromSource);
    if (mods) {
      actions.Add(ModAction.UpdateAll);
    } else if (read) {
      actions.Add(ModAction.GetMods);
    }

    ModAction? current = _actions.HasCurrent ? _actions.Current : null;
    _actions.Items = actions;
    if (current is { } action) {
      _actions.Select(a => a == action);
    }

    _focus.Count = _list.Items.Count > 0 ? 2 : 1;
    if (_focus.Index >= _focus.Count) {
      _focus.Index = 0;
    }
  }

  /// Whether the package is a mod that the user installed.
  private static bool CanCheck(InstalledPackage package) => package.Explicit;

  #endregion

  #region Drawing

  /// Whether the action row cannot be carried out now: it changes the workspace, and the
  /// server of the workspace runs.
  private bool Disabled(ModAction action) =>
      action is ModAction.InstallFromSource or ModAction.UpdateAll && Context.ServerRunning;

  private static string Label(ModAction action) =>
      action switch {
        ModAction.SetUpServer => CommonText.SetUpServer,
        ModAction.InstallFromSource => ModsText.InstallFromSource,
        ModAction.UpdateAll => ModsText.UpdateAll,
        _ => CommonText.GetMods,
      };

  private static string Detail(ModAction action) =>
      action switch {
        ModAction.SetUpServer => ModsText.SetUpServerDetail,
        ModAction.InstallFromSource => ModsText.InstallFromSourceDetail,
        ModAction.UpdateAll => ModsText.UpdateAllDetail,
        _ => ModsText.GetModsDetail,
      };

  private static string Detail(InstalledPackage package) =>
      package.Explicit ? ModsText.ExplicitDetail : ModsText.DependencyDetail;

  /// A row of the list: the identity and the version in columns, muted for a dependency. An
  /// identity that is too long is cut in its middle, so that its name stays.
  private StyledLine Row(InstalledPackage package) {
    Style style = package.Explicit ? default : Theme.Muted;
    return new StyledLine(PackageCells.PadMiddle(package.Identity, _nameWidth) + "  ", style)
        .Add(TextWidth.Fit(package.Version, _versionWidth), style);
  }

  private void DrawList(ScreenBuffer screen, Rect rect, bool focused) {
    if (rect.Height < 1) {
      return;
    }

    if (!_loaded) {
      screen.WriteFit(rect.X, rect.Y, rect.Width, CommonText.Loading, Theme.Muted);
      return;
    }

    if (_error is { } error) {
      List<string> lines = TextWidth.Wrap(error, rect.Width);
      for (int i = 0; i < lines.Count && i < rect.Height; i++) {
        screen.Write(rect.X, rect.Y + i, lines[i], Theme.Bad, rect.Right);
      }

      return;
    }

    if (_list.Items.Count == 0) {
      screen.WriteFit(rect.X, rect.Y, rect.Width, CommonText.NoMods, Theme.Muted);
      return;
    }

    // "[ ] " before the name, two columns between the name and the version, and one column
    // for the marks of the rows that are hidden.
    string version = ModsText.Version;
    int widest = Math.Max(TextWidth.Of(version), Math.Min(24, rect.Width / 3));
    _versionWidth = Math.Clamp(
        _list.Items.Max(p => TextWidth.Of(p.Version)), TextWidth.Of(version), widest);
    _nameWidth = Math.Max(1, rect.Width - 4 - 2 - _versionWidth - 1);

    string header = ModsText.Installed(_list.Items.Count);
    if (_list.Checked.Count > 0) {
      header += " · " + ModsText.Checked(_list.Checked.Count);
    }

    screen.WriteFit(rect.X, rect.Y, 4 + _nameWidth, header, Theme.Muted);
    screen.Write(rect.X + 4 + _nameWidth + 2, rect.Y, version, Theme.Muted, rect.Right);
    _list.Draw(screen, rect.Shrink(top: 1), focused);
  }

  /// The details area: what has the focus, an action row or a package, and one sentence about
  /// it; for a package that is not of the version tags of its repository, its source too: the
  /// local path, the URL, or the branch, tag or commit.
  private void DrawDetails(ScreenBuffer screen, Rect rect) {
    StyledLine title;
    string detail;
    string? source = null;
    Style style = Theme.Muted;
    if (_focus.Index == 0 && _actions.HasCurrent) {
      title = new StyledLine(Label(_actions.Current).TrimEnd('…'), Theme.Strong);
      detail = Detail(_actions.Current);
      if (Disabled(_actions.Current)) {
        // Why the row does nothing now.
        detail = CommonText.StopServerFirst;
        style = Theme.Warning;
      }
    } else if (_focus.Index == 1 && _list.HasCurrent) {
      InstalledPackage package = _list.Current;
      int room = Math.Max(1, rect.Width - TextWidth.Of(package.Version) - 2);
      title = new StyledLine(PackageCells.FitMiddle(package.Identity, room), Theme.Strong)
          .Add("  " + package.Version);
      detail = Detail(package);
      source = package.SourceNote;
    } else {
      return;
    }

    screen.Write(rect.X, rect.Y, new string('─', rect.Width), Theme.Border);
    screen.Write(rect.X, rect.Y + 1, title, rect.Right);
    // The last row is that of the source, when the package has one to show; the sentence is
    // then cut to the row that is left.
    int bottom = source is null ? rect.Bottom : rect.Bottom - 1;
    int rows = Math.Max(0, bottom - (rect.Y + 2));
    List<string> lines = TextWidth.Wrap(detail, rect.Width);
    if (rows > 0 && lines.Count > rows) {
      // What has no row ends the last one in an ellipsis.
      lines[rows - 1] = TextWidth.Fit(string.Join(' ', lines.Skip(rows - 1)), rect.Width);
    }

    for (int i = 0; i < lines.Count && i < rows; i++) {
      screen.Write(rect.X, rect.Y + 2 + i, lines[i], style, rect.Right);
    }

    if (source is not null && bottom >= rect.Y + 2) {
      string label = ModsText.Source + "  ";
      int room = rect.Width - TextWidth.Of(label);
      if (room > 0) {
        screen.Write(
            rect.X, bottom,
            new StyledLine(label, Theme.Muted).Add(PackageCells.FitMiddle(source, room)),
            rect.Right);
      }
    }
  }

  #endregion

  #region Actions

  private void Run(ModAction action) {
    switch (action) {
      case ModAction.SetUpServer:
        Context.Navigate(PageId.Server, PageRequest.SetUpServer);
        break;
      case ModAction.InstallFromSource:
        PromptInstall();
        break;
      case ModAction.UpdateAll:
        UpdateAll();
        break;
      case ModAction.GetMods:
        Context.Navigate(PageId.GetMods);
        break;
    }
  }

  /// The action menu of the selected package, or of the checked ones when there are any. What
  /// changes the workspace cannot be chosen while its server runs, and says why; the menu
  /// follows the server while it is open.
  private void ShowActions() {
    // What the menu acts on is what the list has now: the menu takes every key until it
    // closes.
    InstalledPackage package = _list.Current;
    InstalledPackage[] targets = _list.Checked.Count > 0 ? [.. _list.Targets] : [];
    Context.Show(new ServerMenu((menu, running) => Actions(menu, running, package, targets)));
  }

  /// The action menu of a package, or of the checked ones when there are any, as it is while
  /// the server of the workspace runs or does not. Each action closes the menu that shows it.
  private Menu Actions(
      ServerMenu shown, bool running, InstalledPackage package, InstalledPackage[] targets) {
    MenuItem Item(string label, Action run, string? reason = null) =>
        new(
            label,
            () => {
              shown.Close();
              run();
            },
            Enabled: reason is null, Reason: reason);
    MenuItem Change(string label, Action run, string? reason = null) =>
        Item(label, run, reason ?? (running ? CommonText.StopServerFirst : null));

    string identity = package.Identity;
    string title;
    List<MenuItem> items;
    if (targets.Length > 0) {
      // In the order of the list. lip update takes the source of each, and lip uninstall its
      // identity.
      string[] sources = [.. targets.Select(p => p.UpdateArgument).Distinct()];
      string[] identities = [.. targets.Select(p => p.Identity)];
      title = ModsText.Checked(targets.Length);
      items = [
        // Of the row that is highlighted, which may be none of the checked ones.
        Item(ModsText.DetailsOf(Cells.BareName(identity)), () => ShowDetails(identity)),
        Change(
            ModsText.UpdateChecked(targets.Length),
            () => Update(sources, ModsText.UpdateChecked(targets.Length), newest: true)),
        Change(
            ModsText.UninstallChecked(targets.Length),
            () => Uninstall(identities, ModsText.UninstallChecked(targets.Length))),
        Item(ModsText.UncheckAll, _list.Checked.Clear),
      ];
    } else {
      title = identity;
      items = [Item(ModsText.Details, () => ShowDetails(identity))];
      // By the source that the state file records, not by the identity, which would name the
      // version tags of a repository: a package of a URL or of a local path has none, and a
      // package of a branch, a tag or a commit would be moved back to them
      // (protocol/lip-update.md). A package of the version tags, or of one version, moves
      // to the highest version that fits. Updating a dependency makes it explicitly
      // installed, which the plan says.
      items.Add(Change(
          ModsText.Update,
          () => Update(
              [package.UpdateArgument], ModsText.UpdateOperation(identity), newest: true)));
      // Only a repository has other versions to name.
      items.Add(Change(
          ModsText.UpdateTo, () => PromptUpdateTo(package),
          package.Repository is null ? ModsText.UpdateToNeedsRepository : null));
      if (package.Explicit) {
        // lip uninstall refuses a dependency: it leaves with what needs it.
        items.Add(Change(
            ModsText.Uninstall,
            () => Uninstall([identity], ModsText.UninstallOperation(identity))));
      }
    }

    return new Menu(title, items);
  }

  /// Shows what the catalogue says of a package, as the Get mods page does. The catalogue is
  /// the one that every page shares (ModCatalogClient), which keeps what it read for a while:
  /// the page keeps none of its own. While it is read, a dialog waits for it; Esc there
  /// abandons the request. Nothing is written.
  private void ShowDetails(string identity) {
    CancellationTokenSource cancel = new();
    // The catalogue is of no workspace. What the client kept answers at once.
    Task<CatalogIndex> read = Context.Catalog.GetIndex(cancel.Token);
    if (read.IsCompletedSuccessfully) {
      cancel.Dispose();
      ShowDetails(read.Result, identity);
      return;
    }

    CatalogueWait wait = new(() => {
      _waiting = null;
      cancel.Cancel();
    });
    _waiting = wait;
    Context.Show(wait);
    Context.Background(
        () => read,
        index => {
          cancel.Dispose();
          if (EndWait(wait)) {
            ShowDetails(index, identity);
          }
        },
        _ => {
          cancel.Dispose();
          if (EndWait(wait)) {
            Context.Message(ModsText.Details, ModsText.CatalogueUnreadable, Theme.Bad);
          }
        },
        anyWorkspace: true);
  }

  /// Closes the dialog that waits for the catalogue; false when the user gave up waiting.
  private bool EndWait(Dialog wait) {
    if (!ReferenceEquals(_waiting, wait)) {
      return false;
    }

    _waiting = null;
    Context.Close(wait);
    return true;
  }

  private void ShowDetails(CatalogIndex index, string identity) {
    if (index.Find(identity) is { } mod) {
      Context.Show(new ModDetailsDialog(mod));
    } else {
      Context.Message(ModsText.Details, ModsText.NotInCatalogue);
    }
  }

  /// Asks how to name what to install: by browsing for a local file or folder, or by typing
  /// package arguments. selected is the way that is highlighted.
  private void PromptInstall(int selected = 0) =>
      Context.Show(new ChoiceDialog(
          ModsText.InstallTitle,
          [
            new ChoiceItem(ModsText.Browse, ModsText.BrowseDetail),
            new ChoiceItem(ModsText.TypeSource, ModsText.TypeSourceDetail),
          ],
          chosen => {
            if (chosen == 0) {
              BrowseInstall();
            } else {
              TypeInstall();
            }
          },
          selected: selected));

  /// Asks for a local .zip file or folder with the file browser, from the workspace, then
  /// installs it. The path is one package argument, whatever characters it has. Esc goes back
  /// to the choice.
  private void BrowseInstall() {
    System.IO.Abstractions.IPath paths = Context.FileSystem.Path;
    WorkspaceList workspaces = Context.Workspaces;
    string workspace = Context.Workspace;
    Context.Show(PathPicker.OpenFile(
        ModsText.InstallLocal, Context.Workspace, [".zip"],
        path => {
          // The title names the file or the folder, not the way to it.
          string name = paths.GetFileName(path);
          Install([path], name.Length > 0 ? name : path);
        },
        cancel: () => PromptInstall(0),
        // The folder that the browser starts at is no package.
        validate: path => workspaces.Same(path, workspace) ? ModsText.IsWorkspace : null,
        orFolder: true));
  }

  /// Asks for the package arguments of lip install, then installs them. Esc goes back to the
  /// choice.
  private void TypeInstall() =>
      Context.Show(new PromptDialog(
          ModsText.InstallTitle,
          [new PromptField(ModsText.Source, "", ModsText.SourcePlaceholder)],
          values => {
            List<string> arguments = SplitArguments(values[0]);
            Install(arguments, string.Join(' ', arguments));
          },
          cancel: () => PromptInstall(1),
          validate: values =>
              SplitArguments(values[0]).Count == 0 ? CommonText.Required : null));

  private void Install(IReadOnlyList<string> arguments, string name) {
    LipClient client = Context.Client;
    // The plan that the user applies confirms what a setup uninstalls.
    Context.PlanAndRun(
        ModsText.InstallOperation(name),
        run => client.Install(
            arguments, run.DryRun, run.Token, run.AcceptEula, confirmed: true, run.Session));
  }

  /// Asks for the ref of lip update package@ref, then updates the package to it: the
  /// repository of its source, with the variant, at the ref. The server may have started since
  /// the menu was made.
  private void PromptUpdateTo(InstalledPackage package) {
    if (package.Repository is not { } repository
        || Context.RefuseWhileServerRuns(ModsText.UpdateToTitle)) {
      return;
    }

    Context.Show(new PromptDialog(
        ModsText.UpdateToTitle,
        [new PromptField(repository + "@", "", ModsText.RefPlaceholder)],
        values => {
          string argument = $"{repository}@{RefOf(values[0])}";
          Update([argument], ModsText.UpdateOperation(argument), newest: false);
        },
        validate: values => RefOf(values[0]) is null ? ModsText.RefInvalid : null));
  }

  /// What follows the @ of a package argument, as the user wrote it with or without the @: a
  /// version, or branch:, tag: or rev: with a name (protocol/lip-package.md, "Sources"); null
  /// when it is none of them.
  private static string? RefOf(string text) {
    string value = text.Trim();
    if (value.StartsWith('@')) {
      value = value[1..];
    }

    return GitRef.TryParse(value, out GitRef? gitRef, out _) ? gitRef.ToString() : null;
  }

  /// Updates every mod that the user installed as Update updates one, which lip update
  /// without operands does not: it keeps a mod that was installed at one version there.
  private void UpdateAll() {
    LipClient client = Context.Client;
    Context.PlanAndRun(
        ModsText.UpdateAll,
        run => Newest(client.UpdateMods(run.DryRun, run.Token, run.Session)),
        _list.Checked.Clear);
  }

  /// Updates the packages that the arguments name. newest: the update is to the newest
  /// version that fits, as Update and Update all mean it, and not to a version or a ref
  /// that the user named.
  private void Update(IReadOnlyList<string> arguments, string title, bool newest) {
    LipClient client = Context.Client;
    Context.PlanAndRun(
        title,
        run => {
          Task<OperationResult> update =
              client.Update(arguments, run.DryRun, run.Token, run.Session);
          return newest ? Newest(update) : update;
        },
        _list.Checked.Clear);
  }

  /// The result of an update to the newest version that fits. The interface asks for it
  /// without the version that a mod was installed at (InstalledPackage.UpdateArgument), so
  /// for a mod that is at its newest version already, lip would do nothing but record that
  /// source in place of the one with the version. That is nothing that the user asked for
  /// or would see, so such an update has nothing to carry out: its plan says that nothing
  /// would change, however often it is asked for, and the record stays as it is. An update
  /// to a version that the user names is carried out as it is, the record included.
  private static async Task<OperationResult> Newest(Task<OperationResult> update) {
    OperationResult result = await update;
    return result.OnlySources ? OperationResult.Unchanged : result;
  }

  private void Uninstall(IReadOnlyList<string> identities, string title) {
    LipClient client = Context.Client;
    Context.PlanAndRun(
        title, run => client.Uninstall(identities, run.DryRun, run.Token, run.Session),
        _list.Checked.Clear);
  }

  /// The words of a line of arguments; a word in double quotes may hold spaces.
  private static List<string> SplitArguments(string text) {
    List<string> words = [];
    System.Text.StringBuilder word = new();
    bool quoted = false;
    bool any = false;
    foreach (char c in text) {
      if (c == '"') {
        quoted = !quoted;
        any = true;
      } else if (char.IsWhiteSpace(c) && !quoted) {
        if (any) {
          words.Add(word.ToString());
          word.Clear();
          any = false;
        }
      } else {
        word.Append(c);
        any = true;
      }
    }

    if (any) {
      words.Add(word.ToString());
    }

    // A pair of quotes with nothing between them names nothing.
    words.RemoveAll(w => w.Length == 0);
    return words;
  }

  #endregion
}
