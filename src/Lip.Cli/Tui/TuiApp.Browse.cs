using System.Diagnostics;
using System.Globalization;
using Lip.Core.Catalog;

namespace Lip.Cli.Tui;

/// The Browse tab: the mods of the catalogue (protocol/lip-tui.md, "Catalogue").
public sealed partial class TuiApp {
  private static readonly string?[] _sides = ["server", "client", null];

  private readonly TextField _search = new();
  private readonly Dictionary<string, CatalogDetail> _details = new(StringComparer.Ordinal);
  private readonly HashSet<string> _detailsRequested = new(StringComparer.Ordinal);
  private CatalogIndex? _catalog;
  private string? _catalogError;
  private bool _catalogLoading;
  private bool _searching;
  private CatalogSort _sort = CatalogSort.Relevance;
  private int _side;
  // The index of the featured tag that mods must carry, or -1.
  private int _tag = -1;
  private IReadOnlyList<CatalogMod> _mods = [];
  private int _modSelected;
  private int _modTop;
  private long _modSelectedAt;

  private (string, string)[] BrowseHints() =>
      _searching
          ? [("Enter", "done"), ("Esc", "clear"), ("Ctrl+U", "erase")]
          : [
            ("/", "search"), ("Enter", "install"), ("v", "versions"), ("r", "readme"),
            ("s", "sort"), ("f", "side"), ("t", "tag"), ("o", "open"), ("R", "reload"),
          ];

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

  private void SelectMods() {
    if (_catalog is null) {
      return;
    }

    string? keep = SelectedMod?.Id;
    _mods = CatalogQuery.Select(
        _catalog, _search.Value, _sides[_side],
        _tag < 0 ? null : CatalogQuery.FeaturedTags[_tag].Id, _sort);
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

  /// Reads the file of a mod, once; then calls loaded, when given, also when it was read before.
  private void LoadDetail(string id, Action<CatalogDetail>? loaded) {
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
        });
      }
    });
  }

  private void DrawBrowse(Rect body) {
    int filtersWidth = body.Width >= 100 ? 30 : 0;
    int detailsWidth = Math.Max(30, (body.Width - filtersWidth) * 2 / 5);
    int listWidth = body.Width - filtersWidth - detailsWidth;
    if (filtersWidth > 0) {
      DrawFilters(Panel(
          _screen, new Rect(body.X, body.Y, filtersWidth, body.Height), "Filters"));
    }

    string side = _sides[_side] is { } s ? $" for {s}" : "";
    Rect list = Panel(
        _screen, new Rect(body.X + filtersWidth, body.Y, listWidth, body.Height),
        _catalog is null ? "Mods" : $"Mods — {_mods.Count}{side} · sort: {_sort}",
        focused: true);
    Rect details = Panel(
        _screen,
        new Rect(body.X + filtersWidth + listWidth, body.Y, detailsWidth, body.Height),
        SelectedMod?.Name ?? "Details");

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
    if (SelectedMod is { } selected) {
      DrawModDetails(details, selected);
    }
  }

  private void DrawFilters(Rect area) {
    int y = area.Y;
    void Heading(string text, string key) {
      _screen.Write(area.X, y, text, Theme.Muted);
      _screen.Write(area.Right - key.Length, y++, key, Theme.Muted);
    }

    void Option(string text, bool chosen, int? count = null) {
      if (y >= area.Bottom) {
        return;
      }

      _screen.Write(area.X, y, chosen ? "(●)" : "( )", chosen ? Theme.Accent : default);
      _screen.WriteFit(area.X + 4, y, area.Width - 9, text, chosen ? Theme.Strong : default);
      if (count is int n) {
        string number = n.ToString(CultureInfo.InvariantCulture);
        _screen.Write(area.Right - number.Length, y, number, Theme.Muted);
      }

      y++;
    }

    Heading("Side", "f");
    Option("Server", _side == 0, _catalog?.Mods.Count(m => m.RunsOn("server")));
    Option("Client", _side == 1, _catalog?.Mods.Count(m => m.RunsOn("client")));
    Option("Any", _side == 2, _catalog?.Mods.Count);
    y++;
    Heading("Sort", "s");
    foreach (CatalogSort sort in Enum.GetValues<CatalogSort>()) {
      Option(sort.ToString(), _sort == sort);
    }

    y++;
    Heading("Tag", "t");
    Option("Any", _tag < 0);
    for (int i = 0; i < CatalogQuery.FeaturedTags.Count; i++) {
      FeaturedTag tag = CatalogQuery.FeaturedTags[i];
      Option(tag.Label, _tag == i, _catalog?.Mods.Count(m =>
          (_sides[_side] is not { } side || m.RunsOn(side))
              && CatalogQuery.DisplayTags(m).Contains(tag.Id)));
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

    Field("Runs on", $"{string.Join(", ", mod.Sides)} · {string.Join(", ", mod.Platforms)}");
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

    if (!mod.RunsOn("server")) {
      y++;
      foreach (string line in TextWidth.Wrap(
          "This mod has no build for a server; lip manages servers only.", area.Width)) {
        Line(new StyledLine(line, Theme.Warning));
      }
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

    int before = _modSelected;
    if (MoveSelection(key, ref _modSelected, _mods.Count, 10)) {
      if (before != _modSelected) {
        _modSelectedAt = Environment.TickCount64;
      }

      return;
    }

    if (key.Key == ConsoleKey.Enter) {
      if (SelectedMod is { } mod) {
        PromptInstall(mod.Id);
      }

      return;
    }

    switch (key.KeyChar) {
      case '/':
        _searching = true;
        break;
      case 's':
        _sort = (CatalogSort)(((int)_sort + 1) % Enum.GetValues<CatalogSort>().Length);
        SelectMods();
        break;
      case 'f':
        _side = (_side + 1) % _sides.Length;
        SelectMods();
        break;
      case 't':
        _tag = _tag + 1 >= CatalogQuery.FeaturedTags.Count ? -1 : _tag + 1;
        SelectMods();
        break;
      case 'R':
        _catalog = null;
        _details.Clear();
        _detailsRequested.Clear();
        LoadCatalog();
        break;
      case 'v' when SelectedMod is { } mod:
        LoadDetail(mod.Id, detail => ShowVersions(mod, detail));
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

  private void ShowVersions(CatalogMod mod, CatalogDetail detail) {
    if (detail.Versions.Count == 0) {
      _status = new StyledLine($"The catalogue lists no version of {mod.Name}.", Theme.Warning);
      return;
    }

    Push(new ListDialog(
        this, $"{mod.Name} — versions (Enter installs)",
        [
          .. detail.Versions.Select(version => new StyledLine()
              .Add(version.Version.PadRight(24))
              .Add(Date(version.Released), Theme.Muted)
              .Add(version.Prerelease ? "  prerelease" : "", Theme.Warning)),
        ],
        chosen => PromptInstall($"{mod.Id}@{detail.Versions[chosen].Version}")));
  }

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
