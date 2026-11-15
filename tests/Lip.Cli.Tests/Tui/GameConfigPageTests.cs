using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GameConfig;

namespace Lip.Cli.Tests.Tui;

/// The Game config page of the interface, driven by the keys of a fake terminal.
public sealed class GameConfigPageTests {
  /// A server.properties as the server writes one, with CRLF line breaks and a line that is
  /// neither a property nor a comment of one.
  private const string Properties =
      "server-name=Dedicated Server\r\n"
      + "# Used as the server name\r\n"
      + "# Allowed values: Any string without semicolon symbol.\r\n"
      + "\r\n"
      + "gamemode=survival\r\n"
      + "# Sets the game mode for new players.\r\n"
      + "# Allowed values: \"survival\", \"creative\", or \"adventure\"\r\n"
      + "\r\n"
      + "allow-cheats=false\r\n"
      + "# If true then cheats like commands can be used.\r\n"
      + "# Allowed values: \"true\" or \"false\"\r\n"
      + "\r\n"
      + "max-players=10\r\n"
      + "# The maximum number of players that can play on the server.\r\n"
      + "# Allowed values: Any positive integer\r\n"
      + "\r\n"
      + "server-port=19132\r\n"
      + "# Which IPv4 port the server should listen to.\r\n"
      + "# Allowed values: Integers in the range [1, 65535]\r\n"
      + "\r\n"
      + "#a line that lip keeps  \r\n";

  private const string Allowlist = """
      [
        // The owner.
        { "ignoresPlayerLimit": false, "name": "Alex", "xuid": "111", "note": "kept" },
      ]
      """;

  private const string Permissions = """
      [ { "permission": "member", "xuid": "222" } ]
      """;

  private static string File(string name) => Path.Join(TuiHarness.DefaultRoot, name);

  /// Starts the interface on a workspace with the files given. Not async, as
  /// TuiHarness.Start, so that the test reads text in the language of the interface.
  private static TuiHarness Start(
      Language language = Language.En,
      string? properties = Properties,
      string? allowlist = null,
      string? permissions = null,
      int width = 120,
      int height = 36) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddDirectory(TuiHarness.DefaultRoot);
    void Add(string name, string? text) {
      if (text is not null) {
        fs.AddFile(File(name), new MockFileData(text));
      }
    }

    Add("server.properties", properties);
    Add("allowlist.json", allowlist);
    Add("permissions.json", permissions);
    return TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      FileSystem = fs,
      Width = width,
      Height = height,
    });
  }

  /// Saves with the Save button, from the rows of a section.
  private static async Task Save(TuiHarness tui) {
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(screen => screen.Contains($"Enter {CommonText.HintSelect.Text}"));
    tui.Press(ConsoleKey.Enter);
    // Saved, and the focus is back on the rows, away from the buttons: the message of a save
    // before this one may still be shown.
    await tui.WaitFor(screen => screen.Contains(CommonText.Saved)
        && screen.Contains($"{CommonText.HintMove.Key} {CommonText.HintMove.Text}")
        && !screen.Contains($"{CommonText.HintSwitch.Key} {CommonText.HintSwitch.Text}"));
  }

  [Theory]
  [InlineData(Language.En, "Game config")]
  [InlineData(Language.ZhHans, "游戏配置")]
  [InlineData(Language.ZhHant, "遊戲設定")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.GameConfig);

    Assert.Equal(title, tui.Text(() => GameConfigText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Fact]
  public async Task Properties_ShowTheDescriptionOfTheSelectedOne_OnlyInTheDetails() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name  Dedicated Server");

    Assert.Contains(GameConfigText.AppliesOnStart, tui.Screen);
    Assert.Contains("Used as the server name", tui.Screen);
    Assert.DoesNotContain("Sets the game mode", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Sets the game mode for new players.");
    Assert.DoesNotContain("Used as the server name", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Edit  Tab Next  Esc Back", tui.Screen);
  }

  [Fact]
  public async Task Space_TogglesABoolean_AndSaveReplacesOnlyItsValue() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    Assert.Contains($"[ {CommonText.Save} ] [ {CommonText.Discard} ]", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("Space Toggle");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    Assert.Contains("Properties *", tui.Screen);
    Assert.Contains(GameConfigText.Original("false"), tui.Screen);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));

    await Save(tui);

    Assert.Equal(
        Properties.Replace("allow-cheats=false", "allow-cheats=true"),
        tui.Fs.File.ReadAllText(File("server.properties")));
    Assert.DoesNotContain("* allow-cheats", tui.Screen);
    Assert.DoesNotContain("Properties *", tui.Screen);
    Assert.Contains("allow-cheats [x]", tui.Screen);
    Assert.False(tui.Fs.File.Exists(File("allowlist.json")));
    Assert.False(tui.Fs.File.Exists(File("permissions.json")));
  }

  [Fact]
  public async Task Enter_OnABoolean_TogglesIt_AndEnterAgainOffersToResetIt() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("* allow-cheats [x]");
    await tui.WaitFor("Enter Actions  Space Toggle");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Reset);
    Assert.Contains(GameConfigText.Toggle, tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("* allow-cheats");
    Assert.Contains("allow-cheats [ ]", tui.Screen);
    Assert.DoesNotContain("Properties *", tui.Screen);
  }

  [Fact]
  public async Task Enter_OnAnEnumeration_OpensItsChoices() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("gamemode     survival");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("adventure");
    tui.Snapshot("gameconfig-choice");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("* gamemode     creative");
    Assert.Contains(GameConfigText.Original("survival"), tui.Screen);

    // Esc in the choices changes nothing.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Choose);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("adventure");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ gamemode ─");
    Assert.Contains("* gamemode     creative", tui.Screen);

    await Save(tui);
    Assert.Equal(
        Properties.Replace("gamemode=survival", "gamemode=creative"),
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task Enter_OnAnInteger_AsksForIt_AndRefusesWhatIsNotOne() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-port  19132");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.End);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.ValueBetween(1, 65535));
    tui.Type("9");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotBetween(1, 65535));
    tui.Snapshot("gameconfig-prompt-error");
    Assert.DoesNotContain("* server-port", tui.Screen);

    tui.Press(ConsoleKey.Backspace, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("* server-port  191");

    // The number of players has no upper bound, and is not a word.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => screen.Contains("─ max-players ─"));
    tui.Press(ConsoleKey.Backspace, 2);
    tui.Type("many");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotAnInteger);
    tui.Press(ConsoleKey.Backspace, 4);
    tui.Type("20");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("* max-players  20");

    await Save(tui);
    Assert.Equal(
        Properties.Replace("max-players=10", "max-players=20")
            .Replace("server-port=19132", "server-port=191"),
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  /// A positive integer is not 0 and not below 0, and "equal to 5 or greater" is not 4: the
  /// prompt says the least value, and refuses what is below it.
  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task Enter_OnAPositiveInteger_RefusesZeroAndNegativeNumbers(Language language) {
    const string properties =
        "max-players=10\n# Allowed values: Any positive integer\n"
        + "view-distance=32\n# Allowed values: Positive integer equal to 5 or greater.\n"
        + "player-idle-timeout=30\n# Allowed values: Any non-negative integer.\n";
    using TuiHarness tui = Start(language, properties);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("max-players");
    tui.Press(ConsoleKey.Enter);

    // max-players: 0 and -3 are refused, 25 is taken.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.ValueAtLeast(1));
    tui.Press(ConsoleKey.Backspace, 2);
    tui.Type("0");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotAtLeast(1));
    tui.Snapshot("gameconfig-positive");
    tui.Press(ConsoleKey.Backspace);
    tui.Type("-3");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains(GameConfigText.NotAtLeast(1), tui.Screen);
    Assert.Contains("─ max-players ─", tui.Screen);
    // What is no integer is told so; an integer of too many digits is outside the bounds.
    tui.Press(ConsoleKey.Backspace, 2);
    tui.Type("1.5");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotAnInteger);
    tui.Press(ConsoleKey.Backspace, 3);
    tui.Type("-99999999999999999999");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotAtLeast(1));
    tui.Press(ConsoleKey.Backspace, 21);
    tui.Type("25");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => Regex.IsMatch(screen, @"\* max-players\s+25"), "the players");

    // view-distance: 4 and 0 are refused, 5 is taken.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.ValueAtLeast(5));
    tui.Press(ConsoleKey.Backspace, 2);
    tui.Type("4");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotAtLeast(5));
    tui.Press(ConsoleKey.Backspace);
    tui.Type("0");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains(GameConfigText.NotAtLeast(5), tui.Screen);
    tui.Press(ConsoleKey.Backspace);
    tui.Type("5");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => Regex.IsMatch(screen, @"\* view-distance\s+5"), "the distance");

    // player-idle-timeout: 0 is taken, -1 is refused.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.ValueAtLeast(0));
    tui.Press(ConsoleKey.Backspace, 2);
    tui.Type("-1");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotAtLeast(0));
    tui.Press(ConsoleKey.Backspace, 2);
    tui.Type("0");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(
        screen => Regex.IsMatch(screen, @"\* player-idle-timeout\s+0"), "the timeout");

    await Save(tui);
    Assert.Equal(
        properties.Replace("max-players=10", "max-players=25")
            .Replace("view-distance=32", "view-distance=5")
            .Replace("player-idle-timeout=30", "player-idle-timeout=0"),
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task Enter_OnAText_AsksForIt_AndAnUnchangedTextIsNoChange() {
    using TuiHarness tui = Start(
        properties: "level-name= Bedrock level\nmotd=\n# The message of the day.\n");
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("level-name");
    tui.Press(ConsoleKey.Enter);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => screen.Contains("─ level-name ─"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ level-name ─");
    Assert.DoesNotContain("*", tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("The message of the day.");
    Assert.Contains($"motd       {GameConfigText.EmptyValue}", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => screen.Contains("─ motd ─"));
    tui.Type("Hello there");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("* motd       Hello there");
    Assert.Contains(GameConfigText.Original(GameConfigText.EmptyValue), tui.Screen);

    await Save(tui);
    Assert.Equal(
        "level-name= Bedrock level\nmotd=Hello there\n# The message of the day.\n",
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task MissingProperties_AreNotCreated_AndTheListsAreReadAsEmpty() {
    using TuiHarness tui = Start(properties: null);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor(screen => screen.Contains("server.properties does not exist yet."));
    Assert.Contains("↑↓ Move  Tab Next  Esc Back", tui.Screen);

    // Nothing to enter at the right: the focus stays on the sections.
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(GameConfigText.AddPlayer);
    Assert.Contains(GameConfigText.AllowlistDetail, tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(GameConfigText.PermissionsDetail);

    Assert.False(tui.Fs.File.Exists(File("server.properties")));
    Assert.False(tui.Fs.File.Exists(File("allowlist.json")));
    Assert.False(tui.Fs.File.Exists(File("permissions.json")));
  }

  [Fact]
  public async Task Allowlist_AddsAPlayer_ChangesAndRemovesOne_InSimplifiedChinese() {
    using TuiHarness tui = Start(Language.ZhHans, allowlist: Allowlist);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("属性");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Alex 111");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("Enter 选择");

    // Add player…: a name is needed, and one that is not listed yet.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("XUID（可选）");
    tui.Snapshot("gameconfig-add-player");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Type("alex");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("已在列表中");
    tui.Press(ConsoleKey.Backspace, 4);
    tui.Type("Steve");
    tui.Press(ConsoleKey.Tab);
    tui.Type("x");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("XUID 只能是数字。");
    tui.Press(ConsoleKey.Backspace);
    tui.Type("333");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Steve 333");
    await tui.WaitFor("白名单 *");
    await tui.WaitFor("Enter 操作");

    // The new player is selected: its menu switches whether it ignores the player limit.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("移除");
    Assert.Contains("─ Steve ─", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Steve 333  无视人数上限");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("遵守人数上限");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("遵守人数上限");

    // Remove Alex.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Alex ─");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Alex");
    Assert.Equal(Allowlist, tui.Fs.File.ReadAllText(File("allowlist.json")));

    await Save(tui);
    Assert.Equal(
        "[\n    {\n        \"ignoresPlayerLimit\": true,\n        \"name\": \"Steve\",\n"
            + "        \"xuid\": \"333\"\n    }\n]\n",
        tui.Fs.File.ReadAllText(File("allowlist.json")));
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));
    Assert.DoesNotContain("白名单 *", tui.Screen);
  }

  [Fact]
  public async Task Allowlist_KeepsWhatItDoesNotShow_AndAPlayerNeedsNoXuid() {
    using TuiHarness tui = Start(allowlist: Allowlist);
    await tui.GoTo(PageId.GameConfig);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Alex 111");
    tui.Press(ConsoleKey.Enter);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.XuidOptional);
    tui.Type("Sam");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Sam");

    await Save(tui);
    Assert.Equal(
        "[\n    {\n        \"ignoresPlayerLimit\": false,\n        \"name\": \"Alex\",\n"
            + "        \"xuid\": \"111\",\n        \"note\": \"kept\"\n    },\n"
            + "    {\n        \"ignoresPlayerLimit\": false,\n"
            + "        \"name\": \"Sam\"\n    }\n]\n",
        tui.Fs.File.ReadAllText(File("allowlist.json")));
  }

  [Fact]
  public async Task Permissions_AddAPlayer_ChangeAndRemoveOne_InTraditionalChinese() {
    using TuiHarness tui = Start(Language.ZhHant, permissions: Permissions);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("屬性");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("222 member");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter 選擇");

    // Add player…: the xuid, then the permission.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 新增玩家 ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Type("Steve");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("XUID 只能是數字。");
    tui.Press(ConsoleKey.Backspace, 5);
    tui.Type("444");
    tui.Press(ConsoleKey.Enter);
    // The permission offered first is not that of an operator.
    await tui.WaitFor("可以正常遊玩和建造。");
    tui.Snapshot("gameconfig-permission");
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("可以使用所有指令。");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("只能觀看，不能更動世界。");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("444 visitor");
    await tui.WaitFor("權限 *");

    // The menu of the player that was there: another permission.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("變更權限…");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("可以正常遊玩和建造。");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("222 operator");

    // Remove the new one.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 444 ─");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("444");

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter 選擇");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("已儲存。");
    Assert.Equal(
        "[\n    {\n        \"permission\": \"operator\",\n        \"xuid\": \"222\"\n    }\n]\n",
        tui.Fs.File.ReadAllText(File("permissions.json")));
  }

  [Fact]
  public async Task Permissions_OfAListedXuid_AreChangedRatherThanAddedTwice() {
    using TuiHarness tui = Start(permissions: Permissions);
    await tui.GoTo(PageId.GameConfig);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("222 member");
    tui.Press(ConsoleKey.Enter);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => screen.Contains($"─ {GameConfigText.AddPlayerTitle} ─"));
    tui.Type("222");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.MemberDetail);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(GameConfigText.OperatorDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("222 operator");

    Assert.DoesNotContain("222 member", tui.Screen);
    await tui.WaitFor("Enter Actions");
  }

  [Fact]
  public async Task AFileThatCannotBeRead_IsShown_AndNeverWritten() {
    const string broken = "{ \"not\": \"an array\" }";
    using TuiHarness tui = Start(allowlist: broken, permissions: "[ oops");
    await tui.GoTo(PageId.GameConfig);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("lip cannot read allowlist.json.");
    tui.Snapshot("gameconfig-unreadable");
    Assert.Contains(broken, tui.Screen);
    Assert.DoesNotContain(GameConfigText.AddPlayer, tui.Screen);

    // Nothing to act on at the right.
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("lip cannot read permissions.json.");
    Assert.Contains("[ oops", tui.Screen);

    // Saving another file leaves both as they are.
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    await Save(tui);
    Assert.Equal(broken, tui.Fs.File.ReadAllText(File("allowlist.json")));
    Assert.Equal("[ oops", tui.Fs.File.ReadAllText(File("permissions.json")));
  }

  [Fact]
  public async Task PropertiesThatAreNotUtf8_AreNotShownAsProperties_NorWritten() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    byte[] bytes = [0x6D, 0x6F, 0x74, 0x64, 0x3D, 0xFF, 0xFE, 0x0A];
    fs.AddFile(File("server.properties"), new MockFileData(bytes));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.GameConfig);

    await tui.WaitFor("lip cannot read server.properties.");
    Assert.DoesNotContain("motd", tui.Screen);
    Assert.Equal(bytes, fs.File.ReadAllBytes(File("server.properties")));
  }

  [Fact]
  public async Task Save_AsksBeforeItOverwritesAFileThatChangedMeanwhile() {
    using TuiHarness tui = Start(allowlist: Allowlist);
    await tui.GoTo(PageId.GameConfig);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Alex 111");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.XuidOptional);
    tui.Type("Sam");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Sam");

    // The server writes the file while the page has it open.
    const string server = "[{\"ignoresPlayerLimit\":false,\"name\":\"Kai\"}]";
    tui.Fs.File.WriteAllText(File("allowlist.json"), server);

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.ChangedOnDisk);
    tui.Snapshot("gameconfig-conflict");
    Assert.Contains($"[ {CommonText.Overwrite} ] [ {CommonText.Cancel} ]", tui.Screen);
    // Enter is Cancel: nothing is written, and the changes stay.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(CommonText.ChangedOnDisk);
    Assert.Equal(server, tui.Fs.File.ReadAllText(File("allowlist.json")));
    Assert.Contains("Allowlist *", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.ChangedOnDisk);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Saved);
    Assert.Contains("\"Sam\"", tui.Fs.File.ReadAllText(File("allowlist.json")));
    Assert.DoesNotContain("Kai", tui.Fs.File.ReadAllText(File("allowlist.json")));

    // What was written is what the file is compared with from then on: the new player,
    // still selected, is removed without a question.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Remove);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Allowlist *");
    await Save(tui);
    Assert.DoesNotContain("Sam", tui.Fs.File.ReadAllText(File("allowlist.json")));
  }

  [Fact]
  public async Task Page_ReadsTheFilesAgain_WheneverItIsShown() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("gamemode     survival");

    await tui.GoTo(PageId.Home);
    tui.Fs.File.WriteAllText(
        File("server.properties"), Properties.Replace("gamemode=survival", "gamemode=adventure"));
    await tui.GoTo(PageId.GameConfig);

    await tui.WaitFor("gamemode     adventure");
  }

  [Fact]
  public async Task DiscardButton_AsksFirst_ThenReadsTheFilesAgain() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");

    // ↓ below the last row reaches the buttons; → goes to Discard.
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("←→ Switch  Enter Select  Tab Next  ↑↓ Move  Esc Back");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.DiscardQuestion);
    tui.Snapshot("gameconfig-discard");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.DiscardQuestion);
    Assert.Contains("* allow-cheats [x]", tui.Screen);

    // ↑ goes back to the rows, and ↓ to the buttons again.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("↑↓ Move");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("←→ Switch");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.DiscardQuestion);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("allow-cheats [ ]");
    Assert.DoesNotContain("*", tui.Screen);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));
    // The buttons are disabled again, and the focus is back on the rows.
    await tui.WaitFor("↑↓ Move  Enter Edit");
  }

  [Fact]
  public async Task LeavingWithChanges_AsksToSaveOrDiscard_AndCancelStays() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");

    // Esc goes back to the sections, then to the sidebar; another page asks first.
    tui.Press(ConsoleKey.Escape, 2);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.UnsavedQuestion);
    tui.Snapshot("gameconfig-unsaved");
    Assert.Contains(
        $"[ {CommonText.Save} ] [ {CommonText.Discard} ] [ {CommonText.Cancel} ]", tui.Screen);
    Assert.Contains("server.properties", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.UnsavedQuestion);
    Assert.Equal(PageId.GameConfig, tui.Context.CurrentPage);
    Assert.Contains("* allow-cheats [x]", tui.Screen);

    // Discard leaves, and writes nothing.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.UnsavedQuestion);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.ModConfig);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));

    // Coming back shows the file as it is.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("allow-cheats [ ]");
    Assert.DoesNotContain("* allow-cheats", tui.Screen);
  }

  [Fact]
  public async Task LeavingWithChanges_SavesThemWhenAsked_ThenLeaves() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");

    tui.Press(ConsoleKey.Escape, 2);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(CommonText.UnsavedQuestion);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.GetMods);
    Assert.Equal(
        Properties.Replace("allow-cheats=false", "allow-cheats=true"),
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task Quitting_AndAnotherWorkspace_AskAboutChangesToo() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    string other = Path.Join(TuiHarness.DefaultRoot, "other");
    tui.Fs.AddDirectory(other);
    tui.Fs.AddFile(Path.Join(other, "server.properties"), new MockFileData("motd=other\n"));
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");

    // Ctrl+C asks about the changes before it asks to quit.
    tui.Interrupt();
    await tui.WaitFor(CommonText.UnsavedQuestion);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.UnsavedQuestion);
    Assert.DoesNotContain(CommonText.QuitQuestion, tui.Screen);

    // Another workspace: saved to the one that was edited, and then the other one is shown.
    await tui.Invoke(() => tui.Context.AddWorkspace(other, out _));
    await tui.WaitFor(CommonText.UnsavedQuestion);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("motd");
    Assert.Equal(
        Properties.Replace("allow-cheats=false", "allow-cheats=true"),
        tui.Fs.File.ReadAllText(File("server.properties")));
    Assert.Equal("motd=other\n", tui.Fs.File.ReadAllText(Path.Join(other, "server.properties")));
    Assert.DoesNotContain("allow-cheats", tui.Screen);

    Assert.Equal(0, await tui.Quit());
  }

  [Fact]
  public async Task Tab_GoesRoundTheAreas_AndSkipsTheButtonsWithoutChanges() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    Assert.Contains("↑↓ Move  Enter Open  Tab Next  Esc Back", tui.Screen);

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Edit");
    // Nothing to save: Tab leaves the page for the sidebar.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter Edit");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Space Toggle");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Open");
    // ← in the rows goes back to the sections as well.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("Space Toggle");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("Enter Open");
    Assert.True(tui.Context.ContentFocused);

    // Tab from the sidebar starts at the sections, and Shift+Tab at the buttons, the last
    // area while there are changes.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntil(() => !tui.Context.ContentFocused);
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("←→ Switch");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Open");
  }

  [Fact]
  public async Task Esc_GoesBackOneLevelAtATime_FromTheButtonsToTheRowsToTheSections() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter Edit");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch  Enter Select  Tab Next  ↑↓ Move  Esc Back");

    // The buttons were reached from the rows: Esc goes back to them, then to the sections,
    // then to the sidebar, and the changes are still there.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Space Toggle");
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Enter Open");
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntil(() => !tui.Context.ContentFocused);
    Assert.Contains("* allow-cheats [x]", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Page_FitsTheTerminal_InEveryLanguage(Language language, int width, int height) {
    using TuiHarness tui = Start(
        language,
        Properties + "server-authoritative-block-breaking-pick-range-scalar=1.5\n"
            + "# Allowed values: Any number\n",
        Allowlist, Permissions, width, height);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("gamemode");
    tui.Snapshot("gameconfig-sections");

    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(GameConfigText.Properties + " *");
    tui.Snapshot("gameconfig-properties");
    // The buttons and the note are whole, and so is the hint bar.
    Assert.Contains(
        $"[ {CommonText.Save} ] [ {CommonText.Discard} ]", tui.Screen);
    Assert.Contains(GameConfigText.AppliesOnStart, tui.Screen);
    Assert.Contains($"Esc {CommonText.HintBack.Text}", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Reset);
    tui.Snapshot("gameconfig-property-menu");
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.End);
    await tui.WaitFor("Allowed values: Any number");
    tui.Snapshot("gameconfig-long-property");

    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"Enter {CommonText.HintActions.Text}");
    tui.Snapshot("gameconfig-allowlist");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Remove);
    tui.Snapshot("gameconfig-player-menu");
    tui.Press(ConsoleKey.Escape);

    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("222 member");
    tui.Snapshot("gameconfig-permissions");

    tui.Press(ConsoleKey.Tab, 2);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    tui.Snapshot("gameconfig-buttons");
  }

  #region While the server runs

  /// Goes to the page and to the row of allow-cheats, a boolean, in the properties.
  private static async Task ToAllowCheats(TuiHarness tui) {
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("allow-cheats [ ]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("If true then cheats");
  }

  [Fact]
  public async Task WhileTheServerRuns_PropertiesAreOnlyShown_AndAreEditedAgainOnceItStops() {
    using TuiHarness tui = Start();
    await ToAllowCheats(tui);
    Assert.Contains("Enter Edit  Space Toggle", tui.Screen);
    Assert.Contains(GameConfigText.AppliesOnStart, tui.Screen);

    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToEdit);
    Assert.DoesNotContain(GameConfigText.AppliesOnStart, tui.Screen);
    // The values and the descriptions are still read; the hints offer no editing.
    Assert.Contains("allow-cheats [ ]", tui.Screen);
    Assert.Contains("If true then cheats", tui.Screen);
    await tui.WaitFor(" ↑↓ Move  Enter Actions  Tab Next  Esc Back");
    Assert.DoesNotContain("Space Toggle", tui.Screen);
    Assert.DoesNotContain("Enter Edit", tui.Screen);

    // Space switches nothing, and says why.
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(CommonText.StopServerFirst);
    Assert.Contains("allow-cheats [ ]", tui.Screen);
    Assert.DoesNotContain("*", tui.Screen);

    // Enter shows the action of the row, which cannot be chosen.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ allow-cheats ─");
    tui.Snapshot("gameconfig-running-menu");
    Assert.Contains(GameConfigText.Toggle, tui.Screen);
    Assert.Contains(" ↑↓ Move  Esc Close", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Spacebar);
    await tui.Settle();
    Assert.Contains("─ allow-cheats ─", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ allow-cheats ─");
    Assert.Contains("allow-cheats [ ]", tui.Screen);

    // An enumeration and a text open no dialog to change them either.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ gamemode ─");
    Assert.Contains(GameConfigText.Choose, tui.Screen);
    Assert.DoesNotContain("Enter Select", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ gamemode ─");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ server-name ─");
    Assert.Contains(GameConfigText.Edit, tui.Screen);
    Assert.DoesNotContain(GameConfigText.Value + " ", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ server-name ─");

    // Nothing can be saved or discarded: Tab leaves the page.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));

    // The server stops: the page is edited again, as it is.
    await tui.SetServerRunning(false);
    await tui.WaitFor(GameConfigText.AppliesOnStart);
    Assert.DoesNotContain(GameConfigText.StopToEdit, tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter Edit");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("Enter Edit  Space Toggle");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    await Save(tui);
    Assert.Equal(
        Properties.Replace("allow-cheats=false", "allow-cheats=true"),
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task WhileTheServerRuns_TheAllowlistIsOnlyShown_InSimplifiedChinese() {
    using TuiHarness tui = Start(Language.ZhHans, allowlist: Allowlist);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("属性");
    await tui.SetServerRunning(true);
    await tui.WaitFor("服务器运行中，停止后才能修改。");
    Assert.DoesNotContain("下次启动服务器时生效。", tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Alex 111");
    tui.Press(ConsoleKey.RightArrow);
    // Add player… is disabled: its details say why, no hint offers it, and Enter asks nothing.
    await tui.WaitFor("请先停止服务器。");
    Assert.Contains("开启 allow-list 后，只有这些玩家可以加入。", tui.Screen);
    await tui.WaitFor(" ↑↓ 移动  Tab 下一项  Esc 返回");
    tui.Snapshot("gameconfig-running-add-player");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Spacebar);
    await tui.Settle();
    Assert.DoesNotContain("XUID（可选）", tui.Screen);

    // The actions of a player are shown, and none of them can be chosen.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter 操作");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Alex ─");
    tui.Snapshot("gameconfig-running-player-menu");
    Assert.Contains("无视人数上限", tui.Screen);
    Assert.Contains("移除", tui.Screen);
    Assert.Contains("请先停止服务器。", tui.Screen);
    Assert.DoesNotContain("Enter 选择", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains("─ Alex ─", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Alex ─");

    Assert.Contains("Alex 111", tui.Screen);
    Assert.DoesNotContain("*", tui.Screen);
    Assert.Equal(Allowlist, tui.Fs.File.ReadAllText(File("allowlist.json")));
  }

  [Fact]
  public async Task WhileTheServerRuns_ThePermissionsAreOnlyShown_InTraditionalChinese() {
    using TuiHarness tui = Start(Language.ZhHant, permissions: Permissions);
    await tui.SetServerRunning(true);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("伺服器執行中，停止後才能修改。");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("222 member");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("請先停止伺服器。");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.DoesNotContain("─ 新增玩家 ─", tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter 操作");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 222 ─");
    Assert.Contains("變更權限…", tui.Screen);
    Assert.Contains("移除", tui.Screen);
    Assert.Contains("請先停止伺服器。", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.DoesNotContain("operator", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ 222 ─");
    Assert.Equal(Permissions, tui.Fs.File.ReadAllText(File("permissions.json")));

    // Once the server has stopped the player is changed, and the change is saved.
    await tui.SetServerRunning(false);
    await tui.WaitFor("下次啟動伺服器時生效。");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("變更權限…");
    await tui.WaitFor("Enter 選擇");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("operator");
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("222 operator");
    await Save(tui);
    Assert.Contains("\"operator\"", tui.Fs.File.ReadAllText(File("permissions.json")));
  }

  [Fact]
  public async Task AServerThatStarts_KeepsTheChangesUnsaved_UntilItHasStopped() {
    using TuiHarness tui = Start();
    await ToAllowCheats(tui);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");

    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToSave);
    Assert.Contains("* allow-cheats [x]", tui.Screen);
    Assert.Contains("Properties *", tui.Screen);

    // Of the buttons only Discard can be chosen: Enter asks, and Esc keeps the changes.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" Enter Select  Tab Next  ↑↓ Move  Esc Back");
    Assert.DoesNotContain("←→ Switch", tui.Screen);
    tui.Snapshot("gameconfig-running-unsaved");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.DiscardQuestion);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.DiscardQuestion);
    Assert.Contains("* allow-cheats [x]", tui.Screen);

    // Leaving cannot save: Enter is Cancel, which stays.
    tui.Press(ConsoleKey.Escape, 3);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(GameConfigText.CannotSaveNow);
    tui.Snapshot("gameconfig-running-leave");
    Assert.Contains($"[ {CommonText.Discard} ] [ {CommonText.Cancel} ]", tui.Screen);
    Assert.DoesNotContain($"[ {CommonText.Save} ] [ {CommonText.Discard} ] [", tui.Screen);
    Assert.Contains("server.properties", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(GameConfigText.CannotSaveNow);
    Assert.Equal(PageId.GameConfig, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Contains("* allow-cheats [x]", tui.Screen);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));

    // The server stops: the changes are still there, and are saved.
    await tui.SetServerRunning(false);
    await tui.WaitFor(GameConfigText.AppliesOnStart);
    Assert.Contains("* allow-cheats [x]", tui.Screen);
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("←→ Switch  Enter Select");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Saved);
    Assert.Equal(
        Properties.Replace("allow-cheats=false", "allow-cheats=true"),
        tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task AServerThatStarts_LetsTheChangesBeDiscarded_ToLeaveThePage() {
    using TuiHarness tui = Start();
    await ToAllowCheats(tui);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToSave);

    tui.Press(ConsoleKey.Escape, 2);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(GameConfigText.CannotSaveNow);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.ModConfig);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));

    // Coming back shows the file as it is, still only shown.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("allow-cheats [ ]");
    await tui.WaitFor(GameConfigText.StopToEdit);
    Assert.DoesNotContain("*", tui.Screen);
  }

  [Fact]
  public async Task AServerThatStarts_WhileADialogAsksForAValue_KeepsTheValueUnsaved() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name  Dedicated Server");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter Edit");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ server-name ─");

    await tui.SetServerRunning(true);
    tui.Type("!");
    tui.Press(ConsoleKey.Enter);

    // What the user typed is not lost: it is a change like the others, and is not written.
    await tui.WaitUntilGone("─ server-name ─");
    await tui.WaitFor("* server-name  Dedicated Server!");
    await tui.WaitFor(GameConfigText.StopToSave);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));

    // The row offers its actions from then on, and none of them can be chosen.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Reset);
    await tui.WaitFor(" ↑↓ Move  Esc Close");
    Assert.Contains(CommonText.StopServerFirst, tui.Screen);
  }

  [Fact]
  public async Task AServerThatStarts_WhileAMenuIsOpen_KeepsItsActionsFromChangingAnything() {
    using TuiHarness tui = Start(allowlist: Allowlist);
    await tui.GoTo(PageId.GameConfig);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Alex 111");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter Actions");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Remove);

    // Remove was offered before the server started; chosen after, it removes nobody.
    await tui.SetServerRunning(true);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("─ Alex ─");
    await tui.WaitFor(CommonText.StopServerFirst);
    Assert.Contains("Alex 111", tui.Screen);
    Assert.DoesNotContain("Allowlist *", tui.Screen);
    Assert.Contains(GameConfigText.StopToEdit, tui.Screen);
  }

  [Fact]
  public async Task AServerThatStops_LeavesTheChosenButtonAlone_WhileTheButtonsHaveTheFocus() {
    using TuiHarness tui = Start();
    await ToAllowCheats(tui);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToSave);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" Enter Select  Tab Next  ↑↓ Move  Esc Back");

    // Discard is chosen, the only button there was: Enter still discards, and asks first.
    await tui.SetServerRunning(false);
    await tui.WaitFor("←→ Switch  Enter Select");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.DiscardQuestion);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Fact]
  public async Task APageOpenedWhileTheServerRuns_ReadsItsFilesAgain_OnceTheServerHasStopped() {
    using TuiHarness tui = Start(allowlist: Allowlist);
    await tui.SetServerRunning(true);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor(GameConfigText.StopToEdit);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Alex 111");

    // A menu is open when the server stops: the files are read once it has closed.
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter Actions");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Alex ─");
    tui.Fs.File.WriteAllText(
        File("allowlist.json"), "[{\"ignoresPlayerLimit\":false,\"name\":\"Kai\",\"xuid\":\"9\"}]");
    await tui.SetServerRunning(false);
    await tui.WaitFor(GameConfigText.AppliesOnStart);
    await tui.Settle();
    Assert.Contains("Alex 111", tui.Screen);
    tui.Press(ConsoleKey.Escape);

    await tui.WaitFor("Kai 9");
    Assert.DoesNotContain("Alex", tui.Screen);
  }

  [Fact]
  public async Task Save_WritesNothing_WhenTheServerStartedSinceTheInterfaceLooked() {
    using TuiHarness tui = Start();
    await ToAllowCheats(tui);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch  Enter Select");

    // The server starts and Save is pressed before the interface looks at its lock again.
    await tui.Invoke(() => {
      tui.SetServerLockHeld(true, tui.Root);
      tui.Press(ConsoleKey.Enter);
      return true;
    });

    await tui.WaitFor(CommonText.StopServerFirst);
    Assert.Contains($"─ {GameConfigText.SaveTitle} ─", tui.Screen);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone($"─ {GameConfigText.SaveTitle} ─");

    // The changes stay, and the page says that they are saved once the server has stopped.
    await tui.WaitFor(GameConfigText.StopToSave);
    Assert.Contains("* allow-cheats [x]", tui.Screen);
    Assert.DoesNotContain(CommonText.Saved, tui.Screen);
  }

  [Fact]
  public async Task AServerThatStops_HasItsFilesReadAgain_UnlessTheyHaveChanges() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("gamemode     survival");
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToEdit);

    // The server writes its file while it runs; the page shows it once the server stopped.
    string written = Properties.Replace("gamemode=survival", "gamemode=adventure");
    tui.Fs.File.WriteAllText(File("server.properties"), written);
    await tui.SetServerRunning(false);
    await tui.WaitFor("gamemode     adventure");
    Assert.Contains(GameConfigText.AppliesOnStart, tui.Screen);

    // With changes the page keeps what it has, and saving asks before it overwrites.
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats [x]");
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToSave);
    tui.Fs.File.WriteAllText(File("server.properties"), Properties);
    await tui.SetServerRunning(false);
    await tui.WaitFor(GameConfigText.AppliesOnStart);
    await tui.Settle();
    Assert.Contains("* allow-cheats [x]", tui.Screen);
    Assert.Contains("gamemode     adventure", tui.Screen);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch  Enter Select");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.ChangedOnDisk);
    Assert.Equal(Properties, tui.Fs.File.ReadAllText(File("server.properties")));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task WhileTheServerRuns_ThePageFitsTheTerminal_InEveryLanguage(
      Language language, int width, int height) {
    using TuiHarness tui = Start(language, Properties, Allowlist, Permissions, width, height);
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("gamemode");
    tui.Snapshot("gameconfig-stopped");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(GameConfigText.Properties + " *");

    // With changes: the note is whole, beside the buttons.
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToSave);
    tui.Snapshot("gameconfig-running-changes");
    Assert.Contains($"[ {CommonText.Save} ] [ {CommonText.Discard} ]", tui.Screen);
    Assert.Contains($"Enter {CommonText.HintActions.Text}", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Reset);
    await tui.WaitFor(CommonText.StopServerFirst);
    tui.Snapshot("gameconfig-running-property-menu");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GameConfigText.Reset);

    // Without: discarded, and only shown.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.DiscardQuestion);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.StopToEdit);
    await tui.WaitFor("gamemode");
    tui.Snapshot("gameconfig-running-properties");
    Assert.DoesNotContain(GameConfigText.Properties + " *", tui.Screen);

    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(CommonText.StopServerFirst);
    tui.Snapshot("gameconfig-running-allowlist");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Remove);
    tui.Snapshot("gameconfig-running-player-menu");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GameConfigText.Remove);
  }

  #endregion
}
