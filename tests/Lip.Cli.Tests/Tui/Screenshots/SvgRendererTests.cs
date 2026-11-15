using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// The frames that screenshots are drawn from, and the pictures drawn of them. The grid of a
/// picture starts 22 pixels from the left and 48 from the top of a picture without labels in
/// its margins, and a cell is 9 by 20.
public sealed class SvgRendererTests {
  private const double Left = 22;
  private const double Top = 48;
  private const double Baseline = 14.5;
  private static readonly XNamespace _svg = "http://www.w3.org/2000/svg";

  private static XElement Draw(ScreenBuffer screen, params Note[] notes) =>
      XDocument.Parse(SvgRenderer.Render(Frame.Of(screen), notes)).Root!;

  private static List<XElement> Texts(XElement picture) => [.. picture.Descendants(_svg + "text")];

  private static XElement Text(XElement picture, string text) =>
      Texts(picture).Single(element => element.Value == text);

  private static double Number(XElement element, string attribute) =>
      double.Parse(element.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);

  private static string? Fill(XElement element) =>
      element.Attribute("fill")?.Value ?? element.Parent?.Attribute("fill")?.Value;

  [Fact]
  public void Frame_OfAScreen_HasItsTextItsStylesAndItsWideCharacters() {
    ScreenBuffer screen = new(12, 2);
    screen.Write(0, 0, "ab", Theme.Accent);
    screen.Write(2, 0, "模组", Theme.Selected);
    screen.Write(
        0, 1, "x", Style.Of(Color.TrueColor | 0x102030, 200, underline: true, inverse: true));

    Frame frame = Frame.Of(screen);

    Assert.Equal(screen.Text, frame.Text);
    Assert.Equal((12, 2), (frame.Width, frame.Height));
    Assert.Equal(new FrameCell("a", Theme.Accent, false), frame[0, 0]);
    Assert.Equal(new FrameCell("模", Theme.Selected, false), frame[2, 0]);
    Assert.Equal(new FrameCell(" ", Theme.Selected, true), frame[3, 0]);
    Assert.Equal(new FrameCell("组", Theme.Selected, false), frame[4, 0]);
    Assert.Equal(
        Style.Of(Color.TrueColor | 0x102030, 200, underline: true, inverse: true),
        frame[0, 1].Style);
    Assert.Equal(default, frame[11, 1].Style);
  }

  [Fact]
  public void Frame_Find_GivesTheCellsOfATextAfterWideCharacters() {
    ScreenBuffer screen = new(20, 3);
    screen.Write(0, 1, "安装 [ Apply ] x [ Apply ]");

    Frame frame = Frame.Of(screen);

    Assert.Equal(new CellRect(5, 1, 9, 1), frame.Find("[ Apply ]"));
    Assert.Equal(new CellRect(17, 1, 3, 1), frame.Find("[ A", 2));
    Assert.Equal(new CellRect(0, 1, 4, 1), frame.Find("安装"));
    Assert.Null(frame.Find("[ Apply ]", 3));
    Assert.Null(frame.Find("Cancel"));
  }

  [Fact]
  public void Frame_Replace_KeepsTheStylesAndTheWidthOfTheRow() {
    ScreenBuffer screen = new(16, 1);
    screen.Write(0, 0, "up 00:00:07", Theme.Good);
    screen.Write(11, 0, "    │", Theme.Border);
    Frame frame = Frame.Of(screen);

    Frame same = frame.Replace(new Regex(@"\d\d:\d\d:\d\d"), _ => "00:12:34");
    Frame shorter = frame.Replace(new Regex(@"\d\d:\d\d:\d\d"), _ => "1h");
    Frame longer = frame.Replace(new Regex(@"\d\d:\d\d:\d\d"), _ => "00:12:34 模");

    Assert.Equal("up 00:12:34    │", same.RowText(0));
    Assert.Equal(Theme.Good, same[10, 0].Style);
    Assert.Equal("up 1h          │", shorter.RowText(0));
    Assert.Equal("up 00:12:34 模 │", longer.RowText(0));
    Assert.Equal(Theme.Border, longer[12, 0].Style);
    Assert.True(longer[13, 0].Continuation);
    Assert.Equal("up 00:00:07    │", frame.RowText(0));
    // Nothing but spaces may give way to a longer replacement.
    Assert.Throws<InvalidOperationException>(
        () => frame.Replace(new Regex("up"), _ => "upper"));
  }

  [Fact]
  public void Picture_IsWellFormed_WithoutScriptsFontsOrAnythingFromElsewhere() {
    ScreenBuffer screen = new(30, 4);
    screen.Box(new Rect(0, 0, 30, 4), "Mods & <more>", Theme.Border, Theme.Strong);
    screen.Write(2, 1, "a \"quoted\" <tag> & more");

    string text = SvgRenderer.Render(
        Frame.Of(screen), [Note.Arrow(At.Text("quoted"), "A <label> & more")]);
    XElement picture = XDocument.Parse(text).Root!;

    Assert.Equal(_svg + "svg", picture.Name);
    Assert.DoesNotContain("<script", text);
    Assert.DoesNotContain("href", text);
    Assert.DoesNotContain("url(", text);
    Assert.DoesNotContain("@font-face", text);
    Assert.DoesNotContain("\r", text);
    Assert.Contains(Texts(picture), element => element.Value == "a \"quoted\" <tag> & more");
    // The size of the picture is that of its view box.
    Assert.Equal(
        $"0 0 {picture.Attribute("width")!.Value} {picture.Attribute("height")!.Value}",
        picture.Attribute("viewBox")!.Value);
  }

  [Fact]
  public void Text_IsPlacedAtItsColumn_AndStretchedToTheWidthOfItsCells() {
    ScreenBuffer screen = new(40, 3);
    screen.Write(3, 1, "Create a server");
    screen.Write(30, 2, "x");

    XElement picture = Draw(screen);

    XElement run = Text(picture, "Create a server");
    Assert.Equal(Left + (3 * 9), Number(run, "x"));
    Assert.Equal(Top + 20 + Baseline, Number(run, "y"));
    Assert.Equal(15 * 9, Number(run, "textLength"));
    Assert.Equal("spacingAndGlyphs", run.Attribute("lengthAdjust")!.Value);
    Assert.Equal(Left + (30 * 9), Number(Text(picture, "x"), "x"));
    // The picture is as wide as the cells, the padding of the window and the margins.
    Assert.Equal((40 * 9) + 24 + 20, Number(picture, "width"));
    Assert.Equal((3 * 20) + 30 + 8 + 12 + 20, Number(picture, "height"));
  }

  [Fact]
  public void Text_SeparatedByMoreThanOneSpace_IsPlacedRunByRun() {
    ScreenBuffer screen = new(40, 1);
    screen.Write(0, 0, "Game       1.26.60  ok");

    XElement picture = Draw(screen);

    Assert.Equal(["Game", "1.26.60", "ok"], Texts(picture).Skip(1).Select(t => t.Value));
    Assert.Equal(Left + (11 * 9), Number(Text(picture, "1.26.60"), "x"));
    Assert.Equal(Left + (20 * 9), Number(Text(picture, "ok"), "x"));
  }

  [Fact]
  public void WideCharacters_TakeTwoCellsEach_WhateverTheFont() {
    ScreenBuffer screen = new(40, 1);
    screen.Write(2, 0, "安装 mods 模组。ok");

    XElement picture = Draw(screen);

    // Each wide character is in the middle of its two cells: columns 2-3, 4-5, 12-13, ...
    Assert.Equal(
        $"{Left + (3 * 9)} {Left + (5 * 9)}", Text(picture, "安装").Attribute("x")!.Value);
    Assert.Equal("middle", Text(picture, "安装").Attribute("text-anchor")!.Value);
    Assert.Null(Text(picture, "安装").Attribute("textLength"));
    Assert.Equal(
        $"{Left + (13 * 9)} {Left + (15 * 9)} {Left + (17 * 9)}",
        Text(picture, "模组。").Attribute("x")!.Value);
    // What follows them starts at the column after their cells.
    Assert.Equal(Left + (7 * 9), Number(Text(picture, "mods"), "x"));
    Assert.Equal(Left + (18 * 9), Number(Text(picture, "ok"), "x"));
  }

  [Fact]
  public void Symbols_AreInTheMiddleOfTheirCells() {
    ScreenBuffer screen = new(20, 1);
    screen.Write(4, 0, "● Running ✓");

    XElement picture = Draw(screen);

    Assert.Equal(Left + (4.5 * 9), Number(Text(picture, "●"), "x"));
    Assert.Equal("middle", Text(picture, "●").Attribute("text-anchor")!.Value);
    Assert.Equal(Left + (6 * 9), Number(Text(picture, "Running"), "x"));
    Assert.Equal(Left + (14.5 * 9), Number(Text(picture, "✓"), "x"));
  }

  [Fact]
  public void Colors_AreThoseOfThePalette_AndBoldIsBold() {
    ScreenBuffer screen = new(40, 3);
    screen.Write(0, 0, "plain");
    screen.Write(8, 0, "accent", Theme.Accent);
    screen.Write(16, 0, "bad", Theme.Bad);
    screen.Write(0, 1, "selected  ", Theme.Selected);
    screen.Write(12, 1, "inverse", Style.Of(Color.Green, inverse: true));
    screen.Write(0, 2, "true", Style.Of(Color.TrueColor | 0x12AB34));
    screen.Write(8, 2, "cube", Style.Of(197));
    screen.Write(16, 2, "gray", Style.Of(245));

    XElement picture = Draw(screen);

    Assert.Equal(SvgRenderer.TextColor, Fill(Text(picture, "plain")));
    Assert.Equal(SvgRenderer.Foreground(Color.Cyan), Fill(Text(picture, "accent")));
    Assert.Equal("700", Text(picture, "accent").Attribute("font-weight")!.Value);
    Assert.Null(Text(picture, "plain").Attribute("font-weight"));
    Assert.Equal(SvgRenderer.Foreground(Color.Red), Fill(Text(picture, "bad")));
    Assert.Equal("#ffffff", Fill(Text(picture, "selected")));
    Assert.Equal("#12ab34", Fill(Text(picture, "true")));
    // Color 197 is index 196 of the palette of 256: the brightest red of its cube.
    Assert.Equal("#ff0000", Fill(Text(picture, "cube")));
    Assert.Equal("#808080", Fill(Text(picture, "gray")));
    // The background of the selected cells is one rectangle of ten cells in the second row.
    XElement selected = picture.Descendants(_svg + "rect")
        .Single(rect => rect.Attribute("fill")!.Value == SvgRenderer.Background(Color.Blue));
    Assert.Equal((Left, Top + 20, 90.0, 20.0), (
        Number(selected, "x"), Number(selected, "y"), Number(selected, "width"),
        Number(selected, "height")));
    // Inverse swaps the colors: the text is as dark as the window, on green.
    Assert.Equal(SvgRenderer.WindowColor, Fill(Text(picture, "inverse")));
    Assert.Contains(picture.Descendants(_svg + "rect"), rect =>
        rect.Attribute("fill")!.Value == SvgRenderer.Foreground(Color.Green)
            && Number(rect, "x") == Left + (12 * 9) && Number(rect, "width") == 7 * 9);
    // Every color of the palette is one of the picture, legible on the window.
    Assert.All(
        Enumerable.Range(1, 16),
        color => Assert.Matches("^#[0-9a-f]{6}$", SvgRenderer.Foreground(color)));
  }

  [Fact]
  public void Underline_IsALineBelowTheText() {
    ScreenBuffer screen = new(20, 1);
    screen.Write(2, 0, "link", Style.Of(underline: true));

    XElement picture = Draw(screen);

    XElement line = picture.Descendants(_svg + "path").Single(
        path => path.Attribute("d")!.Value == $"M{Left + 18} {Top + 17.5}H{Left + 54}");
    Assert.Equal(SvgRenderer.TextColor, line.Attribute("stroke")!.Value);
  }

  [Fact]
  public void Boxes_AreLinesThatMeetAtTheEdgesOfTheCells_NotText() {
    ScreenBuffer screen = new(10, 3);
    screen.Box(new Rect(0, 0, 10, 3), "", Theme.Border, Theme.Strong);

    XElement picture = Draw(screen);

    Assert.DoesNotContain(Texts(picture), text => Regex.IsMatch(text.Value, "[─│┌┐└┘]"));
    XElement border = picture.Descendants(_svg + "path").Single(
        path => path.Attribute("stroke")?.Value == SvgRenderer.Foreground(Color.BrightBlack));
    // One line for each side: from the middle of a corner to the middle of the other, less
    // and more half the width of the line, so that the corners are closed.
    Assert.Equal(
        $"M{Left + 3.88} {Top + 10}H{Left + 86.13}"
            + $"M{Left + 3.88} {Top + 50}H{Left + 86.13}"
            + $"M{Left + 4.5} {Top + 9.38}V{Top + 50.63}"
            + $"M{Left + 85.5} {Top + 9.38}V{Top + 50.63}",
        border.Attribute("d")!.Value);
    Assert.True(SvgRenderer.IsDrawn("┼"));
    Assert.False(SvgRenderer.IsDrawn("+"));
  }

  [Fact]
  public void Boxes_WithATitle_AreLinesAtBothSidesOfIt() {
    ScreenBuffer screen = new(20, 2);
    screen.Box(new Rect(0, 0, 20, 2), "Mods", Theme.Border, Theme.Strong);

    XElement picture = Draw(screen);

    // "┌─ Mods ─…┐": the line ends at the cell of the space before the title, and goes on
    // after the space behind it.
    string lines = picture.Descendants(_svg + "path")
        .Single(path => path.Attribute("stroke") is not null).Attribute("d")!.Value;
    Assert.StartsWith($"M{Left + 3.88} {Top + 10}H{Left + 18}M{Left + 72} {Top + 10}H", lines);
    Assert.Equal(Left + 27, Number(Text(picture, "Mods"), "x"));
  }

  [Fact]
  public void Blocks_AreRectangles_SideBySideAsOne() {
    ScreenBuffer screen = new(20, 1);
    screen.Write(2, 0, "███░░", Theme.Accent);
    screen.Write(8, 0, "▌");

    XElement picture = Draw(screen);

    List<XElement> blocks = [
      .. picture.Descendants(_svg + "rect").Where(rect => Number(rect, "y") == Top
          && Number(rect, "height") == 20 && Number(rect, "width") < 100),
    ];
    Assert.Equal(
        [(Left + 18, 27.0, null), (Left + 45, 18.0, "0.3"), (Left + 72, 4.5, null)],
        blocks.Select(rect => (
            Number(rect, "x"), Number(rect, "width"), rect.Attribute("fill-opacity")?.Value)));
    Assert.DoesNotContain(Texts(picture), text => text.Value.Contains('█'));
  }

  [Fact]
  public void Targets_AreFoundByTheTextOfTheFrame() {
    ScreenBuffer screen = new(40, 8);
    screen.Box(new Rect(0, 0, 12, 8), "", Theme.Border, Theme.Strong);
    screen.Box(new Rect(12, 0, 28, 8), "Mods", Theme.Border, Theme.Strong);
    screen.Write(2, 2, "Mods");
    screen.Write(14, 2, "[ ] 模组 Alpha    1.2.0");
    screen.Write(14, 5, "[ Apply ] [ Cancel ]");
    Frame frame = Frame.Of(screen);

    Assert.Equal(new CellRect(18, 2, 10, 1), At.Text("模组 Alpha").Resolve(frame));
    // The row of a list is what is between the borders of its box.
    Assert.Equal(new CellRect(13, 2, 26, 1), At.Row("Alpha").Resolve(frame));
    Assert.Equal(new CellRect(24, 5, 10, 1), At.Button("Cancel").Resolve(frame));
    // The box with the title, not the entry of the sidebar with the same text.
    Assert.Equal(new CellRect(12, 0, 28, 8), At.Panel("Mods").Resolve(frame));
    Assert.Equal(
        new CellRect(14, 5, 20, 1), At.Button("Apply").To(At.Button("Cancel")).Resolve(frame));
    Assert.Equal(new CellRect(16, 1, 14, 3), At.Text("模组 Alpha").Pad(2, 1).Resolve(frame));
    Assert.Equal(new CellRect(0, 7, 40, 1), At.Rows(7).Resolve(frame));
    Assert.Equal(new CellRect(3, 4, 2, 2), At.Cells(3, 4, 2, 2).Resolve(frame));

    // A target that the frame does not have says so, with the frame.
    InvalidOperationException missing = Assert.Throws<InvalidOperationException>(
        () => SvgRenderer.Render(frame, [Note.Box(At.Button("Save"))]));
    Assert.Contains("[ Save ]", missing.Message);
    Assert.Contains("[ Apply ] [ Cancel ]", missing.Message);
    Assert.Throws<InvalidOperationException>(() => At.Panel("Home").Resolve(frame));
    Assert.Throws<InvalidOperationException>(() => At.Cells(39, 7, 2).Resolve(frame));
  }

  [Fact]
  public void Annotations_AreDrawnAtTheCellsOfTheirTargets() {
    ScreenBuffer screen = new(40, 6);
    screen.Write(10, 2, "[ Apply ]");
    screen.Write(10, 4, "安装");
    Target button = At.Button("Apply");

    XElement picture = Draw(
        screen, Note.Box(button), Note.Ellipse(At.Text("安装")),
        Note.Callout(2, button, "Carries the plan out."), Note.Callout(1, At.Text("安装")),
        Note.Focus(button), Note.Keys("↓", "Enter"));

    // The box is around the nine cells of the button, a little wider than they are.
    XElement box = picture.Descendants(_svg + "rect").Single(
        rect => rect.Attribute("stroke")?.Value == SvgRenderer.NoteColor
            && rect.Attribute("fill")!.Value == SvgRenderer.NoteColor);
    Assert.Equal((Left + 90 - 4, Top + 40 - 2, 81.0 + 8, 20.0 + 4), (
        Number(box, "x"), Number(box, "y"), Number(box, "width"), Number(box, "height")));
    XElement ellipse = picture.Descendants(_svg + "ellipse").Single();
    Assert.Equal((Left + 90 + 18, Top + 80 + 10), (Number(ellipse, "cx"), Number(ellipse, "cy")));

    // The numbers are at the left of their targets; the one with a text is in the legend
    // below the window too, before its text.
    List<XElement> circles = [
      .. picture.Descendants(_svg + "circle")
          .Where(circle => circle.Attribute("fill")!.Value == SvgRenderer.NoteColor),
    ];
    Assert.Equal(3, circles.Count);
    Assert.Equal((Left + 90 - 14, Top + 50), (Number(circles[0], "cx"), Number(circles[0], "cy")));
    Assert.Equal((Left + 90 - 14, Top + 90), (Number(circles[1], "cx"), Number(circles[1], "cy")));
    double window = Top + (6 * 20) + 12;
    Assert.True(Number(circles[2], "cy") > window);
    XElement legend = Text(picture, "Carries the plan out.");
    Assert.True(Number(legend, "y") > window);
    Assert.True(Number(legend, "x") > Number(circles[2], "cx"));
    Assert.True(Number(Text(picture, "Enter"), "y") > Number(legend, "y"));
    Assert.True(Number(picture, "height") > Number(Text(picture, "Enter"), "y"));

    // The dimming leaves the cells of the button out.
    XElement dim = picture.Descendants(_svg + "path")
        .Single(path => path.Attribute("fill-rule")?.Value == "evenodd");
    Assert.EndsWith(
        $"M{Left + 87} {Top + 37}H{Left + 174}V{Top + 63}H{Left + 87}z",
        dim.Attribute("d")!.Value);
  }

  [Fact]
  public void Arrows_ComeFromLabelsInTheMargins_WhichMakeRoomForThem() {
    ScreenBuffer screen = new(40, 6);
    screen.Write(10, 2, "[ Apply ]");
    Target button = At.Button("Apply");

    XElement plain = Draw(screen);
    XElement right = Draw(screen, Note.Arrow(button, "Press this"));
    XElement left = Draw(screen, Note.Arrow(button, "按这里", Side.Left));
    XElement both = Draw(
        screen, Note.Arrow(button, "Above", Side.Top), Note.Arrow(button, "Below", Side.Bottom));

    // "Press this" is ten characters of 7.8 pixels, in a label that is 16 wider, 28 from
    // the window.
    double label = (10 * 7.8) + 16;
    Assert.Equal(Number(plain, "width") + label + 28, Number(right, "width"));
    Assert.Equal(Number(plain, "height"), Number(right, "height"));
    double window = Number(plain, "width") - 10;
    Assert.Equal(window + 28 + 8, Number(Text(right, "Press this"), "x"));
    // The arrow ends at the right edge of the button, in the middle of its row.
    Assert.Contains(right.Descendants(_svg + "path"), path =>
        path.Attribute("d")!.Value.StartsWith(
            $"M{Left + 171 + 5} {Top + 50}L", StringComparison.Ordinal));

    // A label at the left moves the window, and everything in it, to the right.
    double moved = (6 * 7.8) + 16 + 28;
    Assert.Equal(Number(plain, "width") + moved, Number(left, "width"));
    Assert.Equal(Left + moved + 90, Number(Text(left, "[ Apply ]"), "x"));
    Assert.Equal("按这里", Text(left, "按这里").Value);

    Assert.Equal(Number(plain, "height") + (2 * (28 + 22)), Number(both, "height"));
    Assert.True(Number(Text(both, "Above"), "y") < Number(Text(both, "[ Apply ]"), "y"));
    Assert.True(Number(Text(both, "Below"), "y") > Number(Text(both, "[ Apply ]"), "y"));
  }

  [Fact]
  public void Labels_AtTheSameSide_DoNotLieOnEachOther() {
    ScreenBuffer screen = new(40, 6);
    screen.Write(2, 2, "one two");

    XElement picture = Draw(
        screen, Note.Arrow(At.Text("one"), "First"), Note.Arrow(At.Text("two"), "Second"));

    Assert.True(
        Number(Text(picture, "Second"), "y") - Number(Text(picture, "First"), "y") >= 22);
  }

  [Fact]
  public void Annotations_ThatDoNotFit_MakeThePictureLarger() {
    ScreenBuffer screen = new(20, 3);
    screen.Write(0, 2, "a b c");

    XElement picture = Draw(
        screen, Note.KeysAfter("Then press", "Ctrl+C", "Enter", "PgDn", "Home", "Space"),
        Note.Arrow(At.Text("a"), "One"), Note.Arrow(At.Text("b"), "Two │ three"),
        Note.Arrow(At.Text("c"), "Four"));

    // Everything drawn is inside the picture: the keys go on in another row, and the labels
    // that were moved down below the window make the picture higher.
    double width = Number(picture, "width");
    double height = Number(picture, "height");
    Assert.All(picture.Descendants(_svg + "rect"), rect => {
      Assert.True(Number(rect, "x") + Number(rect, "width") <= width);
      Assert.True(Number(rect, "y") + Number(rect, "height") <= height);
    });
    Assert.True(Number(Text(picture, "Space"), "y") > Number(Text(picture, "Ctrl+C"), "y"));
    // A label is text throughout, the characters that draw boxes too.
    Assert.Contains(Texts(picture), text => text.Value == "│");
  }

  [Fact]
  public void Picture_OfTheSameFrameAndAnnotations_IsTheSameByteForByte() {
    string Once() {
      ScreenBuffer screen = new(60, 10);
      screen.Box(new Rect(0, 0, 60, 10), "模组", Theme.Border, Theme.Accent);
      screen.Write(2, 2, "[ ] github.com/LiteLDev/LeviOptimize  1.8.1", Theme.Selected);
      screen.Write(2, 4, "████░░░░ 50%", Theme.Accent);
      return SvgRenderer.Render(
          Frame.Of(screen),
          [
            Note.Callout(1, At.Row("LeviOptimize"), "一个模组"), Note.Arrow(At.Text("50%"), "Half"),
            Note.Focus(At.Panel("模组")), Note.KeysAfter("Then", "↓", "Enter"),
          ],
          new SvgOptions { Title = "lip — mods" });
    }

    string first = Once();

    Assert.Equal(first, Once());
    Assert.Contains("<title>lip — mods</title>", first);
    // No number is written in the way of the culture, and none with more than two decimals.
    Assert.DoesNotMatch(@"\d,\d|\d\.\d{3}", Regex.Replace(first, "<text[^>]*>[^<]*</text>", ""));
  }
}
