using System.Text;
using Lip.Cli.Servers;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui;

public class ScreenTests {
  [Fact]
  public void Write_WideCharacters_TakeTwoColumns() {
    ScreenBuffer screen = new(10, 1);

    int end = screen.Write(0, 0, "领地x");

    Assert.Equal(5, end);
    Assert.Equal("领地x", screen.RowText(0));
  }

  [Fact]
  public void Write_PastTheLimit_IsClippedBeforeAWideCharacterThatDoesNotFit() {
    ScreenBuffer screen = new(10, 1);

    int end = screen.Write(0, 0, "a领地", default, limit: 4);

    Assert.Equal(3, end);
    Assert.Equal("a领", screen.RowText(0));
  }

  [Theory]
  [InlineData(3, "领 │地x")]
  [InlineData(2, "领│ 地x")]
  [InlineData(4, "领域│ x")]
  public void Write_OverOneHalfOfAWideCharacter_KeepsTheWidthOfTheRow(int x, string expected) {
    // Arrange: wide characters in the columns 0 to 1, 2 to 3 and 4 to 5.
    ScreenBuffer screen = new(8, 1);
    screen.Write(0, 0, "领域地x");

    // Act: as the border of a dialog above the page.
    screen.Write(x, 0, "│");

    // Assert
    Assert.Equal(expected, screen.RowText(0));
    Assert.Equal(7, TextWidth.Of(screen.RowText(0)));
  }

  [Fact]
  public void Fill_OverOneHalfOfAWideCharacter_KeepsTheWidthOfTheRow() {
    ScreenBuffer screen = new(8, 1);
    screen.Write(0, 0, "领域地x");

    screen.Fill(new Rect(1, 0, 2, 1), default);

    Assert.Equal("    地x", screen.RowText(0));
  }

  [Fact]
  public void WriteFit_TooLong_EndsInAnEllipsis() {
    ScreenBuffer screen = new(20, 1);

    screen.WriteFit(0, 0, 6, "LegacyScriptEngine");

    Assert.Equal("Legac…", screen.RowText(0));
  }

  [Fact]
  public void Box_DrawsABorderWithItsTitle() {
    ScreenBuffer screen = new(12, 3);

    screen.Box(new Rect(0, 0, 12, 3), "Mods", default, default);

    Assert.Equal("┌─ Mods ───┐\n│          │\n└──────────┘", screen.Text);
  }

  [Fact]
  public void RowAnsi_SetsTheStyleOnlyWhereItChanges() {
    ScreenBuffer screen = new(4, 1);
    screen.Write(0, 0, "ab", Style.Of(Color.Red, bold: true));

    Assert.Equal("\x1b[0;1;31mab\x1b[0m  \x1b[0m", screen.RowAnsi(0));
  }

  [Theory]
  // Chinese text is broken between its characters, not only at the spaces around a name.
  [InlineData("此工作區使用 linux 組建，它無法在本系統上執行。", 20,
      "此工作區使用 linux|組建，它無法在本系統|上執行。")]
  // A mark that closes does not start a line, and one that opens does not end one.
  [InlineData("安裝遊戲，然後啟動", 8, "安裝遊|戲，然後|啟動")]
  [InlineData("請看（說明）文字", 6, "請看|（說|明）文|字")]
  [InlineData("請輸入“名稱”後繼續", 8, "請輸入|“名稱”後|繼續")]
  // What is part of the character before it stays with it: a skin tone, a joined emoji.
  [InlineData("好👍🏽好", 4, "好|👍🏽|好")]
  [InlineData("好👩‍💻好", 4, "好|👩‍💻|好")]
  // A name in Latin letters stays whole between the characters around it.
  [InlineData("需要LeviLamina才能執行", 12, "需要|LeviLamina才|能執行")]
  public void Wrap_BreaksChineseTextBetweenCharacters(string text, int width, string expected) {
    List<string> lines = TextWidth.Wrap(text, width);

    Assert.Equal(expected, string.Join('|', lines));
    Assert.All(lines, line => Assert.True(TextWidth.Of(line) <= width));
  }

  [Theory]
  [InlineData("one two three", 7, "one two|three")]
  [InlineData("abcdefgh", 3, "abc|def|gh")]
  [InlineData("a\nb", 5, "a|b")]
  public void Wrap_BreaksAtSpacesAndInsideLongWords(string text, int width, string expected) {
    Assert.Equal(expected, string.Join('|', TextWidth.Wrap(text, width)));
  }

  /// A word that is longer than a line, such as the identity of a mod, a URL or a path, is
  /// broken after the marks between its parts, not in the middle of a name; a word that fits
  /// a line stays whole; and only a word without such a mark is cut where the line ends.
  [Theory]
  [InlineData(
      "github.com/LiteLDev/LegacyScriptEngine", 20, "github.com/LiteLDev/|LegacyScriptEngine")]
  [InlineData("github.com/LiteLDev/LegacyScriptEngine#lua", 14,
      "github.com/|LiteLDev/|LegacyScriptEn|gine#lua")]
  [InlineData("ID github.com/acme/Alpha", 16, "ID github.com/|acme/Alpha")]
  [InlineData("see https://example.com/a/b", 12, "see https://|example.com/|a/b")]
  [InlineData(@"D:\servers\survival\mods", 12, @"D:\servers\|survival\|mods")]
  [InlineData("levilamina-release-windows-x64.zip", 16, "levilamina-|release-windows-|x64.zip")]
  [InlineData("user_name@example.com", 12, "user_name@|example.com")]
  [InlineData("a short.word fits", 10, "a|short.word|fits")]
  [InlineData("version 1.26.60 here", 12, "version|1.26.60 here")]
  public void Wrap_BreaksALongWordAfterTheMarksBetweenItsParts(
      string text, int width, string expected) {
    List<string> lines = TextWidth.Wrap(text, width);

    Assert.Equal(expected, string.Join('|', lines));
    Assert.All(lines, line => Assert.True(TextWidth.Of(line) <= width));
    // The styled line is broken at the same places.
    Assert.Equal(
        expected,
        string.Join('|', TextWidth.Wrap(new StyledLine(text), width).Select(row => row.PlainText)));
  }

  /// What a server writes is wrapped as text is: at spaces and after the marks of a long
  /// word, with its styles and its indentation kept, and never inside a character that is
  /// two columns wide or one that is two UTF-16 code units.
  [Fact]
  public void Wrap_OfAStyledLine_BreaksAtWords_KeepsStyles_AndNeverSplitsACharacter() {
    StyledLine line = new StyledLine("[INFO] ", Theme.Good)
        .Add("Loaded the mod github.com/LiteLDev/LegacyMoney", Theme.Strong);

    List<StyledLine> rows = TextWidth.Wrap(line, 24);

    Assert.Equal(
        ["[INFO] Loaded the mod", "github.com/LiteLDev/", "LegacyMoney"],
        rows.Select(row => row.PlainText));
    Assert.Equal(
        [new Span("[INFO] ", Theme.Good), new Span("Loaded the mod", Theme.Strong)], rows[0].Spans);
    Assert.Equal([new Span("LegacyMoney", Theme.Strong)], rows[2].Spans);

    // Indentation is kept, and a row is never left with spaces only.
    Assert.Equal(
        ["    at Some.Method", "in file.cs"],
        TextWidth.Wrap(new StyledLine("    at Some.Method in file.cs"), 18)
            .Select(row => row.PlainText));
    Assert.Equal(
        ["    abcdefgh", "ij"],
        TextWidth.Wrap(new StyledLine("    abcdefghij"), 12).Select(row => row.PlainText));

    // Wide characters and surrogate pairs: each row holds whole characters of at most the
    // width, and together they are the line.
    foreach (string text in (string[])["伺服器已啟動完成", "a伺服器已啟動", "😀😀😀😀😀", "ab😀cd😀ef", "好👍🏽好"]) {
      for (int width = 1; width <= 7; width++) {
        List<string> wrapped = [
          .. TextWidth.Wrap(new StyledLine(text), width).Select(row => row.PlainText),
        ];
        Assert.Equal(text, string.Concat(wrapped));
        Assert.All(wrapped, row => {
          Assert.True(TextWidth.Of(row) <= Math.Max(2, width), $"'{row}' at {width}");
          Assert.False(char.IsLowSurrogate(row[0]), $"'{text}' at {width}");
          Assert.False(char.IsHighSurrogate(row[^1]), $"'{text}' at {width}");
        });
        Assert.Equal(
            text,
            string.Concat(TextWidth.Wrap(text, width)));
      }
    }

    Assert.Equal([""], TextWidth.Wrap(new StyledLine(), 10).Select(row => row.PlainText));
  }

  [Fact]
  public void AnsiLog_SgrSequences_StyleTheTextAndOthersAreDropped() {
    AnsiLog log = new();

    log.Append(Encoding.UTF8.GetBytes(
        "\x1b]0;title\a\x1b[32mINFO\x1b[0m ok\x1b[2K\r\n\x1b[38;5;208mwarn\n"));

    Assert.Equal(3, log.Lines.Count);
    Assert.Equal([new Span("INFO", Style.Of(Color.Green)), new Span(" ok", default)],
        log.Lines[0].Spans);
    Assert.Equal([new Span("warn", Style.Of(209))], log.Lines[1].Spans);
  }

  [Fact]
  public void AnsiLog_CarriageReturnWithoutLineFeed_WritesTheLineAgain() {
    AnsiLog log = new();

    log.Append(Encoding.UTF8.GetBytes("10%\r50%\r100%\ndone"));

    Assert.Equal(["100%", "done"], log.Lines.Select(line => line.PlainText));
  }

  [Fact]
  public void AnsiLog_CharacterSplitAcrossChunks_IsDecodedOnce() {
    AnsiLog log = new();
    byte[] bytes = Encoding.UTF8.GetBytes("领地");

    log.Append(bytes[..2]);
    log.Append(bytes[2..]);

    Assert.Equal("领地", log.Lines[0].PlainText);
  }

  [Fact]
  public void AnsiLog_MoreLinesThanItKeeps_DropsTheOldest() {
    AnsiLog log = new(maxLines: 3);

    log.Append(Encoding.UTF8.GetBytes("1\n2\n3\n4\n"));

    Assert.Equal(["3", "4", ""], log.Lines.Select(line => line.PlainText));
  }

  [Fact]
  public void LineSinkStream_PassesOnEachLineAndTheRestWhenDisposed() {
    List<string> lines = [];
    using (LineSinkStream stream = new((line, _) => lines.Add(line))) {
      stream.Write(Encoding.UTF8.GetBytes("one\r\ntw"));
      stream.Write(Encoding.UTF8.GetBytes("o\nrest"));
    }

    Assert.Equal(["one", "two", "rest"], lines);
  }

  [Fact]
  public void LineSinkStream_LineEndedByACarriageReturnAlone_IsTransient() {
    List<(string, bool)> lines = [];
    using (LineSinkStream stream = new((line, transient) => lines.Add((line, transient)))) {
      stream.Write(Encoding.UTF8.GetBytes("10%\r50%\r100%\r\ndone\n"));
    }

    Assert.Equal([("10%", true), ("50%", true), ("100%", false), ("done", false)], lines);
  }

  [Fact]
  public async Task QueueInputStream_ReadsWhatIsSentThenEndsOnceComplete() {
    QueueInputStream stream = new();
    stream.Send("stop\n");
    stream.Complete();
    byte[] buffer = new byte[3];

    CancellationToken token = TestContext.Current.CancellationToken;

    int first = await stream.ReadAsync(buffer, token);
    int second = await stream.ReadAsync(buffer, token);
    int third = await stream.ReadAsync(buffer, token);

    Assert.Equal((3, 2, 0), (first, second, third));
  }
}
