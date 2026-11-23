using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Editing;
using Semver;

namespace Lip.Cli.Tui.Pages.Tools;

/// Finds a mod of the Marketplace (protocol/lip-tui.md, "Catalogue") to depend on: what is
/// typed searches the mods as the Marketplace page does, ↑ and ↓ move, Enter chooses.
public sealed class MarketplaceDialog : Dialog {
  private readonly ModCatalogClient _catalog;
  private readonly Action<CatalogMod> _chosen;
  private readonly TextField _search = new();
  private readonly ListView<CatalogMod> _list;
  private CatalogIndex? _index;
  private string? _error;
  private bool _loading;

  public MarketplaceDialog(ModCatalogClient catalog, Action<CatalogMod> chosen) {
    _catalog = catalog;
    _chosen = chosen;
    _list = new ListView<CatalogMod>(mod => new StyledLine()
        .Add(Cells.Pad(mod.Name, 24) + " ")
        .Add(mod.Id, Theme.Muted)) {
      Empty = () => _error ?? (_index is null ? CommonText.Loading : ManifestEditorText.NoMatch),
    };
  }

  public override IReadOnlyList<Hint> Hints =>
      [CommonText.HintMove, CommonText.HintSelect, .. _list.PageHints, CommonText.HintCancel];

  public override void OnTick() => Load();

  public override void Draw(ScreenBuffer screen) {
    Load();
    int width = Ui.DialogWidth(screen, 90);
    Rect inner = Ui.DialogFrame(
        screen, ManifestEditorText.MarketplaceTitle, width, Math.Min(screen.Height - 2, 24));
    int x = screen.Write(inner.X, inner.Y, ManifestEditorText.Search + ": ", Theme.Strong);
    _search.Draw(
        screen, x, inner.Y, inner.Right - x, true, ManifestEditorText.SearchPlaceholder);
    screen.Write(inner.X, inner.Y + 1, new string('─', inner.Width), Theme.Border);
    _list.Draw(screen, inner.Shrink(top: 2, bottom: 3), true);
    screen.Write(inner.X, inner.Bottom - 3, new string('─', inner.Width), Theme.Border);
    if (_list.HasCurrent) {
      CatalogMod mod = _list.Current;
      List<string> about = TextWidth.Wrap(mod.Description, inner.Width);
      screen.WriteFit(inner.X, inner.Bottom - 2, inner.Width, mod.Id, Theme.Accent);
      screen.WriteFit(inner.X, inner.Bottom - 1, inner.Width, about.FirstOrDefault() ?? "", Theme.Muted);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Enter:
        if (_list.HasCurrent) {
          CatalogMod mod = _list.Current;
          Close();
          _chosen(mod);
        }

        return;
      case ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown:
        _list.OnKey(key);
        return;
    }

    string before = _search.Value;
    _search.OnKey(key);
    if (_search.Value != before) {
      Select();
      _list.Selected = 0;
    }
  }

  /// Reads the index of the catalogue once.
  private void Load() {
    if (_loading || _index is not null || Owner is null) {
      return;
    }

    _loading = true;
    _error = null;
    ModCatalogClient catalog = _catalog;
    Context.Background(
        () => catalog.GetIndex(),
        index => {
          _index = index;
          Select();
        },
        ex => {
          _loading = false;
          _error = ex.Message;
        },
        anyWorkspace: true);
  }

  private void Select() {
    if (_index is not null) {
      _list.Items = CatalogQuery.Select(
          _index, _search.Value, CatalogFilter.For(null), CatalogSort.Relevance);
    }
  }
}

/// Asks for the version of a dependency: at the top, when the package has variants, a row of
/// them that ← and → choose from; below, its versions, newest first, and a last row for a
/// range without a version. chosen gets the key of the dependency, with the variant, and the
/// version, or null.
public sealed class DependencyVersionDialog : Dialog {
  private readonly string _id;
  private readonly PackageLookup _lookup;
  private readonly IReadOnlyList<string> _variants;
  private readonly Action<string, SemVersion?> _chosen;
  private readonly ListView<int> _list;
  private int _variant;
  private IReadOnlyList<SemVersion> _versions = [];

  /// key: the package id, with #variant when the variant is already known.
  public DependencyVersionDialog(
      string key, PackageLookup lookup, SemVersion? selected, Action<string, SemVersion?> chosen) {
    _id = key.Split('#')[0];
    _lookup = lookup;
    _chosen = chosen;
    string variant = DependencyChecks.Variant(key);
    _variants = key.Contains('#') || lookup.Detail is null ? [variant]
        : lookup.Detail.ServerVariants();
    _variant = Math.Max(0, _variants.ToList().IndexOf(variant));
    _list = new ListView<int>(Render) { Wrap = true };
    Refresh();
    if (selected is not null) {
      int at = _versions.ToList().FindIndex(
          v => SemVersion.PrecedenceComparer.Compare(v, selected) == 0);
      _list.Selected = Math.Max(0, at);
    }
  }

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintMove,
    CommonText.HintSelect,
    .. _variants.Count > 1 ? [CommonText.HintSwitch] : (Hint[])[],
    .. _list.PageHints,
    CommonText.HintCancel,
  ];

  private string Key => _variants[_variant].Length == 0 ? _id : $"{_id}#{_variants[_variant]}";

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 64);
    List<(string Text, Style Style)> notes = [];
    if (_lookup.Error is { } error) {
      notes.Add((ManifestEditorText.Remote(
          new RemoteCheck(RemoteStatus.Unknown, RemoteReason.CannotCheck, error)), Theme.Warning));
    } else if (_versions.Count == 0) {
      notes.Add((ManifestEditorText.NoVersions, Theme.Warning));
    }

    List<(string Text, Style Style)> rows = [
      .. notes.SelectMany(n => TextWidth.Wrap(n.Text, width - 4).Select(l => (l, n.Style))),
    ];
    int above = (_variants.Count > 1 ? 2 : 0) + rows.Count;
    Rect inner = Ui.DialogFrame(
        screen, ManifestEditorText.VersionTitle(_id), width,
        above + Math.Min(_list.Items.Count, 14) + 4);
    int y = inner.Y;
    if (_variants.Count > 1) {
      int x = inner.X;
      for (int i = 0; i < _variants.Count; i++) {
        string name = _variants[i].Length == 0 ? ManifestEditorText.DefaultVariant : _variants[i];
        x = screen.Write(
            x, y, i == _variant ? $"[{name}]" : $" {name} ", i == _variant ? Theme.Accent : default,
            inner.Right);
      }

      screen.Write(inner.X, y + 1, new string('─', inner.Width), Theme.Border);
      y += 2;
    }

    foreach ((string text, Style style) in rows) {
      screen.Write(inner.X, y++, text, style, inner.Right);
    }

    _list.Draw(screen, new Rect(inner.X, y, inner.Width, Math.Max(1, inner.Bottom - 2 - y)), true);
    screen.Write(inner.X, inner.Bottom - 2, new string('─', inner.Width), Theme.Border);
    screen.WriteFit(
        inner.X, inner.Bottom - 1, inner.Width,
        Key + (_list.Current < _versions.Count ? "@" + _versions[_list.Current] : ""), Theme.Muted);
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.LeftArrow when _variants.Count > 1:
        _variant = (_variant + _variants.Count - 1) % _variants.Count;
        Refresh();
        return;
      case ConsoleKey.RightArrow when _variants.Count > 1:
        _variant = (_variant + 1) % _variants.Count;
        Refresh();
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        SemVersion? version = _list.Current < _versions.Count ? _versions[_list.Current] : null;
        string chosen = Key;
        Close();
        _chosen(chosen, version);
        return;
    }

    _list.OnKey(key);
  }

  private void Refresh() {
    _versions = _lookup.VersionsOf(_variants[_variant]);
    _list.Items = [.. Enumerable.Range(0, _versions.Count + 1)];
    _list.Selected = 0;
  }

  private StyledLine Render(int index) {
    if (index >= _versions.Count) {
      return new StyledLine(ManifestEditorText.AnyVersionRow, Theme.Muted);
    }

    SemVersion version = _versions[index];
    DateTimeOffset? released = _lookup.Detail?.Versions
        .FirstOrDefault(v => v.Version == version.ToString())?.Released;
    return new StyledLine()
        .Add(Cells.Pad(version.ToString(), 22) + " ")
        .Add(Cells.Pad(Cells.Date(released), 12), Theme.Muted)
        .Add(version.IsPrerelease ? ManifestEditorText.Prerelease : "", Theme.Warning);
  }
}

/// Asks for the range of a dependency: the presets for the version chosen (RangePresets), the
/// recommended one first, and a range that the user types, checked as it is typed. ↑ and ↓
/// move, typing goes to the typed range, Enter takes the range, Esc cancels.
public sealed class RangeDialog : Dialog {
  private readonly string _key;
  private readonly SemVersion? _version;
  private readonly IReadOnlyList<SemVersion>? _known;
  private readonly Action<string> _done;
  private readonly IReadOnlyList<RangePreset> _presets;
  private readonly TextField _custom;
  private int _selected;

  /// current: the range that the dependency has, when it has one. known: the versions of the
  /// package, newest first, when they are known, to say how many the range has.
  public RangeDialog(
      string key, SemVersion? version, string? current, IReadOnlyList<SemVersion>? known,
      Action<string> done) {
    _key = key;
    _version = version;
    _known = known;
    _done = done;
    _presets = version is null ? [RangePreset.Any] : RangePresets.All;
    _custom = new TextField(current ?? "");
    RangePreset? match = current is null ? (version is null ? null : RangePreset.Patch)
        : version is null ? (current.Trim() == "*" ? RangePreset.Any : null)
        : RangePresets.Match(current, version);
    _selected = match is { } preset ? _presets.ToList().IndexOf(preset)
        : current is { Length: > 0 } ? _presets.Count
        : 0;
    if (_selected < 0) {
      _selected = _presets.Count;
    }
  }

  public override IReadOnlyList<Hint> Hints =>
      [CommonText.HintMove, CommonText.HintConfirm, CommonText.HintCancel];

  private bool OnCustom => _selected == _presets.Count;

  /// The range chosen, or null when the typed one is not a range.
  private string? Range =>
      !OnCustom ? Of(_presets[_selected])
          : VersionRange.TryParse(_custom.Value.Trim(), out _) ? _custom.Value.Trim()
          : null;

  private string Of(RangePreset preset) =>
      _version is null ? "*" : RangePresets.Of(preset, _version);

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 76);
    int rows = _presets.Count + 1;
    Rect inner = Ui.DialogFrame(screen, ManifestEditorText.RangeTitle(_key), width, rows + 7);
    int labels = Math.Min(32, inner.Width / 2);
    for (int i = 0; i < _presets.Count; i++) {
      int y = inner.Y + i;
      bool current = i == _selected;
      screen.Write(inner.X, y, current ? "(•) " : "( ) ", current ? Theme.Accent : Theme.Muted);
      screen.WriteFit(
          inner.X + 4, y, labels - 4, ManifestEditorText.Preset(_presets[i]),
          current ? Theme.Strong : default);
      screen.WriteFit(inner.X + labels + 1, y, inner.Width - labels - 1, Of(_presets[i]), Theme.Accent);
    }

    int customY = inner.Y + _presets.Count;
    screen.Write(inner.X, customY, OnCustom ? "(•) " : "( ) ", OnCustom ? Theme.Accent : Theme.Muted);
    screen.WriteFit(
        inner.X + 4, customY, labels - 4, ManifestEditorText.Custom,
        OnCustom ? Theme.Strong : default);
    _custom.Draw(screen, inner.X + labels + 1, customY, inner.Width - labels - 3, OnCustom, "*");
    if (_custom.Value.Trim().Length > 0) {
      bool valid = VersionRange.TryParse(_custom.Value.Trim(), out _);
      screen.Write(inner.Right - 1, customY, valid ? "✓" : "✗", valid ? Theme.Good : Theme.Bad);
    }

    int at = customY + 2;
    screen.Write(inner.X, at - 1, new string('─', inner.Width), Theme.Border);
    string about = OnCustom ? ManifestEditorText.CustomAbout
        : ManifestEditorText.PresetAbout(_presets[_selected]);
    foreach (string line in TextWidth.Wrap(about, inner.Width).Take(2)) {
      screen.Write(inner.X, at++, line, Theme.Muted, inner.Right);
    }

    if (OnCustom && PackageDraftChecks.Range(_custom.Value.Trim()) is { } problem) {
      foreach (string line in TextWidth.Wrap(ManifestEditorText.Problem(problem), inner.Width).Take(2)) {
        if (at < inner.Bottom) {
          screen.Write(inner.X, at++, line, Theme.Bad, inner.Right);
        }
      }
    } else if (_known is not null && Range is { } range && at < inner.Bottom
        && VersionRange.TryParse(range, out VersionRange? parsed)) {
      List<SemVersion> matching = [.. _known.Where(parsed!.IsSatisfiedBy)];
      screen.WriteFit(
          inner.X, at, inner.Width,
          ManifestEditorText.Matches(matching.Count, matching.FirstOrDefault()?.ToString()),
          matching.Count > 0 ? Theme.Good : Theme.Warning);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.UpArrow:
        _selected = (_selected + _presets.Count) % (_presets.Count + 1);
        return;
      case ConsoleKey.DownArrow or ConsoleKey.Tab:
        _selected = (_selected + 1) % (_presets.Count + 1);
        return;
      case ConsoleKey.Enter:
        if (Range is { } range) {
          Close();
          _done(range);
        }

        return;
    }

    // Typing on a preset types a range instead.
    if (!OnCustom && key.KeyChar > ' ' && key.KeyChar != '\x7f'
        && !key.Modifiers.HasFlag(ConsoleModifiers.Control)) {
      _custom.Set("");
      _selected = _presets.Count;
    }

    if (OnCustom) {
      _custom.OnKey(key);
    }
  }
}
