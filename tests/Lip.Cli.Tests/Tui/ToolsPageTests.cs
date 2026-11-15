using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
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
  [InlineData(Language.En, "Tools")]
  [InlineData(Language.ZhHans, "工具")]
  [InlineData(Language.ZhHant, "工具")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Tools);

    Assert.Equal(title, tui.Text(() => ToolsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, "Create lip.toml", "Pack ZIP…", "Found", "None")]
  [InlineData(Language.ZhHans, "创建 lip.toml", "打包 ZIP…", "已存在", "不存在")]
  [InlineData(Language.ZhHant, "建立 lip.toml", "封裝 ZIP…", "已存在", "不存在")]
  public async Task Page_ListsTheActions_TheSelectedOne_AndTheFilesOfTheWorkspace(
      Language language, string init, string pack, string found, string none) {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("tooth.json"), new MockFileData(Tooth));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, FileSystem = fs });

    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(found);

    string screen = tui.Screen;
    Assert.Contains(init, screen);
    Assert.Contains(ToolsText.Migrate, screen);
    Assert.Contains(pack, screen);
    Assert.Contains(ToolsText.View, screen);
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
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.PackAbout);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.ViewAbout);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.InitAbout);

    // The dialogs of the two actions that ask first.
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Output);
    await Frames(tui, "tools-pack");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ToolsText.Output);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/owner/repo@1.0.0");
    await Frames(tui, "tools-view");
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

  [Fact]
  public async Task Init_LipTomlExists_ShowsTheFailure_AndKeepsTheFile() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData("mine"));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("lip.toml already exists");
    await Frames(tui, "tools-init-failure");
    Assert.Equal("mine", fs.File.ReadAllText(InRoot("lip.toml")));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("lip.toml already exists");
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

  [Fact]
  public async Task Migrate_WithoutToothJson_ShowsTheFailure() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("There is no tooth.json");
    Assert.False(tui.Fs.File.Exists(InRoot("lip.toml")));
  }

  [Fact]
  public async Task Pack_ResolvesBothPathsAgainstTheSelectedWorkspace() {
    // The current directory of the file system is not the selected workspace, as in the
    // daemon, whose working directory is not that of the client.
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(Path.Join(_package, "mod", "lip.toml"), new MockFileData("format = 4"));
    fs.AddFile(Path.Join(_package, "mod", "bin", "tool.exe"), new MockFileData("exe"));
    fs.AddFile(Path.Join(_package, "other.txt"), new MockFileData("other"));
    fs.AddDirectory(Path.Join(_package, "dist"));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { FileSystem = fs, WorkingDirectory = _package });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Output);
    // The directory, which starts as ".", then the archive.
    tui.Press(ConsoleKey.Backspace);
    tui.Type("mod");
    tui.Press(ConsoleKey.DownArrow);
    tui.Type(Path.Join("dist", "tool.zip"));
    tui.Press(ConsoleKey.Enter);

    string archive = Path.Join(_package, "dist", "tool.zip");
    await tui.WaitFor(tui.Text(() => ToolsText.Packed(archive)));
    await Frames(tui, "tools-pack-result");
    Assert.Equal(["bin/tool.exe", "lip.toml"], Entries(fs, archive));
    Assert.False(fs.File.Exists(InRoot(Path.Join("dist", "tool.zip"))));
  }

  [Fact]
  public async Task Pack_DefaultDirectory_IsTheSelectedWorkspace() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(InRoot("stray.txt"), new MockFileData("not packed"));
    fs.AddFile(Path.Join(_package, "lip.toml"), new MockFileData("format = 4"));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = Language.ZhHans,
      FileSystem = fs,
      WorkingDirectory = _package,
    });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Output);
    tui.Press(ConsoleKey.Tab);
    tui.Type("package.zip");
    tui.Press(ConsoleKey.Enter);

    string archive = Path.Join(_package, "package.zip");
    await tui.WaitFor(tui.Text(() => ToolsText.Packed(archive)));
    Assert.Equal(["lip.toml"], Entries(fs, archive));
    Assert.False(fs.File.Exists(InRoot("package.zip")));
  }

  [Fact]
  public async Task Pack_WithoutAnOutputFile_KeepsAsking() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = Language.ZhHant });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Output);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ToolsText.OutputRequired);
    await Frames(tui, "tools-pack-required");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ToolsText.Output);
    Assert.Empty(tui.Fs.Directory.GetFiles(TuiHarness.DefaultRoot));
  }

  [Fact]
  public async Task Pack_DirectoryThatIsMissing_ShowsTheFailure() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Output);
    tui.Press(ConsoleKey.Backspace);
    tui.Type("missing");
    tui.Press(ConsoleKey.Tab);
    tui.Type("package.zip");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("there is no such directory");
    Assert.Contains(InRoot("missing"), tui.Screen);
    Assert.False(tui.Fs.File.Exists(InRoot("package.zip")));
  }

  [Fact]
  public async Task View_ShowsThePackageFileAsJson() {
    MockFileSystem fs = new();
    fs.AddFile(
        InRoot(Path.Join("mod", "lip.toml")),
        new MockFileData("""
            format = 4
            id = "github.com/test/tool"
            version = "1.2.3"

            [[variants.""]]
            side = "server"
            platform = "win-x64"
            """));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ToolsText.ViewAbout);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Package);
    tui.Type("./mod");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("\"id\": \"github.com/test/tool\"");
    await Frames(tui, "tools-view-result");
    string screen = tui.Screen;
    Assert.Contains("─ ./mod ─", screen);
    Assert.Contains("  \"manifest\": {", screen);
    Assert.Contains("  \"versions\": []", screen);
    Assert.Contains($"Esc {CommonText.HintClose.Text}", screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("\"manifest\"");
    Assert.Contains(ToolsText.ViewAbout, tui.Screen);
  }

  [Fact]
  public async Task View_PackageThatIsNotThere_ShowsTheFailure() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = Language.ZhHans });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Package);
    // Without a package the dialog stays.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.PackageRequired);
    tui.Type("./missing.zip");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(CommonText.Failed);
    Assert.Contains("missing.zip", tui.Screen);
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

  /// The names of the entries of a ZIP archive, in ordinal order.
  private static string[] Entries(MockFileSystem fs, string archive) {
    using Stream stream = fs.File.OpenRead(archive);
    using ZipArchive zip = new(stream, ZipArchiveMode.Read);
    return [.. zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal)];
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
