using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;

namespace Lip.Cli.Tests.Tui;

/// The workspace picker, which the workspace of the top bar opens, driven by the keys of a fake
/// terminal.
public sealed class WorkspacePickerTests {
  private static readonly string Root = TuiHarness.DefaultRoot;
  private static readonly string Alpha = Server("alpha");
  private static readonly string Beta = Server("beta");
  private static readonly string Gone = Server("gone");

  private static string Server(string name) =>
      OperatingSystem.IsWindows() ? @"C:\servers\" + name : "/servers/" + name;

  /// A file system whose workspace list names the working directory and the given workspaces.
  /// The working directory has a game; Alpha and Beta exist, Gone does not.
  private static MockFileSystem Files(params string[] listed) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), Root);
    fs.AddDirectory(Root);
    fs.AddFile(Path.Join(Alpha, "server.properties"), new MockFileData("motd=Alpha\n"));
    fs.AddDirectory(Beta);
    fs.AddFile(Path.Join(Root, ".lip", "state.json"), new MockFileData(TuiHarness.StateWithGame));
    string[] list = [Root, .. listed];
    fs.AddFile(
        Path.Join(OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName),
        new MockFileData(System.Text.Json.JsonSerializer.Serialize(list)));
    return fs;
  }

  /// The interface, with the fake server setup, which the picker never uses.
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

  /// Waits until the folder browser has read the folder that it shows.
  private static async Task Browser(TuiHarness tui) {
    await tui.WaitFor($"[ {CommonText.Cancel} ]");
    await tui.WaitUntilGone(PathPickerText.Reading);
  }

  /// What the path bar of the folder browser shows for a folder.
  private static string Bar(string path) => $"{PathPickerText.Folder}  {path}";

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

  /// In the picker, whose list has the focus on the selected workspace, the first row: to the
  /// row at index.
  private static void ToRow(TuiHarness tui, int index) =>
      tui.Press(ConsoleKey.DownArrow, index);

  /// From the list to the row that adds a folder, and Enter.
  private static async Task AddFolder(TuiHarness tui) {
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(WorkspacesText.AddFolderDetail);
    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
  }

  /// The keys of the bottom bar while the workspace of the top bar has the focus.
  private static string TopBarHints(TuiHarness tui) =>
      tui.Text(() => $"↑↓ {CommonText.HintMove.Text}  Enter {CommonText.HintOpen.Text}"
          + $"  Esc {CommonText.HintBack.Text}");

  /// The keys of the bottom bar while the sidebar has the focus.
  private static string SidebarHints(TuiHarness tui) =>
      tui.Text(() => $"↑↓ {CommonText.HintMove.Text}  Enter {CommonText.HintOpen.Text}"
          + $"  Esc {CommonText.HintQuit.Text}");

  /// Waits until the selected workspace is the one at path.
  private static Task Selected(TuiHarness tui, string path) =>
      tui.WaitUntil(
          () => WorkspaceList.PathComparer.Equals(tui.Context.Workspace, path), "the workspace");

  /// The first row of the screen: the top bar.
  private static string TopBar(TuiHarness tui) => tui.Screen.Split('\n')[0];

  [Theory]
  [InlineData(Language.En, "Workspaces")]
  [InlineData(Language.ZhHans, "工作区")]
  [InlineData(Language.ZhHant, "工作區")]
  public async Task Picker_IsOpenedFromTheWorkspaceOfTheTopBar_AndIsNoPageOfTheSidebar(
      Language language, string title) {
    using TuiHarness tui = Start(out _, language: language);
    await tui.Settle();
    Assert.DoesNotContain(PageRegistry.All, entry => entry.Id.ToString() == "Workspaces");

    // ↑ from the first page of the sidebar: the workspace of the top bar stands out, and Enter
    // opens it.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(SidebarHints(tui));
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(TopBarHints(tui));
    Assert.Equal(Theme.Selected, await tui.StyleOf(Root));
    Assert.Contains(
        $"↑↓ {CommonText.HintMove.Text}  Enter {CommonText.HintOpen.Text}"
            + $"  Esc {CommonText.HintBack.Text}",
        tui.Screen);
    tui.Snapshot("picker-top-bar");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"─ {title} ─");
    Assert.Equal(title, tui.Text(() => WorkspacesText.Title));
    await tui.WaitFor("● " + Root);
    tui.Snapshot("picker");

    // Esc closes it, and the top bar has the focus again; ↓ goes back to the first page.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {title} ─");
    await tui.WaitFor(TopBarHints(tui));
    Assert.Equal(Theme.Selected, await tui.StyleOf(Root));
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SidebarHints(tui));
    Assert.Equal(PageId.Home, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Equal(Theme.Muted, await tui.StyleOf(Root));
  }

  [Fact]
  public async Task TopBar_IsBetweenTheLastPageAndTheFirst_AndEscGoesBackToTheSidebar() {
    using TuiHarness tui = Start(out _);
    await tui.GoTo(PageId.Settings);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");

    // ↓ from the last page.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(TopBarHints(tui));
    Assert.Equal(PageId.Settings, await tui.Invoke(() => tui.Context.CurrentPage));

    // Esc goes back to the page of the sidebar, and ↑ from the top bar to the last page.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(SidebarHints(tui));
    tui.Press(ConsoleKey.UpArrow, PageRegistry.All.Count - 1);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.Home, "Home");
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(TopBarHints(tui));
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.Settings, "the last page");
  }

  [Fact]
  public async Task List_ShowsTheAction_TheSelectedWorkspace_AndTheMissingOnes() {
    using TuiHarness tui = Start(out _, Files(Alpha, Gone));

    await tui.OpenWorkspaces();
    await tui.WaitFor(Gone);

    string screen = tui.Screen;
    Assert.Contains("Add folder…", screen);
    Assert.DoesNotContain("New server", screen);
    Assert.Contains("● " + Root, screen);
    Assert.Contains("  " + Alpha, screen);
    Assert.DoesNotContain("● " + Alpha, screen);
    Assert.Contains("missing", screen.Split('\n').Single(l => l.Contains(Gone)));
    // The selected workspace has the focus, with its details and the keys that work on it.
    Assert.Contains("The selected workspace.", screen);
    Assert.Contains("↑↓ Move  Enter Actions  Tab Next  Esc Close", screen);
    tui.Snapshot("picker-list");

    ToRow(tui, 2);
    await tui.WaitFor("This folder no longer exists.");
    tui.Snapshot("picker-row");

    // Up from the first row, to the action.
    tui.Press(ConsoleKey.UpArrow, 3);
    await tui.WaitFor(WorkspacesText.AddFolderDetail);
    Assert.Contains("↑↓ Move  Enter Select  Tab Next  Esc Close", tui.Screen);
  }

  [Fact]
  public async Task Tab_GoesRoundTheActionAndTheList() {
    using TuiHarness tui = Start(out _, Files(Alpha));
    await tui.OpenWorkspaces();
    await tui.WaitFor(Alpha);
    Assert.Contains("Enter Actions  Tab Next", tui.Screen);

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Select  Tab Next");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Actions  Tab Next");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Select  Tab Next");
  }

  [Fact]
  public async Task List_FollowsWhatAnotherInterfaceAdds() {
    using TuiHarness tui = Start(out _);
    await tui.OpenWorkspaces();
    await tui.WaitFor("● " + Root);

    tui.Workspaces.Add(Beta);

    await tui.WaitFor(Beta);
  }

  [Fact]
  public async Task List_MarksAWorkspaceWhoseServerRuns() {
    using TuiHarness tui = Start(out _, Files(Alpha), Language.ZhHans);
    await tui.OpenWorkspaces();
    await tui.WaitFor(Alpha);
    Assert.DoesNotContain("服务器运行中", tui.Screen);

    await tui.SetServerRunning(true, Alpha);

    await tui.WaitFor(screen => screen.Split('\n').Any(
        l => l.Contains(Alpha) && l.Contains("服务器运行中")));
    ToRow(tui, 1);
    await tui.WaitFor("它的服务器正在运行。");
    tui.Snapshot("picker-running-row");

    // It may be selected all the same.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("切换");
    tui.Press(ConsoleKey.Escape);
    await tui.SetServerRunning(false, Alpha);
    await tui.WaitUntilGone("服务器运行中");
  }

  [Fact]
  public async Task Menu_Switch_SelectsTheWorkspace_ClosesThePicker_AndTheTopBarShowsIt() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, Files(Alpha));
    await tui.OpenWorkspaces();
    await tui.WaitFor(Alpha);
    Assert.Contains(Root, TopBar(tui));

    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    Assert.Contains("─ alpha ─", tui.Screen);
    Assert.Contains("Switch", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Select  Esc Close", tui.Screen);
    tui.Snapshot("picker-menu");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("─ Workspaces ─");
    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
    await tui.WaitFor(_ => TopBar(tui).Contains(Alpha), "the top bar");
    tui.Snapshot("picker-switched");
    // Selecting a workspace installs nothing.
    Assert.Empty(setup().Calls);

    // Opened again, the picker is on the workspace now selected.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("● " + Alpha);
    Assert.DoesNotContain("● " + Root, tui.Screen);
  }

  [Fact]
  public async Task Enter_OnTheSelectedWorkspace_ClosesThePicker() {
    using TuiHarness tui = Start(out _, Files(Alpha));
    await tui.OpenWorkspaces();
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("─ Workspaces ─");
    Assert.DoesNotContain("Remove from list", tui.Screen);
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task Menu_OfAMissingWorkspace_OffersOnlyToRemoveIt() {
    using TuiHarness tui = Start(out _, Files(Gone));
    await tui.OpenWorkspaces();
    await tui.WaitFor(Gone);

    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Remove from list");
    Assert.DoesNotContain("Switch", tui.Screen);
  }

  [Fact]
  public async Task Menu_Remove_TakesTheWorkspaceOffTheList_AndChangesNoFile() {
    using TuiHarness tui = Start(out _, Files(Alpha, Beta));
    await tui.OpenWorkspaces();
    await tui.WaitFor(Beta);

    // Cancelling the question removes nothing.
    ToRow(tui, 1);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove it from the list? No file is deleted.");
    tui.Snapshot("picker-remove");
    tui.Press(ConsoleKey.Escape);
    await tui.Settle();
    Assert.Equal([Root, Alpha, Beta], tui.Workspaces.Read());

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove from list");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Remove it from the list? No file is deleted.");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Removed from the list.");
    await tui.WaitUntilGone(Alpha);
    Assert.Contains("─ Workspaces ─", tui.Screen);
    Assert.Equal([Root, Beta], tui.Workspaces.Read());
    Assert.Equal("motd=Alpha\n", tui.Fs.File.ReadAllText(Path.Join(Alpha, "server.properties")));
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task AddFolder_BrowsesToADirectoryThatExists_AndSelectsIt() {
    using TuiHarness tui = Start(out _);
    await tui.OpenWorkspaces();
    await tui.WaitFor("● " + Root);

    // The folder browser starts at the selected workspace.
    await AddFolder(tui);
    Assert.Contains("─ Add folder ─", tui.Screen);
    Assert.Contains(Bar(Root), tui.Screen);
    Assert.Contains("[ Select this folder ] [ New folder… ] [ Cancel ]", tui.Screen);
    tui.Snapshot("picker-add");

    // What is typed in the bar and is no folder is said, and the browser stays.
    await GoToPath(tui, Root, Server("nowhere"));
    await tui.WaitFor("Not found");
    Assert.Equal([Root], tui.Workspaces.Read());

    tui.Press(ConsoleKey.Backspace, Server("nowhere").Length);
    tui.Type(Alpha);
    tui.Press(ConsoleKey.Enter);
    await SelectShown(tui, Alpha);

    // The picker closes once the folder is selected.
    await tui.WaitUntilGone("─ Add folder ─");
    await tui.WaitUntilGone("─ Workspaces ─");
    Assert.Equal([Root, Alpha], tui.Workspaces.Read());
    Assert.Equal(Alpha, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Contains(Alpha, TopBar(tui));
  }

  /// The folder of a new server: a new folder of the folder browser, which creates it, is added
  /// and selected, and nothing is installed in it.
  [Fact]
  public async Task AddFolder_ANewFolder_IsCreatedAddedAndSelected_AndNothingIsInstalled() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, language: Language.ZhHans);
    string directory = Path.Join(Root, "survival");
    await tui.OpenWorkspaces();
    await tui.WaitFor("● " + Root);
    tui.Snapshot("picker-zh");

    await AddFolder(tui);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {PathPickerText.NewFolderTitle} ─");
    tui.Type("survival");
    tui.Press(ConsoleKey.Enter);
    // The new folder is shown, with the focus on the buttons: ← to Select this folder.
    await tui.WaitFor(Bar(directory));
    await tui.WaitUntilGone(PathPickerText.Reading);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);

    await Selected(tui, directory);
    await tui.WaitUntilGone("─ 工作区 ─");
    Assert.True(tui.Fs.Directory.Exists(directory));
    Assert.Equal([Root, directory], tui.Workspaces.Read());
    Assert.Equal(directory, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Contains(directory, TopBar(tui));
    Assert.Empty(setup().Calls);
    Assert.Empty(setup().Sessions);
    tui.Snapshot("picker-added");
  }

  [Fact]
  public async Task AddFolder_OpensAFolderWithTheArrowKeys_InTraditionalChinese() {
    MockFileSystem fs = Files();
    fs.AddDirectory(Path.Join(Root, "inner"));
    using TuiHarness tui = Start(out _, fs, Language.ZhHant);
    await tui.OpenWorkspaces();
    await tui.WaitFor("● " + Root);

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("加入資料夾…");
    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    Assert.Contains("─ 加入資料夾 ─", tui.Screen);
    Assert.Contains("[ 選擇此資料夾 ] [ 新增資料夾… ] [ 取消 ]", tui.Screen);
    await tui.WaitFor("inner" + Path.DirectorySeparatorChar);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await SelectShown(tui, Path.Join(Root, "inner"));

    await Selected(tui, Path.Join(Root, "inner"));
    await tui.WaitUntilGone("─ 工作區 ─");
    Assert.Equal([Root, Path.Join(Root, "inner")], tui.Workspaces.Read());
  }

  [Fact]
  public async Task AddFolder_TheSelectedWorkspace_ChangesNothing_AndEscAddsNothing() {
    using TuiHarness tui = Start(out _);
    await tui.OpenWorkspaces();
    await tui.WaitFor("● " + Root);

    await AddFolder(tui);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Add folder ─");
    Assert.Contains("─ Workspaces ─", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await Browser(tui);
    await SelectShown(tui, Root);

    await tui.WaitUntilGone("─ Add folder ─");
    await tui.WaitUntilGone("─ Workspaces ─");
    Assert.Equal([Root], tui.Workspaces.Read());
    Assert.Equal(Root, await tui.Invoke(() => tui.Context.Workspace));
  }

  [Fact]
  public async Task AddFolder_WhenTheListIsNotWritten_SaysWhy_AndIsBackAtTheFolder() {
    using TuiHarness tui = Start(out _);
    await tui.OpenWorkspaces();
    await tui.WaitFor("● " + Root);
    await AddFolder(tui);
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

  /// "Add folder…" takes any folder, a new one too (protocol/lip-tui.md, "Workspaces"), and
  /// says that the Server page sets it up.
  [Theory]
  [InlineData(Language.En, "The Server page sets it up.")]
  [InlineData(Language.ZhHans, "服务器页会为它安装服务器。")]
  [InlineData(Language.ZhHant, "伺服器頁會為它安裝伺服器。")]
  public async Task AddFolder_ItsSentence_SaysWhereTheServerIsSetUp(Language language, string says) {
    using TuiHarness tui = Start(out _, language: language);
    await tui.OpenWorkspaces();

    tui.Press(ConsoleKey.Tab);

    await tui.WaitFor(says);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 80, 24)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Picker_WorksInEveryLanguage_OnASmallTerminalToo(
      Language language, int width, int height) {
    using TuiHarness tui = Start(out _, Files(Alpha, Gone), language, width, height);
    await tui.OpenWorkspaces();
    await tui.WaitFor(WorkspacesText.Missing);
    Assert.Contains(WorkspacesText.AddFolder, tui.Screen);
    Assert.Contains(WorkspacesText.CurrentDetail, tui.Screen);
    tui.Snapshot("frames-picker");

    // The row of a workspace whose server another lip runs.
    await tui.SetServerRunning(true, Alpha);
    ToRow(tui, 1);
    await tui.WaitFor(WorkspacesText.RunningDetail);
    Assert.Contains(WorkspacesText.ServerRunning, tui.Screen);
    tui.Snapshot("frames-picker-running");
    await tui.SetServerRunning(false, Alpha);
    await tui.WaitUntilGone(WorkspacesText.ServerRunning);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Remove);
    Assert.Contains(WorkspacesText.Switch, tui.Screen);
    tui.Snapshot("frames-picker-menu");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.RemoveQuestion);
    tui.Snapshot("frames-picker-remove");
    tui.Press(ConsoleKey.Escape);

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.AddTitle} ─");
    await Browser(tui);
    tui.Snapshot("frames-picker-add");
  }
}
