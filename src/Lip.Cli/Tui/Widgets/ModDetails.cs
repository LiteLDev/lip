using Lip.Cli.Tui.I18n;
using Lip.Core.Catalog;

namespace Lip.Cli.Tui.Widgets;

/// The details of a mod of the catalogue: its description, then its fields, the names in a
/// column. The same wherever a mod is shown: in a pane of a page, which draws it and passes it
/// the keys while the pane has the focus, and in a ModDetailsDialog. It scrolls when it has
/// more rows than fit.
public sealed class ModDetails {
  private CatalogMod? _mod;
  private CatalogDetail? _detail;
  private bool _failed;
  // The lines that are shown, what they were made from, the first of them that is shown, and
  // how many fit.
  private List<StyledLine> _lines = [];
  private (CatalogMod Mod, CatalogDetail? Detail, bool Failed, int Width, bool Spaced,
      Language Language)? _linesOf;
  private int _top;
  private int _rows = 1;

  /// Whether an empty row follows the description; a pane of few rows has none.
  public bool Spaced { get; set; } = true;

  /// Whether the details have more rows than fit: the keys scroll only then, and a page gives
  /// them the focus only then.
  public bool Scrollable => _lines.Count > _rows;

  /// Whether the first row is shown: a page may take ↑ as "back to what is above" then.
  public bool AtStart => _top == 0;

  /// The key that scrolls, while there is something to scroll.
  public IReadOnlyList<Hint> Hints => Scrollable ? [CommonText.HintScroll] : [];

  /// The keys that page, while there is something to scroll: for the end of the hints of the
  /// owner, where the bottom bar leaves hints out first.
  public IReadOnlyList<Hint> PageHints =>
      Scrollable ? [CommonText.HintPage, CommonText.HintEnds] : [];

  /// What to show: a mod, or none; the file of the mod once it is read, and failed when it
  /// cannot be read. Without either the file is taken to be on its way. The details of
  /// another mod are shown from their start.
  public void Show(CatalogMod? mod, CatalogDetail? detail = null, bool failed = false) {
    if (mod?.Id != _mod?.Id) {
      _top = 0;
    }

    _mod = mod;
    _detail = detail;
    _failed = failed;
  }

  /// Shows the details from their start.
  public void ScrollToStart() => _top = 0;

  /// The rows that the details take in a rectangle of width columns.
  public int RowsFor(int width) {
    Build(width);
    return _lines.Count;
  }

  /// Scrolls by a key; false when the key does not scroll, or nothing is hidden.
  public bool OnKey(ConsoleKeyInfo key) {
    if (!Scrollable) {
      return false;
    }

    int last = _lines.Count - _rows;
    int top = Math.Clamp(_top, 0, last);
    int? moved = key.Key switch {
      ConsoleKey.UpArrow => top - 1,
      ConsoleKey.DownArrow => top + 1,
      ConsoleKey.PageUp => top - _rows,
      ConsoleKey.PageDown => top + _rows,
      ConsoleKey.Home => 0,
      ConsoleKey.End => last,
      _ => null,
    };
    if (moved is not { } to) {
      return false;
    }

    _top = Math.Clamp(to, 0, last);
    return true;
  }

  /// Draws the rows that fit rect, from the first that is shown, with marks at the right
  /// where rows are hidden above or below.
  public void Draw(ScreenBuffer screen, Rect rect) {
    _rows = Math.Max(1, rect.Height);
    if (rect.Height < 1 || rect.Width < 2) {
      _lines = [];
      _linesOf = null;
      return;
    }

    Build(rect.Width);
    _top = Math.Clamp(_top, 0, Math.Max(0, _lines.Count - _rows));
    for (int i = 0; i < _rows && _top + i < _lines.Count; i++) {
      screen.Write(rect.X, rect.Y + i, _lines[_top + i], rect.Right - 1);
    }

    Ui.ScrollMarks(screen, rect, _top > 0, _top + _rows < _lines.Count);
  }

  /// Makes the lines for a rectangle of width columns, whose last one is that of the marks;
  /// again only when what they say changed.
  private void Build(int width) {
    if (_mod is null) {
      _lines = [];
      _linesOf = null;
      return;
    }

    (CatalogMod, CatalogDetail?, bool, int, bool, Language) of =
        (_mod, _detail, _failed, width - 1, Spaced, L.Current);
    if (_linesOf != of) {
      _linesOf = of;
      _lines = Lines(_mod, _detail, _failed, width - 1, Spaced);
    }
  }

  /// The latest version of a mod, with its variant when that is not the default one, as its
  /// file says; a dash when the file says none or cannot be read, and that it is on its way
  /// while it is.
  public static string Latest(CatalogDetail? detail, bool failed) =>
      detail is not null
          ? (detail.LatestVersion ?? "—")
              + (detail.LatestVariant is { Length: > 0 } variant ? $" #{variant}" : "")
          : failed ? "—" : CommonText.Loading;

  /// The details of a mod as lines of at most width columns. A field without a value has no
  /// line: the downloads when the catalogue does not count them or counts none, the stars when
  /// there are none.
  public static List<StyledLine> Lines(
      CatalogMod mod, CatalogDetail? detail, bool failed, int width, bool spaced = true) {
    width = Math.Max(8, width);
    List<StyledLine> lines = [];
    if (mod.Description.Length > 0) {
      lines.AddRange(TextWidth.Wrap(mod.Description, width).Select(row => new StyledLine(row)));
      if (spaced) {
        lines.Add(new StyledLine());
      }
    }

    IEnumerable<string> tags = CatalogQuery.DisplayTags(mod)
        .Select(tag => ModDetailsText.Tag(tag, CatalogQuery.Label(tag)));
    List<(string Name, string Value, Style Style)> fields = [
      (ModDetailsText.Author, mod.Owner, Theme.Accent),
      (
        ModDetailsText.Downloads, mod.Downloads is > 0 and { } count ? Cells.Count(count) : "",
        default),
      (ModDetailsText.Stars, mod.Stars > 0 ? Cells.Count(mod.Stars) : "", Theme.Warning),
      (ModDetailsText.Id, mod.Id, default),
      (ModDetailsText.Latest, Latest(detail, failed), default),
      (ModDetailsText.Platform, string.Join(", ", mod.Platforms), default),
      (
        ModDetailsText.Game,
        string.Join(", ", mod.GameVersions.Take(3)) + (mod.GameVersions.Count > 3 ? ", …" : ""),
        default),
      (ModDetailsText.Tags, string.Join(", ", tags), Theme.Tag),
      (ModDetailsText.License, CatalogQuery.LicenseOf(mod) ?? ModDetailsText.None, default),
      (
        ModDetailsText.Depends,
        string.Join(", ", mod.Dependencies.Select(d => $"{Cells.BareName(d.Key)} {d.Value}")),
        default),
      (ModDetailsText.Updated, Cells.Date(mod.Updated), default),
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
}

/// Shows the details of a mod of the catalogue, as the Get mods page does, above buttons that
/// show its README and open its page in the browser. It reads the file of the mod itself,
/// unless it is given.
public sealed class ModDetailsDialog : Dialog {
  private readonly CatalogMod _mod;
  private readonly ModDetails _details = new();
  private readonly ButtonRow _buttons;
  // Abandons the request for the file of the mod once the dialog is closed.
  private readonly CancellationTokenSource _closed = new();
  private CatalogDetail? _detail;
  private bool _failed;
  private bool _reading;

  /// detail is the file of the mod when it was read before; without it the dialog reads it.
  public ModDetailsDialog(CatalogMod mod, CatalogDetail? detail = null) {
    _mod = mod;
    _detail = detail;
    _buttons = new ButtonRow(
        new Button(() => ModDetailsText.Readme, ShowReadme),
        new Button(() => ModDetailsText.OpenInBrowser, () => Context.OpenUrl(_mod.Url)) {
          Enabled = () => _mod.Url.Length > 0,
        },
        new Button(() => CommonText.Close, Leave));
  }

  /// Closes the dialog: nothing waits for the file of the mod any more.
  private void Leave() {
    _closed.Cancel();
    Close();
  }

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintSwitchButtons, CommonText.HintSelect, .. _details.Hints,
    .. _details.PageHints, CommonText.HintClose,
  ];

  public override void Draw(ScreenBuffer screen) {
    Read();
    _details.Show(_mod, _detail, _failed);
    int width = Ui.DialogWidth(screen, 76);
    Rect inner = Ui.DialogFrame(screen, _mod.Name, width, _details.RowsFor(width - 4) + 4);
    _details.Draw(screen, inner.Shrink(bottom: 2));
    _buttons.Draw(screen, inner.X, inner.Bottom - 1, true, inner.Right);
  }

  public override void OnKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Escape) {
      Leave();
      return;
    }

    if (key.Key == ConsoleKey.Tab) {
      // Round the buttons that can be pressed.
      ConsoleKey step = Ui.IsBackTab(key) ? ConsoleKey.LeftArrow : ConsoleKey.RightArrow;
      if (!_buttons.OnKey(new ConsoleKeyInfo('\0', step, false, false, false))) {
        ConsoleKey back = Ui.IsBackTab(key) ? ConsoleKey.RightArrow : ConsoleKey.LeftArrow;
        while (_buttons.OnKey(new ConsoleKeyInfo('\0', back, false, false, false))) {
        }
      }

      return;
    }

    if (!_buttons.OnKey(key)) {
      _details.OnKey(key);
    }
  }

  /// Reads the file of the mod, once, when the dialog was not given it. The catalogue is of
  /// no workspace.
  private void Read() {
    if (_reading || _detail is not null) {
      return;
    }

    _reading = true;
    ModCatalogClient catalog = Context.Catalog;
    string id = _mod.Id;
    CancellationToken closed = _closed.Token;
    Context.Background(
        () => catalog.GetDetail(id, closed), detail => _detail = detail, _ => _failed = true,
        anyWorkspace: true);
  }

  /// The README is in the file of the mod: while that is on its way, or when it cannot be
  /// read, the bottom bar says so.
  private void ShowReadme() {
    if (_detail is not null) {
      Context.Show(new ViewerDialog(_mod.Name, _detail.Readme ?? ModDetailsText.NoReadme));
    } else if (_failed) {
      Context.Toast(ModDetailsText.NoFile, ToastKind.Bad);
    } else {
      Context.Toast(CommonText.Loading);
    }
  }
}
