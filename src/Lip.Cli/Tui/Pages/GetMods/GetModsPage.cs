using System.Globalization;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.GetMods;

/// The Get mods page: the mods of the catalogue (protocol/lip-tui.md, "Catalogue") that have a
/// build for a server. From top to bottom: the search, the order, the filters; the mods; and
/// the details of the selected mod, at the right of the mods where the page is wide enough.
/// Enter on a mod offers to install it, to choose a version, to read its README and to open
/// its page in the browser.
public sealed class GetModsPage : Page {
  /// The areas of the page that take the keys.
  private enum Area {
    Controls,
    List,
    Details,
  }

  /// The row of the filters among the controls: it says how many are chosen, and Enter opens
  /// the filter panel.
  private sealed class FiltersRow(Func<string> label, CatalogFilters filters)
      : FormField(label) {
    public override IReadOnlyList<Hint> Hints => [CommonText.HintOpen];

    public override bool OnKey(ConsoleKeyInfo key) => false;

    public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
        screen.WriteFit(
            x, y, width,
            filters.Count == 0 ? GetModsText.None : GetModsText.FiltersActive(filters.Count),
            focused ? Theme.Accent : filters.Count == 0 ? default : Theme.Good);
  }

  /// What reading the file of a mod for an action of the user gave: the file, or why it could
  /// not be read, or neither when the user cancelled.
  private sealed record DetailRead(CatalogDetail? Detail, string? Error);

  // The fields of the controls.
  private const int SearchField = 0;
  private const int FiltersField = 2;

  // How long the selection rests on a mod before its file is read, in milliseconds.
  private const int Rest = 300;

  // After how long the catalogue is read again when the page is shown, in milliseconds.
  private const int Stale = 10 * 60 * 1000;

  private readonly CatalogFilters _filters = new();
  private readonly TextRow _search;
  private readonly ChoiceRow _sort;
  private readonly Form _form;
  private readonly ListView<CatalogMod> _list;
  // The files of the mods: those read, those being read, and those that could not be read
  // since the selection last moved.
  private readonly Dictionary<string, CatalogDetail> _details = new(StringComparer.Ordinal);
  private readonly Dictionary<string, Task<CatalogDetail>> _detailReads =
      new(StringComparer.Ordinal);
  private readonly HashSet<string> _detailsFailed = new(StringComparer.Ordinal);
  private HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);
  private string? _installedOf;
  private int _installedRead;
  private CatalogIndex? _catalog;
  // How many times the catalogue was read: what is read of an earlier one is dropped.
  private int _generation;
  private string? _error;
  private bool _loading;
  private long _loadedAt;
  private Area _area = Area.List;
  private long _restedSince;
  private int _rowWidth = 1;
  private bool _wide;
  // The lines of the details that are shown, what they were made from, the first of them that
  // is shown, and how many fit.
  private List<StyledLine> _detailLines = [];
  private (string Id, int Width, bool Wide, string Latest, Language Language)? _detailLinesOf;
  private int _detailTop;
  private int _detailRows = 1;

  public GetModsPage(AppContext context) : base(context) {
    _search = new TextRow(() => GetModsText.Search, "", () => GetModsText.SearchExample) {
      Changed = () => SelectMods(keep: false),
    };
    _sort = new ChoiceRow(
        () => GetModsText.Sort,
        [.. Enum.GetValues<CatalogSort>().Select(
            sort => (Func<string>)(() => GetModsText.SortName(sort)))]) {
      Changed = () => SelectMods(keep: true),
    };
    _form = new Form(_search, _sort, new FiltersRow(() => GetModsText.Filters, _filters));
    _list = new ListView<CatalogMod>(Row) { Empty = () => GetModsText.NoMatch };
  }

  public override string Title => GetModsText.Title;

  public override IReadOnlyList<Hint> Hints {
    get {
      if (_catalog is null) {
        return _error is null ? [] : [GetModsText.HintRetry];
      }

      return _area switch {
        Area.Controls => [
          .. _form.Hints,
          .. _form.Focus == SearchField ? [GetModsText.HintResults] : (Hint[])[],
          CommonText.HintNext,
        ],
        Area.List => _list.HasCurrent
            ? [.. _list.Hints, CommonText.HintActions, CommonText.HintNext]
            : [.. _list.Hints, CommonText.HintNext],
        _ => [CommonText.HintScroll, CommonText.HintPage],
      };
    }
  }

  /// Whether the details have more rows than fit: they take the focus only then.
  private bool DetailsScroll => _detailLines.Count > _detailRows;

  public override void OnShown() {
    if (_catalog is null || Environment.TickCount64 - _loadedAt > Stale) {
      Load();
    }

    LoadInstalled();
  }

  public override void OnWorkspaceChanged() => LoadInstalled();

  /// Reads the file of the selected mod once the selection rested on it for a moment.
  public override void OnTick() {
    if (Context.CurrentPage != PageId.GetMods || !_list.HasCurrent
        || Environment.TickCount64 - _restedSince <= Rest) {
      return;
    }

    string id = _list.Current.Id;
    if (!_details.ContainsKey(id) && !_detailsFailed.Contains(id)) {
      ReadDetail(id);
    }
  }

  #region Reading

  /// Reads the catalogue, unless it is being read. A catalogue that was read before stays
  /// shown meanwhile, and when it cannot be read again.
  private void Load() {
    if (_loading) {
      return;
    }

    _loading = true;
    _error = null;
    ModCatalogClient catalog = Context.Catalog;
    _ = Task.Run(async () => {
      try {
        CatalogIndex index = await catalog.GetIndex();
        Context.Post(() => {
          _loading = false;
          _catalog = index;
          _loadedAt = Environment.TickCount64;
          _generation++;
          _details.Clear();
          _detailReads.Clear();
          _detailsFailed.Clear();
          _filters.Build(index);
          SelectMods(keep: true);
        });
      }
      catch (Exception ex) {
        Context.Post(() => {
          _loading = false;
          if (_catalog is null) {
            _error = ex.Message;
          }
        });
      }
    });
  }

  /// Reads which packages the selected workspace has, for the marks of the mods. The marks of
  /// another workspace are not shown meanwhile.
  private void LoadInstalled() {
    if (_installedOf != Context.Workspace) {
      _installedOf = Context.Workspace;
      _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    int read = ++_installedRead;
    LipClient client = Context.Client;
    Context.Background(
        () => client.ListPackages(),
        packages => {
          if (read == _installedRead) {
            _installed = new HashSet<string>(
                packages.Select(package => package.Identity.Split('#')[0]),
                StringComparer.OrdinalIgnoreCase);
          }
        },
        _ => {
          if (read == _installedRead) {
            _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          }
        });
  }

  /// Reads the file of a mod of the catalogue that is shown, unless it is being read; the page
  /// keeps it once it is read. The task is for an action that waits for the file.
  private Task<CatalogDetail> ReadDetail(string id) {
    if (_detailReads.TryGetValue(id, out Task<CatalogDetail>? reading)) {
      return reading;
    }

    ModCatalogClient catalog = Context.Catalog;
    int generation = _generation;
    Task<CatalogDetail> read = Task.Run(() => catalog.GetDetail(id));
    _detailReads[id] = read;
    _ = read.ContinueWith(
        done => {
          CatalogDetail? detail = done.Exception is null && !done.IsCanceled ? done.Result : null;
          Context.Post(() => {
            if (generation != _generation) {
              return;
            }

            _detailReads.Remove(id);
            if (detail is null) {
              _detailsFailed.Add(id);
            } else {
              _details[id] = detail;
              _detailsFailed.Remove(id);
            }
          });
        },
        TaskScheduler.Default);
    return read;
  }

  /// Carries out an action of the user that needs the file of a mod: at once when the file was
  /// read, otherwise once it is, behind a dialog that Esc cancels. then gets the file, or
  /// null and why when it cannot be read.
  private void WithDetail(CatalogMod mod, Action<CatalogDetail?, string?> then) {
    if (_details.TryGetValue(mod.Id, out CatalogDetail? known)) {
      then(known, null);
      return;
    }

    Task<CatalogDetail> read = ReadDetail(mod.Id);
    Context.RunOperation(
        mod.Name,
        async token => {
          try {
            return new DetailRead(await read.WaitAsync(token), null);
          }
          catch (OperationCanceledException) when (token.IsCancellationRequested) {
            return new DetailRead(null, null);
          }
          catch (CatalogUnavailableException ex) {
            return new DetailRead(null, ex.Message);
          }
        },
        result => {
          if (result.Detail is not null || result.Error is not null) {
            then(result.Detail, result.Error);
          }
        },
        cancellable: true);
  }

  /// The mods that the search and the filters select, in the chosen order. keep: the selected
  /// mod stays selected when it is still among them; otherwise the first one is.
  private void SelectMods(bool keep) {
    if (_catalog is null) {
      return;
    }

    string? before = _list.HasCurrent ? _list.Current.Id : null;
    _list.Items = CatalogQuery.Select(
        _catalog, _search.Value, _filters.ToFilter(), (CatalogSort)_sort.Selected);
    if (!keep || before is null || !_list.Select(mod => mod.Id == before)) {
      _list.Selected = 0;
    }

    if ((_list.HasCurrent ? _list.Current.Id : null) != before) {
      Rested();
    }
  }

  /// Another mod is selected: its details are shown from their start, and its file is read
  /// once the selection rests, again when it could not be read before.
  private void Rested() {
    _restedSince = Environment.TickCount64;
    _detailTop = 0;
    _detailsFailed.Clear();
  }

  #endregion

  #region Drawing

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (_catalog is null) {
      DrawUnavailable(screen, area, focused);
      return;
    }

    _form.Draw(
        screen, new Rect(area.X, area.Y, area.Width, Math.Min(3, area.Height)),
        focused && _area == Area.Controls);
    Rect body = area.Shrink(top: 3);
    if (body.Height < 2) {
      return;
    }

    // The details are at the right of the mods where there is room, and below them otherwise.
    _wide = body.Width >= 84;
    Rect list;
    Rect details;
    if (_wide) {
      int left = body.Width * 11 / 20;
      list = new Rect(body.X, body.Y, left, body.Height);
      details = new Rect(body.X + left + 2, body.Y, body.Width - left - 2, body.Height);
    } else {
      int below = body.Height >= 7 ? Math.Clamp((body.Height - 2) / 2, 2, 8) + 1 : 0;
      list = new Rect(body.X, body.Y, body.Width, body.Height - below);
      details = new Rect(body.X, list.Bottom, body.Width, below);
    }

    Rule(
        screen, list, $"{GetModsText.Count(_list.Items.Count)} · {GetModsText.Installed}",
        focused && _area == Area.List);
    // The last column is that of the marks of rows above and below.
    _rowWidth = Math.Max(1, list.Width - 1);
    _list.Draw(screen, list.Shrink(top: 1), focused && _area == Area.List);

    CatalogMod? mod = _list.HasCurrent ? _list.Current : null;
    if (details.Height >= 2) {
      Rule(screen, details, mod?.Name ?? GetModsText.Details, focused && _area == Area.Details);
    }

    DrawDetails(screen, details.Shrink(top: 1), mod);
    if (_area == Area.Details && !DetailsScroll) {
      _area = Area.List;
    }
  }

  /// The details of a mod, from the first line that is shown, with marks where lines are
  /// hidden above or below.
  private void DrawDetails(ScreenBuffer screen, Rect rect, CatalogMod? mod) {
    _detailRows = Math.Max(1, rect.Height);
    if (mod is null || rect.Height < 1 || rect.Width < 2) {
      _detailLines = [];
      _detailLinesOf = null;
      return;
    }

    // The lines are made again only when what they say changed.
    (string, int, bool, string, Language) of =
        (mod.Id, rect.Width - 1, _wide, Latest(mod), Context.Language);
    if (_detailLinesOf != of) {
      _detailLinesOf = of;
      _detailLines = DetailLines(mod, rect.Width - 1);
    }

    _detailTop = Math.Clamp(_detailTop, 0, Math.Max(0, _detailLines.Count - _detailRows));
    for (int i = 0; i < _detailRows && _detailTop + i < _detailLines.Count; i++) {
      screen.Write(rect.X, rect.Y + i, _detailLines[_detailTop + i], rect.Right);
    }

    Ui.ScrollMarks(
        screen, rect, _detailTop > 0, _detailTop + _detailRows < _detailLines.Count);
  }

  /// While the catalogue is read, that it is; when it cannot be read, a short message, why, and
  /// a button that reads it again.
  private void DrawUnavailable(ScreenBuffer screen, Rect area, bool focused) {
    if (_error is null) {
      screen.WriteFit(area.X, area.Y, area.Width, CommonText.Loading, Theme.Muted);
      return;
    }

    int y = area.Y;
    screen.WriteFit(area.X, y++, area.Width, GetModsText.Unavailable, Theme.Bad);
    int most = Math.Max(0, area.Height - 4);
    foreach (string line in TextWidth.Wrap(_error, area.Width).Take(most)) {
      screen.Write(area.X, y++, line, Theme.Muted, area.Right);
    }

    screen.WriteFit(
        area.X, y + 1, area.Width, $"[ {CommonText.Retry} ]",
        focused ? Theme.Selected : Theme.SelectedInactive);
  }

  /// A line across the top row of rect with a title in it, which stands out while what is
  /// below it has the focus.
  private static void Rule(ScreenBuffer screen, Rect rect, string title, bool focused) {
    if (rect.Width < 6 || rect.Height < 1) {
      return;
    }

    screen.Write(rect.X, rect.Y, new string('─', rect.Width), Theme.Border);
    screen.WriteFit(
        rect.X + 1, rect.Y, rect.Width - 2, $" {title} ", focused ? Theme.Accent : Theme.Strong);
  }

  /// A mod as a row of the list: a mark when it is installed, its name and its author, its
  /// stars, and when it was updated where there is room.
  private StyledLine Row(CatalogMod mod) {
    bool dated = _rowWidth >= 44;
    string stars = string.Create(CultureInfo.InvariantCulture, $"★ {mod.Stars}");
    int room = Math.Max(4, _rowWidth - 2 - 8 - (dated ? 12 : 0));
    string name = TextWidth.Fit(mod.Name, room);
    int rest = room - TextWidth.Of(name);
    string owner = rest > 5 && mod.Owner.Length > 0 ? TextWidth.Fit(" · " + mod.Owner, rest) : "";
    StyledLine line = new StyledLine()
        .Add(_installed.Contains(mod.Id) ? "✓ " : "  ", Theme.Good)
        .Add(name)
        .Add(Cells.Pad(owner, rest), Theme.Muted)
        .Add(new string(' ', Math.Max(0, 8 - TextWidth.Of(stars))) + stars, Theme.Warning);
    return dated ? line.Add("  " + Cells.Date(mod.Updated)) : line;
  }

  /// The latest version of a mod, with its variant when that is not the default one, as its
  /// file says; a dash when the file says none or cannot be read.
  private string Latest(CatalogMod mod) =>
      _details.TryGetValue(mod.Id, out CatalogDetail? detail)
          ? (detail.LatestVersion ?? "—")
              + (detail.LatestVariant is { Length: > 0 } variant ? $" #{variant}" : "")
          : _detailsFailed.Contains(mod.Id) ? "—" : CommonText.Loading;

  /// The details of a mod as lines of at most width columns: its description, then its fields,
  /// the names in a column.
  private List<StyledLine> DetailLines(CatalogMod mod, int width) {
    width = Math.Max(8, width);
    List<StyledLine> lines = [];
    if (mod.Description.Length > 0) {
      lines.AddRange(TextWidth.Wrap(mod.Description, width).Select(row => new StyledLine(row)));
      // Below the mods there are too few rows for an empty one.
      if (_wide) {
        lines.Add(new StyledLine());
      }
    }

    IEnumerable<string> tags = CatalogQuery.DisplayTags(mod)
        .Select(tag => GetModsText.Tag(tag, CatalogQuery.Label(tag)));
    List<(string Name, string Value, Style Style)> fields = [
      (GetModsText.Author, mod.Owner, Theme.Accent),
      (GetModsText.Stars, mod.Stars.ToString(CultureInfo.InvariantCulture), Theme.Warning),
      (GetModsText.Id, mod.Id, default),
      (GetModsText.Latest, Latest(mod), default),
      (GetModsText.Platform, string.Join(", ", mod.Platforms), default),
      (
        GetModsText.Game,
        string.Join(", ", mod.GameVersions.Take(3)) + (mod.GameVersions.Count > 3 ? ", …" : ""),
        default),
      (GetModsText.Tags, string.Join(", ", tags), Theme.Tag),
      (GetModsText.License, CatalogQuery.LicenseOf(mod) ?? GetModsText.None, default),
      (
        GetModsText.Depends,
        string.Join(", ", mod.Dependencies.Select(d => $"{Cells.BareName(d.Key)} {d.Value}")),
        default),
      (GetModsText.Updated, Cells.Date(mod.Updated), default),
    ];
    int names = fields.Max(field => TextWidth.Of(field.Name)) + 2;
    foreach ((string name, string value, Style style) in fields.Where(f => f.Value.Length > 0)) {
      List<string> rows = TextWidth.Wrap(value, Math.Max(4, width - names));
      for (int i = 0; i < rows.Count; i++) {
        lines.Add(new StyledLine()
            .Add(Cells.Pad(i == 0 ? name : "", names), Theme.Muted)
            .Add(rows[i], style));
      }
    }

    return lines;
  }

  #endregion

  #region Keys

  public override bool OnKey(ConsoleKeyInfo key) {
    if (_catalog is null) {
      if (_error is not null && key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar) {
        Load();
        return true;
      }

      return false;
    }

    return _area switch {
      Area.Controls => ControlsKey(key),
      Area.List => ListKey(key),
      _ => DetailsKey(key),
    };
  }

  /// The search, the order and the filters: ↑ and ↓ go from one to the next, and on to the
  /// mods; Enter opens the filters, and goes from the search to the mods it found.
  private bool ControlsKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Enter) {
      if (_form.Focus == FiltersField) {
        Context.Show(new FilterDialog(
            _filters, () => _list.Items.Count, () => SelectMods(keep: true)));
        return true;
      }

      if (_form.Focus == SearchField) {
        _area = Area.List;
        return true;
      }

      return false;
    }

    if (_form.OnKey(key)) {
      return true;
    }

    if (key.Key == ConsoleKey.DownArrow || (key.Key == ConsoleKey.Tab && !Ui.IsBackTab(key))) {
      _area = Area.List;
      return true;
    }

    return false;
  }

  /// The mods: Enter shows the actions of the selected one; ↑ on the first goes to the
  /// controls; → goes to the details at the right, and ↓ on the last to the details below.
  private bool ListKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Enter) {
      if (_list.HasCurrent) {
        ShowActions(_list.Current);
      }

      return true;
    }

    if (key.Key == ConsoleKey.Tab) {
      return Ui.IsBackTab(key) ? ToControls() : ToDetails();
    }

    int before = _list.Selected;
    if (_list.OnKey(key)) {
      if (before != _list.Selected) {
        Rested();
      }

      return true;
    }

    return key.Key switch {
      ConsoleKey.UpArrow => ToControls(),
      ConsoleKey.RightArrow when _wide => ToDetails(),
      ConsoleKey.DownArrow when !_wide => ToDetails(),
      _ => false,
    };
  }

  /// The details: they scroll; Esc, ← and Shift+Tab go back to the mods, and so does ↑ at
  /// their start where they are below the mods.
  private bool DetailsKey(ConsoleKeyInfo key) {
    if (key.Key is ConsoleKey.Escape or ConsoleKey.LeftArrow || Ui.IsBackTab(key)
        || (key.Key == ConsoleKey.UpArrow && _detailTop == 0 && !_wide)) {
      _area = Area.List;
      return true;
    }

    int last = Math.Max(0, _detailLines.Count - _detailRows);
    int? top = key.Key switch {
      ConsoleKey.UpArrow => _detailTop - 1,
      ConsoleKey.DownArrow => _detailTop + 1,
      ConsoleKey.PageUp => _detailTop - _detailRows,
      ConsoleKey.PageDown => _detailTop + _detailRows,
      ConsoleKey.Home => 0,
      ConsoleKey.End => last,
      _ => null,
    };
    if (top is { } moved) {
      _detailTop = Math.Clamp(moved, 0, last);
      return true;
    }

    if (key.Key == ConsoleKey.Tab) {
      // On to the sidebar; the page is entered at the mods again.
      _area = Area.List;
    }

    return false;
  }

  private bool ToControls() {
    _area = Area.Controls;
    _form.Focus = FiltersField;
    return true;
  }

  /// The details take the focus only while they have more rows than fit.
  private bool ToDetails() {
    if (!_list.HasCurrent || !DetailsScroll) {
      return false;
    }

    _area = Area.Details;
    return true;
  }

  #endregion

  #region Actions

  private void ShowActions(CatalogMod mod) =>
      Context.ShowMenu(
          mod.Name,
          new MenuItem(GetModsText.InstallLatest, () => InstallLatest(mod)),
          new MenuItem(GetModsText.ChooseVersion, () => ChooseVersion(mod)),
          new MenuItem(GetModsText.Readme, () => ShowReadme(mod)),
          new MenuItem(
              GetModsText.OpenInBrowser, () => Context.OpenUrl(mod.Url),
              Enabled: mod.Url.Length > 0));

  /// Installs the latest version, in the variant that the catalogue names as the latest: the
  /// default one when the file of the mod cannot be read.
  private void InstallLatest(CatalogMod mod) =>
      WithDetail(mod, (detail, _) => Install(
          mod.Id + (detail?.LatestVariant is { Length: > 0 } variant ? "#" + variant : "")));

  /// Asks for the variant and the version. Without the file of the mod there is no version to
  /// choose from, and the user can still write one.
  private void ChooseVersion(CatalogMod mod) =>
      WithDetail(mod, (detail, _) => Context.Show(new VersionDialog(mod, detail, Install)));

  private void ShowReadme(CatalogMod mod) =>
      WithDetail(mod, (detail, error) => {
        if (detail is null) {
          Context.Toast(error ?? GetModsText.Unavailable, ToastKind.Bad);
        } else {
          Context.Show(new ViewerDialog(mod.Name, detail.Readme ?? GetModsText.NoReadme));
        }
      });

  /// lip install of a package argument, as every workspace operation goes.
  private void Install(string package) {
    LipClient client = Context.Client;
    Context.PlanAndRun(
        GetModsText.InstallTitle(package),
        (dryRun, acceptEula, token) =>
            client.Install([package], dryRun, token, acceptEula, confirmed: true));
  }

  #endregion
}
