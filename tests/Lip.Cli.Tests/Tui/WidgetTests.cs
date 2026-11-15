using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;

namespace Lip.Cli.Tests.Tui;

/// The widgets of the interface, and its languages.
public sealed class WidgetTests {
  private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false) =>
      new(key == ConsoleKey.Spacebar ? ' ' : '\0', key, shift, alt: false, control: false);

  private static ConsoleKeyInfo Character(char c) => new(c, 0, false, false, false);

  [Fact]
  public void ListView_MovesItsSelectionAndScrolls_AndLeavesTheEdgesToItsOwner() {
    ListView<string> list = new(item => new StyledLine(item)) {
      Items = ["one", "two", "three", "four"],
    };
    ScreenBuffer screen = new(10, 2);

    // ↑ on the first row is not handled.
    Assert.False(list.OnKey(Key(ConsoleKey.UpArrow)));
    Assert.True(list.OnKey(Key(ConsoleKey.DownArrow)));
    Assert.True(list.OnKey(Key(ConsoleKey.DownArrow)));
    list.Draw(screen, new Rect(0, 0, 10, 2), focused: true);

    Assert.Equal("three", list.Current);
    Assert.Equal("two      ▲\nthree    ▼", screen.Text);

    // Neither is ↓ on the last one.
    Assert.True(list.OnKey(Key(ConsoleKey.End)));
    Assert.False(list.OnKey(Key(ConsoleKey.DownArrow)));
    Assert.True(list.OnKey(Key(ConsoleKey.Home)));
    Assert.Equal(0, list.Selected);
    Assert.False(list.OnKey(Character('j')));
  }

  [Fact]
  public void ListView_Wrap_GoesRound_AndSpaceChecksOnlyWithMultiSelect() {
    ListView<string> list = new(item => new StyledLine(item)) {
      Wrap = true,
      MultiSelect = true,
      CanCheck = item => item != "b",
      Items = ["a", "b", "c"],
    };
    ScreenBuffer screen = new(8, 3);

    Assert.True(list.OnKey(Key(ConsoleKey.UpArrow)));
    Assert.Equal("c", list.Current);
    Assert.Equal(["c"], list.Targets);
    Assert.True(list.OnKey(Key(ConsoleKey.Spacebar)));
    Assert.True(list.OnKey(Key(ConsoleKey.DownArrow)));
    Assert.True(list.OnKey(Key(ConsoleKey.Spacebar)));
    Assert.True(list.OnKey(Key(ConsoleKey.DownArrow)));
    // b cannot be checked.
    Assert.True(list.OnKey(Key(ConsoleKey.Spacebar)));
    list.Draw(screen, new Rect(0, 0, 8, 3), focused: false);

    Assert.Equal("[x] a\n    b\n[x] c", screen.Text);
    Assert.Equal(["a", "c"], list.Targets);

    // The checks of the rows that are gone are dropped.
    list.Items = ["a", "b"];
    Assert.Equal(["a"], list.Targets);

    ListView<string> single = new(item => new StyledLine(item)) { Items = ["a"] };
    Assert.False(single.OnKey(Key(ConsoleKey.Spacebar)));
  }

  [Fact]
  public void ListView_Empty_ShowsItsTextAndTakesNoKey() {
    ListView<string> list = new(item => new StyledLine(item)) { Empty = () => "Nothing." };
    ScreenBuffer screen = new(12, 1);

    list.Draw(screen, new Rect(0, 0, 12, 1), focused: true);

    Assert.Equal("Nothing.", screen.Text);
    Assert.False(list.HasCurrent);
    Assert.Empty(list.Targets);
    Assert.False(list.OnKey(Key(ConsoleKey.DownArrow)));
  }

  [Fact]
  public void ButtonRow_ArrowsChooseAndEnterPresses_SkippingWhatCannotBeChosen() {
    List<string> pressed = [];
    bool enabled = false;
    ButtonRow row = new(
        new Button("Start", () => pressed.Add("start")),
        new Button("Stop", () => pressed.Add("stop")) { Enabled = () => enabled },
        new Button(() => "Restart", () => pressed.Add("restart")));
    ScreenBuffer screen = new(40, 1);

    Assert.True(row.OnKey(Key(ConsoleKey.RightArrow)));
    Assert.True(row.OnKey(Key(ConsoleKey.Enter)));
    // → on the last button is left to the owner, as ← on the first.
    Assert.False(row.OnKey(Key(ConsoleKey.RightArrow)));
    enabled = true;
    Assert.True(row.OnKey(Key(ConsoleKey.LeftArrow)));
    Assert.True(row.OnKey(Key(ConsoleKey.Spacebar)));
    Assert.True(row.OnKey(Key(ConsoleKey.LeftArrow)));
    Assert.False(row.OnKey(Key(ConsoleKey.LeftArrow)));
    row.Draw(screen, 0, 0, focused: true);

    Assert.Equal(["restart", "stop"], pressed);
    Assert.Equal("[ Start ] [ Stop ] [ Restart ]", screen.Text);
  }

  [Fact]
  public void Form_MovesBetweenItsFields_AndEachTakesItsOwnKeys() {
    int changes = 0;
    TextRow name = new(() => "Name", "ab", () => "my-server") { Changed = () => changes++ };
    ChoiceRow build = new(() => "Build", [() => "windows", () => "linux"]);
    ToggleRow loader = new(() => "Loader", on: true);
    Form form = new(name, build, loader);
    ScreenBuffer screen = new(30, 3);

    // The text field takes the characters, a space among them.
    Assert.True(form.OnKey(Character('c')));
    Assert.True(form.OnKey(Key(ConsoleKey.Spacebar)));
    Assert.True(form.OnKey(Character('d')));
    Assert.Equal("abc d", name.Value);
    Assert.Equal(3, changes);
    Assert.False(form.OnKey(Key(ConsoleKey.UpArrow)));
    Assert.False(form.OnKey(Key(ConsoleKey.Enter)));

    // The choice goes round with ← and →.
    Assert.True(form.OnKey(Key(ConsoleKey.DownArrow)));
    Assert.True(form.OnKey(Key(ConsoleKey.LeftArrow)));
    Assert.Equal(1, build.Selected);
    Assert.True(form.OnKey(Key(ConsoleKey.RightArrow)));
    Assert.Equal(0, build.Selected);

    // Space switches the toggle; Tab on the last field is left to the owner.
    Assert.True(form.OnKey(Key(ConsoleKey.Tab)));
    Assert.True(form.OnKey(Key(ConsoleKey.Spacebar)));
    Assert.False(loader.On);
    Assert.False(form.OnKey(Key(ConsoleKey.Tab)));
    Assert.True(form.OnKey(Key(ConsoleKey.Tab, shift: true)));
    Assert.Equal(1, form.Focus);
    form.Draw(screen, new Rect(0, 0, 30, 3), focused: true);

    Assert.Equal("  Name    abc d\n> Build   ‹ windows ›\n  Loader  [ ]", screen.Text);
  }

  [Fact]
  public void Form_Stacked_WrapsALabelThatIsLongerThanItIsWide() {
    Form form = new(
        new TextRow(() => "one two three four five six seven eight", "v"),
        new TextRow(() => "Name", "w")) { Stacked = true };

    // Five rows of label, the value, an empty row, and the second field.
    Assert.Equal(9, form.HeightFor(10));
    ScreenBuffer screen = new(10, 9);
    form.Draw(screen, new Rect(0, 0, 10, 9), focused: true);
    Assert.Equal(
        "one two\nthree four\nfive six\nseven\neight\n> v\n\nName\n  w", screen.Text);

    // Without the room for all of it, the label ends with an ellipsis, the value is still
    // shown, and the field after it is not, which the mark at the right says.
    screen = new ScreenBuffer(10, 3);
    form.Draw(screen, new Rect(0, 0, 10, 3), focused: true);
    Assert.Equal("one two\nthree fou…\n> v      ▼", screen.Text);

    // The focused field is among those shown.
    form.Focus = 1;
    form.Draw(screen = new ScreenBuffer(10, 3), new Rect(0, 0, 10, 3), focused: true);
    Assert.Equal("Name     ▲\n> w\n", screen.Text);
  }

  [Fact]
  public void TextField_EditsAtTheCursor_AndShowsItsPlaceholderWhenEmpty() {
    TextField field = new("领地");
    ScreenBuffer screen = new(12, 1);

    field.OnKey(Key(ConsoleKey.LeftArrow));
    field.OnKey(Character('x'));
    field.OnKey(Key(ConsoleKey.Delete));
    Assert.Equal("领x", field.Value);
    Assert.False(field.OnKey(Key(ConsoleKey.Enter)));
    Assert.False(field.OnKey(new ConsoleKeyInfo('u', ConsoleKey.U, false, false, control: true)));

    field.Set("");
    field.Draw(screen, 0, 0, 12, focused: false, "my-server");
    Assert.Equal("my-server", screen.Text);
  }

  [Fact]
  public void TextField_TreatsASurrogatePairAsOneCharacter() {
    string emoji = char.ConvertFromUtf32(0x1F600);
    TextField field = new("a" + emoji + "b");

    field.OnKey(Key(ConsoleKey.LeftArrow));
    field.OnKey(Key(ConsoleKey.LeftArrow));
    Assert.Equal(1, field.Cursor);
    field.OnKey(Key(ConsoleKey.Delete));
    Assert.Equal("ab", field.Value);

    field.Set(emoji);
    field.OnKey(Key(ConsoleKey.Backspace));
    Assert.Equal("", field.Value);
    Assert.Equal(0, field.Cursor);
  }

  [Fact]
  public void TextViewer_WrapsAtSpacesKeepingTheIndentation_AndScrolls() {
    TextViewer viewer = new();
    viewer.SetText("  \"name\": \"a long value\"\nend");
    ScreenBuffer screen = new(12, 2);

    viewer.Draw(screen, new Rect(0, 0, 12, 2));
    Assert.Equal("  \"name\": \"a\nlong value\"▼", screen.Text);
    Assert.True(viewer.Scrollable);
    Assert.Equal(3, viewer.RowsFor(12));

    Assert.True(viewer.OnKey(Key(ConsoleKey.DownArrow)));
    screen.Clear();
    viewer.Draw(screen, new Rect(0, 0, 12, 2));
    Assert.Equal("long value\"▲\nend", screen.Text);

    // Text that fits takes no key.
    viewer.SetText("short");
    viewer.Draw(screen, new Rect(0, 0, 12, 2));
    Assert.False(viewer.OnKey(Key(ConsoleKey.DownArrow)));
  }

  [Fact]
  public void TextViewer_KeepsTheStylesOfWhatItWraps() {
    TextViewer viewer = new() {
      Lines = [new StyledLine().Add("error: ", Theme.Bad).Add("no such package")],
    };
    ScreenBuffer screen = new(10, 3);

    viewer.Draw(screen, new Rect(0, 0, 10, 3));

    Assert.Equal("error: no\nsuch\npackage", screen.Text);
    Assert.StartsWith(Theme.Bad.Ansi() + "error: " + default(Style).Ansi() + "no", screen.RowAnsi(0));
  }

  [Fact]
  public void FocusRing_TabGoesRoundTheAreas_AndLeavesAtTheEnds() {
    FocusRing ring = new(2);

    Assert.False(ring.OnKey(Key(ConsoleKey.Tab, shift: true)));
    Assert.True(ring.OnKey(Key(ConsoleKey.Tab)));
    Assert.Equal(1, ring.Index);
    Assert.False(ring.OnKey(Key(ConsoleKey.Tab)));
    Assert.False(ring.OnKey(Key(ConsoleKey.RightArrow)));
    Assert.True(ring.Previous());
  }

  [Fact]
  public void Panes_SplitAnArea() {
    Rect area = new(2, 1, 40, 10);

    Assert.Equal((new Rect(2, 1, 15, 10), new Rect(17, 1, 25, 10)), Panes.Columns(area, 15));
    Assert.Equal((new Rect(2, 1, 40, 3), new Rect(2, 4, 40, 7)), Panes.Rows(area, 3));
    // The other pane keeps some room.
    Assert.Equal(30, Panes.Columns(area, 100).Left.Width);
  }

  [Theory]
  [InlineData("zh-CN", Language.ZhHans)]
  [InlineData("zh_CN.UTF-8", Language.ZhHans)]
  [InlineData("zh", Language.ZhHans)]
  [InlineData("zh-Hans-HK", Language.ZhHans)]
  [InlineData("zh-SG", Language.ZhHans)]
  [InlineData("zh-TW", Language.ZhHant)]
  [InlineData("zh_HK.UTF-8", Language.ZhHant)]
  [InlineData("zh-MO", Language.ZhHant)]
  [InlineData("zh-Hant", Language.ZhHant)]
  [InlineData("zh_hant_CN@pinyin", Language.ZhHant)]
  [InlineData("en-US", Language.En)]
  [InlineData("en_GB.UTF-8", Language.En)]
  [InlineData("fr-FR", null)]
  [InlineData("C", null)]
  [InlineData("POSIX", null)]
  [InlineData("", null)]
  [InlineData(null, null)]
  public void Languages_OfLocale_IsTheLanguageThatTheLocaleNames(string? locale, Language? expected) {
    Assert.Equal(expected, Languages.OfLocale(locale));
  }

  [Fact]
  public void Languages_Codes_AreTheValuesOfTheConfigurationKey() {
    Assert.Equal(
        Lip.Core.Services.ConfigService.Languages, Languages.All.Select(Languages.Code));
    Assert.All(Languages.All, l => Assert.Equal(l, Languages.Parse(Languages.Code(l))));
    Assert.Null(Languages.Parse("zh"));
    Assert.Null(Languages.Parse(""));
  }

  [Fact]
  public async Task L_IsTheLanguageOfTheInterfaceThatAsks_AndFollowsItsChanges() {
    LanguageScope chinese = new(Language.ZhHans);
    LanguageScope english = new();

    // Two interfaces at once, each with its own language.
    Task<string> first = Task.Run(async () => {
      L.Bind(chinese);
      await Task.Yield();
      return await Task.Run(() => CommonText.Cancel);
    }, TestContext.Current.CancellationToken);
    Task<string> second = Task.Run(() => {
      L.Bind(english);
      return CommonText.Cancel;
    }, TestContext.Current.CancellationToken);

    Assert.Equal("取消", await first);
    Assert.Equal("Cancel", await second);

    // Outside any interface the text is English.
    Assert.Equal("Cancel", CommonText.Cancel);
    Assert.Equal("關閉", L.In(Language.ZhHant, () => CommonText.Close));

    // A change is seen by what the interface started before it.
    L.Bind(chinese);
    Task<string> later = Task.Run(async () => {
      await Task.Delay(50);
      return CommonText.Quit;
    }, TestContext.Current.CancellationToken);
    chinese.Language = Language.ZhHant;
    Assert.Equal("結束", await later);
    L.Bind(null);
  }
}
