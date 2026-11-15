using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// Text that scrolls: a README, a JSON text, the messages of a command. ↑ and ↓ scroll by a
/// row, PgUp and PgDn by a page, Home and End to its start and its end.
public sealed class TextViewer {
  private IReadOnlyList<StyledLine> _lines = [];
  private List<StyledLine> _wrapped = [];
  private int _wrappedWidth = -1;
  private int _top;
  private int _rows = 1;

  /// The lines; each is wrapped to the width it is drawn in.
  public IReadOnlyList<StyledLine> Lines {
    get => _lines;
    set {
      _lines = value;
      _wrappedWidth = -1;
    }
  }

  /// Whether more rows are shown than fit: the keys scroll only then.
  public bool Scrollable => _wrapped.Count > _rows;

  public IReadOnlyList<Hint> Hints => Scrollable ? [CommonText.HintScroll] : [];

  /// Shows plain text, one line per line of it.
  public void SetText(string text, Style style = default) =>
      Lines = [
        .. text.Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .Select(line => new StyledLine(line, style)),
      ];

  /// Shows the text from its start.
  public void ScrollToStart() => _top = 0;

  /// Shows the end of the text, as for a log whose end says how it went.
  public void ScrollToEnd() => _top = int.MaxValue;

  /// The rows that the lines take in width columns.
  public int RowsFor(int width) {
    Wrap(width);
    return _wrapped.Count;
  }

  /// Scrolls by a key; false when the key does not scroll, or nothing is hidden.
  public bool OnKey(ConsoleKeyInfo key) {
    if (!Scrollable) {
      return false;
    }

    int last = _wrapped.Count - _rows;
    int top = Math.Clamp(_top, 0, last);
    int moved = key.Key switch {
      ConsoleKey.UpArrow => top - 1,
      ConsoleKey.DownArrow => top + 1,
      ConsoleKey.PageUp => top - _rows,
      ConsoleKey.PageDown => top + _rows,
      ConsoleKey.Home => 0,
      ConsoleKey.End => last,
      _ => int.MinValue,
    };
    if (moved == int.MinValue) {
      return false;
    }

    _top = Math.Clamp(moved, 0, last);
    return true;
  }

  /// Draws the rows that fit rect.
  public void Draw(ScreenBuffer screen, Rect rect) {
    _rows = Math.Max(1, rect.Height);
    if (rect.Height < 1 || rect.Width < 1) {
      return;
    }

    Wrap(rect.Width);
    _top = Math.Clamp(_top, 0, Math.Max(0, _wrapped.Count - _rows));
    for (int i = 0; i < _rows && _top + i < _wrapped.Count; i++) {
      screen.Write(rect.X, rect.Y + i, _wrapped[_top + i], rect.Right);
    }

    Ui.ScrollMarks(screen, rect, _top > 0, _top + _rows < _wrapped.Count);
  }

  private void Wrap(int width) {
    if (width == _wrappedWidth) {
      return;
    }

    _wrappedWidth = width;
    _wrapped = [.. _lines.SelectMany(line => WrapLine(line, width))];
  }

  /// A styled line as rows of at most width columns, broken at spaces where there are any;
  /// the spaces at the start of the line are kept.
  private static List<StyledLine> WrapLine(StyledLine line, int width) {
    width = Math.Max(1, width);
    if (TextWidth.Of(line.PlainText) <= width) {
      return [line];
    }

    List<(string Text, int Width, Style Style)> cells = [];
    foreach (Span span in line.Spans) {
      foreach (System.Text.Rune rune in span.Text.EnumerateRunes()) {
        System.Text.Rune shown = rune.Value == 9 ? new System.Text.Rune(' ') : rune;
        cells.Add((shown.ToString(), TextWidth.Of(shown), span.Style));
      }
    }

    List<StyledLine> rows = [];
    int start = 0;
    while (start < cells.Count) {
      // The cells that fit, then back to the last space among them when the line goes on.
      int end = start;
      int used = 0;
      while (end < cells.Count && used + cells[end].Width <= width) {
        used += cells[end].Width;
        end++;
      }

      end = Math.Max(end, start + 1);
      int next = end;
      if (end < cells.Count && cells[end].Text == " ") {
        next = end + 1;
      } else if (end < cells.Count) {
        int space = end - 1;
        while (space > start && cells[space].Text != " ") {
          space--;
        }

        if (space > start && cells[start..space].Any(c => c.Text != " ")) {
          end = space;
          next = space + 1;
        }
      }

      StyledLine row = new();
      for (int i = start; i < end;) {
        int run = i;
        System.Text.StringBuilder text = new();
        while (run < end && cells[run].Style == cells[i].Style) {
          text.Append(cells[run].Text);
          run++;
        }

        row.Add(text.ToString(), cells[i].Style);
        i = run;
      }

      rows.Add(row);
      start = next;
    }

    return rows;
  }
}
