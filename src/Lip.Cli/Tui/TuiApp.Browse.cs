using System.Diagnostics;
using System.Globalization;
using Lip.Core.Catalog;

namespace Lip.Cli.Tui;

/// The Browse tab: the mods of the catalogue (protocol/lip-tui.md, "Catalogue") that have a
/// build for a server, the only side lip manages (protocol/lip.md, "Environment").
public sealed partial class TuiApp {
  private const string Side = "server";

  private enum FilterKind {
    Heading,
    GameVersion,
    Tag,
  }

  /// A row of the Filters pane: a heading, or a game version or a tag that mods are filtered by,
  /// with the number of mods that have it.
  private sealed record FilterRow(FilterKind Kind, string Key, string Label, int Count);

  private readonly TextField _search = new();
  private readonly Dictionary<string, CatalogDetail> _details = new(StringComparer.Ordinal);
  private readonly HashSet<string> _detailsRequested = new(StringComparer.Ordinal);
  private readonly HashSet<string> _includeTags = new(StringComparer.Ordinal);
  private readonly HashSet<string> _excludeTags = new(StringComparer.Ordinal);
  private readonly HashSet<string> _gameVersions = new(StringComparer.Ordinal);
  private CatalogIndex? _catalog;
  private string? _catalogError;
  private bool _catalogLoading;
  private bool _searching;
  private bool _filtering;
  private CatalogSort _sort = CatalogSort.Relevance;
  private List<FilterRow> _filterRows = [];
  private int _filterSelected;
  private int _filterTop;
  private IReadOnlyList<CatalogMod> _mods = [];
  private int _modSelected;
  private int _modTop;
  private long _modSelectedAt;

  private int FilterCount => _includeTags.Count + _excludeTags.Count + _gameVersions.Count;

  private (string, string)[] BrowseHints() {
    if (_searching) {
      return [("Enter", "done"), ("Esc", "clear"), ("Ctrl+U", "erase")];
    }

    return _filtering
        ? [
          ("Space", "include"), ("x", "exclude"), ("c", "clear all"), ("↑/↓", "select"),
          ("Esc", "back to the mods"),
        ]
        : [
          ("/", "search"), ("f", "filters"), ("s", "sort"), ("Enter", "install"),
          ("r", "readme"), ("o", "open"), ("R", "reload"),
        ];
  }

  private CatalogMod? SelectedMod => _modSelected < _mods.Count ? _mods[_modSelected] : null;

  private void LoadCatalog() {
    if (_catalogLoading) {
      return;
    }

    _catalogLoading = true;
    _catalogError = null;
    _ = Task.Run(async () => {
      try {
        CatalogIndex index = await _catalogClient.GetIndex();
        Post(() => {
          _catalog = index;
          _catalogLoading = false;
          BuildFilterRows(index);
          SelectMods();
        });
      }
      catch (Exception ex) {
        Post(() => {
          _catalogLoading = false;
          _catalogError = ex.Message;
        });
      }
    });
  }

  /// The game versions, newest first, then the featured tags in the order of levimc.org/mods,
  /// then the other tags, the most used first: each with the number of mods for a server that
  /// have it, and only when there is one.
  private void BuildFilterRows(CatalogIndex index) {
    List<CatalogMod> mods = [.. index.Mods.Where(mod => mod.RunsOn(Side))];
    Dictionary<string, int> versions = new(StringComparer.Ordinal);
    Dictionary<string, int> tags = new(StringComparer.Ordinal);
    foreach (CatalogMod mod in mods) {
      foreach (string version in mod.GameVersions.Distinct(StringComparer.Ordinal)) {
        versions[version] = versions.GetValueOrDefault(version) + 1;
      }

      foreach (string tag in CatalogQuery.DisplayTags(mod)) {
        tags[tag] = tags.GetValueOrDefault(tag) + 1;
      }
    }

    List<FilterRow> rows = [];
    void Group(string heading, IEnumerable<FilterRow> items) {
      List<FilterRow> group = [.. items];
      if (group.Count > 0) {
        rows.Add(new FilterRow(FilterKind.Heading, "", heading, 0));
        rows.AddRange(group);
      }
    }

    Group("Game version", versions
        .OrderByDescending(v => VersionKey(v.Key), StringComparer.Ordinal)
        .Select(v => new FilterRow(FilterKind.GameVersion, v.Key, v.Key, v.Value)));
    Group("Tags", CatalogQuery.FeaturedTags
        .Where(tag => tags.ContainsKey(tag.Id))
        .Select(tag => new FilterRow(FilterKind.Tag, tag.Id, tag.Label, tags[tag.Id])));
    Group("Other tags", tags
        .Where(tag => !CatalogQuery.IsFeatured(tag.Key))
        .OrderByDescending(tag => tag.Value).ThenBy(tag => tag.Key, StringComparer.Ordinal)
        .Select(tag => new FilterRow(FilterKind.Tag, tag.Key, tag.Key, tag.Value)));
    _filterRows = rows;
    _filterSelected = Math.Min(1, Math.Max(0, rows.Count - 1));
    _filterTop = 0;
    _includeTags.IntersectWith(tags.Keys);
    _excludeTags.IntersectWith(tags.Keys);
    _gameVersions.IntersectWith(versions.Keys);
  }

  /// A version so that versions compare as their numbers do: each number padded.
  private static string VersionKey(string version) =>
      string.Join('.', version.Split('.').Select(part => part.PadLeft(6, '0')));

  private void SelectMods() {
    if (_catalog is null) {
      return;
    }

    string? keep = SelectedMod?.Id;
    _mods = CatalogQuery.Select(
        _catalog, _search.Value,
        new CatalogFilter(Side, _includeTags, _excludeTags, _gameVersions), _sort);
    int kept = keep is null ? -1 : _mods.ToList().FindIndex(mod => mod.Id == keep);
    _modSelected = Math.Max(0, kept);
    _modSelectedAt = Environment.TickCount64;
  }

  /// Reads the file of the selected mod once the selection rested on it for a moment.
  private void BrowseTick() {
    if (_tab == BrowseTab && SelectedMod is { } mod
        && Environment.TickCount64 - _modSelectedAt > 300) {
      LoadDetail(mod.Id, null);
    }
  }

  /// Reads the file of a mod, once; then calls loaded, when given, also when it was read
  /// before, or failed when it cannot be read.
  private void LoadDetail(string id, Action<CatalogDetail>? loaded, Action? failed = null) {
    if (_details.TryGetValue(id, out CatalogDetail? known)) {
      loaded?.Invoke(known);
      return;
    }

    if (loaded is null && !_detailsRequested.Add(id)) {
      return;
    }

    _ = Task.Run(async () => {
      try {
        CatalogDetail detail = await _catalogClient.GetDetail(id);
        Post(() => {
          _details[id] = detail;
          loaded?.Invoke(detail);
        });
      }
      catch (Exception ex) {
        Post(() => {
          if (loaded is not null) {
            _status = new StyledLine(ex.Message, Theme.Bad);
          }

          failed?.Invoke();
        });
      }
    });
  }

  private void DrawBrowse(Rect body) {
    // A narrow terminal shows the filters only while they are used, in place of the details.
    bool wide = body.Width >= 100;
    int filtersWidth = wide || _filtering ? 30 : 0;
    int detailsWidth = wide || !_filtering ? Math.Max(30, (body.Width - filtersWidth) * 2 / 5) : 0;
    int listWidth = body.Width - filtersWidth - detailsWidth;
    if (filtersWidth > 0) {
      DrawFilters(Panel(
          _screen, new Rect(body.X, body.Y, filtersWidth, body.Height),
          FilterCount == 0 ? "Filters" : $"Filters ({FilterCount})", focused: _filtering));
    }

    string filters = FilterCount == 0 ? "" : $" · {FilterCount} filters";
    Rect list = Panel(
        _screen, new Rect(body.X + filtersWidth, body.Y, listWidth, body.Height),
        _catalog is null ? "Mods" : $"Mods — {_mods.Count} · sort: {_sort}{filters}",
        focused: !_filtering);

    int x = _screen.Write(list.X, list.Y, "Search ", Theme.Muted);
    _search.Draw(
        _screen, x, list.Y, list.Right - x, _searching, "/ name, id, author, tag, description");
    if (_catalog is null) {
      string message = _catalogError
          ?? (_catalogLoading ? "Reading the catalogue of levimc.org..." : "");
      int y = list.Y + 2;
      foreach (string line in TextWidth.Wrap(message, list.Width)) {
        _screen.Write(list.X, y++, line, _catalogError is null ? Theme.Muted : Theme.Bad);
      }

      if (_catalogError is not null) {
        _screen.Write(list.X, y + 1, "Press R to try again.", Theme.Muted);
      }

      return;
    }

    _screen.Write(list.X + 2, list.Y + 1, "Name · author", Theme.Muted);
    _screen.Write(list.Right - 19, list.Y + 1, "Stars", Theme.Muted);
    _screen.Write(list.Right - 10, list.Y + 1, "Updated", Theme.Muted);
    int rows = list.Height - 3;
    _modTop = ScrollTo(_modSelected, _modTop, rows);
    for (int i = 0; i < rows && _modTop + i < _mods.Count; i++) {
      CatalogMod mod = _mods[_modTop + i];
      int y = list.Y + 2 + i;
      if (IsInstalled(mod.Id)) {
        _screen.Write(list.X, y, "✓", Theme.Good);
      }

      int end = _screen.WriteFit(list.X + 2, y, list.Width - 24, mod.Name);
      _screen.WriteFit(end, y, list.Right - 21 - end, " · " + mod.Owner, Theme.Muted);
      _screen.Write(
          list.Right - 19, y,
          string.Create(CultureInfo.InvariantCulture, $"★ {mod.Stars,5}"), Theme.Warning);
      _screen.Write(list.Right - 10, y, Date(mod.Updated));
      if (_modTop + i == _modSelected) {
        _screen.Restyle(list.X - 1, y, list.Width + 2, Theme.Selected);
      }
    }

    string position = _mods.Count == 0
        ? "No mod matches."
        : $"{_modSelected + 1} of {_mods.Count} · ✓ installed";
    _screen.WriteFit(list.X, list.Bottom - 1, list.Width, position, Theme.Muted);
    if (detailsWidth > 0) {
      Rect details = Panel(
          _screen,
          new Rect(body.X + filtersWidth + listWidth, body.Y, detailsWidth, body.Height),
          SelectedMod?.Name ?? "Details");
      if (SelectedMod is { } selected) {
        DrawModDetails(details, selected);
      }
    }
  }

  private void DrawFilters(Rect area) {
    if (_filterRows.Count == 0) {
      _screen.WriteFit(area.X, area.Y, area.Width, "Nothing to filter by.", Theme.Muted);
      return;
    }

    _filterTop = ScrollTo(_filterSelected, _filterTop, area.Height);
    // The heading of the first row stays in view.
    if (_filterSelected == 1) {
      _filterTop = 0;
    }

    for (int i = 0; i < area.Height && _filterTop + i < _filterRows.Count; i++) {
      FilterRow row = _filterRows[_filterTop + i];
      int y = area.Y + i;
      if (row.Kind == FilterKind.Heading) {
        _screen.WriteFit(area.X, y, area.Width, row.Label, Theme.Muted);
        continue;
      }

      (string mark, Style style) = row.Kind == FilterKind.GameVersion
          ? _gameVersions.Contains(row.Key) ? ("[+]", Theme.Good) : ("[ ]", default(Style))
          : _includeTags.Contains(row.Key) ? ("[+]", Theme.Good)
          : _excludeTags.Contains(row.Key) ? ("[-]", Theme.Bad)
          : ("[ ]", default);
      string count = row.Count.ToString(CultureInfo.InvariantCulture);
      _screen.Write(area.X, y, mark, style);
      _screen.WriteFit(
          area.X + 4, y, area.Width - 5 - count.Length, row.Label,
          mark == "[ ]" ? default : style);
      _screen.Write(area.Right - count.Length, y, count, Theme.Muted);
      if (_filtering && _filterTop + i == _filterSelected) {
        _screen.Restyle(area.X - 1, y, area.Width + 2, Theme.Selected);
      }
    }
  }

  private void DrawModDetails(Rect area, CatalogMod mod) {
    int y = area.Y;
    void Line(StyledLine line) {
      if (y < area.Bottom) {
        _screen.Write(area.X, y, line, area.Right);
      }

      y++;
    }

    void Field(string name, string value, Style style = default) {
      List<string> lines = TextWidth.Wrap(value, Math.Max(8, area.Width - 9));
      for (int i = 0; i < lines.Count; i++) {
        Line(new StyledLine().Add(i == 0 ? name.PadRight(9) : new string(' ', 9), Theme.Muted)
            .Add(lines[i], style));
      }
    }

    Line(new StyledLine().Add("by ", Theme.Muted).Add(mod.Owner, Theme.Accent)
        .Add($"  ★ {mod.Stars}", Theme.Warning));
    foreach (string line in TextWidth.Wrap(mod.Description, area.Width).Take(4)) {
      Line(new StyledLine(line));
    }

    y++;
    Field("Mod ID", mod.Id);
    _details.TryGetValue(mod.Id, out CatalogDetail? detail);
    if (detail is not null) {
      Field(
          "Latest",
          (detail.LatestVersion ?? "none")
              + (detail.LatestVariant is { Length: > 0 } variant ? $" #{variant}" : ""));
    }

    if (mod.Platforms.Count > 0) {
      Field("Platform", string.Join(", ", mod.Platforms));
    }

    if (mod.GameVersions.Count > 0) {
      Field(
          "Game",
          string.Join(", ", mod.GameVersions.Take(3)) + (mod.GameVersions.Count > 3 ? ", …" : ""));
    }

    IReadOnlyList<string> tags = CatalogQuery.DisplayTags(mod);
    if (tags.Count > 0) {
      Field("Tags", string.Join(", ", tags.Select(CatalogQuery.Label)), Theme.Tag);
    }

    if (detail is not null) {
      Field("License", detail.License ?? "none declared");
    }

    foreach ((string id, string range) in mod.Dependencies) {
      Field("Depends", $"{id[(id.LastIndexOf('/') + 1)..]} {range}");
    }

    Field("Used by", $"{mod.RequiredBy} mods");
    Field("Updated", Date(mod.Updated));
    if (detail is not null) {
      Field("Versions", detail.Versions.Count.ToString(CultureInfo.InvariantCulture));
    } else {
      y++;
      Line(new StyledLine("Reading the details...", Theme.Muted));
    }
  }

  private static string Date(DateTimeOffset? time) =>
      time?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

  private void BrowseKey(ConsoleKeyInfo key) {
    if (_searching) {
      switch (key.Key) {
        case ConsoleKey.Enter or ConsoleKey.DownArrow:
          _searching = false;
          return;
        case ConsoleKey.Escape:
          _searching = false;
          _search.Set("");
          SelectMods();
          return;
      }

      if (_search.OnKey(key)) {
        SelectMods();
      }

      return;
    }

    if (_filtering) {
      FilterKey(key);
      return;
    }

    int before = _modSelected;
    if (MoveSelection(key, ref _modSelected, _mods.Count, 10)) {
      if (before != _modSelected) {
        _modSelectedAt = Environment.TickCount64;
      }

      return;
    }

    if (key.Key == ConsoleKey.Enter) {
      if (SelectedMod is { } mod) {
        LoadDetail(mod.Id, detail => ChooseVersion(mod, detail), () => ChooseVersion(mod, null));
      }

      return;
    }

    switch (key.KeyChar) {
      case '/':
        _searching = true;
        break;
      case 'f' when _catalog is not null:
        _filtering = true;
        break;
      case 's':
        _sort = (CatalogSort)(((int)_sort + 1) % Enum.GetValues<CatalogSort>().Length);
        SelectMods();
        break;
      case 'R':
        _catalog = null;
        _details.Clear();
        _detailsRequested.Clear();
        LoadCatalog();
        break;
      case 'r' when SelectedMod is { } mod:
        LoadDetail(mod.Id, detail => Push(new MessageDialog(
            this, $"{mod.Name} — README",
            [.. (detail.Readme ?? "This mod has no README.").Split('\n')
                .Select(line => new StyledLine(line.TrimEnd('\r')))],
            ["Close"])));
        break;
      case 'o' when SelectedMod is { Url.Length: > 0 } mod:
        OpenInBrowser(mod.Url);
        break;
    }
  }

  /// The keys of the Filters pane: Space includes a game version or a tag, x excludes a tag,
  /// and either takes it back when pressed again.
  private void FilterKey(ConsoleKeyInfo key) {
    if (key.Key is ConsoleKey.Escape or ConsoleKey.RightArrow or ConsoleKey.Enter
        || key.KeyChar == 'f') {
      _filtering = false;
      return;
    }

    int selected = _filterSelected;
    if (MoveSelection(key, ref selected, _filterRows.Count, 10)) {
      // Headings are passed over, in the direction of the move.
      int step = selected >= _filterSelected ? 1 : -1;
      while (selected >= 0 && selected < _filterRows.Count
          && _filterRows[selected].Kind == FilterKind.Heading) {
        selected += step;
      }

      if (selected >= 0 && selected < _filterRows.Count) {
        _filterSelected = selected;
      }

      return;
    }

    if (key.KeyChar == 'c') {
      _includeTags.Clear();
      _excludeTags.Clear();
      _gameVersions.Clear();
      SelectMods();
      return;
    }

    if (_filterSelected >= _filterRows.Count) {
      return;
    }

    FilterRow row = _filterRows[_filterSelected];
    bool include = key.Key == ConsoleKey.Spacebar || key.KeyChar is ' ' or '+';
    bool exclude = key.KeyChar is 'x' or '-';
    if (row.Kind == FilterKind.GameVersion && include) {
      if (!_gameVersions.Remove(row.Key)) {
        _gameVersions.Add(row.Key);
      }
    } else if (row.Kind == FilterKind.Tag && (include || exclude)) {
      HashSet<string> chosen = include ? _includeTags : _excludeTags;
      HashSet<string> other = include ? _excludeTags : _includeTags;
      other.Remove(row.Key);
      if (!chosen.Remove(row.Key)) {
        chosen.Add(row.Key);
      }
    } else {
      return;
    }

    SelectMods();
  }

  /// Asks which version of a mod to install: the latest, one that the catalogue lists (none
  /// when its file could not be read), or one that the user writes, such as a commit.
  private void ChooseVersion(CatalogMod mod, CatalogDetail? detail) {
    IReadOnlyList<CatalogVersion> versions = detail?.Versions ?? [];
    List<StyledLine> items = [
      new StyledLine().Add("Latest".PadRight(24), Theme.Strong)
          .Add(detail?.LatestVersion ?? "", Theme.Muted),
      .. versions.Select(version => new StyledLine()
          .Add(version.Version.PadRight(24))
          .Add(Date(version.Released), Theme.Muted)
          .Add(version.Prerelease ? "  prerelease" : "", Theme.Warning)),
      new StyledLine().Add("Custom...".PadRight(24), Theme.Accent)
          .Add("a version, branch, tag or commit", Theme.Muted),
    ];
    Push(new ListDialog(this, $"Install {mod.Name} — choose a version", items, chosen => {
      if (chosen == 0) {
        InstallMod(mod.Id);
      } else if (chosen <= versions.Count) {
        InstallMod($"{mod.Id}@{versions[chosen - 1].Version}");
      } else {
        Push(new PromptDialog(
            this, $"Install {mod.Name} — custom version",
            [
              new PromptField(
                  "What follows the @ of the package: a version, branch:<name>, tag:<name> or"
                      + " rev:<commit>",
                  "", "1.2.3   branch:main   tag:v1.2.3   rev:0123abc"),
            ],
            values => {
              if (values[0].Length > 0) {
                InstallMod($"{mod.Id}@{values[0].TrimStart('@')}");
              }
            }));
      }
    }));
  }

  private void InstallMod(string package) =>
      PlanAndRun($"Install {package}", (dryRun, token) =>
          _client.Install([package], dryRun, null, token));

  private void OpenInBrowser(string url) {
    try {
      using Process? process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
      _status = new StyledLine($"Opened {url}", Theme.Muted);
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
        or InvalidOperationException or PlatformNotSupportedException) {
      _status = new StyledLine($"Could not open {url}: {ex.Message}", Theme.Bad);
    }
  }
}
