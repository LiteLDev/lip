using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui.Pages.Mods;

/// An action row above the list of the Mods page.
internal enum ModAction {
  NewServer,
  InstallFromSource,
  UpdateAll,
  GetMods,
}

/// The Mods page: the installed packages of the selected workspace, with lip install, lip
/// update and lip uninstall (protocol/lip-install.md, lip-update.md, lip-uninstall.md,
/// lip-list.md). Action rows stand above the list; Enter on a package opens its action menu,
/// which acts on the checked packages when there are any.
public sealed class ModsPage : Page {
  // The rows that the details area takes: a rule, a title, and a sentence of up to two rows.
  private const int DetailRows = 4;

  private readonly ListView<ModAction> _actions;
  private readonly ListView<InstalledPackage> _list;
  // The areas that take the keys: 0 the action rows, 1 the list, when it has rows.
  private readonly FocusRing _focus = new(1);
  // The workspace that the list was read of.
  private string? _workspace;
  private bool _loaded;
  private string? _error;
  // How many times the list was read.
  private int _reads;
  // The columns of the name and of the version of a row, as last drawn.
  private int _nameWidth = 1;
  private int _versionWidth = 1;

  public ModsPage(AppContext context) : base(context) {
    _actions = new ListView<ModAction>(action => new StyledLine(Label(action)));
    // Only what both lip update and lip uninstall take can be checked: neither a dependency,
    // which cannot be uninstalled, nor the game or the loader, which cannot be updated.
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
          : _focus.Count > 1
              ? [CommonText.HintMove, CommonText.HintSelect, CommonText.HintNext]
              : [CommonText.HintMove, CommonText.HintSelect];

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
    if (request == PageRequest.InstallFromSource) {
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
        if (_actions.HasCurrent) {
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
          // The mods that the user installed, then their dependencies, then the server.
          _list.Items = [
            .. packages.OrderBy(p => IsSystem(p) ? 2 : p.Explicit ? 0 : 1),
          ];
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
    if (read && !_list.Items.Any(IsGame)) {
      // A workspace without a game needs one before any mod.
      actions.Add(ModAction.NewServer);
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

  private static string Location(InstalledPackage package) => package.Identity.Split('#')[0];

  /// Whether the package is the game (protocol/lip-setup.md, "Terms").
  private static bool IsGame(InstalledPackage package) =>
      BedrockServer.IsRepository(Location(package));

  /// Whether the package is the loader.
  private static bool IsLoader(InstalledPackage package) =>
      GameSetup.IsLoaderRepository(Location(package));

  /// Whether the package is the game or the loader, which lip setup installs and lip update
  /// refuses.
  private static bool IsSystem(InstalledPackage package) => IsGame(package) || IsLoader(package);

  /// Whether the package is a mod that the user installed.
  private static bool CanCheck(InstalledPackage package) =>
      package.Explicit && !IsSystem(package);

  #endregion

  #region Drawing

  private static string Label(ModAction action) =>
      action switch {
        ModAction.NewServer => CommonText.NewServer,
        ModAction.InstallFromSource => ModsText.InstallFromSource,
        ModAction.UpdateAll => ModsText.UpdateAll,
        _ => CommonText.GetMods,
      };

  private static string Detail(ModAction action) =>
      action switch {
        ModAction.NewServer => ModsText.NewServerDetail,
        ModAction.InstallFromSource => ModsText.InstallFromSourceDetail,
        ModAction.UpdateAll => ModsText.UpdateAllDetail,
        _ => ModsText.GetModsDetail,
      };

  private static string Detail(InstalledPackage package) =>
      IsGame(package) ? ModsText.GameDetail
          : IsLoader(package) ? ModsText.LoaderDetail
          : package.Explicit ? ModsText.ExplicitDetail
          : ModsText.DependencyDetail;

  /// A row of the list: the identity and the version in columns, muted for a dependency.
  private StyledLine Row(InstalledPackage package) {
    Style style = package.Explicit ? default : Theme.Muted;
    string name = TextWidth.Fit(package.Identity, _nameWidth);
    return new StyledLine(name + new string(' ', _nameWidth - TextWidth.Of(name) + 2), style)
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
  /// it.
  private void DrawDetails(ScreenBuffer screen, Rect rect) {
    StyledLine title;
    string detail;
    if (_focus.Index == 0 && _actions.HasCurrent) {
      title = new StyledLine(Label(_actions.Current).TrimEnd('…'), Theme.Strong);
      detail = Detail(_actions.Current);
    } else if (_focus.Index == 1 && _list.HasCurrent) {
      InstalledPackage package = _list.Current;
      int room = Math.Max(1, rect.Width - TextWidth.Of(package.Version) - 2);
      title = new StyledLine(TextWidth.Fit(package.Identity, room), Theme.Strong)
          .Add("  " + package.Version);
      detail = Detail(package);
    } else {
      return;
    }

    screen.Write(rect.X, rect.Y, new string('─', rect.Width), Theme.Border);
    screen.Write(rect.X, rect.Y + 1, title, rect.Right);
    List<string> lines = TextWidth.Wrap(detail, rect.Width);
    for (int i = 0; i < lines.Count && rect.Y + 2 + i < rect.Bottom; i++) {
      screen.Write(rect.X, rect.Y + 2 + i, lines[i], Theme.Muted, rect.Right);
    }
  }

  #endregion

  #region Actions

  private void Run(ModAction action) {
    switch (action) {
      case ModAction.NewServer:
        Context.Navigate(PageId.Workspaces, PageRequest.NewServer);
        break;
      case ModAction.InstallFromSource:
        PromptInstall();
        break;
      case ModAction.UpdateAll:
        Update([], ModsText.UpdateAll);
        break;
      case ModAction.GetMods:
        Context.Navigate(PageId.GetMods);
        break;
    }
  }

  /// The action menu of the selected package, or of the checked ones when there are any.
  private void ShowActions() {
    if (_list.Checked.Count > 0) {
      // In the order of the list.
      string[] identities = [.. _list.Targets.Select(p => p.Identity)];
      Context.ShowMenu(
          ModsText.Checked(identities.Length),
          new MenuItem(
              ModsText.UpdateChecked(identities.Length),
              () => Update(identities, ModsText.UpdateChecked(identities.Length))),
          new MenuItem(
              ModsText.UninstallChecked(identities.Length),
              () => Uninstall(identities, ModsText.UninstallChecked(identities.Length))),
          new MenuItem(ModsText.UncheckAll, _list.Checked.Clear));
      return;
    }

    InstalledPackage package = _list.Current;
    string identity = package.Identity;
    List<MenuItem> items = [];
    if (IsSystem(package)) {
      // lip update refuses the game and the loader: lip setup changes them.
      items.Add(new MenuItem(ModsText.GoToWorkspaces, () => Context.Navigate(PageId.Workspaces)));
    } else {
      // Updating a dependency makes it explicitly installed, which the plan says.
      items.Add(new MenuItem(
          ModsText.Update, () => Update([identity], ModsText.UpdateOperation(identity))));
      items.Add(new MenuItem(ModsText.UpdateTo, () => PromptUpdateTo(identity)));
      if (package.Explicit) {
        // lip uninstall refuses a dependency: it leaves with what needs it.
        items.Add(new MenuItem(
            ModsText.Uninstall,
            () => Uninstall([identity], ModsText.UninstallOperation(identity))));
      }
    }

    Context.ShowMenu(identity, [.. items]);
  }

  /// Asks for the package arguments of lip install, then installs them.
  private void PromptInstall() =>
      Context.Show(new PromptDialog(
          ModsText.InstallTitle,
          [new PromptField(ModsText.Source, "", ModsText.SourcePlaceholder)],
          values => {
            List<string> arguments = SplitArguments(values[0]);
            LipClient client = Context.Client;
            // The plan that the user applies confirms what a setup uninstalls.
            Context.PlanAndRun(
                ModsText.InstallOperation(string.Join(' ', arguments)),
                run => client.Install(
                    arguments, run.DryRun, run.Token, run.AcceptEula, confirmed: true,
                    run.Session));
          },
          validate: values =>
              SplitArguments(values[0]).Count == 0 ? CommonText.Required : null));

  /// Asks for the ref of lip update package@ref, then updates the package to it.
  private void PromptUpdateTo(string identity) =>
      Context.Show(new PromptDialog(
          ModsText.UpdateToTitle,
          [new PromptField(identity + "@", "", ModsText.RefPlaceholder)],
          values => {
            string argument = $"{identity}@{RefOf(values[0])}";
            Update([argument], ModsText.UpdateOperation(argument));
          },
          validate: values => RefOf(values[0]) is null ? ModsText.RefInvalid : null));

  /// What follows the @ of a package argument, as the user wrote it with or without the @: a
  /// version, or branch:, tag: or rev: with a name (protocol/lip-package.md, "Sources"); null
  /// when it is none of them.
  private static string? RefOf(string text) {
    string value = text.Trim();
    if (value.StartsWith('@')) {
      value = value[1..];
    }

    if (value.Any(char.IsWhiteSpace)) {
      return null;
    }

    foreach (string kind in (string[])["branch:", "tag:", "rev:"]) {
      if (value.StartsWith(kind, StringComparison.Ordinal)) {
        return value.Length > kind.Length ? value : null;
      }
    }

    return SemVersion.TryParse(value, SemVersionStyles.Strict, out _) ? value : null;
  }

  private void Update(IReadOnlyList<string> arguments, string title) {
    LipClient client = Context.Client;
    Context.PlanAndRun(
        title, run => client.Update(arguments, run.DryRun, run.Token, run.Session),
        _list.Checked.Clear);
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
