using System.Text;
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
  [InlineData("one two three", 7, "one two|three")]
  [InlineData("abcdefgh", 3, "abc|def|gh")]
  [InlineData("a\nb", 5, "a|b")]
  public void Wrap_BreaksAtSpacesAndInsideLongWords(string text, int width, string expected) {
    Assert.Equal(expected, string.Join('|', TextWidth.Wrap(text, width)));
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
