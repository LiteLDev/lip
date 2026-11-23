using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;

namespace Lip.Cli.Tests.Tui;

/// The Tools page of the interface, driven by the keys of a fake terminal.
public sealed class ToolsPageTests {
  /// A tooth.json of format 3 whose variant names no platform, so that lip pkg migrate asks.
  private const string Tooth = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "github.com/test/tool",
        "version": "1.0.0",
        "variants": [
          { "assets": [ { "type": "self", "placements": [
            { "type": "file", "src": "tool.exe", "dest": "tool.exe" } ] } ] }
        ]
      }
      """;

  /// A directory that is not the current directory of the file system of the harness.
  private static readonly string _package =
      OperatingSystem.IsWindows() ? @"C:\packages\tool" : "/packages/tool";

  private static string InRoot(string name) => Path.Join(TuiHarness.DefaultRoot, name);

  [Theory]
  [InlineData(Language.En, "Develop Tools")]
  [InlineData(Language.ZhHans, "开发工具")]
  [InlineData(Language.ZhHant, "開發工具")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Tools);

    Assert.Equal(title, tui.Text(() => ToolsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, "Create lip.toml", "Found", "None")]
  [InlineData(Language.ZhHans, "创建 lip.toml", "已存在", "不存在")]
  [InlineData(Language.ZhHant, "建立 lip.toml", "已存在", "不存在")]
  public async Task Page_ListsTheActions_TheSelectedOne_AndTheFilesOfTheWorkspace(
      Language language, string init, string found, string none) {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("tooth.json"), new MockFileData(Tooth));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, FileSystem = fs });

    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(found);

    string screen = tui.Screen;
    Assert.Contains(init, screen);
    Assert.Contains(ToolsText.Migrate, screen);
    Assert.Contains(ToolsText.InitAbout, screen);
    Assert.Contains(TuiHarness.DefaultRoot, screen);
    Assert.Matches($@"lip\.toml\s+{none}", screen);
    Assert.Matches($@"tooth\.json\s+{found}", screen);
    Assert.Contains($"↑↓ {CommonText.HintMove.Text}", screen);
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", screen);
    await Frames(tui, "tools");

    // ↓ selects the next action, whose sentence the details area then shows.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.MigrateAbout);
    Assert.DoesNotContain(ToolsText.InitAbout, tui.Screen);

    // lip pkg init and lip pkg migrate are the only actions: ↓ from the second wraps to the first.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.InitAbout);
  }

  [Fact]
  public async Task Letters_DoNothing() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);
    string before = tui.Screen;

    tui.Type("cmpvi");
    await tui.Settle();

    Assert.Equal(before, tui.Screen);
  }

  [Fact]
  public async Task Init_AsksForThePackageIdInTheInterface_AndWritesLipToml() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
    Assert.Contains("the package id of the package", tui.Screen);
    await Frames(tui, "tools-init-question");
    tui.Type("github.com/owner/repo");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ToolsText.Created("lip.toml"));
    await Frames(tui, "tools-init-result");
    Assert.Contains("github.com/owner/repo", tui.Fs.File.ReadAllText(InRoot("lip.toml")));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ToolsText.Created("lip.toml"));
    Assert.Matches($@"lip\.toml\s+{ToolsText.Found}", tui.Screen);
  }

  [Fact]
  public async Task Init_AnswerThatIsNoPackageId_IsAskedForAgain() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
    tui.Type("no id");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Invalid answer");
    await tui.WaitFor(CommonText.Question);
    tui.Type("github.com/owner/repo");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ToolsText.Created("lip.toml"));
    Assert.True(tui.Fs.File.Exists(InRoot("lip.toml")));
  }

  [Fact]
  public async Task Init_QuestionNotAnswered_WritesNothing() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = Language.ZhHant });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
    tui.Press(ConsoleKey.Escape);

    await tui.WaitFor(CommonText.Cancelled);
    Assert.False(tui.Fs.File.Exists(InRoot("lip.toml")));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.Cancelled);
    Assert.Contains(ToolsText.InitAbout, tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, "lip.toml already exists here.")]
  [InlineData(Language.ZhHans, "这里已有 lip.toml。")]
  [InlineData(Language.ZhHant, "這裡已有 lip.toml。")]
  public async Task Init_LipTomlExists_IsDisabledWithTheReason_UntilTheFileIsGone(
      Language language, string reason) {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData("mine"));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    // The action is disabled: the reason is below its sentence, and Enter does nothing.
    await tui.WaitFor(reason);
    Assert.Equal(reason, ToolsText.Exists("lip.toml"));
    Assert.Contains(ToolsText.InitAbout, tui.Screen);
    Assert.DoesNotContain($"Enter {CommonText.HintSelect.Text}", tui.Screen);
    await Frames(tui, "tools-init-disabled");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.DoesNotContain(CommonText.Question, tui.Screen);
    Assert.Equal("mine", fs.File.ReadAllText(InRoot("lip.toml")));

    // The file is deleted outside the interface: the action can be chosen again.
    fs.File.Delete(InRoot("lip.toml"));
    await tui.WaitUntilGone(reason);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
  }

  [Fact]
  public async Task Migrate_AsksInTheInterface_AndReplacesToothJson() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("tooth.json"), new MockFileData(Tooth));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = Language.ZhHans, FileSystem = fs });
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
    tui.Type("win-x64");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ToolsText.Migrated);
    await Frames(tui, "tools-migrate-result");
    Assert.Contains("github.com/test/tool", fs.File.ReadAllText(InRoot("lip.toml")));
    Assert.False(fs.File.Exists(InRoot("tooth.json")));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ToolsText.Migrated);
    Assert.Matches($@"lip\.toml\s+{ToolsText.Found}", tui.Screen);
    Assert.Matches($@"tooth\.json\s+{ToolsText.Missing}", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, "There is no tooth.json here.")]
  [InlineData(Language.ZhHans, "这里没有 tooth.json。")]
  [InlineData(Language.ZhHant, "這裡沒有 tooth.json。")]
  public async Task Migrate_WithoutToothJson_IsDisabledWithTheReason_UntilTheFileIsThere(
      Language language, string reason) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);
    Assert.DoesNotContain(reason, tui.Screen);

    // The action is disabled: the reason is below its sentence, and Enter does nothing.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(reason);
    Assert.Equal(reason, ToolsText.NoFile("tooth.json"));
    Assert.Contains(ToolsText.MigrateAbout, tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains(reason, tui.Screen);
    Assert.False(tui.Fs.File.Exists(InRoot("lip.toml")));

    // The file is written outside the interface: the action can be chosen.
    tui.Fs.File.WriteAllText(InRoot("tooth.json"), Tooth);
    await tui.WaitUntilGone(reason);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
  }

  [Fact]
  public async Task Migrate_LipTomlExists_IsDisabledWithTheReason() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData("mine"));
    fs.AddFile(InRoot("tooth.json"), new MockFileData(Tooth));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.MigrateAbout);
    await tui.WaitFor(ToolsText.Exists("lip.toml"));
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();

    Assert.DoesNotContain(CommonText.Question, tui.Screen);
    Assert.Equal("mine", fs.File.ReadAllText(InRoot("lip.toml")));
    Assert.True(fs.File.Exists(InRoot("tooth.json")));
  }

  [Fact]
  public async Task AnotherWorkspace_ShowsItsFiles() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData("format = 4"));
    fs.AddDirectory(_package);
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    await tui.Invoke(() => tui.Context.AddWorkspace(_package, out _));

    await tui.WaitUntilGone(ToolsText.Found);
    await tui.WaitFor(ToolsText.Missing);
    Assert.Contains(_package, tui.Screen);
  }

  /// Writes the frame at 120×36 and at 60×16 for visual review, when LIP_TUI_DUMP is set.
  private static async Task Frames(TuiHarness tui, string name) {
    if (Environment.GetEnvironmentVariable("LIP_TUI_DUMP") is not { Length: > 0 }) {
      return;
    }

    await tui.Settle();
    tui.Snapshot(name);
    tui.Resize(60, 16);
    await tui.WaitFor(_ => tui.Terminal.Width == 60 && tui.App.ScreenText.Split('\n').Length == 16);
    await tui.Settle();
    tui.Snapshot(name);
    tui.Resize(120, 36);
    await tui.WaitFor(_ => tui.App.ScreenText.Split('\n').Length == 36);
    await tui.Settle();
  }
}
