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
    License,
    Dependency,
  }

  /// A row of the Filters pane: the heading of a section, or a game version, a tag, a license
  /// family or a dependency that mods are filtered by, with the number of mods that have it.
  /// Section is the label of the heading the row is below, or of the heading itself.
  private sealed record FilterRow(
      FilterKind Kind, string Section, string Key, string Label, int Count);

  private readonly TextField _search = new();
  private readonly Dictionary<string, CatalogDetail> _details = new(StringComparer.Ordinal);
  private readonly HashSet<string> _detailsRequested = new(StringComparer.Ordinal);
  private readonly HashSet<string> _includeTags = new(StringComparer.Ordinal);
  private readonly HashSet<string> _excludeTags = new(StringComparer.Ordinal);
  private readonly HashSet<string> _gameVersions = new(StringComparer.Ordinal);
  private readonly HashSet<string> _licenses = new(StringComparer.Ordinal);
  private readonly HashSet<string> _includeDependencies = new(StringComparer.Ordinal);
  private readonly HashSet<string> _excludeDependencies = new(StringComparer.Ordinal);
  // The sections of the Filters pane that show their rows; the others show their heading only.
  private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
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

  private int FilterCount =>
      _includeTags.Count + _excludeTags.Count + _gameVersions.Count + _licenses.Count
          + _includeDependencies.Count + _excludeDependencies.Count;

  /// The chosen values of a kind of filter that mods must have.
  private HashSet<string> Included(FilterKind kind) => kind switch {
    FilterKind.GameVersion => _gameVersions,
    FilterKind.License => _licenses,
    FilterKind.Dependency => _includeDependencies,
    _ => _includeTags,
  };

  /// The chosen values of a kind of filter that mods must not have; null for a kind that
  /// cannot be excluded.
  private HashSet<string>? Excluded(FilterKind kind) => kind switch {
    FilterKind.Tag => _excludeTags,
    FilterKind.Dependency => _excludeDependencies,
    _ => null,
  };

  private bool IsChosen(FilterRow row) =>
      row.Kind != FilterKind.Heading
          && (Included(row.Kind).Contains(row.Key)
              || Excluded(row.Kind)?.Contains(row.Key) == true);

  /// The rows of the Filters pane that are shown: every heading, and the rows of the
  /// expanded sections.
  private List<FilterRow> VisibleFilterRows =>
      [.. _filterRows.Where(row =>
          row.Kind == FilterKind.Heading || _expanded.Contains(row.Section))];

  private (string, string)[] BrowseHints() {
    if (_searching) {
      return [("Enter", "done"), ("↑/↓", "to the mods"), ("Esc", "clear"), ("Ctrl+U", "erase")];
    }

    return _filtering
        ? [
          ("Enter/Space", "include, or expand"), ("x", "exclude"), ("c", "clear all"),
          ("←", "collapse"), ("→/Esc", "back to the mods"),
        ]
        : [
          ("/", "search"), ("←", "filters"), ("s", "sort"), ("Enter", "install"),
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

  /// The game versions, newest first; the featured tags in the order of levimc.org/mods; the
  /// other tags, the license families and the dependencies, the most used first: each with the
  /// number of mods for a server that have it, and only when there is one.
  private void BuildFilterRows(CatalogIndex index) {
    List<CatalogMod> mods = [.. index.Mods.Where(mod => mod.RunsOn(Side))];
    Dictionary<string, int> versions = new(StringComparer.Ordinal);
    Dictionary<string, int> tags = new(StringComparer.Ordinal);
    Dictionary<string, int> licenses = new(StringComparer.Ordinal);
    Dictionary<string, int> dependencies = new(StringComparer.Ordinal);
    Dictionary<string, string> dependencyNames = new(StringComparer.Ordinal);
    foreach (CatalogMod mod in mods) {
      foreach (string version in mod.GameVersions.Distinct(StringComparer.Ordinal)) {
        versions[version] = versions.GetValueOrDefault(version) + 1;
      }

      foreach (string tag in CatalogQuery.DisplayTags(mod)) {
        tags[tag] = tags.GetValueOrDefault(tag) + 1;
      }

      string family = CatalogQuery.LicenseFamily(mod);
      licenses[family] = licenses.GetValueOrDefault(family) + 1;
      foreach (string id in mod.Dependencies.Keys) {
        string dependency = CatalogQuery.DependencyKey(id);
        string bare = id.Split('#')[0];
        dependencies[dependency] = dependencies.GetValueOrDefault(dependency) + 1;
        dependencyNames.TryAdd(dependency, bare[(bare.LastIndexOf('/') + 1)..]);
      }
    }

    List<FilterRow> rows = [];
    void Section(string heading, FilterKind kind, IEnumerable<(string Key, string Label, int Count)> items) {
      List<FilterRow> section = [
        .. items.Select(item => new FilterRow(kind, heading, item.Key, item.Label, item.Count)),
      ];
      if (section.Count > 0) {
        rows.Add(new FilterRow(FilterKind.Heading, heading, heading, heading, section.Count));
        rows.AddRange(section);
      }
    }

    Section("Game version", FilterKind.GameVersion, versions
        .OrderByDescending(v => VersionKey(v.Key), StringComparer.Ordinal)
        .Select(v => (v.Key, v.Key, v.Value)));
    Section("Tags", FilterKind.Tag, CatalogQuery.FeaturedTags
        .Where(tag => tags.ContainsKey(tag.Id))
        .Select(tag => (tag.Id, tag.Label, tags[tag.Id])));
    Section("Other tags", FilterKind.Tag, tags
        .Where(tag => !CatalogQuery.IsFeatured(tag.Key))
        .OrderByDescending(tag => tag.Value).ThenBy(tag => tag.Key, StringComparer.Ordinal)
        .Select(tag => (tag.Key, tag.Key, tag.Value)));
    Section("License", FilterKind.License, CatalogQuery.LicenseFamilies
        .Where(family => licenses.ContainsKey(family.Id))
        .Select(family => (family.Id, family.Label, licenses[family.Id])));
    Section("Depends on", FilterKind.Dependency, dependencies
        .OrderByDescending(d => d.Value).ThenBy(d => d.Key, StringComparer.Ordinal)
        .Select(d => (d.Key, dependencyNames[d.Key], d.Value)));
    _filterRows = rows;
    _filterSelected = 0;
    _filterTop = 0;
    _includeTags.IntersectWith(tags.Keys);
    _excludeTags.IntersectWith(tags.Keys);
    _gameVersions.IntersectWith(versions.Keys);
    _licenses.IntersectWith(licenses.Keys);
    _includeDependencies.IntersectWith(dependencies.Keys);
    _excludeDependencies.IntersectWith(dependencies.Keys);
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
        new CatalogFilter(Side, _includeTags, _excludeTags, _gameVersions) {
          Licenses = _licenses,
          IncludeDependencies = _includeDependencies,
          ExcludeDependencies = _excludeDependencies,
        },
        _sort);
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
    List<FilterRow> rows = VisibleFilterRows;
    if (rows.Count == 0) {
      _screen.WriteFit(area.X, area.Y, area.Width, "Nothing to filter by.", Theme.Muted);
      return;
    }

    _filterSelected = Math.Clamp(_filterSelected, 0, rows.Count - 1);
    _filterTop = ScrollTo(_filterSelected, _filterTop, area.Height);
    for (int i = 0; i < area.Height && _filterTop + i < rows.Count; i++) {
      FilterRow row = rows[_filterTop + i];
      int y = area.Y + i;
      if (row.Kind == FilterKind.Heading) {
        // A collapsed section still says how many of its rows are chosen.
        int chosen = _filterRows.Count(r => r.Section == row.Section && IsChosen(r));
        string note = chosen > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{chosen} chosen")
            : row.Count.ToString(CultureInfo.InvariantCulture);
        _screen.Write(area.X, y, _expanded.Contains(row.Section) ? "▾" : "▸", Theme.Accent);
        _screen.WriteFit(area.X + 2, y, area.Width - 3 - note.Length, row.Label, Theme.Strong);
        _screen.Write(
            area.Right - note.Length, y, note, chosen > 0 ? Theme.Good : Theme.Muted);
      } else {
        (string mark, Style style) = Included(row.Kind).Contains(row.Key) ? ("[+]", Theme.Good)
            : Excluded(row.Kind)?.Contains(row.Key) == true ? ("[-]", Theme.Bad)
            : ("[ ]", default(Style));
        string count = row.Count.ToString(CultureInfo.InvariantCulture);
        _screen.Write(area.X + 2, y, mark, style);
        _screen.WriteFit(
            area.X + 6, y, area.Width - 7 - count.Length, row.Label,
            mark == "[ ]" ? default : style);
        _screen.Write(area.Right - count.Length, y, count, Theme.Muted);
      }

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

    Field("License", CatalogQuery.LicenseOf(mod) ?? "none declared");
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
        case ConsoleKey.Enter:
          _searching = false;
          return;
        case ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp
            or ConsoleKey.PageDown:
          // On to the mods that the search found.
          _searching = false;
          MoveSelection(key, ref _modSelected, _mods.Count, 10, wrap: true);
          _modSelectedAt = Environment.TickCount64;
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
    if (MoveSelection(key, ref _modSelected, _mods.Count, 10, wrap: true)) {
      if (before != _modSelected) {
        _modSelectedAt = Environment.TickCount64;
      }

      return;
    }

    switch (key.Key) {
      case ConsoleKey.LeftArrow when _catalog is not null:
        _filtering = true;
        return;
      case ConsoleKey.Escape when _search.Value.Length > 0:
        _search.Set("");
        SelectMods();
        return;
    }

    if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar) {
      if (SelectedMod is { } mod) {
        LoadDetail(
            mod.Id, detail => Push(new VersionDialog(this, mod, detail)),
            () => Push(new VersionDialog(this, mod, null)));
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

  /// The keys of the Filters pane, to the left of the mods: → goes back to the mods, as Esc
  /// does. On the heading of a section, Enter and Space show or hide its rows, and ← hides
  /// them. On a row, Enter and Space include it, x excludes it when it is a tag or a dependency
  /// (either takes it back when pressed again), and ← goes to its heading.
  private void FilterKey(ConsoleKeyInfo key) {
    if (key.Key is ConsoleKey.Escape or ConsoleKey.RightArrow || key.KeyChar == 'f') {
      _filtering = false;
      return;
    }

    List<FilterRow> rows = VisibleFilterRows;
    if (MoveSelection(key, ref _filterSelected, rows.Count, 10, wrap: true)) {
      return;
    }

    if (key.KeyChar == 'c') {
      _includeTags.Clear();
      _excludeTags.Clear();
      _gameVersions.Clear();
      _licenses.Clear();
      _includeDependencies.Clear();
      _excludeDependencies.Clear();
      SelectMods();
      return;
    }

    if (_filterSelected >= rows.Count) {
      return;
    }

    FilterRow row = rows[_filterSelected];
    bool include = key.Key == ConsoleKey.Spacebar || key.KeyChar is ' ' or '+';
    bool exclude = key.KeyChar is 'x' or '-';
    if (row.Kind == FilterKind.Heading) {
      bool expanded = _expanded.Contains(row.Section);
      if (key.Key == ConsoleKey.Enter || include
          || (key.Key == ConsoleKey.LeftArrow && expanded)) {
        if (!_expanded.Remove(row.Section)) {
          _expanded.Add(row.Section);
        }
      }

      return;
    }

    include |= key.Key == ConsoleKey.Enter;

    if (key.Key == ConsoleKey.LeftArrow) {
      // Back to the heading of the section, which hides its rows.
      _expanded.Remove(row.Section);
      _filterSelected = VisibleFilterRows.FindIndex(r =>
          r.Kind == FilterKind.Heading && r.Section == row.Section);
      return;
    }

    HashSet<string> included = Included(row.Kind);
    HashSet<string>? excluded = Excluded(row.Kind);
    if (!(include || (exclude && excluded is not null))) {
      return;
    }

    HashSet<string> chosen = include ? included : excluded!;
    (include ? excluded : included)?.Remove(row.Key);
    if (!chosen.Remove(row.Key)) {
      chosen.Add(row.Key);
    }

    SelectMods();
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

  /// Asks which variant and which version of a mod to install: the latest version, one that
  /// the catalogue lists for the variant, or one that the user writes, such as a commit. The
  /// catalogue lists none when the file of the mod could not be read.
  private sealed class VersionDialog : Dialog {
    private readonly TuiApp _app;
    private readonly CatalogMod _mod;
    private readonly IReadOnlyList<CatalogVersion> _all;
    private readonly List<string> _variants;
    private int _variant;
    private int _selected;
    private int _top;
    private int _rows = 1;

    public VersionDialog(TuiApp app, CatalogMod mod, CatalogDetail? detail) {
      _app = app;
      _mod = mod;
      _all = detail?.Versions ?? [];
      // The variants as the versions list them, newest version first, the default one first.
      _variants = [
        .. _all.SelectMany(version => version.Variants).Distinct()
            .OrderBy(variant => variant.Length == 0 ? 0 : 1),
      ];
      if (_variants.Count == 0) {
        _variants.Add("");
      }

      _variant = Math.Max(0, _variants.IndexOf(detail?.LatestVariant ?? ""));
    }

    public override (string, string)[] Hints =>
        _variants.Count > 1
            ? [("Enter", "install"), ("←/→", "variant"), ("c", "custom version"), ("Esc", "cancel")]
            : [("Enter", "install"), ("c", "custom version"), ("Esc", "cancel")];

    private string Variant => _variants[_variant];

    private List<CatalogVersion> Versions =>
        [.. _all.Where(version => version.Variants.Contains(Variant))];

    /// The package argument of the mod in the chosen variant, at a version when given.
    private string Package(string? version) =>
        _mod.Id + (Variant.Length == 0 ? "" : "#" + Variant)
            + (version is null ? "" : "@" + version);

    public override void Draw(ScreenBuffer screen) {
      List<CatalogVersion> versions = Versions;
      Rect inner = Frame(
          screen, $"Install {_mod.Name}", Math.Min(screen.Width - 4, 72), versions.Count + 8);
      int x = screen.Write(inner.X, inner.Y, "Variant  ", Theme.Muted);
      x = screen.Write(x, inner.Y, _variants.Count > 1 ? "◀ " : "", Theme.Accent);
      x = screen.Write(x, inner.Y, Variant.Length == 0 ? "(default)" : Variant, Theme.Strong);
      x = screen.Write(x, inner.Y, _variants.Count > 1 ? " ▶" : "", Theme.Accent);
      if (_variants.Count > 1) {
        screen.Write(x + 2, inner.Y, $"{_variant + 1} of {_variants.Count}", Theme.Muted);
      }

      screen.WriteFit(inner.X, inner.Y + 1, inner.Width, Package(null), Theme.Muted);
      _rows = Math.Max(1, inner.Height - 5);
      _selected = Math.Clamp(_selected, 0, versions.Count);
      _top = ScrollTo(_selected, _top, _rows);
      for (int i = 0; i < _rows && _top + i <= versions.Count; i++) {
        int y = inner.Y + 3 + i;
        if (_top + i == 0) {
          string latest = versions.FirstOrDefault(v => !v.Prerelease)?.Version
              ?? versions.FirstOrDefault()?.Version ?? "";
          screen.Write(inner.X, y, "Latest", Theme.Strong);
          screen.Write(inner.X + 24, y, latest, Theme.Muted);
        } else {
          CatalogVersion version = versions[_top + i - 1];
          screen.WriteFit(inner.X, y, 23, version.Version);
          int end = screen.Write(inner.X + 24, y, Date(version.Released), Theme.Muted);
          screen.Write(end + 2, y, version.Prerelease ? "prerelease" : "", Theme.Warning);
        }

        if (_top + i == _selected) {
          screen.Restyle(inner.X - 1, y, inner.Width + 2, Theme.Selected);
        }
      }

      int hint = screen.Write(inner.X, inner.Bottom - 1, "c", Theme.Strong);
      screen.WriteFit(
          hint + 1, inner.Bottom - 1, inner.Right - hint - 1,
          "custom: another version, a branch, a tag or a commit", Theme.Muted);
    }

    public override void OnKey(ConsoleKeyInfo key) {
      List<CatalogVersion> versions = Versions;
      switch (key.Key) {
        case ConsoleKey.Escape:
          _app.Close(this);
          return;
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          _app.Close(this);
          _app.InstallMod(Package(_selected == 0 ? null : versions[_selected - 1].Version));
          return;
        case ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Tab:
          bool back = key.Key == ConsoleKey.LeftArrow
              || key.Modifiers.HasFlag(ConsoleModifiers.Shift);
          _variant = (_variant + (back ? _variants.Count - 1 : 1)) % _variants.Count;
          _selected = 0;
          _top = 0;
          return;
      }

      if (key.KeyChar == 'c') {
        Custom();
        return;
      }

      MoveSelection(key, ref _selected, versions.Count + 1, _rows, wrap: true);
    }

    /// Asks for what follows the @ of the package; cancelling comes back to this dialog.
    private void Custom() {
      _app.Close(this);
      string package = Package(null);
      _app.Push(new PromptDialog(
          _app, $"Install {package}",
          [
            new PromptField(
                "What follows the @ of the package: a version, branch:<name>, tag:<name> or"
                    + " rev:<commit>",
                "", "1.2.3   branch:main   tag:v1.2.3   rev:0123abc"),
          ],
          values => {
            if (values[0].Length > 0) {
              _app.InstallMod($"{package}@{values[0].TrimStart('@')}");
            }
          },
          () => _app.Push(this)));
    }
  }
}
