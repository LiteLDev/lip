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

  /// The text as lines of at most width columns, broken at spaces where there are any.
  public static List<string> Wrap(string text, int width) {
    List<string> lines = [];
    width = Math.Max(1, width);
    foreach (string paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n')) {
      StringBuilder line = new();
      int used = 0;
      foreach (string word in paragraph.Replace('\t', ' ').Split(' ')) {
        int w = Of(word);
        if (used > 0 && used + 1 + w > width) {
          lines.Add(line.ToString());
          line.Clear();
          used = 0;
        }

        if (used > 0) {
          line.Append(' ');
          used++;
        }

        foreach (Rune rune in word.EnumerateRunes()) {
          int rw = Of(rune);
          if (used + rw > width) {
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

  /// A styled line as rows of at most width columns.
  public static List<StyledLine> Wrap(StyledLine line, int width) {
    List<StyledLine> rows = [new StyledLine()];
    width = Math.Max(1, width);
    int used = 0;
    foreach (Span span in line.Spans) {
      StringBuilder part = new();
      foreach (Rune rune in span.Text.EnumerateRunes()) {
        int w = Of(rune);
        if (used + w > width) {
          rows[^1].Add(part.ToString(), span.Style);
          part.Clear();
          rows.Add(new StyledLine());
          used = 0;
        }

        part.Append(rune.ToString());
        used += w;
      }

      if (part.Length > 0) {
        rows[^1].Add(part.ToString(), span.Style);
      }
    }

    return rows;
  }
}

/// A rectangle of cells.
public readonly record struct Rect(int X, int Y, int Width, int Height) {
  public int Right => X + Width;
  public int Bottom => Y + Height;

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
        _cells[(y * Width) + x] = new Cell { Rune = _space, Style = style };
      }
    }
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
        _cells[(y * Width) + x] = new Cell { Rune = rune, Style = style };
        if (w == 2) {
          _cells[(y * Width) + x + 1] = new Cell { Rune = _space, Style = style, Continuation = true };
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
