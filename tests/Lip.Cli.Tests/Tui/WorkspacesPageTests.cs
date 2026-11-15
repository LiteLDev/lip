using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
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

  /// The file of LeviLamina in the catalogue: its newest version is for the game 1.21.50, and
  /// none is for 1.21.40.
  private const string Loader = """
      { "id": "github.com/LiteLDev/LeviLamina", "versions": [ {
        "version": "1.1.0",
        "variants": { "": [ {
          "side": "server", "platform": "win-x64",
          "deps": {"github.com/LiteLDev/bds": "1.21.50"}
        } ] }
      } ] }
      """;

  /// The pages of lip, with a Workspaces page that takes lip to run on Linux on x64, or not,
  /// whatever system the tests run on.
  internal static IReadOnlyList<PageEntry> Pages(bool linux) => [
    .. PageRegistry.All.Select(entry => entry.Id == PageId.Workspaces
        ? entry with { Create = context => new WorkspacesPage(context, () => linux) }
        : entry),
  ];

  /// The interface on a system that is not Linux on x64 unless linux is set, with a catalogue
  /// that tells which games have a LeviLamina unless another is given, and the fake server
  /// setup, or what wrap makes of it.
  private static TuiHarness Start(
      out Func<FakeServerSetup> setup,
      MockFileSystem? fs = null,
      Language language = Language.En,
      int width = 120,
      int height = 36,
      bool linux = false,
      Func<string, string>? catalog = null,
      Func<FakeServerSetup, IServerSetup>? wrap = null) {
    FakeServerSetup? fake = null;
    TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      FileSystem = fs ?? Files(),
      Pages = Pages(linux),
      Catalog = catalog ?? (_ => Loader),
      ServerSetup = harness => {
        fake = new FakeServerSetup(harness);
        return wrap?.Invoke(fake) ?? fake;
      },
    });
    setup = () => fake!;
    return tui;
  }

  /// Waits until the folder browser has read the folder that it shows.
  private static async Task Browser(TuiHarness tui) {
    await tui.WaitFor($"[ {CommonText.Cancel} ]");
    await tui.WaitUntilGone(PathPickerText.Reading);
  }

  /// What the path bar of the folder browser shows for a folder.
  private static string Bar(string path) => $"{PathPickerText.Folder}  {path}";

  /// In the folder browser: chooses the folder of a name in the folder shown, with the button
  /// for a new folder.
  private static async Task NewFolder(TuiHarness tui, string name) {
    await Browser(tui);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {PathPickerText.NewFolderTitle} ─");
    tui.Type(name);
    tui.Press(ConsoleKey.Enter);
  }

  /// In the folder browser, which shows the folder at shown: goes to a path typed in its bar.
  private static async Task GoToPath(TuiHarness tui, string shown, string path) {
    await Browser(tui);
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor($"Enter {PathPickerText.HintGo.Text}");
    tui.Press(ConsoleKey.Backspace, shown.Length);
    tui.Type(path);
    tui.Press(ConsoleKey.Enter);
  }

  /// In the folder browser, once it shows the folder at a path: chooses that folder.
  private static async Task SelectShown(TuiHarness tui, string path) {
    await tui.WaitFor(Bar(path));
    await tui.WaitUntilGone(PathPickerText.Reading);
    tui.Press(ConsoleKey.Tab);
    await tui.Settle();
    tui.Press(ConsoleKey.Enter);
  }

  /// The sessions that the fake setup was given, in order.
  private static List<OperationSession?> Sessions(FakeServerSetup setup) {
    lock (setup.Calls) {
      return [.. setup.Sessions];
    }
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
  public async Task Tab_GoesRoundTheActionsTheListAndTheSidebar() {
    using TuiHarness tui = Start(out _, Files(false, Alpha));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    Assert.Contains(" ↑↓ Move  Enter Select  Tab Next  Esc Back", tui.Screen);

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" ↑↓ Move  Enter Actions  Tab Next  Esc Back");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");

    // From the sidebar Tab starts at the actions, and Shift+Tab at the list.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Select  Tab Next");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Actions  Tab Next");
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

      // A workspace is not set up while its server runs: the action is there, says why, and
      // does nothing.
      ToRow(tui, 0);
      await tui.WaitFor("The selected workspace, with its server running.");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("Change game or loader version…");
      await tui.WaitFor("Stop the server first.");
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
  public async Task AddFolder_BrowsesToADirectoryThatExists_AndSelectsIt() {
    using TuiHarness tui = Start(out _);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);

    // The folder browser starts at the selected workspace.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    Assert.Contains("─ Add folder ─", tui.Screen);
    Assert.Contains(Bar(Root), tui.Screen);
    Assert.Contains("[ Select this folder ] [ New folder… ] [ Cancel ]", tui.Screen);
    tui.Snapshot("workspaces-add");

    // What is typed in the bar and is no folder is said, and the browser stays.
    await GoToPath(tui, Root, Server("nowhere"));
    await tui.WaitFor("Not found");
    tui.Snapshot("workspaces-add-error");
    Assert.Equal([Root], tui.Workspaces.Read());

    tui.Press(ConsoleKey.Backspace, Server("nowhere").Length);
    tui.Type(Alpha);
    tui.Press(ConsoleKey.Enter);
    await SelectShown(tui, Alpha);

    await tui.WaitFor("● " + Alpha);
    Assert.DoesNotContain("─ Add folder ─", tui.Screen);
    Assert.Equal([Root, Alpha], tui.Workspaces.Read());
    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
    // The row of the folder has the focus.
    await tui.WaitFor("Enter Actions");
  }

  [Fact]
  public async Task AddFolder_OpensAFolderWithTheArrowKeys_InTraditionalChinese() {
    MockFileSystem fs = Files();
    fs.AddDirectory(Path.Join(Root, "inner"));
    using TuiHarness tui = Start(out _, fs, Language.ZhHant);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    Assert.Contains("─ 加入資料夾 ─", tui.Screen);
    Assert.Contains("[ 選擇此資料夾 ] [ 新增資料夾… ] [ 取消 ]", tui.Screen);
    await tui.WaitFor("inner" + Path.DirectorySeparatorChar);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await SelectShown(tui, Path.Join(Root, "inner"));

    await tui.WaitFor("● " + Path.Join(Root, "inner"));
    Assert.Equal([Root, Path.Join(Root, "inner")], tui.Workspaces.Read());
  }

  [Fact]
  public async Task AddFolder_TheSelectedWorkspace_ChangesNothing_AndEscAddsNothing() {
    using TuiHarness tui = Start(out _);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Add folder ─");

    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    await SelectShown(tui, Root);

    await tui.WaitUntilGone("─ Add folder ─");
    await tui.WaitFor("The selected workspace.");
    Assert.Equal([Root], tui.Workspaces.Read());
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task AddFolder_WhenTheListIsNotWritten_SaysWhy_AndIsBackAtTheFolder() {
    using TuiHarness tui = Start(out _);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + Root);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await GoToPath(tui, Root, Alpha);
    await tui.WaitFor(Bar(Alpha));
    await tui.WaitUntilGone(PathPickerText.Reading);
    tui.Fs.File.SetAttributes(tui.WorkspaceListPath, FileAttributes.ReadOnly);

    tui.Press(ConsoleKey.Tab);
    await tui.Settle();
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(CommonText.WorkspaceListUnwritable);
    Assert.Contains("─ Add folder ─", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(CommonText.WorkspaceListUnwritable);
    Assert.Contains(Bar(Alpha), tui.Screen);
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task NewServer_InSimplifiedChinese_CreatesTheDirectory_SelectsIt_AndSetsItUp() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, language: Language.ZhHans);
    string directory = Path.Join(Root, "survival");
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("新建服务器…");

    // The directory, from a folder browser that starts at the selected workspace.
    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    Assert.Contains("─ 新建服务器 ─", tui.Screen);
    Assert.Contains(Bar(Root), tui.Screen);
    Assert.Contains("[ 选择此文件夹 ] [ 新建文件夹… ] [ 取消 ]", tui.Screen);
    tui.Snapshot("wizard-folder");
    await NewFolder(tui, "survival");

    // The game version: the newest first, each with whether LeviLamina supports it. Lip does
    // not run on Linux here, so no build is named.
    await tui.WaitFor("─ 游戏版本 ─");
    Assert.Contains("1.21.50  LeviLamina ✓   最新", tui.Screen);
    Assert.Contains("1.21.40  无 LeviLamina", tui.Screen);
    Assert.Contains("模组需要 LeviLamina，它支持此版本。", tui.Screen);
    Assert.DoesNotContain("linux", tui.Screen);
    Assert.Contains("↑↓ 移动  Enter 选择  Esc 取消", tui.Screen);
    tui.Snapshot("wizard-game");
    Assert.False(tui.Fs.Directory.Exists(directory));
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("模组需要 LeviLamina，而此版本没有。");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);

    // The loader: the build is not asked for, since windows is the only one.
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

    // The game versions, the loaders, the dry run and the setup were all in one session.
    List<OperationSession?> sessions = Sessions(setup());
    Assert.Equal(4, sessions.Count);
    Assert.NotNull(sessions[0]);
    Assert.All(sessions, session => Assert.Same(sessions[0], session));

    // The row of the new workspace is selected, and has the focus: Enter opens its menu.
    await tui.WaitFor("当前工作区。");
    await tui.WaitFor($"Enter {CommonText.HintActions.Text}");
    tui.Snapshot("wizard-done");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("更改游戏或加载器版本…");
    Assert.Contains("─ survival ─", tui.Screen);
    Assert.DoesNotContain("从列表移除", tui.Screen);
  }

  [Fact]
  public async Task NewServer_OnLinux_OffersTheLinuxBuild_WhichHasNoLoader() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, linux: true);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);

    // A folder that exists is opened, and chosen with its button.
    await GoToPath(tui, Root, Beta);
    await SelectShown(tui, Beta);
    await tui.WaitFor("─ Game version ─");
    // A game version says its build only when it lacks one that others have.
    Assert.Contains("1.21.50  LeviLamina ✓   newest", tui.Screen);
    Assert.Contains("1.21.40  No LeviLamina  windows only", tui.Screen);
    tui.Snapshot("wizard-game-linux");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    Assert.Contains("Supports LeviLamina and its mods", tui.Screen);
    tui.Snapshot("wizard-build");

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
  public async Task NewServer_OnLinux_AGameWithOneBuild_IsNotAskedForTheBuild() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, linux: true);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "old");
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(WorkspacesText.HasNoLoaderDetail);
    tui.Press(ConsoleKey.Enter);

    // The game 1.21.40 has the build windows only.
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("A plain server, without mods.");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(["1.21.40 Windows False  dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task NewServer_NotOnLinux_ListsNoGameThatHasOnlyTheLinuxBuild() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, wrap: fake => new LinuxOnlyGame(fake));
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "survival");

    await tui.WaitFor("─ Game version ─");
    Assert.DoesNotContain("1.21.60", tui.Screen);
    Assert.DoesNotContain("linux", tui.Screen);
    // The first version that is listed is the newest one that can be set up here.
    Assert.Contains("1.21.50  LeviLamina ✓   newest", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(["1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task NewServer_OnLinux_AGameThatHasOnlyTheLinuxBuild_IsSetUpWithoutMoreChoices() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, linux: true, wrap: fake => new LinuxOnlyGame(fake));
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "survival");

    await tui.WaitFor("─ Game version ─");
    Assert.Contains("1.21.60  No LeviLamina  linux only  newest", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(0, setup().LoaderLists);
    Assert.Equal(["1.21.60 Linux False  dryRun=True acceptEula=False"], Calls(setup()));
  }

  /// A server setup whose newest game has the build linux only.
  private sealed class LinuxOnlyGame(FakeServerSetup setup) : IServerSetup {
    public async Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) => [
          new GameVersion(SemVersion.Parse("1.21.60"), Windows: false, Linux: true),
          .. await setup.GameVersions(client, session),
        ];

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        setup.LoaderVersions(client, game, cancellationToken, session);

    public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
        setup.Setup(client, request, run);
  }

  [Fact]
  public async Task NewServer_WhenTheCatalogueCannotBeRead_ListsTheGamesWithoutTheColumn() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup,
        catalog: _ => throw new InvalidOperationException("The catalogue is down."));
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "survival");

    await tui.WaitFor("─ Game version ─");
    Assert.Contains("1.21.50  newest", tui.Screen);
    Assert.DoesNotContain("LeviLamina", tui.Screen);
    tui.Snapshot("wizard-game-no-catalogue");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Single(Calls(setup()));
  }

  [Fact]
  public async Task NewServer_WhenTheCatalogueIsSlow_ListsTheGamesWithoutWaitingForIt() {
    using ManualResetEventSlim answered = new();
    try {
      using TuiHarness tui = Start(out Func<FakeServerSetup> setup, catalog: _ => {
        answered.Wait(TestContext.Current.CancellationToken);
        return Loader;
      });
      await tui.GoTo(PageId.Workspaces);
      tui.Press(ConsoleKey.Enter);
      await NewFolder(tui, "survival");

      // A few seconds after the game versions are read, they are listed as they are.
      await tui.WaitFor("─ Game version ─");
      Assert.Contains("1.21.50  newest", tui.Screen);
      Assert.DoesNotContain("LeviLamina", tui.Screen);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("─ Loader ─");

      // Once the catalogue has answered, the list has its column.
      answered.Set();
      tui.Press(ConsoleKey.Escape);
      await tui.WaitFor("─ Game version ─");
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone("─ Game version ─");
      await NewFolder(tui, "survival");
      await tui.WaitFor("─ Game version ─");
      await tui.WaitFor("1.21.50  LeviLamina ✓   newest");
      Assert.Empty(Calls(setup()));
    }
    finally {
      answered.Set();
    }
  }

  [Fact]
  public async Task NewServer_ReadsTheCatalogueOnce_AndEverythingInOneSession() {
    List<string> urls = [];
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, catalog: url => {
      lock (urls) {
        urls.Add(url);
      }

      return Loader;
    });
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "first");
    await tui.WaitFor("─ Game version ─");

    // Back to the folder, and on with another one: the game versions are asked for again, in
    // the same session, and the catalogue is not.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Game version ─");
    await NewFolder(tui, "second");
    await tui.WaitFor("─ Game version ─");
    Assert.Contains("LeviLamina ✓", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await ApplyAndAccept(tui);

    lock (urls) {
      Assert.Single(urls, ModCatalogClient.DetailUrl(GameSetup.LoaderRepository));
    }

    List<OperationSession?> sessions = Sessions(setup());
    Assert.Equal(5, sessions.Count);
    Assert.NotNull(sessions[0]);
    Assert.All(sessions, session => Assert.Same(sessions[0], session));
    Assert.Equal(Path.Join(Root, "second"), await tui.Invoke(() => tui.Context.Workspace));
    Assert.False(tui.Fs.Directory.Exists(Path.Join(Root, "first")));
  }

  [Fact]
  public async Task NewServer_EscGoesBackAStep_AndOutOfTheFirstChangesNothing() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "survival");
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(WorkspacesText.HasNoLoaderDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Back to the game, whose row is still the chosen one, and to the folder browser.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    Assert.Contains(WorkspacesText.HasNoLoaderDetail, tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ New server ─");
    await Browser(tui);
    Assert.Contains(Bar(Root), tui.Screen);
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
    await NewFolder(tui, "survival");
    await tui.WaitFor("─ Game version ─");
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

    // The name of a new folder that is that of a file: said, and the browser is back.
    await NewFolder(tui, "notes.txt");
    await tui.WaitFor("That is a file");
    Assert.Contains("─ New server ─", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("That is a file");

    // The path of a file typed in the bar: the browser says so itself.
    await GoToPath(tui, Root, Path.Join(Root, "notes.txt"));
    await tui.WaitFor("Not a folder");
    Assert.Contains("─ New server ─", tui.Screen);
    Assert.Empty(Calls(setup()));
    Assert.Empty(Sessions(setup()));
    Assert.Equal([Root], tui.Workspaces.Read());
  }

  [Fact]
  public async Task NewServer_WhenTheLoadersCannotBeRead_ShowsWhy_AndIsBackAtTheGame() {
    using TuiHarness tui = Start(out _, wrap: fake => new NoLoaders(fake));
    await tui.GoTo(PageId.Workspaces);
    tui.Press(ConsoleKey.Enter);
    await NewFolder(tui, "survival");
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Finding loader versions · Failed");
    Assert.Contains("GitHub is not reached.", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("Finding loader versions · Failed");
    Assert.Contains("─ Game version ─", tui.Screen);
    Assert.False(tui.Fs.Directory.Exists(Path.Join(Root, "survival")));
    Assert.Equal([Root], tui.Workspaces.Read());
  }

  /// A server setup that cannot read the versions of the loader.
  private sealed class NoLoaders(FakeServerSetup setup) : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        Task.FromException<IReadOnlyList<SemVersion>>(
            new InvalidOperationException("GitHub is not reached."));

    public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
        setup.Setup(client, request, run);
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

    await Browser(tui);
    Assert.Contains(Bar(Root), tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Another folder…");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Another folder…");
  }

  [Fact]
  public async Task NewServer_InAFolderWhoseServerRuns_IsRefused_AndChangesNothing() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await tui.GoTo(PageId.Workspaces);
    // Another lip runs the server of a folder that the workspace list does not name.
    tui.SetServerLockHeld(true, Beta);
    tui.Press(ConsoleKey.Enter);
    await GoToPath(tui, Root, Beta);
    await SelectShown(tui, Beta);

    await tui.WaitFor("Stop the server first.");
    Assert.Contains("─ New server ─", tui.Screen);
    tui.Snapshot("wizard-server-runs");
    tui.Press(ConsoleKey.Enter);

    // The folder browser is back, at that folder.
    await tui.WaitUntilGone("Stop the server first.");
    await Browser(tui);
    Assert.Contains(Bar(Beta), tui.Screen);
    Assert.Empty(Calls(setup()));
    Assert.Empty(Sessions(setup()));
    Assert.Equal([Root], tui.Workspaces.Read());
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task NewServer_InTheSelectedWorkspaceWhoseServerRuns_IsRefused() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, Files(empty: true));
    await tui.Settle();
    await tui.SetServerRunning(true);
    await tui.Invoke(() => tui.Context.Navigate(PageId.Workspaces, PageRequest.NewServer));
    await tui.WaitFor("Another folder…");

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Stop the server first.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Stop the server first.");
    Assert.Contains("Another folder…", tui.Screen);
    Assert.Empty(Sessions(setup()));
  }

  [Fact]
  public async Task ChangeVersions_InTraditionalChinese_SelectsTheWorkspace_AndSetsItUpAgain() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(false, Alpha), Language.ZhHant);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    Assert.Contains("建立伺服器…", tui.Screen);
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
    Assert.Contains("1.21.50  LeviLamina ✓   最新", tui.Screen);
    Assert.Contains("1.21.40  無 LeviLamina", tui.Screen);
    Assert.Contains("模組需要 LeviLamina，它支援此版本。", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("模組需要 LeviLamina，而此版本沒有。");
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
    List<OperationSession?> sessions = Sessions(setup());
    Assert.Equal(4, sessions.Count);
    Assert.NotNull(sessions[0]);
    Assert.All(sessions, session => Assert.Same(sessions[0], session));
  }

  [Fact]
  public async Task List_MarksAWorkspaceWhoseServerAnotherLipRuns_AndDoesNotSetItUp() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(false, Alpha), Language.ZhHans);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    Assert.DoesNotContain("服务器运行中", tui.Screen);

    await tui.SetServerRunning(true, Alpha);

    await tui.WaitFor(screen => screen.Split('\n').Any(
        l => l.Contains(Alpha) && l.Contains("服务器运行中")));
    ToRow(tui, 1);
    await tui.WaitFor("它的服务器正在运行。");
    tui.Snapshot("workspaces-running-row");

    // The action is there, says why it cannot be chosen, and does nothing.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("更改游戏或加载器版本…");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("请先停止服务器。");
    await tui.WaitFor("↑↓ 移动  Esc 关闭");
    tui.Snapshot("workspaces-running-menu");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains("更改游戏或加载器版本…", tui.Screen);
    Assert.DoesNotContain("─ 游戏版本 ─", tui.Screen);
    Assert.Empty(Sessions(setup()));

    // Once the server has stopped, the mark goes and the action works again.
    tui.Press(ConsoleKey.Escape);
    await tui.SetServerRunning(false, Alpha);
    await tui.WaitUntilGone("服务器运行中");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("更改游戏或加载器版本…");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 游戏版本 ─");
  }

  [Fact]
  public async Task ChangeVersions_WhenTheServerStartsBeforeTheSetup_IsRefused() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, Files(false, Alpha));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Another lip starts the server while the wizard is at its last choice.
    await tui.SetServerRunning(true, Alpha);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Stop the server first.");
    Assert.Contains("─ Set up server ─", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Stop the server first.");
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Empty(Calls(setup()));
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task ChangeVersions_WhenTheServerStartsUnseenByTheInterface_IsRefusedToo() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, Files(false, Alpha));
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // The server starts between two looks of the interface.
    tui.SetServerLockHeld(true, Alpha);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Stop the server first.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Stop the server first.");
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Empty(Calls(setup()));
  }

  /// A lock file with a game of a version and a variant, and LeviLamina of a version unless
  /// that is null.
  private static string State(string game, string variant, string? loader) {
    static string Package(string id, string variant, string version) => $$"""
        { "manifest": { "format": 4, "id": "{{id}}", "version": "{{version}}",
            "variants": { "{{variant}}": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "{{variant}}", "explicit": true,
          "ref": "{{id}}{{(variant.Length > 0 ? "#" + variant : "")}}@{{version}}", "files": [] }
        """;
    string[] packages = [
      Package("github.com/LiteLDev/bds", variant, game),
      .. loader is null ? (string[])[] : [Package(GameSetup.LoaderRepository, "", loader)],
    ];
    return $$"""
        { "format": 4, "side": "server", "platform": "win-x64",
          "packages": [ {{string.Join(", ", packages)}} ] }
        """;
  }

  [Theory]
  // The game, its build and its loader are the ones selected in each step.
  [InlineData("1.21.40", "", "1.0.0", false, true, "1.21.40 Windows True 1.0.0")]
  // A game without a loader starts on "No loader".
  [InlineData("1.21.50", "", null, true, true, "1.21.50 Windows False ")]
  // The Linux build, which has no loader to choose.
  [InlineData("1.21.50", "linux", null, true, false, "1.21.50 Linux False ")]
  // A game version that has no Linux build: the build windows, which is not asked for, with
  // the newest loader, since the Linux build could have none.
  [InlineData("1.21.40", "linux", null, false, true, "1.21.40 Windows True 1.1.0")]
  // A game version that the builds list does not name any more: the newest one.
  [InlineData("1.20.0", "", "0.9.0", true, true, "1.21.50 Windows True 1.1.0")]
  public async Task ChangeVersions_OnLinux_StartsOnWhatTheWorkspaceHas(
      string game, string variant, string? loader, bool build, bool loaders, string request) {
    MockFileSystem fs = Files(false, Alpha);
    fs.AddFile(
        Path.Join(Alpha, ".lip", "state.json"), new MockFileData(State(game, variant, loader)));
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, fs, linux: true);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // Act: Enter in each step takes what it starts on.
    await tui.WaitFor("─ Game version ─");
    if (game != "1.20.0") {
      Assert.Matches($@"{Regex.Escape(game)}\s.*{WorkspacesText.Installed}", tui.Screen);
    }

    tui.Snapshot("change-versions-game");
    tui.Press(ConsoleKey.Enter);
    if (build) {
      await tui.WaitFor("─ Build ─");
      tui.Press(ConsoleKey.Enter);
    }

    if (loaders) {
      await tui.WaitFor("─ Loader ─");
      tui.Snapshot("change-versions-loader");
      tui.Press(ConsoleKey.Enter);
    }

    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Assert.Equal([$"{request} dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task ChangeVersions_NotOnLinux_OfAWorkspaceOfTheLinuxBuild_OffersWindowsOnly() {
    MockFileSystem fs = Files(false, Alpha);
    fs.AddFile(
        Path.Join(Alpha, ".lip", "state.json"), new MockFileData(State("1.21.50", "linux", null)));
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, fs);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("─ Game version ─");
    Assert.DoesNotContain("linux", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Assert.Equal(["1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ChangeVersions_MarksWhatIsInstalled_InEveryLanguageAndSize(
      Language language, int width, int height) {
    MockFileSystem fs = Files(false, Alpha);
    fs.AddFile(
        Path.Join(Alpha, ".lip", "state.json"), new MockFileData(State("1.21.40", "", "1.0.0")));
    using TuiHarness tui = Start(out _, fs, language, width, height);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(tui.Fs.Path.GetFileName(Alpha));
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // The column of LeviLamina is as wide in every row, whatever the language.
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    Assert.Contains($"1.21.50  LeviLamina ✓   {WorkspacesText.Newest}", tui.Screen);
    Assert.Contains(
        $"1.21.40  {WorkspacesText.HasNoLoader}  {WorkspacesText.Installed}", tui.Screen);
    Assert.Contains(WorkspacesText.HasNoLoaderDetail, tui.Screen);
    tui.Snapshot("change-game");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Loader} ─");
    tui.Snapshot("change-loader");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");

    // The workspace was selected for its setup: its row has the selection and the focus.
    await tui.WaitFor($"Enter {CommonText.HintActions.Text}");
    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
    tui.Snapshot("change-cancelled");
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ChangeVersions_OnLinux_NamesTheOnlyBuild_InEveryLanguageAndSize(
      Language language, int width, int height) {
    MockFileSystem fs = Files(false, Alpha);
    fs.AddFile(
        Path.Join(Alpha, ".lip", "state.json"), new MockFileData(State("1.21.50", "", "1.1.0")));
    using TuiHarness tui = Start(out _, fs, language, width, height, linux: true);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(tui.Fs.Path.GetFileName(Alpha));
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    Assert.Contains(
        $"1.21.50  LeviLamina ✓   {WorkspacesText.Newest}  {WorkspacesText.Installed}",
        tui.Screen);
    Assert.Contains(
        $"1.21.40  {WorkspacesText.HasNoLoader}  {WorkspacesText.OnlyBuild("windows")}",
        tui.Screen);
    tui.Snapshot("change-game-linux");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Build} ─");
    tui.Snapshot("change-build-linux");
  }

  [Fact]
  public async Task ChangeVersions_OnLinux_BackFromTheLoaders_KeepsTheBuildThatWasChosen() {
    MockFileSystem fs = Files(false, Alpha);
    fs.AddFile(
        Path.Join(Alpha, ".lip", "state.json"), new MockFileData(State("1.21.50", "linux", null)));
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, fs, linux: true);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Alpha);
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);

    // The build starts on linux, which the workspace has; the user chooses windows.
    await tui.WaitFor(WorkspacesText.LinuxDetail);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(WorkspacesText.WindowsDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Esc goes back to the build, on what the user chose there.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(WorkspacesText.WindowsDetail);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    Assert.Empty(Calls(setup()));
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
    Assert.Contains(CommonText.NewServer, tui.Screen);
    Assert.Contains(WorkspacesText.AddFolder, tui.Screen);
    Assert.Contains(WorkspacesText.NewServerDetail, tui.Screen);
    tui.Snapshot("frames-list");

    // The row of a workspace whose server another lip runs, and its menu.
    await tui.SetServerRunning(true, Alpha);
    ToRow(tui, 1);
    await tui.WaitFor(WorkspacesText.RunningDetail);
    Assert.Contains(WorkspacesText.ServerRunning, tui.Screen);
    tui.Snapshot("frames-row-running");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.StopServerFirst);
    tui.Snapshot("frames-menu-running");
    tui.Press(ConsoleKey.Escape);
    await tui.SetServerRunning(false, Alpha);
    await tui.WaitUntilGone(WorkspacesText.ServerRunning);

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
    await tui.WaitFor($"─ {WorkspacesText.AddTitle} ─");
    await Browser(tui);
    tui.Snapshot("frames-add");
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.NewServerTitle} ─");
    await Browser(tui);
    tui.Snapshot("frames-folder");
    await NewFolder(tui, "survival");
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    Assert.Contains($"1.21.50  LeviLamina ✓   {WorkspacesText.Newest}", tui.Screen);
    Assert.Contains($"1.21.40  {WorkspacesText.HasNoLoader}", tui.Screen);
    Assert.Contains(WorkspacesText.HasLoaderDetail, tui.Screen);
    tui.Snapshot("frames-game");
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
