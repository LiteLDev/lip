using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// A cell of a frame: its character, its style, and whether it is the right half of the wide
/// character in the cell before it.
public readonly record struct FrameCell(string Text, Style Style, bool Continuation);

/// A rectangle of cells of a frame.
public readonly record struct CellRect(int X, int Y, int Width, int Height) {
  public int Right => X + Width;
  public int Bottom => Y + Height;

  /// The smallest rectangle that holds this one and the other.
  public CellRect Union(CellRect other) {
    int x = Math.Min(X, other.X);
    int y = Math.Min(Y, other.Y);
    return new CellRect(
        x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
  }
}

/// A frame that the interface drew: its cells with their styles, as a screenshot is drawn from.
public sealed class Frame {
  private static readonly FrameCell _blank = new(" ", default, false);
  private readonly FrameCell[] _cells;

  private Frame(int width, int height) {
    Width = width;
    Height = height;
    _cells = new FrameCell[width * height];
    Array.Fill(_cells, _blank);
  }

  public int Width { get; }
  public int Height { get; }

  public FrameCell this[int x, int y] => _cells[(y * Width) + x];

  /// The frame of the rows of a screen, each as text with the escape sequences of its styles,
  /// as ScreenBuffer.RowAnsi and App.ScreenAnsi give them.
  public static Frame Parse(IReadOnlyList<string> rows) {
    List<List<FrameCell>> parsed = [.. rows.Select(ParseRow)];
    Frame frame = new(Math.Max(1, parsed.Max(row => row.Count)), parsed.Count);
    for (int y = 0; y < parsed.Count; y++) {
      for (int x = 0; x < parsed[y].Count; x++) {
        frame._cells[(y * frame.Width) + x] = parsed[y][x];
      }
    }

    return frame;
  }

  /// The frame of a screen buffer.
  public static Frame Of(ScreenBuffer screen) =>
      Parse([.. Enumerable.Range(0, screen.Height).Select(screen.RowAnsi)]);

  /// A row as plain text, without the spaces at its end, as ScreenBuffer.RowText gives it.
  public string RowText(int y) => FullRowText(y).TrimEnd();

  /// The frame as plain text, one row per line, as App.ScreenText gives it.
  public string Text => string.Join('\n', Enumerable.Range(0, Height).Select(RowText));

  /// The cells of the nth place, from the top and then from the left, where the frame shows
  /// the text, which is of one row; null when there are fewer.
  public CellRect? Find(string text, int nth = 1) {
    if (text.Length == 0) {
      return null;
    }

    int seen = 0;
    for (int y = 0; y < Height; y++) {
      string row = FullRowText(y);
      for (int at = row.IndexOf(text, StringComparison.Ordinal); at >= 0;
          at = row.IndexOf(text, at + 1, StringComparison.Ordinal)) {
        if (++seen == nth) {
          return new CellRect(TextWidth.Of(row[..at]), y, TextWidth.Of(text), 1);
        }
      }
    }

    return null;
  }

  /// The frame with what matches the pattern in a row replaced. Each cell of a replacement
  /// keeps the style that the cell had. A replacement that takes fewer columns than what it
  /// replaces is followed by spaces, and one that takes more takes the place of the spaces
  /// that follow, which must be there.
  public Frame Replace(Regex pattern, MatchEvaluator replacement) {
    Frame result = new(Width, Height);
    Array.Copy(_cells, result._cells, _cells.Length);
    for (int y = 0; y < Height; y++) {
      string row = FullRowText(y);
      foreach (Match match in pattern.Matches(row)) {
        string text = replacement(match);
        int x = TextWidth.Of(row[..match.Index]);
        int room = TextWidth.Of(match.Value);
        int more = TextWidth.Of(text) - room;
        if (more < 0) {
          text += new string(' ', -more);
        } else if (x + room + more > Width || Enumerable.Range(x + room, more).Any(
            column => this[column, y].Text != " " || this[column, y].Continuation)) {
          throw new InvalidOperationException(
              $"'{text}' takes {more} more columns than '{match.Value}', which it replaces,"
                  + " and no spaces follow that it could take.");
        }

        foreach (Rune rune in text.EnumerateRunes()) {
          int width = TextWidth.Of(rune);
          if (width == 0) {
            continue;
          }

          result._cells[(y * Width) + x] = new FrameCell(rune.ToString(), this[x, y].Style, false);
          if (width == 2) {
            result._cells[(y * Width) + x + 1] = new FrameCell(" ", this[x + 1, y].Style, true);
          }

          x += width;
        }
      }
    }

    return result;
  }

  /// A row as plain text with the spaces at its end.
  private string FullRowText(int y) {
    StringBuilder builder = new();
    for (int x = 0; x < Width; x++) {
      if (!this[x, y].Continuation) {
        builder.Append(this[x, y].Text);
      }
    }

    return builder.ToString();
  }

  private static List<FrameCell> ParseRow(string row) {
    List<FrameCell> cells = [];
    Style style = default;
    for (int i = 0; i < row.Length;) {
      if (row[i] == '\x1b' && i + 1 < row.Length && row[i + 1] == '[') {
        int end = row.IndexOf('m', i);
        if (end < 0) {
          throw new FormatException("An escape sequence of the row does not end.");
        }

        style = Apply(style, row[(i + 2)..end]);
        i = end + 1;
        continue;
      }

      Rune rune = Rune.GetRuneAt(row, i);
      i += rune.Utf16SequenceLength;
      int width = TextWidth.Of(rune);
      if (width == 0) {
        continue;
      }

      cells.Add(new FrameCell(rune.ToString(), style, false));
      if (width == 2) {
        cells.Add(new FrameCell(" ", style, true));
      }
    }

    return cells;
  }

  /// The style after the parameters of a "select graphic rendition" sequence.
  private static Style Apply(Style style, string parameters) {
    int[] codes = [
      .. parameters.Split(';').Select(
          code => code.Length == 0 ? 0 : int.Parse(code, CultureInfo.InvariantCulture)),
    ];
    for (int i = 0; i < codes.Length; i++) {
      int code = codes[i];
      if (code == 0) {
        style = default;
      } else if (code == 1) {
        style = style with { Bold = true };
      } else if (code == 4) {
        style = style with { Underline = true };
      } else if (code == 7) {
        style = style with { Inverse = true };
      } else if (code is (>= 30 and <= 37) or (>= 90 and <= 97)) {
        style = style with { Fg = code < 90 ? code - 29 : code - 81 };
      } else if (code is (>= 40 and <= 47) or (>= 100 and <= 107)) {
        style = style with { Bg = code < 100 ? code - 39 : code - 91 };
      } else if (code is 38 or 48) {
        int color;
        if (i + 2 < codes.Length && codes[i + 1] == 5) {
          color = codes[i + 2] + 1;
          i += 2;
        } else if (i + 4 < codes.Length && codes[i + 1] == 2) {
          color = Color.TrueColor | (codes[i + 2] << 16) | (codes[i + 3] << 8) | codes[i + 4];
          i += 4;
        } else {
          throw new FormatException($"The color of '{parameters}' is not understood.");
        }

        style = code == 38 ? style with { Fg = color } : style with { Bg = color };
      } else {
        throw new FormatException($"The escape sequence '{parameters}' is not understood.");
      }
    }

    return style;
  }
}
