using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Workspaces;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tests.Tui;

/// The Workspaces page of the interface, driven by the keys of a fake terminal.
public sealed class WorkspacesPageTests {
  private static readonly string Root = TuiHarness.DefaultRoot;
  private static readonly string Alpha = Server("alpha");
  private static readonly string Beta = Server("beta");
  private static readonly string Gone = Server("gone");

  private static string Server(string name) =>
      OperatingSystem.IsWindows() ? @"C:\servers\" + name : "/servers/" + name;

  /// A file system whose workspace list names the working directory and the given workspaces.
  /// The working directory has a game unless empty is set; Alpha and Beta exist, Gone does not.
  private static MockFileSystem Files(bool empty = false, params string[] listed) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), Root);
    fs.AddDirectory(Root);
    fs.AddFile(Path.Join(Alpha, "server.properties"), new MockFileData("motd=Alpha\n"));
    fs.AddDirectory(Beta);
    if (!empty) {
      fs.AddFile(Path.Join(Root, ".lip", "state.json"), new MockFileData(TuiHarness.StateWithGame));
    }

    string[] list = [Root, .. listed];
    fs.AddFile(
        Path.Join(OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName),
        new MockFileData(System.Text.Json.JsonSerializer.Serialize(list)));
    return fs;
  }

  private static TuiHarness Start(
      out Func<FakeServerSetup> setup,
      MockFileSystem? fs = null,
      Language language = Language.En,
      int width = 120,
      int height = 36) {
    FakeServerSetup? fake = null;
    TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      FileSystem = fs ?? Files(),
      ServerSetup = harness => fake = new FakeServerSetup(harness),
    });
    setup = () => fake!;
    return tui;
  }

  private static List<string> Calls(FakeServerSetup setup) {
    lock (setup.Calls) {
      return [.. setup.Calls];
    }
  }

  /// From the first action row to the row of the list at index.
  private static void ToRow(TuiHarness tui, int index) =>
      tui.Press(ConsoleKey.DownArrow, 2 + index);

  /// Applies the plan, accepts the license, and closes the result.
  private static async Task ApplyAndAccept(TuiHarness tui) {
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
  }

  [Theory]
  [InlineData(Language.En, "Workspaces")]
  [InlineData(Language.ZhHans, "工作区")]
  [InlineData(Language.ZhHant, "工作區")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Workspaces);

    Assert.Equal(title, tui.Text(() => WorkspacesText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Fact]
  public async Task List_ShowsTheActions_TheSelectedWorkspace_AndTheMissingOnes() {
    using TuiHarness tui = Start(out _, Files(false, Alpha, Gone));

    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Gone);

    string screen = tui.Screen;
    Assert.Contains("New server…", screen);
    Assert.Contains("Add existing folder…", screen);
    Assert.Contains("● " + Root, screen);
    Assert.Contains("  " + Alpha, screen);
    Assert.DoesNotContain("● " + Alpha, screen);
    Assert.Contains("missing", screen.Split('\n').Single(l => l.Contains(Gone)));
    // The details of the row that has the focus, and the keys that work on it.
    Assert.Contains("Creates a folder and installs the game in it.", screen);
    Assert.Contains("↑↓ Move  Enter Select  Tab Next  Esc Back", screen);
    tui.Snapshot("workspaces-list");

    ToRow(tui, 2);
    await tui.WaitFor("This folder no longer exists.");
    Assert.Contains("↑↓ Move  Enter Actions  Tab Next  Esc Back", tui.Screen);
    tui.Snapshot("workspaces-row");
  }

  [Fact]
  public async Task List_FollowsWhatAnotherInterfaceAdds() {
    using TuiHarness tui = Start(out _);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);

    tui.Workspaces.Add(Beta);

    await tui.WaitFor(Beta);
  }

  [Fact]
  public async Task List_ShowsThatTheServerOfAWorkspaceRuns() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
      });
      await tui.GoTo(PageId.Workspaces);
      await tui.WaitFor("● " + Root);
      Assert.DoesNotContain("server running", tui.Screen);

      await tui.Invoke(() => tui.Context.StartServer("Start server"));

      await tui.WaitFor(screen => screen.Split('\n').Any(
          l => l.Contains("● " + Root) && l.Contains("server running")));

      // A workspace is not set up while its server runs: the action is there, and does nothing.
      ToRow(tui, 0);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("Change game or loader version…");
      tui.Press(ConsoleKey.Enter);
      await tui.Settle();
      Assert.Contains("Change game or loader version…", tui.Screen);
      Assert.DoesNotContain("─ Game version ─", tui.Screen);
      tui.Press(ConsoleKey.Escape);
      tui.Servers.Find(tui.Root)!.Send("exit");
      await tui.WaitUntilGone("server running");
    }
    finally {
      try {
        Directory.Delete(root, recursive: true);
      }
      catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
        // The server still runs: the test failed before it stopped it.
      }
    }
  }

  [Fact]
  public async Task Menu_Switch_SelectsTheWorkspace() {
    using TuiHarness tui = Start(out _, Files(false, Alpha));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);

    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    Assert.Contains("─ alpha ─", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Select  Esc Close", tui.Screen);
    tui.Snapshot("workspaces-menu");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("● " + Alpha);
    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
    Assert.DoesNotContain("● " + Root, tui.Screen);
  }

  [Fact]
  public async Task Menu_OfTheSelectedWorkspace_OffersNeitherSwitchNorRemove() {
    using TuiHarness tui = Start(out _, Files(false, Alpha));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);

    ToRow(tui, 0);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Change game or loader version…");
    Assert.DoesNotContain("Remove from list", tui.Screen);
    Assert.DoesNotContain("Switch", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Change game or loader version…");
  }

  [Fact]
  public async Task Menu_OfAMissingWorkspace_OffersOnlyToRemoveIt() {
    using TuiHarness tui = Start(out _, Files(false, Gone));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Gone);

    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Remove from list");
    Assert.DoesNotContain("Switch", tui.Screen);
    Assert.DoesNotContain("Change game or loader version…", tui.Screen);
  }

  [Fact]
  public async Task Menu_Remove_TakesTheWorkspaceOffTheList_AndChangesNoFile() {
    using TuiHarness tui = Start(out _, Files(false, Alpha, Beta));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Beta);

    // Cancelling the question removes nothing.
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove it from the list? No file is deleted.");
    tui.Snapshot("workspaces-remove");
    tui.Press(ConsoleKey.Escape);
    await tui.Settle();
    Assert.Equal([Root, Alpha, Beta], tui.Workspaces.Read());

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove it from the list? No file is deleted.");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Removed from the list.");
    await tui.WaitUntilGone(Alpha);
    Assert.Equal([Root, Beta], tui.Workspaces.Read());
    Assert.Equal("motd=Alpha\n", tui.Fs.File.ReadAllText(Path.Join(Alpha, "server.properties")));
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task AddFolder_AddsADirectoryThatExists_AndSelectsIt() {
    using TuiHarness tui = Start(out _);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.FolderExample);
    Assert.Contains("─ Add folder ─", tui.Screen);

    // What is not a directory is said, and the dialog stays.
    tui.Type("nowhere");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Not a directory");
    tui.Snapshot("workspaces-add-error");
    Assert.Equal([Root], tui.Workspaces.Read());

    tui.Press(ConsoleKey.Backspace, "nowhere".Length);
    tui.Type(Alpha);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("● " + Alpha);
    Assert.DoesNotContain("─ Add folder ─", tui.Screen);
    Assert.Equal([Root, Alpha], tui.Workspaces.Read());
    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task AddFolder_TakesAPathRelativeToTheSelectedWorkspace() {
    MockFileSystem fs = Files();
    fs.AddDirectory(Path.Join(Root, "inner"));
    using TuiHarness tui = Start(out _, fs);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.FolderExample);
    tui.Type("inner");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("● " + Path.Join(Root, "inner"));
    Assert.Equal([Root, Path.Join(Root, "inner")], tui.Workspaces.Read());
  }

  [Fact]
  public async Task NewServer_InSimplifiedChinese_CreatesTheDirectory_SelectsIt_AndSetsItUp() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, language: Language.ZhHans);
    string directory = Path.Join(Root, "survival");
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("新建服务器…");

    // The directory, relative to the selected workspace.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");
    Assert.Contains("─ 新建服务器 ─", tui.Screen);
    Assert.Contains("文件夹", tui.Screen);
    tui.Snapshot("wizard-folder");
    tui.Type("survival");
    tui.Press(ConsoleKey.Enter);

    // The game version: the newest first.
    await tui.WaitFor("─ 游戏版本 ─");
    Assert.Contains("1.21.50  windows, linux  最新", tui.Screen);
    Assert.Contains("1.21.40  windows", tui.Screen);
    Assert.Contains("↑↓ 移动  Enter 选择  Esc 取消", tui.Screen);
    tui.Snapshot("wizard-game");
    Assert.False(tui.Fs.Directory.Exists(directory));
    tui.Press(ConsoleKey.Enter);

    // The build.
    await tui.WaitFor("─ 构建 ─");
    Assert.Contains("windows", tui.Screen);
    Assert.Contains("linux", tui.Screen);
    tui.Snapshot("wizard-build");
    tui.Press(ConsoleKey.Enter);

    // The loader.
    await tui.WaitFor("─ 加载器 ─");
    Assert.Contains("LeviLamina 1.1.0  最新", tui.Screen);
    Assert.Contains("LeviLamina 1.0.0", tui.Screen);
    Assert.Contains("不安装加载器", tui.Screen);
    tui.Snapshot("wizard-loader");
    tui.Press(ConsoleKey.Enter);

    // The plan, the license, the result.
    await tui.WaitFor("[ 应用 ] [ 取消 ]");
    Assert.Contains("─ 安装服务器 ─", tui.Screen);
    Assert.True(tui.Fs.Directory.Exists(directory));
    tui.Snapshot("wizard-plan");
    await ApplyAndAccept(tui);

    await tui.WaitFor("● " + directory);
    Assert.Equal(directory, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Equal([Root, directory], tui.Workspaces.Read());
    Assert.Equal(
        [
          "1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False",
          "1.21.50 Windows True 1.1.0 dryRun=False acceptEula=True",
        ],
        Calls(setup()));
    Assert.Equal(directory, tui.WorkspaceOf(await tui.Invoke(() => tui.Context.Client)));
  }

  [Fact]
  public async Task NewServer_LinuxBuild_HasNoLoader_AndIsNotAskedForOne() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");
    tui.Type(Beta);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    Assert.Contains("Supports LeviLamina and its mods", tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("No loader; for Linux on x64 only.");
    tui.Press(ConsoleKey.Enter);

    await ApplyAndAccept(tui);
    Assert.Equal(0, setup().LoaderLists);
    Assert.Equal(
        [
          "1.21.50 Linux False  dryRun=True acceptEula=False",
          "1.21.50 Linux False  dryRun=False acceptEula=True",
        ],
        Calls(setup()));
    Assert.Equal(Beta, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task NewServer_OlderGameWithoutLoader_OffersOnlyItsBuilds() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");
    tui.Type("old");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("─ Build ─");
    Assert.DoesNotContain("linux", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("A plain server, without mods.");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(["1.21.40 Windows False  dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task NewServer_EscGoesBackAStep_AndOutOfTheFirstChangesNothing() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");
    tui.Type("survival");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Back to the build, to the game, whose row is still the chosen one, and to the directory.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Build ─");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    Assert.DoesNotContain("linux", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ New server ─");
    Assert.Contains("survival", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ New server ─");
    await tui.Settle();

    Assert.False(tui.Fs.Directory.Exists(Path.Join(Root, "survival")));
    Assert.Equal([Root], tui.Workspaces.Read());
    Assert.Empty(Calls(setup()));
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task NewServer_PlanCancelled_KeepsTheDirectory_AndSetsNothingUp() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    string directory = Path.Join(Root, "survival");
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");
    tui.Type("survival");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Apply ] [ Cancel ]");

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[ Apply ] [ Cancel ]");
    await tui.Settle();

    Assert.Single(Calls(setup()));
    Assert.True(tui.Fs.Directory.Exists(directory));
    Assert.Equal(directory, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task NewServer_ADirectoryThatIsAFile_IsRefused() {
    MockFileSystem fs = Files();
    fs.AddFile(Path.Join(Root, "notes.txt"), new MockFileData("notes"));
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, fs);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Required");
    tui.Type("notes.txt");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("That is a file");
    Assert.Contains("─ New server ─", tui.Screen);
    Assert.Empty(Calls(setup()));
  }

  [Fact]
  public async Task NewServer_WhenTheLoadersCannotBeRead_ShowsWhy_AndIsBackAtTheBuild() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      FileSystem = Files(),
      ServerSetup = harness => new NoLoaders(new FakeServerSetup(harness)),
    });
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("my-server");
    tui.Type("survival");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Finding loader versions · Failed");
    Assert.Contains("GitHub is not reached.", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("Finding loader versions · Failed");
    Assert.Contains("─ Build ─", tui.Screen);
    Assert.False(tui.Fs.Directory.Exists(Path.Join(Root, "survival")));
    Assert.Equal([Root], tui.Workspaces.Read());
  }

  /// A server setup that cannot read the versions of the loader.
  private sealed class NoLoaders(FakeServerSetup setup) : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(LipClient client) =>
        setup.GameVersions(client);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client, SemVersion game, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<SemVersion>>(
            new InvalidOperationException("GitHub is not reached."));

    public Task Setup(
        LipClient client,
        SetupRequest request,
        bool dryRun,
        bool acceptEula,
        CancellationToken cancellationToken) =>
        setup.Setup(client, request, dryRun, acceptEula, cancellationToken);
  }

  [Fact]
  public async Task NewServerRequest_InAWorkspaceWithoutAGame_OffersThatWorkspace() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, Files(empty: true));
    await tui.Settle();

    // As the Home page asks for it.
    await tui.Invoke(() => tui.Context.Navigate(PageId.Workspaces, PageRequest.NewServer));

    await tui.WaitFor("Another folder…");
    Assert.Contains("─ New server ─", tui.Screen);
    Assert.Contains("This folder", tui.Screen);
    Assert.Contains(Root, tui.Screen);
    tui.Snapshot("wizard-place");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await ApplyAndAccept(tui);

    Assert.Equal(2, Calls(setup()).Count);
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Equal([Root], tui.Workspaces.Read());
  }

  [Fact]
  public async Task NewServerRequest_AnotherFolder_AsksForTheDirectory() {
    using TuiHarness tui = Start(out _, Files(empty: true));
    await tui.Settle();
    await tui.Invoke(() => tui.Context.Navigate(PageId.Workspaces, PageRequest.NewServer));
    await tui.WaitFor("Another folder…");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("A folder that does not exist is created.");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("my-server");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Another folder…");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Another folder…");
  }

  [Fact]
  public async Task ChangeVersions_InTraditionalChinese_SelectsTheWorkspace_AndSetsItUpAgain() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(false, Alpha), Language.ZhHant);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    Assert.Contains("新增伺服器…", tui.Screen);
    Assert.Contains("加入現有資料夾…", tui.Screen);

    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("從清單移除");
    Assert.Contains("切換", tui.Screen);
    tui.Snapshot("workspaces-menu");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // The choices of a new server, without its directory.
    await tui.WaitFor("─ 遊戲版本 ─");
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 組建 ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 載入器 ─");
    Assert.Contains("不安裝載入器", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ 套用 ] [ 取消 ]");
    Assert.Contains("─ 安裝伺服器 ─", tui.Screen);
    await ApplyAndAccept(tui);

    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Equal([Root, Alpha], tui.Workspaces.Read());
    Assert.Equal(
        [
          "1.21.40 Windows True 1.0.0 dryRun=True acceptEula=False",
          "1.21.40 Windows True 1.0.0 dryRun=False acceptEula=True",
        ],
        Calls(setup()));
  }

  [Fact]
  public async Task ChangeVersions_Cancelled_KeepsTheSelectedWorkspace() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, Files(false, Alpha));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Game version ─");
    await tui.Settle();

    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Empty(Calls(setup()));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Page_WorksInEveryLanguage_OnASmallTerminalToo(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(false, Alpha, Gone), language, width, height);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(WorkspacesText.Missing);
    Assert.Contains(WorkspacesText.NewServer, tui.Screen);
    Assert.Contains(WorkspacesText.AddFolder, tui.Screen);
    Assert.Contains(WorkspacesText.NewServerDetail, tui.Screen);
    tui.Snapshot("frames-list");

    ToRow(tui, 1);
    await tui.Settle();
    tui.Snapshot("frames-row");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Remove);
    Assert.Contains(WorkspacesText.Switch, tui.Screen);
    Assert.Contains(WorkspacesText.ChangeVersions, tui.Screen);
    tui.Snapshot("frames-menu");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.RemoveQuestion);
    tui.Snapshot("frames-remove");
    tui.Press(ConsoleKey.Escape);

    // Back up to the actions, and through the new server.
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.FolderExample);
    tui.Snapshot("frames-add");
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.NewFolderExample);
    tui.Snapshot("frames-folder");
    tui.Type("survival");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    tui.Snapshot("frames-game");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.WindowsDetail);
    tui.Snapshot("frames-build");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.LoaderDetail);
    tui.Snapshot("frames-loader");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Snapshot("frames-plan");
    await ApplyAndAccept(tui);
    await tui.Settle();
    tui.Snapshot("frames-done");

    Assert.Equal(2, Calls(setup()).Count);
    Assert.Equal(
        Path.Join(Root, "survival"), await tui.Invoke(() => tui.Context.Workspace));
  }
}
