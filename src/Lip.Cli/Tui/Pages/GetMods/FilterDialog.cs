using System.Globalization;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;

namespace Lip.Cli.Tui.Pages.GetMods;

/// The filter panel of the Get mods page: a row that clears the filters, then the sections,
/// each a heading that Enter opens and closes, with its values below it while it is open.
/// Space on a value goes to its next state. The mods behind the panel follow at once.
public sealed class FilterDialog : Dialog {
  private enum RowKind {
    Clear,
    Heading,
    Value,
  }

  /// A row of the panel; Value is that of a row of the kind Value only.
  private sealed record Row(RowKind Kind, FilterSection Section, FilterValue? Value);

  private readonly CatalogFilters _filters;
  private readonly Func<int> _matches;
  private readonly Action _changed;
  private readonly ListView<Row> _list;
  private int _width = 1;
  private int _revision;

  /// matches gives the number of mods that the filters leave; changed is called whenever the
  /// user chose something else.
  public FilterDialog(CatalogFilters filters, Func<int> matches, Action changed) {
    _filters = filters;
    _matches = matches;
    _changed = changed;
    _list = new ListView<Row>(Render) { Wrap = true };
    Refresh();
    // The first heading rather than the row that clears.
    _list.Selected = 1;
  }

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintMove,
    _list.Current.Kind switch {
      RowKind.Clear => CommonText.HintSelect,
      RowKind.Heading => CommonText.HintOpen,
      _ => CommonText.HintToggleEither,
    },
    .. _list.PageHints,
    CommonText.HintClose,
  ];

  public override void Draw(ScreenBuffer screen) {
    // The catalogue was read again behind the panel: its values are other ones.
    if (_revision != _filters.Revision) {
      Refresh();
    }

    Rect inner = Ui.DialogFrame(
        screen, GetModsText.FiltersTitle(_filters.Count), Ui.DialogWidth(screen, 60),
        Math.Min(screen.Height - 2, 24));
    // The last column is that of the marks of rows above and below.
    _width = Math.Max(1, inner.Width - 1);
    _list.Draw(screen, inner.Shrink(bottom: 2), true);
    screen.Write(inner.X, inner.Bottom - 2, new string('─', inner.Width), Theme.Border);
    string count = GetModsText.Count(_matches());
    screen.Write(inner.Right - TextWidth.Of(count), inner.Bottom - 1, count, Theme.Muted);
    if (_list.Current is { Value: { } value }) {
      FilterState state = _filters.StateOf(value);
      screen.WriteFit(
          inner.X, inner.Bottom - 1, inner.Width - TextWidth.Of(count) - 1,
          GetModsText.State(state), StateStyle(state));
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        Row row = _list.Current;
        if (row.Kind == RowKind.Heading) {
          if (!_filters.Expanded.Remove(row.Section)) {
            _filters.Expanded.Add(row.Section);
          }

          Refresh();
          return;
        }

        if (row.Value is { } value) {
          _filters.Cycle(value);
        } else {
          _filters.Clear();
        }

        _changed();
        return;
    }

    _list.OnKey(key);
  }

  /// The rows to show: the heading of every section that has values, and the values of the
  /// sections that are open.
  private void Refresh() {
    List<Row> rows = [new Row(RowKind.Clear, default, null)];
    foreach (IGrouping<FilterSection, FilterValue> section in
        _filters.Values.GroupBy(value => value.Section).OrderBy(group => group.Key)) {
      rows.Add(new Row(RowKind.Heading, section.Key, null));
      if (_filters.Expanded.Contains(section.Key)) {
        rows.AddRange(section.Select(value => new Row(RowKind.Value, section.Key, value)));
      }
    }

    _list.Items = rows;
    _revision = _filters.Revision;
  }

  private StyledLine Render(Row row) {
    if (row.Kind == RowKind.Clear) {
      return new StyledLine(GetModsText.ClearFilters);
    }

    if (row.Value is not { } value) {
      // A heading says how many of its values are chosen, or else how many it has.
      int chosen = _filters.CountOf(row.Section);
      string note = chosen > 0
          ? GetModsText.Selected(chosen)
          : _filters.Values.Count(v => v.Section == row.Section)
              .ToString(CultureInfo.InvariantCulture);
      return new StyledLine()
          .Add(_filters.Expanded.Contains(row.Section) ? "▾ " : "▸ ", Theme.Accent)
          .Add(Cells.Pad(GetModsText.Section(row.Section), NoteRoom(2, note)) + " ", Theme.Strong)
          .Add(note, chosen > 0 ? Theme.Good : Theme.Muted);
    }

    FilterState state = _filters.StateOf(value);
    string mark = state switch {
      FilterState.Included => "[+] ",
      FilterState.Excluded => "[-] ",
      _ => "[ ] ",
    };
    string label = value.Section switch {
      FilterSection.License => GetModsText.LicenseFamily(value.Key, value.Label),
      FilterSection.Tags => GetModsText.Tag(value.Key, value.Label),
      _ => value.Label,
    };
    string count = value.Count.ToString(CultureInfo.InvariantCulture);
    return new StyledLine()
        .Add("  " + mark, StateStyle(state))
        .Add(Cells.Pad(label, NoteRoom(6, count)) + " ", StateStyle(state))
        .Add(count, Theme.Muted);
  }

  /// The columns between what a row starts with, of used columns, and the note at its right
  /// end, with a space before the note.
  private int NoteRoom(int used, string note) =>
      Math.Max(1, _width - used - TextWidth.Of(note) - 1);

  private static Style StateStyle(FilterState state) => state switch {
    FilterState.Included => Theme.Good,
    FilterState.Excluded => Theme.Bad,
    _ => default,
  };
}
