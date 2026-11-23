using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;
using Semver;

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

  /// The package file of a package.
  private const string LipToml = """
      format = 4
      id = "github.com/test/tool"
      version = "1.2.3"

      [[variants.""]]
      side = "server"
      platform = "win-x64"
      """;

  private const string Alpha = "github.com/acme/Alpha";
  private const string Game = "github.com/LiteLDev/bds";
  private const string Commit = "0123456789abcdef0123456789abcdef01234567";

  private static string InRoot(string name) => Path.Join(TuiHarness.DefaultRoot, name);

  /// The title of a dialog, as its frame shows it.
  private static string Titled(string title) => $"─ {title} ─";

  /// The path bar of a path picker that shows the folder.
  private static string Bar(string folder) => $"{PathPickerText.Folder}  {folder}";

  /// The row of a folder in the list of a path picker.
  private static string Folder(string name) => name + Path.DirectorySeparatorChar;

  /// Waits for the path picker with the title, and until it has read its first folder.
  private static async Task Picker(TuiHarness tui, string title) {
    await tui.WaitFor(Titled(title));
    await tui.WaitFor($"[ {CommonText.Cancel} ]");
    await tui.WaitUntilGone(PathPickerText.Reading);
  }

  /// An installed package without files, of its version unless another source is given.
  private static string Package(
      string id, string variant, string version, bool isExplicit, string? source = null,
      string? commit = null) => $$"""
      { "manifest": { "format": 4, "id": "{{id}}", "version": "{{version}}",
          "variants": { "{{variant}}": [ { "side": "server", "platform": "win-x64" } ] } },
        "variant": "{{variant}}", "explicit": {{(isExplicit ? "true" : "false")}},
        "ref": "{{source ?? $"{id}@{version}"}}", {{(
            commit is null ? "" : $"\"commit\": \"{commit}\", ")}}"files": [] }
      """;

  /// The bytes of a ZIP archive that holds a package file.
  private static byte[] Zip() {
    using MemoryStream stream = new();
    using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true)) {
      using StreamWriter writer = new(zip.CreateEntry("lip.toml").Open());
      writer.Write(LipToml);
    }

    return stream.ToArray();
  }

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

    // What the two actions that ask first show.
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    await Frames(tui, "tools-pack-folder");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(Titled(ToolsText.PackFolder));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    Assert.Contains(ToolsText.ViewInstalled, tui.Screen);
    Assert.Contains(ToolsText.ViewTyped, tui.Screen);
    Assert.Contains(ToolsText.ViewBrowse, tui.Screen);
    await Frames(tui, "tools-view");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.ViewTypedAbout);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.ViewBrowseAbout);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ToolsText.ViewBrowseAbout);
    Assert.Contains(ToolsText.ViewAbout, tui.Screen);
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

    // An action that the files do not concern can be chosen.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ToolsText.PackAbout);
    Assert.DoesNotContain(reason, tui.Screen);
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", tui.Screen);
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(reason);

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

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task Pack_BrowsesToTheFolder_ThenToTheOutput_FromTheSelectedWorkspace(
      Language language) {
    // The current directory of the file system is not the selected workspace, as in the
    // daemon, whose working directory is not that of the client.
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(Path.Join(_package, "mod", "lip.toml"), new MockFileData("format = 4"));
    fs.AddFile(Path.Join(_package, "mod", "bin", "tool.exe"), new MockFileData("exe"));
    fs.AddFile(Path.Join(_package, "other.txt"), new MockFileData("other"));
    fs.AddDirectory(Path.Join(_package, "dist"));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      FileSystem = fs,
      WorkingDirectory = _package,
    });
    await tui.GoTo(PageId.Tools);

    // The folder: the browser starts in the workspace, which holds dist and mod.
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    await tui.WaitFor(Bar(_package));
    await tui.WaitFor(Folder("mod"));
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(Path.Join(_package, "mod")));
    await tui.WaitFor(Folder("bin"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    // The archive: the browser starts in the workspace again, with a name to start from.
    await Picker(tui, ToolsText.PackOutput);
    await tui.WaitFor(Bar(_package));
    await tui.WaitFor(Folder("dist"));
    Assert.Contains("package.zip", tui.Screen);
    await Frames(tui, "tools-pack-output");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(Path.Join(_package, "dist")));
    await tui.WaitUntilGone(Folder("mod"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Backspace, "package.zip".Length);
    tui.Type("tool");
    tui.Press(ConsoleKey.Enter);

    string archive = Path.Join(_package, "dist", "tool.zip");
    await tui.WaitFor(tui.Text(() => ToolsText.Packed(archive)));
    await Frames(tui, "tools-pack-result");
    Assert.Equal(["bin/tool.exe", "lip.toml"], Entries(fs, archive));
    Assert.False(fs.File.Exists(InRoot(Path.Join("dist", "tool.zip"))));
  }

  [Fact]
  public async Task Pack_TheWorkspaceIntoItself_LeavesTheArchiveOutOfTheArchive() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(InRoot("stray.txt"), new MockFileData("not packed"));
    fs.AddFile(Path.Join(_package, "lip.toml"), new MockFileData("format = 4"));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { FileSystem = fs, WorkingDirectory = _package });
    await tui.GoTo(PageId.Tools);

    // The folder shown first is the workspace, and so is the folder of the archive.
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackOutput);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    string archive = Path.Join(_package, "package.zip");
    await tui.WaitFor(tui.Text(() => ToolsText.Packed(archive)));
    Assert.Equal(["lip.toml"], Entries(fs, archive));
    Assert.False(fs.File.Exists(InRoot("package.zip")));
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task Pack_OutputThatExists_IsReplacedOnlyWhenTheUserSaysSo(Language language) {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData("format = 4"));
    fs.AddFile(InRoot("package.zip"), new MockFileData("old"));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, FileSystem = fs });
    await tui.GoTo(PageId.Tools);
    string question = tui.Text(() => ToolsText.ReplaceQuestion("package.zip"));

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackOutput);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    // Enter alone keeps the file, and leads back to where the archive is chosen.
    await tui.WaitFor(question);
    Assert.Contains($"[ {ToolsText.Replace} ] [ {CommonText.Cancel} ]", tui.Screen);
    await Frames(tui, "tools-pack-replace");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(question);
    await Picker(tui, ToolsText.PackOutput);
    await tui.WaitFor(Bar(TuiHarness.DefaultRoot));
    Assert.Equal("old", fs.File.ReadAllText(InRoot("package.zip")));

    // The other button replaces it; the archive that was there is not packed into the new.
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(question);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(tui.Text(() => ToolsText.Packed(InRoot("package.zip"))));
    Assert.Equal(["lip.toml"], Entries(fs, InRoot("package.zip")));
  }

  [Fact]
  public async Task Pack_FolderThatIsGoneByThen_ShowsTheFailure_AndWritesNothing() {
    MockFileSystem fs = new();
    fs.AddDirectory(InRoot("mod"));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    await tui.WaitFor(Folder("mod"));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(InRoot("mod")));
    await tui.WaitUntilGone(Folder("mod"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackOutput);
    // Another program removes the folder while the archive is chosen.
    fs.Directory.Delete(InRoot("mod"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("there is no such directory");
    Assert.Contains(InRoot("mod"), tui.Screen);
    Assert.False(fs.File.Exists(InRoot("package.zip")));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("there is no such directory");
    Assert.Contains(ToolsText.PackAbout, tui.Screen);
  }

  [Theory]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task Pack_Esc_GoesBackOneStep_AndWritesNothing(Language language) {
    MockFileSystem fs = new();
    fs.AddDirectory(InRoot("mod"));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    await tui.WaitFor(Folder("mod"));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(InRoot("mod")));
    await tui.WaitUntilGone(Folder("mod"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackOutput);

    // From the archive back to the folder, which is the one that was chosen.
    tui.Press(ConsoleKey.Escape);
    await Picker(tui, ToolsText.PackFolder);
    await tui.WaitFor(Bar(InRoot("mod")));
    Assert.DoesNotContain(Titled(ToolsText.PackOutput), tui.Screen);

    // From the folder back to the page.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(Titled(ToolsText.PackFolder));
    Assert.Contains(ToolsText.Pack, tui.Screen);
    Assert.Empty(fs.Directory.GetFiles(TuiHarness.DefaultRoot, "*", SearchOption.AllDirectories));
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task View_InstalledMod_ShowsItsInstalledVersion(Language language) {
    // A dependency, the game and two mods, one of a variant, in an order that is not that of
    // the list.
    MockFileSystem fs = new();
    fs.AddFile(
        InRoot(Path.Join(".lip", "state.json")),
        new MockFileData($$"""
            { "format": 4, "side": "server", "platform": "win-x64", "packages": [
              {{Package("github.com/acme/Lib", "", "2.0.0", false)}},
              {{Package(Game, "", "1.21.0", true)}},
              {{Package(Alpha, "cuda", "1.2.0", true)}},
              {{Package("github.com/acme/Beta", "", "0.3.1", true)}} ] }
            """));
    string source = Path.Join(_package, "alpha");
    fs.AddFile(Path.Join(source, "lip.toml"), new MockFileData(LipToml));
    // The archives of the test: the repository has a newer version than the installed one.
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    archives.Setup(a => a.ListVersions(Alpha)).ReturnsAsync([
      new SpecVersion(new SemVersion(1, 3, 0), "v1.3.0"),
      new SpecVersion(new SemVersion(1, 2, 0), "v1.2.0"),
    ]);
    archives.Setup(a => a.ResolveGitRef(Alpha, It.Is<GitRef>(r => r.Name == "1.2.0")))
        .ReturnsAsync(Commit);
    archives.Setup(a => a.OpenCommit(Alpha, Commit))
        .ReturnsAsync(new ArchiveHandle("alpha", new DirectorySource(fs, source)));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      FileSystem = fs,
      CreateClient = (interaction, hookOutput, workspace) => new LipClient(
          fs, interaction, new CacheService(fs, interaction), new ConfigService(fs, interaction),
          new McmodService(
              fs, interaction, new LockStore(fs), archives.Object, new ArchiveReader(),
              new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => workspace)),
    });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ToolsText.ViewAbout);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.Enter);

    // The mods of the user, then the dependency, then the game, each with its version.
    await tui.WaitFor(Titled(ToolsText.ViewInstalledTitle));
    await tui.WaitFor(Game);
    string screen = tui.Screen;
    int[] rows = [
      .. new[] { $"{Alpha}#cuda", "github.com/acme/Beta", "github.com/acme/Lib", Game }
          .Select(identity => screen.IndexOf(identity + "  ", StringComparison.Ordinal)),
    ];
    Assert.DoesNotContain(-1, rows);
    Assert.Equal(rows.Order(), rows);
    Assert.Matches(@"Alpha#cuda\s+1\.2\.0", screen);
    Assert.Matches(@"bds\s+1\.21\.0", screen);
    Assert.Contains($"↑↓ {CommonText.HintMove.Text}", screen);
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", screen);
    await Frames(tui, "tools-view-installed");

    // Enter shows the version that is installed, not the newest one.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("\"id\": \"github.com/test/tool\"");
    Assert.Contains(Titled($"{Alpha}#cuda@1.2.0"), tui.Screen);
    Assert.Contains("\"1.3.0\"", tui.Screen);
    await Frames(tui, "tools-view-installed-result");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("\"manifest\"");
    Assert.Contains(ToolsText.ViewAbout, tui.Screen);
  }

  /// A package is shown as it is installed: by the source that the state file records. Its
  /// identity and version name it only when it is of the version tags of a repository.
  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task View_InstalledMod_OfALocalPathABranchOrTheGame_ShowsWhatIsInstalled(
      Language language) {
    const string tool = "github.com/test/tool";
    const string longId = "github.com/some-organisation/a-long-repository-name";
    string local = Path.Join(_package, "local");
    string branch = Path.Join(_package, "branch");
    MockFileSystem fs = new();
    fs.AddFile(
        InRoot(Path.Join(".lip", "state.json")),
        new MockFileData($$"""
            { "format": 4, "side": "server", "platform": "win-x64", "packages": [
              {{Package(Game, "", "1.21.0", true)}},
              {{Package(tool, "", "1.2.3", true, Ref.FromFile(local).ToString())}},
              {{Package(Alpha, "", "1.2.0", true, $"{Alpha}@branch:main", Commit)}},
              {{Package(longId, "cuda", "10.20.30", true, $"{longId}#cuda", Commit)}} ] }
            """));
    fs.AddFile(Path.Join(local, "lip.toml"), new MockFileData(LipToml));
    fs.AddFile(
        Path.Join(branch, "lip.toml"),
        new MockFileData(LipToml.Replace(tool, Alpha, StringComparison.Ordinal)));
    // The archives of the test: the local folder, and the commit of the branch that is
    // installed. The branch itself is not asked for: it may have moved on.
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    archives.Setup(a => a.Open(It.Is<Ref>(r => r.Kind == RefKind.File && r.LocalPath == local)))
        .ReturnsAsync(new ArchiveHandle("local", new DirectorySource(fs, local)));
    archives.Setup(a => a.ListVersions(Alpha)).ReturnsAsync([]);
    archives.Setup(a => a.ResolveGitRef(Alpha, It.Is<GitRef>(r => r.ToString() == "rev:" + Commit)))
        .ReturnsAsync(Commit);
    archives.Setup(a => a.OpenCommit(Alpha, Commit))
        .ReturnsAsync(new ArchiveHandle("branch", new DirectorySource(fs, branch)));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      FileSystem = fs,
      CreateClient = (interaction, hookOutput, workspace) => new LipClient(
          fs, interaction, new CacheService(fs, interaction), new ConfigService(fs, interaction),
          new McmodService(
              fs, interaction, new LockStore(fs), archives.Object, new ArchiveReader(),
              new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => workspace,
              downloadServerBuilds: () => Task.FromResult(
                  """{ "server/win-x64": ["1.21.0.03", "1.20.0.01"] }"""))),
    });
    await tui.GoTo(PageId.Tools);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ToolsText.ViewAbout);

    async Task List() {
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(ToolsText.ViewInstalledAbout);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(Titled(ToolsText.ViewInstalledTitle));
      await tui.WaitFor(Game);
    }

    async Task Close() {
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone("\"manifest\"");
      await tui.WaitFor(ToolsText.ViewAbout);
    }

    // The local folder: the line below the list says where the package is from. The mods are
    // listed by identity: Alpha, the long one, the tool; then the game.
    await List();
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor($"{tool}@1.2.3 · {local}");
    // An identity that is longer than its column keeps its start and its end.
    Assert.Matches(@"github\.com/some-\S*…\S*-name#cuda  10\.20\.30", tui.Screen);
    await Frames(tui, "tools-view-installed-sources");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"\"id\": \"{tool}\"");
    // The title is the folder: what it holds now is shown.
    Assert.Contains(Titled(local), tui.Screen);
    await Close();

    // The branch: the commit that was installed of it.
    await List();
    await tui.WaitFor($"{Alpha}@1.2.0 · branch:main");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"\"id\": \"{Alpha}\"");
    Assert.Contains(Titled($"{Alpha}@1.2.0"), tui.Screen);
    archives.Verify(a => a.OpenCommit(Alpha, Commit), Times.Once);
    await Close();

    // The long identity: the line below the list has it in full.
    await List();
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"{longId}#cuda@10.20.30");
    await Frames(tui, "tools-view-installed-long");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ToolsText.ViewInstalledAbout);

    // The game, which is in no archive, at its version.
    await List();
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor($"{Game}@1.21.0");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"\"id\": \"{Game}\"");
    Assert.Contains("\"version\": \"1.21.0\"", tui.Screen);
    Assert.Contains(Titled($"{Game}@1.21.0"), tui.Screen);
    await Close();
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task View_InstalledMod_InAWorkspaceWithoutPackages_SaysSo(Language language) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ToolsText.NothingInstalled);
    Assert.Contains($"[ {CommonText.Close} ]", tui.Screen);
    await Frames(tui, "tools-view-installed-none");
    // Closing it leads back to the choice of what to show.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ToolsText.NothingInstalled);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ToolsText.ViewInstalledAbout);
  }

  [Fact]
  public async Task View_TypedPackage_ShowsThePackageFileAsJson() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot(Path.Join("mod", "lip.toml")), new MockFileData(LipToml));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ToolsText.ViewAbout);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/owner/repo@1.0.0");
    await Frames(tui, "tools-view-typed");
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
  public async Task View_TypedPackageThatIsNotThere_ShowsTheFailure() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = Language.ZhHans });
    await tui.GoTo(PageId.Tools);

    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/owner/repo@1.0.0");
    // Esc leads back to the choice, with the row that led here selected.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(ToolsText.ViewTypedAbout);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/owner/repo@1.0.0");
    // Without a package the dialog stays.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.PackageRequired);
    tui.Type("./missing.zip");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(CommonText.Failed);
    Assert.Contains("missing.zip", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task View_BrowsedZipOrFolder_ShowsThePackageFileAsJson(Language language) {
    MockFileSystem fs = new();
    fs.AddFile(InRoot(Path.Join("mod", "lip.toml")), new MockFileData(LipToml));
    fs.AddFile(InRoot(Path.Join("dist", "tool.zip")), new MockFileData(Zip()));
    fs.AddFile(InRoot(Path.Join("dist", "notes.txt")), new MockFileData("no package"));
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, FileSystem = fs });
    await tui.GoTo(PageId.Tools);

    // A folder: the browser starts in the workspace, which holds dist and mod.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.ViewLocal);
    await tui.WaitFor(Bar(TuiHarness.DefaultRoot));
    await tui.WaitFor(Folder("mod"));
    Assert.Contains($"[ {PathPickerText.SelectFolder} ] [ {CommonText.Cancel} ]", tui.Screen);
    await Frames(tui, "tools-view-browse");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(InRoot("mod")));
    await tui.WaitUntilGone(Folder("dist"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("\"id\": \"github.com/test/tool\"");
    Assert.Contains(Titled("mod"), tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("\"manifest\"");

    // A ZIP archive, which the list shows, unlike the other files.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.ViewLocal);
    await tui.WaitFor(Folder("dist"));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(InRoot("dist")));
    await tui.WaitFor("tool.zip");
    Assert.DoesNotContain("notes.txt", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("\"id\": \"github.com/test/tool\"");
    Assert.Contains(Titled("tool.zip"), tui.Screen);
    await Frames(tui, "tools-view-browse-result");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("\"manifest\"");

    // Esc in the browser leads back to the choice, with the row that led here selected.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.ViewLocal);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(ToolsText.ViewBrowseAbout);
    Assert.DoesNotContain(Titled(ToolsText.ViewLocal), tui.Screen);
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
