using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.GetMods;

/// What a lip: link (protocol/lip-uri.md) opens: the package that it names, what the catalogue
/// says about it, and the folder to install it into, which the user chooses here and the link
/// never does. [Install…] carries out lip install of the package as every workspace operation
/// goes, with its plan to apply or cancel; nothing is installed before. Esc, and [Cancel],
/// close the dialog and install nothing.
public sealed class LinkDialog : Dialog {
  private const int InstallButton = 0;
  private const int ChooseButton = 1;
  private const int CancelButton = 2;

  private readonly LipUri _link;
  private readonly TextViewer _viewer = new();
  private bool _started;
  private bool _reading = true;
  private CatalogMod? _mod;
  private CatalogDetail? _detail;
  private string? _catalogError;
  private int _button = -1;
  // Whether the selected workspace is one of the workspace list, and which workspace that was
  // found for: a link starts the interface on a folder that it did not add to the list.
  private string? _checkedFor;
  private bool _listed;

  public LinkDialog(LipUri link) {
    _link = link;
  }

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintSwitchButtons, CommonText.HintSelect, .. _viewer.Hints, CommonText.HintCancel,
  ];

  /// Whether the user may install now: the catalogue was read, or could not be, and a folder
  /// of the workspace list is chosen.
  private bool Ready => !_reading && WorkspaceChosen;

  private bool WorkspaceChosen {
    get {
      if (_checkedFor != Context.Workspace) {
        _checkedFor = Context.Workspace;
        try {
          _listed = Context.Workspaces.Read().Any(w => Context.Workspaces.Same(w, _checkedFor));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
          _listed = false;
        }
      }

      return _listed;
    }
  }

  public override void OnTick() => Start();

  /// Reads the catalogue for the package: index.json, then the file of the mod when the
  /// catalogue lists it.
  private void Start() {
    if (_started) {
      return;
    }

    _started = true;
    ModCatalogClient catalog = Context.Catalog;
    string path = _link.ModulePath;
    Context.Background(
        async () => {
          CatalogIndex index = await catalog.GetIndex();
          CatalogMod? mod = index.Find(path);
          CatalogDetail? detail = null;
          if (mod is not null) {
            try {
              detail = await catalog.GetDetail(mod.Id);
            }
            catch (CatalogUnavailableException) {
              // The entry of index.json says enough to go on.
            }
          }

          return (mod, detail);
        },
        found => {
          (_mod, _detail) = found;
          _reading = false;
        },
        ex => {
          _catalogError = ex.Message;
          _reading = false;
        },
        anyWorkspace: true);
  }

  public override void Draw(ScreenBuffer screen) {
    Start();
    _viewer.Lines = Lines();
    if (_button < 0 && !_reading) {
      // The button that Enter presses at first: none that installs a package the catalogue
      // does not list.
      _button = !WorkspaceChosen ? ChooseButton : _mod is null ? CancelButton : InstallButton;
    }

    string[] buttons = [LinkText.InstallButton, LinkText.ChooseWorkspace, CommonText.Cancel];
    int width = Ui.DialogWidth(screen, 84);
    Rect inner = Ui.DialogFrame(
        screen, LinkText.Title, width, _viewer.RowsFor(width - 4) + 4,
        _mod is null && !_reading ? Theme.Bad : null);
    _viewer.Draw(screen, inner.Shrink(bottom: 2));
    int x = inner.X;
    int selected = Math.Max(0, _button);
    for (int i = 0; i < buttons.Length; i++) {
      Style style = i == selected ? Theme.Selected
          : i == InstallButton && !Ready ? Theme.Muted
          : default;
      x = screen.Write(x, inner.Bottom - 1, $"[ {buttons[i]} ]", style, inner.Right) + 1;
    }
  }

  private List<StyledLine> Lines() {
    List<StyledLine> lines = [new StyledLine(LinkText.Asked, Theme.Warning), new StyledLine()];
    void Row(string label, string value, Style style = default) =>
        lines.Add(new StyledLine().Add(Cells.Pad(label, 10), Theme.Muted).Add(value, style));

    Row(LinkText.Package, _link.PackageArgument, Theme.Strong);
    Row(LinkText.Name, _mod?.Name ?? (_reading ? "…" : LinkText.Unknown));
    Row(LinkText.Author, _mod is { Owner.Length: > 0 } ? _mod.Owner
        : _reading ? "…" : LinkText.Unknown);
    Row(LinkText.Version, _link.Version ?? LinkText.Latest(_detail?.LatestVersion));
    Row(LinkText.Variant, _link.Variant == "" ? GetModsText.DefaultVariant : _link.Variant);
    Row(LinkText.Dependencies, Dependencies());
    if (WorkspaceChosen) {
      Row(LinkText.Workspace, Context.Workspace, Theme.Accent);
    } else {
      Row(LinkText.Workspace, LinkText.NoWorkspace, Theme.Bad);
    }

    lines.Add(new StyledLine());
    if (_reading) {
      lines.Add(new StyledLine(CommonText.Loading, Theme.Muted));
    } else if (_catalogError is { } error) {
      lines.Add(new StyledLine(LinkText.CatalogueUnreadable(error), Theme.Bad));
    } else if (_mod is null) {
      lines.Add(new StyledLine(LinkText.NotListed, Theme.Bad));
    } else {
      lines.Add(new StyledLine(LinkText.Listed, Theme.Good));
      if (_link.Version is { } version && _detail is { Versions.Count: > 0 } detail
          && !detail.Versions.Any(v => v.Version == version)) {
        lines.Add(new StyledLine(LinkText.VersionNotListed(version), Theme.Warning));
      }
    }

    lines.Add(new StyledLine(LinkText.PlanFollows, Theme.Muted));
    return lines;
  }

  /// The packages that the version and the variant need, as the catalogue lists them for a
  /// server; unknown when it does not.
  private string Dependencies() {
    if (_reading) {
      return "…";
    }

    IReadOnlyDictionary<string, string>? needs = null;
    string? version = _link.Version ?? _detail?.LatestVersion;
    if (_detail?.Versions.FirstOrDefault(v => v.Version == version) is { } entry
        && entry.Builds.Count > 0) {
      Dictionary<string, string> all = new(StringComparer.OrdinalIgnoreCase);
      foreach (CatalogBuild build in entry.ServerBuilds(_link.Variant)) {
        foreach ((string id, string range) in build.Dependencies) {
          all.TryAdd(id, range);
        }
      }

      needs = all;
    } else if (_mod is not null && _link.Version is null && _link.Variant == "") {
      needs = _mod.Dependencies;
    }

    return needs is null ? LinkText.Unknown
        : needs.Count == 0 ? LinkText.None
        : string.Join(", ", needs.Select(
            pair => $"{PackageArguments.Abbreviate(pair.Key)} {pair.Value}".TrimEnd()));
  }

  public override void OnKey(ConsoleKeyInfo key) {
    int selected = Math.Max(0, _button);
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        Press(selected);
        return;
      case ConsoleKey.LeftArrow:
        _button = Math.Max(0, selected - 1);
        return;
      case ConsoleKey.RightArrow:
        _button = Math.Min(CancelButton, selected + 1);
        return;
      case ConsoleKey.Tab:
        _button = (selected + (Ui.IsBackTab(key) ? CancelButton : 1)) % (CancelButton + 1);
        return;
    }

    _viewer.OnKey(key);
  }

  private void Press(int button) {
    switch (button) {
      case ChooseButton:
        // The picker opens above this dialog, which shows the folder chosen once it closes.
        Context.ChooseWorkspace();
        return;
      case CancelButton:
        Close();
        return;
    }

    if (_reading) {
      Context.Toast(CommonText.Loading);
      return;
    }

    if (!WorkspaceChosen) {
      Context.Toast(LinkText.NoWorkspace, ToastKind.Bad);
      Context.ChooseWorkspace();
      return;
    }

    string title = GetModsText.InstallTitle(_link.PackageArgument);
    if (Context.RefuseWhileServerRuns(title)) {
      return;
    }

    AppContext context = Context;
    string package = _link.PackageArgument;
    Close();
    // A workspace without a game is offered a server instead, as the Marketplace does.
    LipClient client = context.Client;
    context.RunOperation(
        title,
        async _ => CatalogWorkspace.Of(await client.ListPackages()),
        workspace => {
          if (workspace.Game is null) {
            context.Confirm(
                title, GetModsText.NoServer, CommonText.SetUpServer,
                () => context.Navigate(PageId.Server, PageRequest.SetUpServer));
            return;
          }

          context.PlanAndRun(
              title,
              run => client.Install(
                  [package], run.DryRun, run.Token, run.AcceptEula, confirmed: true,
                  run.Session));
        });
  }
}
