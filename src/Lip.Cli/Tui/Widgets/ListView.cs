using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// A list of rows, one of them selected, that scrolls. ↑ and ↓ move the selection, PgUp, PgDn,
/// Home and End move it further; with MultiSelect, Space checks the selected row. Enter is left
/// to the owner, which opens the row or shows its action menu.
///
/// Without Wrap, ↑ on the first row and ↓ on the last are not handled, so that the owner can
/// move the focus to what is above or below the list.
public sealed class ListView<T>(Func<T, StyledLine> render) where T : notnull {
  private IReadOnlyList<T> _items = [];
  private int _selected;
  private int _top;
  private int _rows = 1;

  /// The rows. Setting them keeps the selection at its index, and the checks of the rows that
  /// are still there.
  public IReadOnlyList<T> Items {
    get => _items;
    set {
      _items = value;
      _selected = Math.Clamp(_selected, 0, Math.Max(0, value.Count - 1));
      Checked.IntersectWith(value);
    }
  }

  /// The index of the selected row.
  public int Selected {
    get => _selected;
    set => _selected = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1));
  }

  /// Whether ↑ on the first row goes to the last, and ↓ on the last to the first.
  public bool Wrap { get; init; }

  /// Whether Space checks rows.
  public bool MultiSelect { get; init; }

  /// Which rows Space checks: all of them when not given.
  public Func<T, bool>? CanCheck { get; init; }

  /// What an empty list shows.
  public Func<string>? Empty { get; init; }

  /// The rows that are checked.
  public HashSet<T> Checked { get; } = [];

  /// How many rows the list showed when it was last drawn.
  public int VisibleRows => _rows;

  /// Whether there is a selected row.
  public bool HasCurrent => _selected < _items.Count;

  /// The selected row. Throws when the list is empty: see HasCurrent.
  public T Current => _items[_selected];

  /// The checked rows, in the order of the list; when none is checked, the selected row.
  public IReadOnlyList<T> Targets =>
      Checked.Count > 0 ? [.. _items.Where(Checked.Contains)] : HasCurrent ? [Current] : [];

  public IReadOnlyList<Hint> Hints =>
      MultiSelect ? [CommonText.HintMove, CommonText.HintCheck] : [CommonText.HintMove];

  /// The keys that page through the list, while it has more rows than it shows: for the end
  /// of the hints of the owner, where the bottom bar leaves hints out first.
  public IReadOnlyList<Hint> PageHints =>
      _items.Count > _rows ? [CommonText.HintPage, CommonText.HintEnds] : [];

  /// Selects the first row that match holds for; false when there is none.
  public bool Select(Func<T, bool> match) {
    for (int i = 0; i < _items.Count; i++) {
      if (match(_items[i])) {
        _selected = i;
        return true;
      }
    }

    return false;
  }

  /// Moves the selection, or checks a row, by a key; false when the key does neither.
  public bool OnKey(ConsoleKeyInfo key) {
    if (_items.Count == 0) {
      return false;
    }

    int last = _items.Count - 1;
    switch (key.Key) {
      case ConsoleKey.UpArrow:
        if (_selected == 0 && !Wrap) {
          return false;
        }

        _selected = _selected == 0 ? last : _selected - 1;
        return true;
      case ConsoleKey.DownArrow:
        if (_selected == last && !Wrap) {
          return false;
        }

        _selected = _selected == last ? 0 : _selected + 1;
        return true;
      case ConsoleKey.PageUp:
        _selected = Math.Max(0, _selected - _rows);
        return true;
      case ConsoleKey.PageDown:
        _selected = Math.Min(last, _selected + _rows);
        return true;
      case ConsoleKey.Home:
        _selected = 0;
        return true;
      case ConsoleKey.End:
        _selected = last;
        return true;
      case ConsoleKey.Spacebar when MultiSelect:
        T item = _items[_selected];
        if (CanCheck is null || CanCheck(item)) {
          if (!Checked.Remove(item)) {
            Checked.Add(item);
          }
        }

        return true;
      default:
        return false;
    }
  }

  /// Draws the rows that fit rect, the selected one among them, highlighted as the list has the
  /// focus or not.
  public void Draw(ScreenBuffer screen, Rect rect, bool focused) {
    _rows = Math.Max(1, rect.Height);
    if (rect.Height < 1 || rect.Width < 1) {
      return;
    }

    if (_items.Count == 0) {
      screen.WriteFit(rect.X, rect.Y, rect.Width, Empty?.Invoke() ?? "", Theme.Muted);
      return;
    }

    _top = Ui.ScrollTo(_selected, Math.Min(_top, Math.Max(0, _items.Count - _rows)), _rows);
    for (int i = 0; i < _rows && _top + i < _items.Count; i++) {
      T item = _items[_top + i];
      int y = rect.Y + i;
      int x = rect.X;
      if (MultiSelect) {
        bool checkable = CanCheck is null || CanCheck(item);
        x = screen.Write(
            x, y, Checked.Contains(item) ? "[x] " : checkable ? "[ ] " : "    ",
            Checked.Contains(item) ? Theme.Good : Theme.Muted, rect.Right);
      }

      screen.Write(x, y, render(item), rect.Right);
      if (_top + i == _selected) {
        screen.Restyle(
            rect.X, y, rect.Width, focused ? Theme.Selected : Theme.SelectedInactive);
      }
    }

    Ui.ScrollMarks(screen, rect, _top > 0, _top + _rows < _items.Count);
  }
}
