using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;

namespace Lip.Cli.Tui.Pages.GetMods;

/// Asks which version of a mod to install. At the top, one row of tabs with every variant of
/// the mod that has a build for a server; ← and → choose another. Below, the versions of the
/// chosen variant that fit the game and the LeviLamina of the workspace
/// (CatalogCompatibility), newest first, and a last row that asks for what follows the @ of
/// the package, such as a branch or a commit. The catalogue lists no version when the file of
/// the mod could not be read.
public sealed class VersionDialog : Dialog {
  /// A row of the list: a version, or the row that asks for one when Version is null.
  private sealed record Row(CatalogVersion? Version);

  private readonly CatalogMod _mod;
  private readonly CatalogDetail? _detail;
  private readonly CatalogWorkspace _workspace;
  private readonly IReadOnlyList<string> _variants;
  private readonly Action<string> _install;
  private readonly ListView<Row> _list;
  private int _variant;
  // Whether the tabs have the focus, rather than the versions.
  private bool _onTabs;

  /// workspace says which game and which LeviLamina the versions must fit. install gets the
  /// package argument to install, once the dialog is closed.
  public VersionDialog(
      CatalogMod mod, CatalogDetail? detail, CatalogWorkspace workspace, Action<string> install) {
    _mod = mod;
    _detail = detail;
    _workspace = workspace;
    _install = install;
    _variants = detail?.ServerVariants() ?? [""];
    // The variant that the catalogue names as the latest when a version of it fits, otherwise
    // the first one that has a version that fits.
    int latest = Math.Max(0, _variants.ToList().IndexOf(detail?.LatestVariant ?? ""));
    int fits = Enumerable.Range(0, _variants.Count).FirstOrDefault(i => Versions(i).Count > 0, -1);
    _variant = Versions(latest).Count > 0 || fits < 0 ? latest : fits;
    // With tabs above them, ↑ on the first row goes to the tabs instead of round.
    _list = new ListView<Row>(Render) { Wrap = !Switchable };
    Refresh();
  }

  public override IReadOnlyList<Hint> Hints => _onTabs
      ? [CommonText.HintChange, new Hint("↓/Tab", CommonText.HintNext.Text), CommonText.HintClose]
      : [
        CommonText.HintMove,
        CommonText.HintSelect,
        .. Switchable ? [GetModsText.HintVariant, CommonText.HintNext] : (Hint[])[],
        .. _list.PageHints,
        CommonText.HintClose,
      ];

  private string Variant => _variants[_variant];

  /// Whether the tabs are shown: not for a mod whose only variant is the default one.
  private bool Tabs => _variants.Count > 1 || _variants[0].Length > 0;

  /// Whether there is another variant to choose: the tabs take the focus only then.
  private bool Switchable => _variants.Count > 1;

  /// The versions of a variant that fit the workspace.
  private IReadOnlyList<CatalogVersion> Versions(int variant) =>
      _detail is null ? [] : CatalogCompatibility.Versions(_detail, _variants[variant], _workspace);

  /// The package argument of the mod in the chosen variant, at a version when given.
  private string Package(string? version) =>
      _mod.Id + (Variant.Length == 0 ? "" : "#" + Variant)
          + (version is null ? "" : "@" + version);

  /// What is said above the rows: that the file of the mod cannot be read, or that no version
  /// of the chosen variant fits what the workspace has, naming it; nothing otherwise.
  private (string Text, Style Style)? Note() {
    if (_detail is null) {
      return (ModDetailsText.NoFile, Theme.Muted);
    }

    if (_list.Items.Count > 1 || !_detail.Versions.Any(v => v.Variants.Contains(Variant))) {
      return null;
    }

    // What rules the versions out: LeviLamina when the variant depends on it, otherwise the
    // game.
    string game = _workspace.Game ?? "";
    return (
        !CatalogCompatibility.NeedsLoader(_detail, Variant) ? GetModsText.NoVersionForGame(game)
            : _workspace.Loader is { } loader ? GetModsText.NoVersionForLoader(loader)
            : GetModsText.NoVersionWithoutLoader(game),
        Theme.Warning);
  }

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 64);
    (string Text, Style Style)? note = Note();
    List<string> noteRows = note is null ? [] : TextWidth.Wrap(note.Value.Text, width - 4);
    int above = (Tabs ? 2 : 0) + noteRows.Count;
    Rect inner = Ui.DialogFrame(screen, _mod.Name, width, above + _list.Items.Count + 4);
    int y = inner.Y;
    if (Tabs) {
      DrawTabs(screen, inner.X, y, inner.Width);
      screen.Write(inner.X, y + 1, new string('─', inner.Width), Theme.Border);
      y += 2;
    }

    foreach (string row in noteRows) {
      screen.Write(inner.X, y++, row, note!.Value.Style, inner.Right);
    }

    _list.Draw(
        screen, new Rect(inner.X, y, inner.Width, Math.Max(0, inner.Bottom - 2 - y)), !_onTabs);
    screen.Write(inner.X, inner.Bottom - 2, new string('─', inner.Width), Theme.Border);
    // What the selected row installs.
    screen.WriteFit(
        inner.X, inner.Bottom - 1, inner.Width,
        _list.Current.Version is { } version ? Package(version.Version) : Package(null) + "@…",
        Theme.Muted);
  }

  /// The variants as tabs on one row of width columns, the chosen one in brackets. When they
  /// do not all fit, the row starts at the first variant that leaves the chosen one in sight,
  /// and ‹ and › at its ends say that there are more at that side. It is never more than one
  /// row.
  private void DrawTabs(ScreenBuffer screen, int x, int y, int width) {
    List<string> cells = [
      .. _variants.Select((variant, i) => {
        string name = variant.Length == 0 ? GetModsText.DefaultVariant : variant;
        return i == _variant ? $"[{name}]" : $" {name} ";
      }),
    ];
    int[] widths = [.. cells.Select(TextWidth.Of)];
    Style Of(int i) => i != _variant ? default : _onTabs ? Theme.Selected : Theme.Accent;
    if (widths.Sum() <= width) {
      for (int i = 0; i < cells.Count; i++) {
        x = screen.Write(x, y, cells[i], Of(i));
      }

      return;
    }

    // Two columns at each end are those of the marks.
    int room = Math.Max(1, width - 4);
    int first = 0;
    while (first < _variant && widths[first.._variant].Sum() + widths[_variant] > room) {
      first++;
    }

    // No room is left empty at the right while variants are hidden at the left.
    while (first > 0 && widths[(first - 1)..].Sum() <= room) {
      first--;
    }

    int at = x + 2;
    int end = x + 2 + room;
    int shown = first;
    for (; shown < cells.Count; shown++) {
      if (widths[shown] > end - at) {
        // What is left takes the start of the next variant, when that says something.
        if (shown == first || end - at >= 4) {
          screen.Write(at, y, TextWidth.Fit(cells[shown], end - at), Of(shown));
        }

        break;
      }

      at = screen.Write(at, y, cells[shown], Of(shown));
    }

    if (first > 0) {
      screen.Write(x, y, "‹", Theme.Muted);
    }

    if (shown < cells.Count) {
      screen.Write(x + width - 1, y, "›", Theme.Muted);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Tab when Switchable:
        _onTabs = !_onTabs;
        return;
      case ConsoleKey.LeftArrow when Switchable:
        Switch(_variants.Count - 1);
        return;
      case ConsoleKey.RightArrow when Switchable:
        Switch(1);
        return;
    }

    if (_onTabs) {
      if (key.Key == ConsoleKey.DownArrow) {
        _onTabs = false;
        _list.Selected = 0;
      }

      return;
    }

    switch (key.Key) {
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        if (_list.Current.Version is { } version) {
          Close();
          _install(Package(version.Version));
        } else {
          Custom();
        }

        return;
      case ConsoleKey.UpArrow when Switchable && _list.Selected == 0:
        _onTabs = true;
        return;
    }

    _list.OnKey(key);
  }

  /// Chooses the variant that is step after the chosen one, round.
  private void Switch(int step) {
    _variant = (_variant + step) % _variants.Count;
    Refresh();
  }

  /// The rows of the chosen variant, the newest version selected.
  private void Refresh() {
    _list.Items = [.. Versions(_variant).Select(version => new Row(version)), new Row(null)];
    _list.Selected = 0;
  }

  private static StyledLine Render(Row row) =>
      row.Version is not { } version
          ? new StyledLine(GetModsText.Custom)
          : new StyledLine()
              .Add(Cells.Pad(version.Version, 22) + " ")
              .Add(Cells.Pad(Cells.Date(version.Released), 12), Theme.Muted)
              .Add(version.Prerelease ? GetModsText.Prerelease : "", Theme.Warning);

  /// Asks for what follows the @ of the package; cancelling comes back to this dialog.
  private void Custom() {
    AppContext context = Context;
    string package = Package(null);
    Close();
    context.Show(new PromptDialog(
        GetModsText.CustomTitle,
        [new PromptField(package + "@", "", GetModsText.CustomExample)],
        values => _install($"{package}@{values[0].TrimStart('@')}"),
        () => context.Show(this),
        values => values[0].TrimStart('@').Length == 0 ? CommonText.Required : null));
  }
}
