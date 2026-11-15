using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;

namespace Lip.Cli.Tui.Pages.GetMods;

/// Asks which version of a mod to install: a row for the variant when the mod has several,
/// the versions that the catalogue lists for that variant, newest first, and a row that asks
/// for what follows the @ of the package, such as a branch or a commit. The catalogue lists no
/// version when the file of the mod could not be read.
public sealed class VersionDialog : Dialog {
  private enum RowKind {
    Variant,
    Version,
    Custom,
  }

  /// A row of the dialog; Version is that of a row of the kind Version only.
  private sealed record Row(RowKind Kind, CatalogVersion? Version);

  private readonly CatalogMod _mod;
  private readonly IReadOnlyList<CatalogVersion> _all;
  private readonly List<string> _variants;
  private readonly Action<string> _install;
  private readonly ListView<Row> _list;
  private int _variant;

  /// install gets the package argument to install, once the dialog is closed.
  public VersionDialog(CatalogMod mod, CatalogDetail? detail, Action<string> install) {
    _mod = mod;
    _all = detail?.Versions ?? [];
    _install = install;
    // The variants as the versions list them, the default one first.
    _variants = [
      .. _all.SelectMany(version => version.Variants).Distinct(StringComparer.Ordinal)
          .OrderBy(variant => variant.Length == 0 ? 0 : 1),
    ];
    if (_variants.Count == 0) {
      _variants.Add("");
    }

    _variant = Math.Max(0, _variants.IndexOf(detail?.LatestVariant ?? ""));
    _list = new ListView<Row>(Render) { Wrap = true };
    Refresh();
    // The newest version rather than the variant.
    _list.Selected = _variants.Count > 1 ? 1 : 0;
  }

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintMove,
    _list.Current.Kind == RowKind.Variant ? CommonText.HintChange : CommonText.HintSelect,
    CommonText.HintClose,
  ];

  private string Variant => _variants[_variant];

  /// The package argument of the mod in the chosen variant, at a version when given.
  private string Package(string? version) =>
      _mod.Id + (Variant.Length == 0 ? "" : "#" + Variant)
          + (version is null ? "" : "@" + version);

  public override void Draw(ScreenBuffer screen) {
    Rect inner = Ui.DialogFrame(
        screen, _mod.Name, Ui.DialogWidth(screen, 60), _list.Items.Count + 4);
    _list.Draw(screen, inner.Shrink(bottom: 2), true);
    screen.Write(inner.X, inner.Bottom - 2, new string('─', inner.Width), Theme.Border);
    // What the selected row installs.
    Row row = _list.Current;
    screen.WriteFit(
        inner.X, inner.Bottom - 1, inner.Width,
        row.Kind == RowKind.Custom ? Package(null) + "@…" : Package(row.Version?.Version),
        Theme.Muted);
  }

  public override void OnKey(ConsoleKeyInfo key) {
    Row row = _list.Current;
    if (row.Kind == RowKind.Variant) {
      int step = key.Key switch {
        ConsoleKey.LeftArrow => _variants.Count - 1,
        ConsoleKey.RightArrow or ConsoleKey.Spacebar => 1,
        _ => 0,
      };
      if (step != 0) {
        _variant = (_variant + step) % _variants.Count;
        Refresh();
        return;
      }
    }

    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        if (row.Kind == RowKind.Custom) {
          Custom();
        } else if (row.Version is { } version) {
          Close();
          _install(Package(version.Version));
        }

        return;
    }

    _list.OnKey(key);
  }

  /// The rows of the chosen variant.
  private void Refresh() {
    List<Row> rows = [];
    if (_variants.Count > 1) {
      rows.Add(new Row(RowKind.Variant, null));
    }

    rows.AddRange(_all
        .Where(version => version.Variants.Contains(Variant))
        .Select(version => new Row(RowKind.Version, version)));
    rows.Add(new Row(RowKind.Custom, null));
    _list.Items = rows;
  }

  private StyledLine Render(Row row) {
    switch (row.Kind) {
      case RowKind.Variant:
        return new StyledLine()
            .Add(GetModsText.Variant + "  ", Theme.Muted)
            .Add($"‹ {(Variant.Length == 0 ? GetModsText.DefaultVariant : Variant)} ›");
      case RowKind.Custom:
        return new StyledLine(GetModsText.Custom);
      default:
        CatalogVersion version = row.Version!;
        return new StyledLine()
            .Add(Cells.Pad(version.Version, 22) + " ")
            .Add(Cells.Pad(Cells.Date(version.Released), 12), Theme.Muted)
            .Add(version.Prerelease ? GetModsText.Prerelease : "", Theme.Warning);
    }
  }

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
