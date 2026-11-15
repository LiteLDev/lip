using System.Globalization;
using System.Text;

namespace Lip.Cli.Tui;

/// Colors of a Style: 0 is the default color of the terminal, 1 to 256 the color of the palette
/// with that number less one, and a value with TrueColor set the RGB color in its low 24 bits.
public static class Color {
  public const int Default = 0;
  public const int Red = 2;
  public const int Green = 3;
  public const int Yellow = 4;
  public const int Blue = 5;
  public const int Magenta = 6;
  public const int Cyan = 7;
  public const int BrightBlack = 9;
  public const int BrightWhite = 16;
  public const int TrueColor = 0x1000000;
}

/// How a cell is drawn. The default value is the default of the terminal.
public readonly record struct Style(int Fg, int Bg, bool Bold, bool Underline, bool Inverse) {
  public static Style Of(int fg = Color.Default, int bg = Color.Default, bool bold = false,
      bool underline = false, bool inverse = false) =>
      new(fg, bg, bold, underline, inverse);

  /// The escape sequence that sets the style, after resetting the one before.
  public string Ansi() {
    StringBuilder builder = new("\x1b[0");
    if (Bold) {
      builder.Append(";1");
    }

    if (Underline) {
      builder.Append(";4");
    }

    if (Inverse) {
      builder.Append(";7");
    }

    AppendColor(builder, Fg, foreground: true);
    AppendColor(builder, Bg, foreground: false);
    return builder.Append('m').ToString();
  }

  private static void AppendColor(StringBuilder builder, int color, bool foreground) {
    if (color == Color.Default) {
      return;
    }

    if ((color & Color.TrueColor) != 0) {
      builder.Append(CultureInfo.InvariantCulture,
          $";{(foreground ? 38 : 48)};2;{(color >> 16) & 0xFF};{(color >> 8) & 0xFF};{color & 0xFF}");
      return;
    }

    int index = color - 1;
    if (index < 8) {
      builder.Append(CultureInfo.InvariantCulture, $";{(foreground ? 30 : 40) + index}");
    } else if (index < 16) {
      builder.Append(CultureInfo.InvariantCulture, $";{(foreground ? 90 : 100) + index - 8}");
    } else {
      builder.Append(CultureInfo.InvariantCulture, $";{(foreground ? 38 : 48)};5;{index}");
    }
  }
}

/// The styles of the interface.
public static class Theme {
  public static readonly Style Text = default;
  public static readonly Style Muted = Style.Of(Color.BrightBlack);
  public static readonly Style Accent = Style.Of(Color.Cyan, bold: true);
  public static readonly Style Strong = Style.Of(bold: true);
  public static readonly Style Selected = Style.Of(Color.BrightWhite, Color.Blue);
  // The selected row of a list that does not have the focus.
  public static readonly Style SelectedInactive = Style.Of(Color.BrightWhite, Color.BrightBlack);
  public static readonly Style Good = Style.Of(Color.Green);
  public static readonly Style Warning = Style.Of(Color.Yellow);
  public static readonly Style Bad = Style.Of(Color.Red, bold: true);
  public static readonly Style Tag = Style.Of(Color.Magenta);
  public static readonly Style Border = Style.Of(Color.BrightBlack);
}

/// A run of text in one style.
public readonly record struct Span(string Text, Style Style);

/// A line of spans.
public sealed class StyledLine {
  public List<Span> Spans { get; } = [];

  public StyledLine() {
  }

  public StyledLine(string text, Style style = default) {
    Spans.Add(new Span(text, style));
  }

  public StyledLine Add(string text, Style style = default) {
    Spans.Add(new Span(text, style));
    return this;
  }

  public string PlainText => string.Concat(Spans.Select(span => span.Text));
}

/// The number of columns text takes in a terminal.
public static class TextWidth {
  public static int Of(Rune rune) {
    int value = rune.Value;
    if (value < 0x20 || (value >= 0x7F && value < 0xA0)) {
      return 0;
    }

    switch (Rune.GetUnicodeCategory(rune)) {
      case UnicodeCategory.NonSpacingMark:
      case UnicodeCategory.EnclosingMark:
      case UnicodeCategory.Format:
        return 0;
    }

    return value is (>= 0x1100 and <= 0x115F)
        or (>= 0x2E80 and <= 0x303E)
        or (>= 0x3041 and <= 0x33FF)
        or (>= 0x3400 and <= 0x4DBF)
        or (>= 0x4E00 and <= 0x9FFF)
        or (>= 0xA000 and <= 0xA4CF)
        or (>= 0xAC00 and <= 0xD7A3)
        or (>= 0xF900 and <= 0xFAFF)
        or (>= 0xFE30 and <= 0xFE4F)
        or (>= 0xFF00 and <= 0xFF60)
        or (>= 0xFFE0 and <= 0xFFE6)
        or (>= 0x1F300 and <= 0x1FAFF)
        or (>= 0x20000 and <= 0x3FFFD)
        ? 2
        : 1;
  }

  public static int Of(string text) {
    int width = 0;
    foreach (Rune rune in text.EnumerateRunes()) {
      width += Of(rune);
    }

    return width;
  }

  /// The text cut to at most width columns, ending in an ellipsis when it was cut.
  public static string Fit(string text, int width) {
    if (Of(text) <= width) {
      return text;
    }

    if (width <= 0) {
      return "";
    }

    StringBuilder builder = new();
    int used = 0;
    foreach (Rune rune in text.EnumerateRunes()) {
      int w = Of(rune);
      if (used + w > width - 1) {
        break;
      }

      builder.Append(rune.ToString());
      used += w;
    }

    return builder.Append('…').ToString();
  }

  /// The text cut to its last width columns, starting with an ellipsis when it was cut: the end
  /// of a path says more than its start.
  public static string FitEnd(string text, int width) {
    if (Of(text) <= width) {
      return text;
    }

    if (width <= 0) {
      return "";
    }

    int start = text.Length;
    int used = 1;
    foreach (Rune rune in text.EnumerateRunes().Reverse()) {
      int w = Of(rune);
      if (used + w > width) {
        break;
      }

      used += w;
      start -= rune.Utf16SequenceLength;
    }

    return "…" + text[start..];
  }

  // What does not start a line, and what does not end one, where a line is broken between
  // two characters and not at a space.
  private const string NotAtStart = "，。、；：！？）」』》〉】”’…—,.;:!?)]}%";
  private const string NotAtEnd = "（「『《〈【“‘([{";

  /// Whether a line may be broken between two characters of a word: one of them is two
  /// columns wide, the first is no mark that opens, and the second is neither a mark that
  /// closes nor part of the character before it, as a combining mark, a variation selector,
  /// what follows a zero width joiner, and an emoji modifier are.
  private static bool BreaksBetween(Rune first, Rune second) =>
      (Of(first) == 2 || Of(second) == 2)
      && Of(second) > 0
      && first.Value != 0x200D
      && second.Value is < 0x1F3FB or > 0x1F3FF
      && !(second.IsBmp && NotAtStart.Contains((char)second.Value))
      && !(first.IsBmp && NotAtEnd.Contains((char)first.Value));

  // What a word that is longer than a line is broken after, where it has any: the marks
  // between the parts of an identity, a path, a URL or a file name.
  private const string SoftBreaks = "/\\.-_#@";

  private static readonly Rune _blank = new(' ');

  /// Whether a word that is longer than a line may be broken between two of its characters:
  /// after a mark that separates its parts, and not inside a run of them, as in "://".
  private static bool SoftBreakBetween(Rune first, Rune second) =>
      first.IsBmp && SoftBreaks.Contains((char)first.Value)
      && !(second.IsBmp && SoftBreaks.Contains((char)second.Value));

  /// The text as lines of at most width columns, broken at spaces where there are any, and
  /// before or after a character that is two columns wide, as Chinese text is broken, but not
  /// before a mark that closes or after one that opens. A word that is longer than a line is
  /// broken after the marks between its parts (/ \ . - _ # @) where it has any, and where the
  /// line ends otherwise, never inside a character.
  public static List<string> Wrap(string text, int width) {
    List<string> lines = [];
    width = Math.Max(1, width);
    foreach (string paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n')) {
      StringBuilder line = new();
      int used = 0;
      foreach ((string unit, bool spaced) in Units(paragraph.Replace('\t', ' '), width)) {
        int w = Of(unit);
        bool space = spaced && used > 0;
        if (used > 0 && used + (space ? 1 : 0) + w > width) {
          lines.Add(line.ToString());
          line.Clear();
          used = 0;
          space = false;
        }

        if (space) {
          line.Append(' ');
          used++;
        }

        foreach (Rune rune in unit.EnumerateRunes()) {
          int rw = Of(rune);
          if (used > 0 && used + rw > width) {
            lines.Add(line.ToString());
            line.Clear();
            used = 0;
          }

          line.Append(rune.ToString());
          used += rw;
        }
      }

      lines.Add(line.ToString());
    }

    return lines;
  }

  /// The parts of a paragraph that a line is not broken inside of unless it must be, each
  /// with whether a space is before it: the words, and in a word each character that is two
  /// columns wide, with the marks that stay with it. A word that is longer than width columns
  /// is given as its parts, each ending with the mark that follows it.
  private static IEnumerable<(string Unit, bool Spaced)> Units(string paragraph, int width) {
    bool first = true;
    foreach (string word in paragraph.Split(' ')) {
      StringBuilder unit = new();
      bool spaced = !first;
      bool parts = Of(word) > width;
      first = false;
      Rune? before = null;
      foreach (Rune rune in word.EnumerateRunes()) {
        if (before is { } last
            && (BreaksBetween(last, rune) || (parts && SoftBreakBetween(last, rune)))) {
          yield return (unit.ToString(), spaced);
          unit.Clear();
          spaced = false;
        }

        unit.Append(rune.ToString());
        before = rune;
      }

      yield return (unit.ToString(), spaced);
    }
  }

  /// A styled line as rows of at most width columns, broken as Wrap breaks a text: at a
  /// space, which then ends the row and is not shown; at a character that is two columns
  /// wide; in a word that is longer than a row after the marks between its parts; and where
  /// the row ends otherwise, never inside a character. The spaces at the start of the line
  /// are kept, and a tab of a line that is broken is a space. A line that fits is its one
  /// row, as it is.
  public static List<StyledLine> Wrap(StyledLine line, int width) {
    width = Math.Max(1, width);
    // Most lines fit, and are the row as they are.
    int columns = 0;
    foreach (Span span in line.Spans) {
      columns += Of(span.Text);
    }

    if (columns <= width) {
      return [line];
    }

    List<(Rune Rune, int Width, Style Style)> cells = [];
    foreach (Span span in line.Spans) {
      foreach (Rune rune in span.Text.EnumerateRunes()) {
        Rune shown = rune.Value == 9 ? _blank : rune;
        cells.Add((shown, Of(shown), span.Style));
      }
    }

    List<StyledLine> rows = [];
    int start = 0;
    while (start < cells.Count) {
      // The cells that fit, with what takes no column after them.
      int end = start;
      int used = 0;
      while (end < cells.Count && used + cells[end].Width <= width) {
        used += cells[end].Width;
        end++;
      }

      end = Math.Max(end, start + 1);
      while (end < cells.Count && cells[end].Width == 0) {
        end++;
      }

      int next = end;
      if (end < cells.Count && cells[end].Rune == _blank) {
        next = end + 1;
      } else if (end < cells.Count) {
        (end, next) = BreakBefore(cells, start, end, width);
      }

      StyledLine row = new();
      for (int i = start; i < end;) {
        int run = i;
        StringBuilder text = new();
        while (run < end && cells[run].Style == cells[i].Style) {
          text.Append(cells[run].Rune.ToString());
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

  /// Where the row of the cells from start that ends before the cell at end, which goes on in
  /// a word, is broken instead: the cell that the row ends before, and the cell that the next
  /// row starts at. That is the last space of the row, or the last place between two wide
  /// characters, unless the word after it is longer than a row and has a mark between its
  /// parts in this row: then after the last such mark. A row is never left with spaces only.
  private static (int End, int Next) BreakBefore(
      List<(Rune Rune, int Width, Style Style)> cells, int start, int end, int width) {
    int content = start;
    while (content < end && cells[content].Rune == _blank) {
      content++;
    }

    (int End, int Next)? word = null;
    int soft = -1;
    for (int at = end; at > content; at--) {
      Rune before = cells[at - 1].Rune;
      Rune after = cells[at].Rune;
      if (after == _blank) {
        continue;
      }

      if (before == _blank) {
        // The space ends the row; the spaces before it go with it.
        int last = at - 1;
        while (last > content && cells[last - 1].Rune == _blank) {
          last--;
        }

        word = (last, at);
        break;
      }

      if (BreaksBetween(before, after)) {
        word = (at, at);
        break;
      }

      if (soft < 0 && SoftBreakBetween(before, after)) {
        soft = at;
      }
    }

    if (word is not { } found) {
      return soft >= 0 ? (soft, soft) : (end, end);
    }

    if (soft < 0) {
      return found;
    }

    // The word that the next row would start with, up to its next space or wide character.
    int columns = 0;
    for (int i = found.Next; i < cells.Count && columns <= width; i++) {
      bool ends = cells[i].Rune == _blank
          || (i > found.Next && BreaksBetween(cells[i - 1].Rune, cells[i].Rune));
      if (ends) {
        break;
      }

      columns += cells[i].Width;
    }

    return columns > width ? (soft, soft) : found;
  }
}

/// A rectangle of cells.
public readonly record struct Rect(int X, int Y, int Width, int Height) {
  public int Right => X + Width;
  public int Bottom => Y + Height;

  /// The rectangle without rows at its top and its bottom.
  public Rect Shrink(int top = 0, int bottom = 0) =>
      new(X, Y + top, Width, Math.Max(0, Height - top - bottom));

  /// The rectangle inside a border of one cell, with one more column of padding at each side.
  public Rect Inner => new(X + 2, Y + 1, Math.Max(0, Width - 4), Math.Max(0, Height - 2));
}

/// The cells of the terminal, drawn to and then written as escape sequences.
public sealed class ScreenBuffer {
  private struct Cell {
    public Rune Rune;
    public Style Style;
    // The right half of a wide character.
    public bool Continuation;
  }

  private static readonly Rune _space = new(' ');
  private readonly Cell[] _cells;

  public ScreenBuffer(int width, int height) {
    Width = Math.Max(1, width);
    Height = Math.Max(1, height);
    _cells = new Cell[Width * Height];
    Clear();
  }

  public int Width { get; }
  public int Height { get; }

  public void Clear() => Fill(new Rect(0, 0, Width, Height), default);

  public void Fill(Rect rect, Style style) {
    for (int y = Math.Max(0, rect.Y); y < Math.Min(Height, rect.Bottom); y++) {
      for (int x = Math.Max(0, rect.X); x < Math.Min(Width, rect.Right); x++) {
        Set(x, y, new Cell { Rune = _space, Style = style });
      }
    }
  }

  /// Sets a cell. A wide character takes two cells: when one of them is set to something
  /// else, such as the border of a dialog above the page, the other becomes a space, so that
  /// the row keeps its width.
  private void Set(int x, int y, Cell cell) {
    int at = (y * Width) + x;
    if (_cells[at].Continuation) {
      _cells[at - 1].Rune = _space;
    } else if (x + 1 < Width && _cells[at + 1].Continuation) {
      _cells[at + 1] = new Cell { Rune = _space, Style = _cells[at + 1].Style };
    }

    _cells[at] = cell;
  }

  /// Writes text from (x, y) up to, not including, the column limit; returns the column after
  /// the last cell written.
  public int Write(int x, int y, string text, Style style = default, int limit = int.MaxValue) {
    limit = Math.Min(limit, Width);
    if (y < 0 || y >= Height) {
      return x;
    }

    foreach (Rune rune in text.EnumerateRunes()) {
      int w = TextWidth.Of(rune);
      if (w == 0) {
        continue;
      }

      if (x + w > limit) {
        break;
      }

      if (x >= 0) {
        Set(x, y, new Cell { Rune = rune, Style = style });
        if (w == 2) {
          Set(x + 1, y, new Cell { Rune = _space, Style = style, Continuation = true });
        }
      }

      x += w;
    }

    return x;
  }

  /// Writes text cut with an ellipsis to width columns.
  public int WriteFit(int x, int y, int width, string text, Style style = default) =>
      Write(x, y, TextWidth.Fit(text, width), style, x + width);

  /// Writes a styled line, clipped at the column limit.
  public int Write(int x, int y, StyledLine line, int limit = int.MaxValue) {
    foreach (Span span in line.Spans) {
      x = Write(x, y, span.Text, span.Style, limit);
    }

    return x;
  }

  /// Sets the style of the cells of a row from x for width columns, keeping their text.
  public void Restyle(int x, int y, int width, Style style) {
    if (y < 0 || y >= Height) {
      return;
    }

    for (int i = Math.Max(0, x); i < Math.Min(Width, x + width); i++) {
      _cells[(y * Width) + i].Style = style;
    }
  }

  /// Draws a border around the rectangle, with a title in its top edge.
  public void Box(Rect rect, string title, Style border, Style titleStyle) {
    if (rect.Width < 2 || rect.Height < 2) {
      return;
    }

    string horizontal = new('─', rect.Width - 2);
    Write(rect.X, rect.Y, "┌" + horizontal + "┐", border);
    Write(rect.X, rect.Bottom - 1, "└" + horizontal + "┘", border);
    for (int y = rect.Y + 1; y < rect.Bottom - 1; y++) {
      Write(rect.X, y, "│", border);
      Write(rect.Right - 1, y, "│", border);
    }

    if (title.Length > 0 && rect.Width > 6) {
      WriteFit(rect.X + 2, rect.Y, rect.Width - 4, " " + title + " ", titleStyle);
    }
  }

  /// A row as text with the escape sequences of its styles.
  public string RowAnsi(int y) {
    StringBuilder builder = new();
    Style? current = null;
    for (int x = 0; x < Width; x++) {
      Cell cell = _cells[(y * Width) + x];
      if (cell.Continuation) {
        continue;
      }

      if (current != cell.Style) {
        builder.Append(cell.Style.Ansi());
        current = cell.Style;
      }

      builder.Append(cell.Rune.ToString());
    }

    return builder.Append("\x1b[0m").ToString();
  }

  /// A row as plain text, without the spaces at its end.
  public string RowText(int y) {
    StringBuilder builder = new();
    for (int x = 0; x < Width; x++) {
      Cell cell = _cells[(y * Width) + x];
      if (!cell.Continuation) {
        builder.Append(cell.Rune.ToString());
      }
    }

    return builder.ToString().TrimEnd();
  }

  /// The screen as plain text, one row per line.
  public string Text => string.Join('\n', Enumerable.Range(0, Height).Select(RowText));
}
