using System.Globalization;
using System.Text;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// How a screenshot is drawn, in pixels of the picture at its own size.
public sealed record SvgOptions {
  /// The title in the title bar of the window.
  public string Title { get; init; } = "lip";

  /// The size of a cell. The font is 15 pixels high, whose characters are about 9 wide.
  public double CellWidth { get; init; } = 9;

  public double CellHeight { get; init; } = 20;
}

/// Draws a frame of the interface as an SVG picture of a terminal window, with annotations
/// above it. The picture is one file without fonts, scripts or anything from elsewhere; what
/// the frame shows stays text. The same frame and annotations give the same picture, byte for
/// byte.
///
/// Nothing is left to the font of whoever looks at the picture but the shapes of the letters:
/// every run of text is placed at the column of its first cell and stretched to the width of
/// its cells, wide characters get runs of their own, and the characters that draw boxes and
/// blocks are drawn as lines and rectangles.
public static class SvgRenderer {
  /// The fonts of the text, the first that the viewer has: fonts of one width, then fonts
  /// with Chinese characters, which are twice as wide in all of them.
  public const string FontFamily =
      "ui-monospace,'Cascadia Mono','Cascadia Code',Consolas,'SF Mono',Menlo,"
      + "'DejaVu Sans Mono','Liberation Mono','Noto Sans Mono CJK SC','Sarasa Mono SC',"
      + "'Microsoft YaHei UI','PingFang SC','Noto Sans CJK SC',monospace";

  /// The window is dark on any page: the pages of the documentation are light or dark, and
  /// the interface is drawn on the colors of the terminal, which is dark more often than not.
  public const string WindowColor = "#1a1d24";
  public const string TitleBarColor = "#272b35";
  public const string BorderColor = "#3d4454";
  public const string TextColor = "#d8dee9";
  public const string MutedColor = "#9aa3b5";

  /// The color of the annotations: none of the palette, and bright on the window.
  public const string NoteColor = "#ffa94d";

  // The 16 colors of the terminal as the color of text.
  private static readonly string[] _foreground = [
    "#5c6575", "#f07178", "#9ad17b", "#e5c07b", "#6cb0f5", "#c792ea", "#56c8d8", "#c8ced9",
    "#8b95a7", "#ff8b92", "#b5e890", "#f6d58e", "#8cc4ff", "#dcb0ff", "#7fe0ee", "#ffffff",
  ];

  // The same as the color behind text, darker where the interface writes white on them.
  private static readonly string[] _background = [
    "#0f1115", "#b23a48", "#3f7d3a", "#8f6b1f", "#2563c9", "#7d4bb0", "#1f7f8f", "#c8ced9",
    "#4a5364", "#ff8b92", "#b5e890", "#f6d58e", "#8cc4ff", "#dcb0ff", "#7fe0ee", "#ffffff",
  ];

  private const double FontSize = 15;
  private const double Baseline = 14.5;
  private const double Padding = 12;
  private const double PaddingTop = 8;
  private const double TitleBar = 30;
  private const double Radius = 10;
  private const double LineWidth = 1.25;
  private const double Margin = 10;
  // The text of the annotations: 13 pixels high, whose characters are about 7.8 wide.
  private const double LabelSize = 13;
  private const double LabelAdvance = 7.8;
  private const double PillHeight = 22;
  private const double PillGap = 28;
  private const double CalloutRadius = 10;

  // The sides of a cell that a character that draws boxes reaches.
  private const int L = 1;
  private const int R = 2;
  private const int U = 4;
  private const int D = 8;

  private static readonly Dictionary<string, int> _lines = new() {
    ["─"] = L | R,
    ["│"] = U | D,
    ["┌"] = R | D,
    ["┐"] = L | D,
    ["└"] = R | U,
    ["┘"] = L | U,
    ["├"] = U | D | R,
    ["┤"] = U | D | L,
    ["┬"] = L | R | D,
    ["┴"] = L | R | U,
    ["┼"] = L | R | U | D,
    ["╭"] = R | D,
    ["╮"] = L | D,
    ["╰"] = R | U,
    ["╯"] = L | U,
    ["━"] = L | R,
    ["┃"] = U | D,
    ["┏"] = R | D,
    ["┓"] = L | D,
    ["┗"] = R | U,
    ["┛"] = L | U,
    ["┣"] = U | D | R,
    ["┫"] = U | D | L,
    ["┳"] = L | R | D,
    ["┻"] = L | R | U,
    ["╋"] = L | R | U | D,
    ["═"] = L | R,
    ["║"] = U | D,
    ["╔"] = R | D,
    ["╗"] = L | D,
    ["╚"] = R | U,
    ["╝"] = L | U,
    ["╠"] = U | D | R,
    ["╣"] = U | D | L,
    ["╦"] = L | R | D,
    ["╩"] = L | R | U,
    ["╬"] = L | R | U | D,
    ["╴"] = L,
    ["╶"] = R,
    ["╵"] = U,
    ["╷"] = D,
    ["┄"] = L | R,
    ["┈"] = L | R,
    ["╌"] = L | R,
    ["┆"] = U | D,
    ["┊"] = U | D,
    ["╎"] = U | D,
  };

  // The part of a cell that a block character fills, as fractions of the cell, and how
  // opaque it is.
  private static readonly Dictionary<string, (double X, double Y, double W, double H, double A)>
      _blocks = new() {
        ["█"] = (0, 0, 1, 1, 1),
        ["▀"] = (0, 0, 1, 0.5, 1),
        ["▄"] = (0, 0.5, 1, 0.5, 1),
        ["▌"] = (0, 0, 0.5, 1, 1),
        ["▐"] = (0.5, 0, 0.5, 1, 1),
        ["░"] = (0, 0, 1, 1, 0.3),
        ["▒"] = (0, 0, 1, 1, 0.55),
        ["▓"] = (0, 0, 1, 1, 0.8),
        ["▁"] = (0, 0.875, 1, 0.125, 1),
        ["▂"] = (0, 0.75, 1, 0.25, 1),
        ["▃"] = (0, 0.625, 1, 0.375, 1),
        ["▅"] = (0, 0.375, 1, 0.625, 1),
        ["▆"] = (0, 0.25, 1, 0.75, 1),
        ["▇"] = (0, 0.125, 1, 0.875, 1),
        ["▏"] = (0, 0, 0.125, 1, 1),
        ["▎"] = (0, 0, 0.25, 1, 1),
        ["▍"] = (0, 0, 0.375, 1, 1),
        ["▋"] = (0, 0, 0.625, 1, 1),
        ["▊"] = (0, 0, 0.75, 1, 1),
        ["▉"] = (0, 0, 0.875, 1, 1),
      };

  /// Whether the renderer draws the character itself, as lines or as a rectangle.
  public static bool IsDrawn(string character) =>
      _lines.ContainsKey(character) || _blocks.ContainsKey(character);

  /// The color of text of a color of a Style, as "#rrggbb".
  public static string Foreground(int color) => Hex(color, _foreground, TextColor);

  /// The color behind text of a color of a Style, as "#rrggbb".
  public static string Background(int color) => Hex(color, _background, WindowColor);

  /// The picture of the frame with the annotations.
  public static string Render(
      Frame frame, IReadOnlyList<Note>? notes = null, SvgOptions? options = null) {
    options ??= new SvgOptions();
    notes ??= [];
    Picture picture = new(frame, notes, options);
    return picture.Draw();
  }

  private static string Hex(int color, string[] palette, string fallback) {
    if (color == Color.Default) {
      return fallback;
    }

    if ((color & Color.TrueColor) != 0) {
      return string.Create(CultureInfo.InvariantCulture, $"#{color & 0xFFFFFF:x6}");
    }

    int index = color - 1;
    if (index < 16) {
      return palette[index];
    }

    if (index < 232) {
      int[] levels = [0, 95, 135, 175, 215, 255];
      int cube = index - 16;
      return string.Create(
          CultureInfo.InvariantCulture,
          $"#{levels[cube / 36]:x2}{levels[cube / 6 % 6]:x2}{levels[cube % 6]:x2}");
    }

    int gray = 8 + (10 * (index - 232));
    return string.Create(CultureInfo.InvariantCulture, $"#{gray:x2}{gray:x2}{gray:x2}");
  }

  /// A number as the picture writes it: at most two decimals, none that are zero.
  private static string N(double value) {
    string text = Math.Round(value, 2, MidpointRounding.AwayFromZero)
        .ToString("0.##", CultureInfo.InvariantCulture);
    return text == "-0" ? "0" : text;
  }

  private static string Escape(string text) {
    StringBuilder builder = new();
    foreach (char c in text) {
      builder.Append(c switch {
        '&' => "&amp;",
        '<' => "&lt;",
        '>' => "&gt;",
        '"' => "&quot;",
        _ => c < ' ' ? " " : c.ToString(),
      });
    }

    return builder.ToString();
  }

  /// A character of a run of text: the column it starts at, counted in cells, and how many
  /// cells it takes.
  private readonly record struct Glyph(string Text, int Column, int Width);

  private enum GlyphKind {
    Space,
    Ascii,
    Wide,
    Other,
  }

  /// What a glyph is written as. In the cells of a frame, the characters that the renderer
  /// draws itself are no text.
  private static GlyphKind KindOf(Glyph glyph, bool cells) {
    if (glyph.Text == " " || (cells && IsDrawn(glyph.Text))) {
      return GlyphKind.Space;
    }

    int value = Rune.GetRuneAt(glyph.Text, 0).Value;
    if (value is > 0x20 and < 0x7F) {
      return GlyphKind.Ascii;
    }

    // Pictographs are wide too, but come from another font than the characters around them.
    return glyph.Width == 2 && value < 0x1F000 ? GlyphKind.Wide : GlyphKind.Other;
  }

  private static List<Glyph> Glyphs(string text) {
    List<Glyph> glyphs = [];
    int column = 0;
    foreach (Rune rune in text.EnumerateRunes()) {
      int width = TextWidth.Of(rune);
      if (width > 0) {
        glyphs.Add(new Glyph(rune.ToString(), column, width));
        column += width;
      }
    }

    return glyphs;
  }

  /// Writes glyphs of one style as text elements. Letters, digits and signs of ASCII that
  /// follow each other, with single spaces between them, are one element stretched to the
  /// width of their cells: a font of one width fills them evenly. Wide characters that follow
  /// each other are one element that places each in the middle of its two cells, and any
  /// other character is an element of its own in the middle of its cell.
  private static void AppendText(
      StringBuilder svg, IReadOnlyList<Glyph> glyphs, double x, double y, double advance,
      string attributes, bool cells = false) {
    for (int i = 0; i < glyphs.Count;) {
      GlyphKind kind = KindOf(glyphs[i], cells);
      if (kind == GlyphKind.Space) {
        i++;
        continue;
      }

      int end = i + 1;
      if (kind != GlyphKind.Other) {
        while (end < glyphs.Count) {
          GlyphKind next = KindOf(glyphs[end], cells);
          if (next == kind) {
            end++;
          } else if (kind == GlyphKind.Ascii && glyphs[end].Text == " "
              && end + 1 < glyphs.Count && KindOf(glyphs[end + 1], cells) == GlyphKind.Ascii) {
            end += 2;
          } else {
            break;
          }
        }
      }

      string text = Escape(string.Concat(glyphs.Skip(i).Take(end - i).Select(g => g.Text)));
      int columns = glyphs[end - 1].Column + glyphs[end - 1].Width - glyphs[i].Column;
      double left = x + (glyphs[i].Column * advance);
      if (kind == GlyphKind.Ascii) {
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{N(left)}\" y=\"{N(y)}\" textLength=\"{N(columns * advance)}\"")
            .Append(" lengthAdjust=\"spacingAndGlyphs\"");
      } else {
        // Each character in the middle of its cells, whatever the font makes of its width.
        string middles = string.Join(' ', glyphs.Skip(i).Take(end - i).Select(
            g => N(x + ((g.Column + (g.Width / 2.0)) * advance))));
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{middles}\" y=\"{N(y)}\" text-anchor=\"middle\"");
      }

      svg.Append(attributes).Append('>').Append(text).Append("</text>\n");
      i = end;
    }
  }

  /// A rectangle of the picture.
  private readonly record struct Box(double X, double Y, double W, double H) {
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterX => X + (W / 2);
    public double CenterY => Y + (H / 2);

    public Box Grow(double by) => new(X - by, Y - by, W + (2 * by), H + (2 * by));
  }

  /// A label in a margin, where it ended up.
  private sealed class Label {
    public required ArrowNote Note { get; init; }
    public required Box Target { get; init; }
    public required double Width { get; init; }
    // The middle of the label along the side it is at.
    public double At { get; set; }
  }

  /// One picture while it is drawn.
  private sealed class Picture {
    private readonly Frame _frame;
    private readonly SvgOptions _options;
    private readonly StringBuilder _svg = new();
    private readonly List<(BoxNote Note, Box Target)> _boxes = [];
    private readonly List<(CalloutNote Note, Box Target)> _callouts = [];
    private readonly List<Box> _focus = [];
    private readonly List<Label> _labels = [];
    private readonly List<KeysNote> _keys = [];
    private readonly double _cw;
    private readonly double _ch;
    private readonly Box _window;
    private readonly Box _grid;
    private readonly Box _footer;
    private readonly List<(int Number, List<string> Lines)> _legend = [];
    private readonly double _width;
    private readonly double _height;

    public Picture(Frame frame, IReadOnlyList<Note> notes, SvgOptions options) {
      _frame = frame;
      _options = options;
      _cw = options.CellWidth;
      _ch = options.CellHeight;
      double windowWidth = (frame.Width * _cw) + (2 * Padding);
      double windowHeight = TitleBar + PaddingTop + (frame.Height * _ch) + Padding;

      // The sizes of the margins come first: they move the window, and with it every target.
      List<(Note Note, CellRect Cells)> placed = [];
      foreach (Note note in notes) {
        if (note is KeysNote keys) {
          _keys.Add(keys);
          continue;
        }

        Target target = note switch {
          BoxNote box => box.Target,
          ArrowNote arrow => arrow.Target,
          CalloutNote callout => callout.Target,
          FocusNote focus => focus.Target,
          _ => throw new InvalidOperationException($"{note} is not an annotation."),
        };
        placed.Add((note, target.Resolve(frame)));
      }

      double MarginAt(Side side) {
        List<ArrowNote> arrows = [
          .. placed.Select(p => p.Note).OfType<ArrowNote>().Where(a => a.Side == side),
        ];
        if (arrows.Count == 0) {
          return Margin;
        }

        return side is Side.Top or Side.Bottom
            ? PillGap + PillHeight + Margin
            : arrows.Max(a => PillWidth(a.Label)) + PillGap + Margin;
      }

      _window = new Box(MarginAt(Side.Left), MarginAt(Side.Top), windowWidth, windowHeight);
      _grid = new Box(
          _window.X + Padding, _window.Y + TitleBar + PaddingTop, frame.Width * _cw,
          frame.Height * _ch);
      foreach ((Note note, CellRect cells) in placed) {
        Box target = new(
            _grid.X + (cells.X * _cw), _grid.Y + (cells.Y * _ch), cells.Width * _cw,
            cells.Height * _ch);
        switch (note) {
          case BoxNote box:
            _boxes.Add((box, target));
            break;
          case CalloutNote callout:
            _callouts.Add((callout, target));
            break;
          case FocusNote:
            _focus.Add(target);
            break;
          case ArrowNote arrow:
            _labels.Add(new Label {
              Note = arrow,
              Target = target,
              Width = PillWidth(arrow.Label),
              At = arrow.Side is Side.Top or Side.Bottom ? target.CenterX : target.CenterY,
            });
            break;
        }
      }

      double right = _window.Right + MarginAt(Side.Right);
      right = Math.Max(right, SpreadLabels(Side.Top));
      right = Math.Max(right, SpreadLabels(Side.Bottom));
      SpreadLabels(Side.Left);
      SpreadLabels(Side.Right);
      _width = right;

      // The legend and the keys are in a panel of their own below the window, so that they
      // are read on the dark of the window whatever the page behind the picture is.
      int legendColumns = (int)((windowWidth - 60) / LabelAdvance);
      foreach ((CalloutNote note, _) in _callouts.Where(c => c.Note.Text is not null)
          .OrderBy(c => c.Note.Number)) {
        _legend.Add((note.Number, TextWidth.Wrap(note.Text!, legendColumns)));
      }

      double footerTop = _window.Bottom + MarginAt(Side.Bottom);
      double footerHeight = (_legend.Sum(entry => entry.Lines.Count) * 22)
          + (_keys.Sum(keys => KeyRows(keys, windowWidth).Count) * 32);
      if (footerHeight > 0) {
        _footer = new Box(_window.X, footerTop, windowWidth, footerHeight + 16);
        _height = _footer.Bottom + Margin;
      } else {
        _height = footerTop;
      }

      // Labels at the left and the right that were moved down past the window are in the
      // picture too.
      foreach (Label label in _labels.Where(l => l.Note.Side is Side.Left or Side.Right)) {
        _height = Math.Max(_height, label.At + (PillHeight / 2) + Margin);
      }
    }

    /// The caption and the badges of the keys, as rows that fit a panel as wide as the
    /// window: each with where it starts, from the left of the panel, and how wide it is.
    private static List<List<(string Text, bool Caption, double X, double Width)>> KeyRows(
        KeysNote keys, double panelWidth) {
      List<List<(string Text, bool Caption, double X, double Width)>> rows = [[]];
      double x = 14;
      if (keys.Caption is { Length: > 0 } caption) {
        double width = TextWidth.Of(caption) * LabelAdvance;
        rows[^1].Add((caption, true, x, width));
        x += width + 12;
      }

      foreach (string key in keys.Badges) {
        double width = Math.Max(26, (TextWidth.Of(key) * LabelAdvance) + 16);
        if (x + width > panelWidth - 14 && rows[^1].Count > 0) {
          rows.Add([]);
          x = 14;
        }

        rows[^1].Add((key, false, x, width));
        x += width + 8;
      }

      return rows;
    }

    private static double PillWidth(string label) => (TextWidth.Of(label) * LabelAdvance) + 16;

    /// Moves the labels of a side apart where they would lie on each other, in the order of
    /// their targets; returns how far to the right the labels above or below the window
    /// reach, with the margin.
    private double SpreadLabels(Side side) {
      bool along = side is Side.Top or Side.Bottom;
      double reach = 0;
      double previous = double.NegativeInfinity;
      foreach (Label label in _labels.Where(l => l.Note.Side == side).OrderBy(l => l.At)) {
        double half = along ? label.Width / 2 : PillHeight / 2;
        double first = along ? Margin + half : _window.Y + half;
        label.At = Math.Max(Math.Max(label.At, first), previous + half + (along ? 8 : 4));
        previous = label.At + half;
        if (along) {
          reach = previous + Margin;
        }
      }

      return reach;
    }

    public string Draw() {
      _svg.Append(CultureInfo.InvariantCulture,
          $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(_width)}\"")
          .Append(CultureInfo.InvariantCulture,
              $" height=\"{N(_height)}\" viewBox=\"0 0 {N(_width)} {N(_height)}\"")
          .Append(" role=\"img\" font-family=\"").Append(FontFamily).Append('"')
          .Append(CultureInfo.InvariantCulture, $" font-size=\"{N(FontSize)}\">\n")
          .Append("<title>").Append(Escape(_options.Title)).Append("</title>\n");
      DrawWindow();
      DrawBackgrounds();
      DrawLines();
      DrawBlocks();
      DrawText();
      DrawFocus();
      DrawBoxes();
      DrawLabels();
      DrawCallouts();
      DrawFooter();
      return _svg.Append("</svg>\n").ToString();
    }

    private void DrawWindow() {
      Box w = _window;
      _svg.Append(CultureInfo.InvariantCulture,
          $"<rect x=\"{N(w.X)}\" y=\"{N(w.Y)}\" width=\"{N(w.W)}\" height=\"{N(w.H)}\"")
          .Append(CultureInfo.InvariantCulture,
              $" rx=\"{N(Radius)}\" fill=\"{WindowColor}\"/>\n")
          .Append(CultureInfo.InvariantCulture,
              $"<path d=\"M{N(w.X)} {N(w.Y + TitleBar)}V{N(w.Y + Radius)}")
          .Append(CultureInfo.InvariantCulture,
              $"a{N(Radius)} {N(Radius)} 0 0 1 {N(Radius)} {N(-Radius)}H{N(w.Right - Radius)}")
          .Append(CultureInfo.InvariantCulture,
              $"a{N(Radius)} {N(Radius)} 0 0 1 {N(Radius)} {N(Radius)}V{N(w.Y + TitleBar)}z\"")
          .Append(CultureInfo.InvariantCulture, $" fill=\"{TitleBarColor}\"/>\n");
      string[] dots = ["#ff5f57", "#febc2e", "#28c840"];
      for (int i = 0; i < dots.Length; i++) {
        _svg.Append(CultureInfo.InvariantCulture,
            $"<circle cx=\"{N(w.X + 18 + (i * 18))}\" cy=\"{N(w.Y + (TitleBar / 2))}\" r=\"5.5\"")
            .Append(CultureInfo.InvariantCulture, $" fill=\"{dots[i]}\"/>\n");
      }

      _svg.Append(CultureInfo.InvariantCulture,
          $"<text x=\"{N(w.CenterX)}\" y=\"{N(w.Y + (TitleBar / 2) + 4.5)}\"")
          .Append(CultureInfo.InvariantCulture,
              $" text-anchor=\"middle\" font-size=\"{N(LabelSize)}\" fill=\"{MutedColor}\">")
          .Append(Escape(_options.Title)).Append("</text>\n")
          .Append(CultureInfo.InvariantCulture,
              $"<rect x=\"{N(w.X + 0.5)}\" y=\"{N(w.Y + 0.5)}\" width=\"{N(w.W - 1)}\"")
          .Append(CultureInfo.InvariantCulture,
              $" height=\"{N(w.H - 1)}\" rx=\"{N(Radius - 0.5)}\" fill=\"none\"")
          .Append(CultureInfo.InvariantCulture, $" stroke=\"{BorderColor}\"/>\n");
    }

    /// The colors of a cell: of its text, and behind it, null where that is the window.
    private (string Fg, string? Bg) Colors(int x, int y) {
      Style style = _frame[x, y].Style;
      if (style.Inverse) {
        return (Background(style.Bg), Foreground(style.Fg));
      }

      return (Foreground(style.Fg), style.Bg == Color.Default ? null : Background(style.Bg));
    }

    private void DrawBackgrounds() {
      _svg.Append("<g shape-rendering=\"crispEdges\">\n");
      for (int y = 0; y < _frame.Height; y++) {
        for (int x = 0; x < _frame.Width;) {
          string? color = Colors(x, y).Bg;
          int end = x + 1;
          while (end < _frame.Width && Colors(end, y).Bg == color) {
            end++;
          }

          if (color is not null) {
            _svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(_grid.X + (x * _cw))}\" y=\"{N(_grid.Y + (y * _ch))}\"")
                .Append(CultureInfo.InvariantCulture,
                    $" width=\"{N((end - x) * _cw)}\" height=\"{N(_ch)}\" fill=\"{color}\"/>\n");
          }

          x = end;
        }
      }

      _svg.Append("</g>\n");
    }

    /// The characters that draw boxes, as lines through the middles of their cells that
    /// reach the edges of the cells, so that the lines of cells side by side are one line.
    private void DrawLines() {
      // The lines of each color, in the order that the colors are first met.
      List<(string Color, StringBuilder Path)> paths = [];
      StringBuilder PathOf(string color) {
        int index = paths.FindIndex(p => p.Color == color);
        if (index < 0) {
          paths.Add((color, new StringBuilder()));
          index = paths.Count - 1;
        }

        return paths[index].Path;
      }

      int Sides(int x, int y) => _lines.GetValueOrDefault(_frame[x, y].Text);
      double half = LineWidth / 2;
      for (int y = 0; y < _frame.Height; y++) {
        double middle = _grid.Y + (y * _ch) + (_ch / 2);
        for (int x = 0; x < _frame.Width; x++) {
          int sides = Sides(x, y);
          if ((sides & (L | R)) == 0) {
            continue;
          }

          string color = Colors(x, y).Fg;
          double center = _grid.X + (x * _cw) + (_cw / 2);
          double from = (sides & L) != 0 ? _grid.X + (x * _cw) : center - half;
          int last = x;
          // The line goes on through the cells at the right that it reaches and that reach it.
          while ((Sides(last, y) & R) != 0 && last + 1 < _frame.Width
              && (Sides(last + 1, y) & L) != 0 && Colors(last + 1, y).Fg == color) {
            last++;
          }

          double to = (Sides(last, y) & R) != 0
              ? _grid.X + ((last + 1) * _cw)
              : _grid.X + (last * _cw) + (_cw / 2) + half;
          PathOf(color).Append(CultureInfo.InvariantCulture, $"M{N(from)} {N(middle)}H{N(to)}");
          x = last;
        }
      }

      for (int x = 0; x < _frame.Width; x++) {
        double center = _grid.X + (x * _cw) + (_cw / 2);
        for (int y = 0; y < _frame.Height; y++) {
          int sides = Sides(x, y);
          if ((sides & (U | D)) == 0) {
            continue;
          }

          string color = Colors(x, y).Fg;
          double middle = _grid.Y + (y * _ch) + (_ch / 2);
          double from = (sides & U) != 0 ? _grid.Y + (y * _ch) : middle - half;
          int last = y;
          while ((Sides(x, last) & D) != 0 && last + 1 < _frame.Height
              && (Sides(x, last + 1) & U) != 0 && Colors(x, last + 1).Fg == color) {
            last++;
          }

          double to = (Sides(x, last) & D) != 0
              ? _grid.Y + ((last + 1) * _ch)
              : _grid.Y + (last * _ch) + (_ch / 2) + half;
          PathOf(color).Append(CultureInfo.InvariantCulture, $"M{N(center)} {N(from)}V{N(to)}");
          y = last;
        }
      }

      if (paths.Count == 0) {
        return;
      }

      _svg.Append(CultureInfo.InvariantCulture,
          $"<g fill=\"none\" stroke-width=\"{N(LineWidth)}\" shape-rendering=\"crispEdges\">\n");
      foreach ((string color, StringBuilder path) in paths) {
        _svg.Append("<path stroke=\"").Append(color).Append("\" d=\"").Append(path)
            .Append("\"/>\n");
      }

      _svg.Append("</g>\n");
    }

    private void DrawBlocks() {
      StringBuilder blocks = new();
      for (int y = 0; y < _frame.Height; y++) {
        for (int x = 0; x < _frame.Width;) {
          string text = _frame[x, y].Text;
          if (!_blocks.TryGetValue(
              text, out (double X, double Y, double W, double H, double A) b)) {
            x++;
            continue;
          }

          string color = Colors(x, y).Fg;
          int end = x + 1;
          // Blocks side by side that fill their cells from edge to edge are one rectangle.
          while (b.X == 0 && b.W == 1 && end < _frame.Width && _frame[end, y].Text == text
              && Colors(end, y).Fg == color) {
            end++;
          }

          blocks.Append(CultureInfo.InvariantCulture,
              $"<rect x=\"{N(_grid.X + ((x + b.X) * _cw))}\"")
              .Append(CultureInfo.InvariantCulture,
                  $" y=\"{N(_grid.Y + ((y + b.Y) * _ch))}\"")
              .Append(CultureInfo.InvariantCulture,
                  $" width=\"{N((end - x - 1 + b.W) * _cw)}\" height=\"{N(b.H * _ch)}\"")
              .Append(CultureInfo.InvariantCulture, $" fill=\"{color}\"");
          if (b.A < 1) {
            blocks.Append(CultureInfo.InvariantCulture, $" fill-opacity=\"{N(b.A)}\"");
          }

          blocks.Append("/>\n");
          x = end;
        }
      }

      if (blocks.Length > 0) {
        _svg.Append("<g shape-rendering=\"crispEdges\">\n").Append(blocks).Append("</g>\n");
      }
    }

    private void DrawText() {
      _svg.Append(CultureInfo.InvariantCulture, $"<g fill=\"{TextColor}\">\n");
      for (int y = 0; y < _frame.Height; y++) {
        double top = _grid.Y + (y * _ch);
        for (int x = 0; x < _frame.Width;) {
          Style style = _frame[x, y].Style;
          List<Glyph> glyphs = [];
          int end = x;
          for (; end < _frame.Width && _frame[end, y].Style == style; end++) {
            FrameCell cell = _frame[end, y];
            if (!cell.Continuation) {
              bool wide = end + 1 < _frame.Width && _frame[end + 1, y].Continuation;
              glyphs.Add(new Glyph(cell.Text, end, wide ? 2 : 1));
            }
          }

          string color = Colors(x, y).Fg;
          string attributes = (color == TextColor ? "" : $" fill=\"{color}\"")
              + (style.Bold ? " font-weight=\"700\"" : "");
          AppendText(_svg, glyphs, _grid.X, top + Baseline, _cw, attributes, cells: true);
          if (style.Underline) {
            int first = glyphs.FindIndex(g => g.Text != " ");
            int last = glyphs.FindLastIndex(g => g.Text != " ");
            if (first >= 0) {
              _svg.Append(CultureInfo.InvariantCulture,
                  $"<path d=\"M{N(_grid.X + (glyphs[first].Column * _cw))} {N(top + _ch - 2.5)}")
                  .Append(CultureInfo.InvariantCulture,
                      $"H{N(_grid.X + ((glyphs[last].Column + glyphs[last].Width) * _cw))}\"")
                  .Append(CultureInfo.InvariantCulture, $" fill=\"none\" stroke=\"{color}\"/>\n");
            }
          }

          x = end;
        }
      }

      _svg.Append("</g>\n");
    }

    /// Dims the cells but for the targets to keep bright.
    private void DrawFocus() {
      if (_focus.Count == 0) {
        return;
      }

      Box all = _grid.Grow(6);
      _svg.Append(CultureInfo.InvariantCulture,
          $"<path fill=\"#0b0d11\" fill-opacity=\"0.62\" fill-rule=\"evenodd\" d=\"{Outline(all)}");
      foreach (Box hole in _focus) {
        _svg.Append(Outline(hole.Grow(3)));
      }

      _svg.Append("\"/>\n");
    }

    private static string Outline(Box box) =>
        $"M{N(box.X)} {N(box.Y)}H{N(box.Right)}V{N(box.Bottom)}H{N(box.X)}z";

    private void DrawBoxes() {
      foreach ((BoxNote note, Box target) in _boxes) {
        if (note.Round) {
          _svg.Append(CultureInfo.InvariantCulture,
              $"<ellipse cx=\"{N(target.CenterX)}\" cy=\"{N(target.CenterY)}\"")
              .Append(CultureInfo.InvariantCulture,
                  $" rx=\"{N((target.W * 0.71) + 3)}\" ry=\"{N((target.H * 0.71) + 3)}\"");
        } else {
          Box box = new(target.X - 4, target.Y - 2, target.W + 8, target.H + 4);
          _svg.Append(CultureInfo.InvariantCulture,
              $"<rect x=\"{N(box.X)}\" y=\"{N(box.Y)}\" width=\"{N(box.W)}\"")
              .Append(CultureInfo.InvariantCulture, $" height=\"{N(box.H)}\" rx=\"6\"");
        }

        _svg.Append(CultureInfo.InvariantCulture,
            $" fill=\"{NoteColor}\" fill-opacity=\"0.1\" stroke=\"{NoteColor}\"")
            .Append(" stroke-width=\"2\"/>\n");
      }
    }

    /// The labels in the margins and their arrows.
    private void DrawLabels() {
      foreach (Label label in _labels) {
        Box t = label.Target;
        (Box pill, double fromX, double fromY, double toX, double toY) = label.Note.Side switch {
          Side.Left => (
              new Box(
                  _window.X - PillGap - label.Width, label.At - (PillHeight / 2), label.Width,
                  PillHeight),
              _window.X - PillGap, label.At, t.X - 5, t.CenterY),
          Side.Right => (
              new Box(
                  _window.Right + PillGap, label.At - (PillHeight / 2), label.Width, PillHeight),
              _window.Right + PillGap, label.At, t.Right + 5, t.CenterY),
          Side.Top => (
              new Box(
                  label.At - (label.Width / 2), _window.Y - PillGap - PillHeight, label.Width,
                  PillHeight),
              label.At, _window.Y - PillGap, t.CenterX, t.Y - 4),
          _ => (
              new Box(
                  label.At - (label.Width / 2), _window.Bottom + PillGap, label.Width,
                  PillHeight),
              label.At, _window.Bottom + PillGap, t.CenterX, t.Bottom + 4),
        };
        double length = Math.Sqrt(Math.Pow(toX - fromX, 2) + Math.Pow(toY - fromY, 2));
        double dx = length == 0 ? 1 : (toX - fromX) / length;
        double dy = length == 0 ? 0 : (toY - fromY) / length;
        string line = $"M{N(fromX)} {N(fromY)}L{N(toX - (dx * 7))} {N(toY - (dy * 7))}";
        string head = $"M{N(toX)} {N(toY)}"
            + $"L{N(toX - (dx * 10) - (dy * 4.5))} {N(toY - (dy * 10) + (dx * 4.5))}"
            + $"L{N(toX - (dx * 10) + (dy * 4.5))} {N(toY - (dy * 10) - (dx * 4.5))}z";
        // A dark line below the bright one keeps the arrow apart from what it crosses.
        _svg.Append(CultureInfo.InvariantCulture,
            $"<path d=\"{line}\" fill=\"none\" stroke=\"{WindowColor}\" stroke-width=\"4\"")
            .Append(" stroke-opacity=\"0.6\" stroke-linecap=\"round\"/>\n")
            .Append(CultureInfo.InvariantCulture,
                $"<path d=\"{line}\" fill=\"none\" stroke=\"{NoteColor}\" stroke-width=\"2\"")
            .Append(" stroke-linecap=\"round\"/>\n")
            .Append(CultureInfo.InvariantCulture,
                $"<path d=\"{head}\" fill=\"{NoteColor}\" stroke=\"{WindowColor}\"")
            .Append(" stroke-width=\"1\" stroke-linejoin=\"round\"/>\n")
            .Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(pill.X)}\" y=\"{N(pill.Y)}\" width=\"{N(pill.W)}\"")
            .Append(CultureInfo.InvariantCulture,
                $" height=\"{N(pill.H)}\" rx=\"6\" fill=\"{NoteColor}\"/>\n");
        AppendText(
            _svg, Glyphs(label.Note.Label), pill.X + 8, pill.Y + 15.5, LabelAdvance,
            $" font-size=\"{N(LabelSize)}\" font-weight=\"700\" fill=\"{WindowColor}\"");
      }
    }

    private void DrawCallouts() {
      foreach ((CalloutNote note, Box t) in _callouts) {
        double away = CalloutRadius + 4;
        (double x, double y) = note.Side switch {
          Side.Left => (t.X - away, t.CenterY),
          Side.Right => (t.Right + away, t.CenterY),
          Side.Top => (t.CenterX, t.Y - away),
          _ => (t.CenterX, t.Bottom + away),
        };
        Number(note.Number, x, y);
      }
    }

    /// A circle with a number in it.
    private void Number(int number, double x, double y) {
      _svg.Append(CultureInfo.InvariantCulture,
          $"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{N(CalloutRadius)}\" fill=\"{NoteColor}\"")
          .Append(CultureInfo.InvariantCulture,
              $" stroke=\"{WindowColor}\" stroke-width=\"1.5\"/>\n")
          .Append(CultureInfo.InvariantCulture,
              $"<text x=\"{N(x)}\" y=\"{N(y + 4.5)}\" text-anchor=\"middle\"")
          .Append(CultureInfo.InvariantCulture,
              $" font-size=\"{N(LabelSize)}\" font-weight=\"700\" fill=\"{WindowColor}\">")
          .Append(CultureInfo.InvariantCulture, $"{number}</text>\n");
    }

    /// The panel below the window: what the numbers stand for, then the keys to press.
    private void DrawFooter() {
      if (_footer.H == 0) {
        return;
      }

      Box f = _footer;
      _svg.Append(CultureInfo.InvariantCulture,
          $"<rect x=\"{N(f.X + 0.5)}\" y=\"{N(f.Y + 0.5)}\" width=\"{N(f.W - 1)}\"")
          .Append(CultureInfo.InvariantCulture,
              $" height=\"{N(f.H - 1)}\" rx=\"{N(Radius - 0.5)}\" fill=\"{TitleBarColor}\"")
          .Append(CultureInfo.InvariantCulture, $" stroke=\"{BorderColor}\"/>\n");
      double y = f.Y + 8;
      string text = $" font-size=\"{N(LabelSize)}\" fill=\"{TextColor}\"";
      foreach ((int number, List<string> lines) in _legend) {
        Number(number, f.X + 12 + CalloutRadius, y + 11);
        foreach (string line in lines) {
          AppendText(_svg, Glyphs(line), f.X + 42, y + 15.5, LabelAdvance, text);
          y += 22;
        }
      }

      foreach (KeysNote keys in _keys) {
        foreach (List<(string Text, bool Caption, double X, double Width)> row
            in KeyRows(keys, f.W)) {
          foreach ((string key, bool caption, double at, double width) in row) {
            double x = f.X + at;
            if (caption) {
              AppendText(
                  _svg, Glyphs(key), x, y + 20.5, LabelAdvance,
                  $" font-size=\"{N(LabelSize)}\" fill=\"{MutedColor}\"");
              continue;
            }

            _svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(x + 0.5)}\" y=\"{N(y + 4.5)}\" width=\"{N(width - 1)}\"")
                .Append(CultureInfo.InvariantCulture,
                    $" height=\"23\" rx=\"5\" fill=\"{WindowColor}\" stroke=\"{NoteColor}\"/>\n");
            AppendText(
                _svg, Glyphs(key),
                x + ((width - (TextWidth.Of(key) * LabelAdvance)) / 2), y + 20.5, LabelAdvance,
                $" font-size=\"{N(LabelSize)}\" font-weight=\"700\" fill=\"{TextColor}\"");
          }

          y += 32;
        }
      }
    }
  }
}
