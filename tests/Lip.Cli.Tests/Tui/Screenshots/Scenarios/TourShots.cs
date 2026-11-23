using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Pages.Home;
using Lip.Cli.Tui.Pages.Mods;
using Lip.Cli.Tui.Pages.Server;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The interface as a whole, for docs/lip/interface/tour.md of liteldev.github.io: its layout,
/// where the focus is, an operation that is cancelled, a failure with hints, the folder browser,
/// and a terminal that is too small.
public sealed class TourShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task LayoutAndFocus(Language language) {
    World world = Worlds.Survival(language, height: 22);
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor("LeviLamina " + World.LoaderVersion);
    await tui.SetServerRunning(true);
    await tui.WaitFor("✓ 4");

    await shot.Capture(
        "tour-01-layout",
        Note.Callout(
            1, At.Text($"lip {World.Version}").To(At.Text(world.Workspace)),
            shot.T(
                "The top line: the version of lip and the folder it works on, the workspace.",
                "顶部一行：lip 的版本，以及它正在操作的文件夹（工作区）。",
                "頂部一列：lip 的版本，以及它正在操作的資料夾（工作區）。"),
            Side.Right),
        Note.Callout(
            2, At.Text("● " + CommonText.ServerRunning),
            shot.T(
                "Whether the server of this workspace runs. Nothing is shown while it does not.",
                "当前工作区的服务器是否在运行。没有运行时这里是空的。",
                "目前工作區的伺服器是否在執行。沒有執行時這裡是空的。")),
        Note.Callout(
            3, At.Text(SettingsText.Title),
            shot.T(
                "The sidebar: the pages. The ● marks the Server page while the server runs.",
                "侧边栏：各个页面。服务器运行时，“服务器”一项后面有 ●。",
                "側邊欄：各個頁面。伺服器執行時，「伺服器」一項後面有 ●。"),
            Side.Bottom),
        Note.Callout(
            4, At.Text($"─ {HomeText.Title} ─"),
            shot.T(
                "The page that is shown, with its name in the frame.", "当前显示的页面，边框上是它的名字。",
                "目前顯示的頁面，邊框上是它的名字。"),
            Side.Right),
        Note.Callout(
            5, At.Text($"{CommonText.HintMove.Key} {CommonText.HintMove.Text}")
                .To(At.Text($"Esc {CommonText.HintBack.Text}")),
            shot.T(
                "The bottom line: the keys that work right now.", "底部一行：此刻可用的按键。",
                "底部一列：此刻可用的按鍵。"),
            Side.Right));

    // Esc goes from the page to the sidebar; ↑ and ↓ then show the other pages.
    await tui.SetServerRunning(false);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(Catalogue.Money);
    await shot.Capture(
        "tour-02-sidebar",
        Note.Callout(
            1, At.Text(ModsText.Title, 2),
            shot.T(
                "The focus is in the sidebar: the entry is bright, and the page shows at once.",
                "焦点在侧边栏：这一项是亮色，右侧立刻显示对应页面。",
                "焦點在側邊欄：這一項是亮色，右側立刻顯示對應頁面。"),
            Side.Right),
        Note.Callout(
            2, At.Text($"Esc {CommonText.HintQuit.Text}"),
            shot.T(
                "In the sidebar, Esc asks whether to quit.", "在侧边栏按 Esc 会询问是否退出。",
                "在側邊欄按 Esc 會詢問是否結束。"),
            Side.Right),
        Note.Keys("Esc", "↓", "↓"));

    // Enter gives the page the focus.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.ContentFocused, "the page");
    await shot.Capture(
        "tour-03-page",
        Note.Callout(
            1, At.Text($"─ {ModsText.Title} ─"),
            shot.T(
                "The focus is in the page: its frame and its name are bright.",
                "焦点在页面里：页面的边框和名字变亮。", "焦點在頁面裡：頁面的邊框和名字變亮。"),
            Side.Right),
        Note.Callout(
            2, At.Row(Catalogue.Gmlib),
            shot.T(
                "The highlighted row is what Enter and Space act on.",
                "高亮的这一行就是 Enter 和 Space 的操作对象。", "反白的這一列就是 Enter 和 Space 的操作對象。")),
        Note.Callout(
            3, At.Text($"Esc {CommonText.HintBack.Text}"),
            shot.T(
                "Esc now goes back to the sidebar.", "此时按 Esc 回到侧边栏。",
                "此時按 Esc 回到側邊欄。"),
            Side.Right),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Cancelled(Language language) {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    World world = Worlds.Empty(language, height: 22);
    world.ServerSetup = harness =>
        new WaitingSetup(harness, new SetupOfShots(harness, world), release.Task);
    using Shot shot = run.Start(world);
    using IDisposable released = new Released(release);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor(HomeText.CreateServerDetail);

    // The setup of a server in this folder, up to the download of the game.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    await tui.WaitFor("1.26.60");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    shot.Scrub(@"(?<=│ )[|/\\-](?= Downloading)", "/");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(" 37%");

    // Esc cancels: lip says so, and undoes what it did.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(CommonText.Cancelling);
    await shot.Capture(
        "tour-04-cancelling",
        Note.Focus(At.Panel(SetupText.SetUpTitle)),
        Note.Box(At.Text(CommonText.Cancelling)),
        Note.KeysAfter(shot.T("After", "按下之后", "按下之後"), "Esc"));

    release.TrySetResult();
    await tui.WaitFor(CommonText.Cancelled);
    await shot.Capture(
        "tour-05-cancelled",
        Note.Focus(At.Panel($"{SetupText.SetUpTitle} · {CommonText.CancelledWord}")),
        Note.Box(At.Text(CommonText.Cancelled)),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task FailureWithHints(Language language) {
    World world = Worlds.Server(language, height: 24).WithRepositories().WithoutNetwork();
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("LeviOptimize · LiteLDev");

    // Installing a mod while GitHub is not reached: the preview fails.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Custom);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.FullReport);
    await shot.Capture(
        "tour-06-failure-hints",
        Note.Callout(
            1, At.Text(CommonText.Error + ":"),
            shot.T(
                "What went wrong, as lip says it, in English.", "出了什么问题（lip 的原话，英文）。",
                "出了什麼問題（lip 的原話，英文）。")),
        Note.Callout(
            2, At.Text(CommonText.HintWord + ":"),
            shot.T(
                "What you can do about it.", "你可以怎么处理。", "你可以怎麼處理。")),
        Note.Callout(
            3, At.Text(CommonText.FullReport),
            shot.T(
                "The file with every detail: attach it when you ask for help.",
                "记录了全部细节的文件：求助时请附上它。", "記錄了全部細節的檔案：求助時請附上它。")),
        Note.Callout(
            4, At.Button(CommonText.Close),
            shot.T(
                "Closes the dialog. lip goes on; nothing was changed.", "关闭对话框。lip 继续运行，什么都没有改动。",
                "關閉對話方塊。lip 繼續執行，什麼都沒有變更。"),
            Side.Right));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Browser(Language language) {
    World world = Worlds.Survival(language, height: 22);
    foreach (string folder in new[] { "worlds", "behavior_packs", "resource_packs" }) {
      world.Fs.AddDirectory(Path.Join(world.Workspace, folder));
    }

    world.Fs.AddDirectory(World.PathOf(@"D:\downloads"));
    world.Fs.AddDirectory(World.PathOf(@"C:\Users\Steve\Documents"));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.OpenWorkspaces();
    await tui.WaitFor(Worlds.Skyblock);

    // Add folder…: the browser opens in the folder of the workspace.
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.AddTitle} ─");
    await tui.WaitUntilGone(PathPickerText.Reading);
    await tui.WaitFor("worlds" + Path.DirectorySeparatorChar);
    await shot.Capture(
        "tour-07-browser",
        Note.Callout(
            1, At.Text(PathPickerText.Folder + "  "),
            shot.T(
                "The path bar: the folder whose content is listed.", "路径栏：列表显示的是这个文件夹的内容。",
                "路徑列：清單顯示的是這個資料夾的內容。")),
        Note.Callout(
            2, At.Row(".."),
            shot.T(
                "Up one level. Enter or ← goes there.", "上一级。按 Enter 或 ← 前往。",
                "上一層。按 Enter 或 ← 前往。")),
        Note.Callout(
            3, At.Row("behavior_packs" + Path.DirectorySeparatorChar)
                .To(At.Row("worlds" + Path.DirectorySeparatorChar)),
            shot.T(
                "The folders inside. Enter or → opens the highlighted one.",
                "里面的文件夹。按 Enter 或 → 打开高亮的那个。", "裡面的資料夾。按 Enter 或 → 開啟反白的那個。")),
        Note.Callout(
            4, At.Button(PathPickerText.SelectFolder).To(At.Button(CommonText.Cancel)),
            shot.T(
                "The buttons. Tab goes to them from the list.", "按钮。在列表中按 Tab 移到这里。",
                "按鈕。在清單中按 Tab 移到這裡。"),
            Side.Right),
        Note.Callout(
            5, At.Text($"Tab {CommonText.HintNext.Text}"),
            shot.T(
                "Tab goes round the path bar, the list and the buttons.", "Tab 在路径栏、列表和按钮之间循环。",
                "Tab 在路徑列、清單和按鈕之間循環。"),
            Side.Top));

    // The path bar takes a path that is typed or pasted.
    tui.Press(ConsoleKey.Tab, 1, ConsoleModifiers.Shift);
    tui.Press(ConsoleKey.Backspace, "survival".Length);
    tui.Type("creative");
    await tui.WaitFor(Worlds.Creative);
    await shot.Capture(
        "tour-08-browser-path",
        Note.Box(At.Text(Worlds.Creative)),
        Note.Arrow(
            At.Text($"Enter {PathPickerText.HintGo.Text}"),
            shot.T("Enter goes to the path typed", "按 Enter 前往输入的路径", "按 Enter 前往輸入的路徑"),
            Side.Bottom),
        Note.Keys("Shift+Tab"));
    tui.Press(ConsoleKey.Escape);

    // ← goes up, and above the top folder of a drive are the drives.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("skyblock" + Path.DirectorySeparatorChar);
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("downloads" + Path.DirectorySeparatorChar);
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor(PathPickerText.AtRoots);
    await shot.Capture(
        "tour-09-browser-drives",
        Note.Callout(
            1, At.Text(PathPickerText.Computer),
            shot.T(
                "Above the top folder of a drive: the drives of the computer.",
                "磁盘根目录的上一级：这台电脑的各个磁盘。", "磁碟根目錄的上一層：這台電腦的各個磁碟。")),
        Note.Callout(
            2, At.Text(PathPickerText.AtRoots),
            shot.T(
                "Nothing can be selected here: open a drive first.", "这里不能选择：请先打开一个磁盘。",
                "這裡不能選擇：請先開啟一個磁碟。")),
        Note.Keys("←", "←", "←"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task TooSmall(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, width: 56, height: 10));
    await shot.Tui.WaitFor(CommonText.TooSmall);
    await shot.Capture("tour-10-too-small", Note.Keys("Ctrl+C"));
  }

  /// A setup that waits in the middle of the download of the game until it is released, and
  /// is then cancelled when the user asked for that, as lip setup is.
  private sealed class WaitingSetup(TuiHarness harness, SetupOfShots setup, Task release)
      : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        setup.LoaderVersions(client, game, cancellationToken, session);

    public async Task<OperationResult> Setup(
        LipClient client, SetupRequest request, OperationRun run) {
      if (run.DryRun) {
        return await setup.Setup(client, request, run);
      }

      await harness.InteractionOf(client).RunWithProgress(
          "Downloading https://www.minecraft.net/bedrockdedicatedserver/bin-win/"
              + $"bedrock-server-{request.Game}.2.zip",
          async progress => {
            progress.Report(37);
            await release;
            run.Token.ThrowIfCancellationRequested();
          });
      return OperationResult.Changed;
    }
  }

  private sealed class Released(TaskCompletionSource release) : IDisposable {
    public void Dispose() => release.TrySetResult();
  }
}
