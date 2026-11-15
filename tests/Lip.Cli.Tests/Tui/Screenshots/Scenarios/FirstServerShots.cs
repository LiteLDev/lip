using System.IO.Abstractions;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Pages.Home;
using Lip.Cli.Tui.Pages.Server;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The tutorial "Your first server" (docs/intro/first_server.md): from an empty folder to a
/// running server with one mod, in the order the page tells it.
public sealed class FirstServerShots(ShotRun run) {
  private const string ModVersion = "1.8.1";
  private const string ModName = "LeviOptimize";
  private const string AssetUrl =
      "https://github.com/LiteLDev/LeviOptimize/releases/download/v1.8.1/LeviOptimize-windows-x64.zip";

  private static readonly string _servers = World.PathOf(@"D:\servers");
  private static readonly string _survival = World.PathOf(@"D:\servers\survival");

  /// What the server of the tutorial writes while it starts.
  private static readonly string[] _started = [
    "NO LOG FILE! - setting up server logging...",
    "[2026-11-14 20:15:02:114 INFO] Starting Server",
    "[2026-11-14 20:15:02:115 INFO] Version: 1.26.60.2",
    "[2026-11-14 20:15:02:115 INFO] Session ID: 6f1c1c1e-0d0f-4a38-9a51-2f2b7c1d9e44",
    "[2026-11-14 20:15:02:116 INFO] Level Name: Bedrock level",
    "[2026-11-14 20:15:02:118 INFO] Game mode: 0 Survival",
    "[2026-11-14 20:15:02:118 INFO] Difficulty: 1 EASY",
    Loader("20:15:02.412", "LeviLamina 26.60.3 is loading mods..."),
    Loader("20:15:02.731", "Loaded LeviOptimize v1.8.1"),
    Loader("20:15:02.732", "1 mod(s) loaded"),
    "[2026-11-14 20:15:03:902 INFO] opening worlds/Bedrock level/db",
    "[2026-11-14 20:15:05:377 INFO] IPv4 supported, port: 19132: Used for gameplay and LAN",
    "[2026-11-14 20:15:05:378 INFO] IPv6 supported, port: 19133: Used for gameplay",
    "[2026-11-14 20:15:05:641 INFO] Server started.",
  ];

  /// What it writes when it reads "list".
  private static readonly string[] _listed = [
    "[2026-11-14 20:17:40:052 INFO] There are 0/10 players online:",
  ];

  /// What it writes when it is asked to stop.
  private static readonly string[] _stopped = [
    "[2026-11-14 20:21:09:310 INFO] Server stop requested.",
    "[2026-11-14 20:21:09:377 INFO] Stopping server...",
    "Quit correctly",
  ];

  /// The interface as it starts in an empty folder, the new server wizard, and a mod of the
  /// Get mods page.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task FromAnEmptyFolderToAMod(Language language) {
    // lip was started in a folder that holds nothing.
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    World world = new(language, workspace: _servers);
    world.ServerSetup = harness => new SetupOfShots(harness, world) { Release = release.Task };
    OfferMod(world);
    using Shot shot = run.Start(world);
    // Whatever becomes of the scenario, the setup does not wait for ever.
    using IDisposable released = new Released(release);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor(HomeText.CreateServerDetail);
    await tui.WaitFor(HomeText.NotInstalled);

    Target sidebar = At.Row(HomeText.Title, 2).To(At.Row(SettingsText.Title));
    Target overview = At.Text(HomeText.Workspace).To(At.Text(HomeText.NotInstalled))
        .To(At.Text(CommonText.ServerStopped)).Pad(1);
    Target steps = At.Text("1  " + HomeText.CreateServer)
        .To(At.Text("3  " + HomeText.StartServer)).Pad(1);
    Target hints = At.Text("↑↓").To(At.Text("Esc " + CommonText.HintBack.Text)).Pad(1);
    await shot.Capture(
        "first-server-01-home",
        Note.Box(sidebar),
        Note.Callout(
            1, sidebar,
            shot.T(
                "The sidebar: the nine pages of lip. The one shown is marked.",
                "侧边栏：lip 的九个页面，当前页面带有标记。",
                "側邊欄：lip 的九個頁面，目前的頁面帶有標記。"),
            Side.Bottom),
        Note.Box(overview),
        Note.Callout(
            2, overview,
            shot.T(
                "The folder that lip works in, and what it holds: nothing yet.",
                "lip 当前所在的文件夹，以及里面有什么：现在还什么都没有。",
                "lip 目前所在的資料夾，以及裡面有什麼：現在還什麼都沒有。"),
            Side.Right),
        Note.Box(steps),
        Note.Callout(
            3, steps,
            shot.T(
                "The three steps of this tutorial. The first one is selected.",
                "本教程的三个步骤，第一步已被选中。",
                "本教學的三個步驟，第一步已被選取。"),
            Side.Right),
        Note.Box(hints),
        Note.Callout(
            4, hints,
            shot.T(
                "The keys that work now. They change as you move.",
                "当前可用的按键，会随着你的操作而变化。",
                "目前可用的按鍵，會隨著你的操作而變化。"),
            Side.Right),
        Note.Keys("Enter"));

    // Step 1 of the Home page starts the wizard: here, or in another folder.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Elsewhere);
    await shot.Capture(
        "first-server-02-where",
        Note.Focus(At.Panel(WorkspacesText.NewServerTitle)),
        Note.Box(At.Row(WorkspacesText.Elsewhere)),
        Note.Keys("↓", "Enter"));

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Cancel} ]");
    await tui.WaitUntilGone(PathPickerText.Reading);
    await shot.Capture(
        "first-server-03-folder",
        Note.Callout(
            1, At.Text(PathPickerText.Folder).To(At.Text(_servers)),
            shot.T(
                "The folder that is open.", "当前打开的文件夹。", "目前開啟的資料夾。")),
        Note.Callout(
            2, At.Row(".."),
            shot.T(
                "What it holds: only \"..\", which goes up one folder.",
                "里面的内容：只有“..”，用来回到上一级文件夹。",
                "裡面的內容：只有「..」，用來回到上一層資料夾。")),
        Note.Callout(
            3, At.Button(PathPickerText.NewFolder),
            shot.T(
                "Makes a folder for the server inside the open one.",
                "在当前文件夹里为服务器新建一个文件夹。",
                "在目前的資料夾裡為伺服器新增一個資料夾。"),
            Side.Bottom),
        Note.Keys("Tab", "→", "Enter"));

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {PathPickerText.NewFolderTitle} ─");
    tui.Type("survival");
    await shot.Capture(
        "first-server-04-folder-name",
        Note.Focus(At.Panel(PathPickerText.NewFolderTitle)),
        Note.Box(At.Text("> survival")),
        Note.Keys("s", "u", "r", "v", "i", "v", "a", "l", "Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor("1.26.60");
    await shot.Capture(
        "first-server-05-game-version",
        Note.Focus(At.Panel(WorkspacesText.GameVersion)),
        Note.Callout(
            1, At.Row("1.26.60"),
            shot.T(
                "The newest version, selected. LeviLamina ✓: mods work on it.",
                "最新版本，已被选中。LeviLamina ✓ 表示这个版本可以装模组。",
                "最新版本，已被選取。LeviLamina ✓ 表示這個版本可以裝模組。")),
        Note.Callout(
            2, At.Row("1.26.30"),
            shot.T(
                "A version that LeviLamina does not support: a server without mods.",
                "LeviLamina 不支持的版本：只能开不带模组的服务器。",
                "LeviLamina 不支援的版本：只能開不含模組的伺服器。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Loader} ─");
    await shot.Capture(
        "first-server-06-loader",
        Note.Focus(At.Panel(WorkspacesText.Loader)),
        Note.Box(At.Row("LeviLamina 26.60.3")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    await shot.Capture(
        "first-server-07-plan",
        Note.Focus(At.Panel(WorkspacesText.SetUpTitle)),
        Note.Callout(
            1, At.Text("Dry run:").To(At.Text("(12 files)")),
            shot.T(
                "What lip is about to install: the game and LeviLamina.",
                "lip 将要安装的东西：游戏和 LeviLamina。",
                "lip 將要安裝的東西：遊戲和 LeviLamina。")),
        Note.Callout(
            2, At.Button(CommonText.Apply),
            shot.T(
                "Nothing has changed yet. Apply carries the plan out.",
                "此时还没有任何改动。选「应用」才会执行。",
                "此時還沒有任何變更。選「套用」才會執行。"),
            Side.Bottom),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    // The toast that named the new workspace is gone by the time a user has read the plan.
    await tui.WaitUntilGone(CommonText.WorkspaceSelected + ": ");
    await shot.Capture(
        "first-server-08-eula",
        Note.Focus(At.Panel(CommonText.EulaTitle)),
        Note.Box(At.Button(CommonText.Accept)),
        Note.Keys("Enter"));

    // The setup waits in the middle of its second download for the screenshot.
    shot.Scrub(@"(?<=│ )[|/\\-](?= Downloading)", "/");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(" 62%");
    await shot.Capture(
        "first-server-09-progress",
        Note.Focus(At.Panel(WorkspacesText.SetUpTitle)),
        Note.Arrow(
            At.Text(" 62%"), shot.T("Still downloading", "仍在下载", "仍在下載"), Side.Right));

    release.TrySetResult();
    await tui.WaitFor("✓ " + CommonText.Done);
    await shot.Capture(
        "first-server-10-result",
        Note.Focus(At.Panel(WorkspacesText.SetUpTitle)),
        Note.Box(At.Text("✓ " + CommonText.Done)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor("● " + _survival);
    await shot.Capture(
        "first-server-11-workspace",
        Note.Callout(
            1, At.Row("● " + _survival),
            shot.T(
                "The new server. ● marks the folder that lip works in now.",
                "新建的服务器。● 表示 lip 现在所在的文件夹。",
                "剛建立的伺服器。● 表示 lip 現在所在的資料夾。")),
        Note.Callout(
            2, At.Text(" · " + _survival),
            shot.T(
                "The top bar always names that folder.",
                "顶栏始终显示这个文件夹。", "頂端列始終顯示這個資料夾。"),
            Side.Right),
        Note.Keys("Esc", "↑", "↑", "↑", "↑", "↑", "↑", "Enter"));

    // Back to the Home page, as a user goes: Esc to the sidebar, up to Home, Enter. The first
    // step is done, and the second is selected.
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.UpArrow, 6);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("LeviLamina " + World.LoaderVersion);
    await tui.WaitFor(HomeText.InstallModsDetail);
    await shot.Capture(
        "first-server-12-home-server",
        Note.Box(At.Text(HomeText.Game).To(At.Text("LeviLamina " + World.LoaderVersion)).Pad(1)),
        Note.Callout(
            1, At.Text(HomeText.Game).To(At.Text("LeviLamina " + World.LoaderVersion)).Pad(1),
            shot.T(
                "The game and the loader that the folder now holds.",
                "这个文件夹现在装有的游戏和加载器。", "這個資料夾現在裝有的遊戲和載入器。"),
            Side.Right),
        Note.Callout(
            2, At.Row(HomeText.InstallMods),
            shot.T(
                "Step 1 is ticked, and step 2 is selected.",
                "第 1 步已打勾，第 2 步已被选中。", "第 1 步已打勾，第 2 步已被選取。")),
        Note.Keys("Enter"));

    // Step 2 opens the Get mods page.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("LegacyScriptEngine · LiteLDev");
    await tui.WaitFor("0.13.2 #lua");
    await shot.Capture(
        "first-server-13-get-mods",
        Note.Callout(
            1, At.Text(GetModsText.Search).To(At.Text(GetModsText.Filters)),
            shot.T(
                "Search, order and filter the list.", "搜索、排序和筛选列表。",
                "搜尋、排序和篩選清單。")),
        Note.Callout(
            2, At.Row("LegacyScriptEngine · LiteLDev").To(At.Row("LegacyRemoteCall · LiteLDev")),
            shot.T(
                "The mods of levimc.org, with downloads, stars and the last update.",
                "levimc.org 上的模组，以及下载量、星标数和最近更新日期。",
                "levimc.org 上的模組，以及下載量、星標數和最近更新日期。")),
        Note.Callout(
            3, At.Text("─ LegacyScriptEngine ─"),
            shot.T(
                "The details of the selected mod.", "所选模组的详情。", "所選模組的詳細資料。")),
        Note.Keys("↑", "↑", "↑"));

    // ↑ from the first mod goes to the filters, the order, and the search.
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Type("optim");
    await tui.WaitFor(GetModsText.Count(1));
    await tui.WaitFor(ModVersion);
    await shot.Capture(
        "first-server-14-search",
        Note.Box(At.Text("optim")),
        Note.Arrow(
            At.Row(ModName + " · LiteLDev"),
            shot.T("The mod that matches", "匹配的模组", "符合的模組"), Side.Right),
        Note.Keys("o", "p", "t", "i", "m", "Enter"));

    // Enter in the search goes to the results; Enter on a mod opens its actions.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter " + CommonText.HintActions.Text);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    await shot.Capture(
        "first-server-15-menu",
        Note.Focus(At.Panel(ModName)),
        Note.Box(At.Row(GetModsText.Install)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Catalogue.Optimize + "@" + ModVersion);
    await shot.Capture(
        "first-server-16-versions",
        Note.Focus(At.Panel(ModName)),
        Note.Callout(
            1, At.Row(ModVersion),
            shot.T(
                "The versions that fit your game and LeviLamina, newest first.",
                "与你的游戏和 LeviLamina 相配的版本，最新的在最上面。",
                "與你的遊戲和 LeviLamina 相符的版本，最新的在最上面。")),
        Note.Callout(
            2, At.Text(Catalogue.Optimize + "@" + ModVersion),
            shot.T(
                "What lip installs when you press Enter.", "按 Enter 后 lip 安装的内容。",
                "按 Enter 後 lip 安裝的內容。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    await shot.Capture(
        "first-server-17-mod-plan",
        Note.Focus(At.Panel(GetModsText.InstallTitle(Catalogue.Optimize + "@" + ModVersion))),
        Note.Box(At.Button(CommonText.Apply)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    await shot.Capture(
        "first-server-18-mod-result",
        Note.Focus(At.Panel(GetModsText.InstallTitle(Catalogue.Optimize + "@" + ModVersion))),
        Note.Box(At.Text("✓ " + CommonText.Done)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor("✓ " + ModName);
    await shot.Capture(
        "first-server-19-mod-installed",
        Note.Box(At.Text("✓ " + ModName)),
        Note.Callout(
            1, At.Text("✓ " + ModName),
            shot.T(
                "✓ marks a mod that is installed.", "✓ 表示模组已安装。", "✓ 表示模組已安裝。")),
        Note.Keys("Esc", "↑", "↑", "↑", "Enter"));

    // Back to the Home page again: two steps are done, and the third is selected.
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(HomeText.StartServerDetail);
    await shot.Capture(
        "first-server-20-home-mods",
        Note.Callout(
            1, At.Text("✓ 1").To(At.Text("✓ 2  " + HomeText.InstallMods)),
            shot.T(
                "Steps 1 and 2 are done.", "第 1 步和第 2 步已完成。", "第 1 步和第 2 步已完成。")),
        Note.Callout(
            2, At.Row(HomeText.StartServer),
            shot.T(
                "Step 3 is selected: Enter starts the server.",
                "第 3 步已被选中：按 Enter 启动服务器。",
                "第 3 步已被選取：按 Enter 啟動伺服器。"),
            Side.Right),
        Note.Keys("Enter"));
  }

  /// The server of the tutorial: started from the Home page, given a command, and stopped.
  /// As in ServerShots.Running, the server that runs is cmd.exe in a folder of its own on the
  /// disk, and what it wrote is replaced by what the game writes.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task StartAndStop(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      // The workspace as the first scenario left it: the game, LeviLamina and one mod. The
      // server of a workspace with a loader is bedrock_server_mod.exe.
      World world = new World(language, width: 110, workspace: _survival).InstallServer(
          new Installed(Catalogue.Optimize, ModVersion));
      File.WriteAllText(
          Path.Join(root, ".lip", "state.json"),
          world.Fs.File.ReadAllText(Path.Join(_survival, ".lip", "state.json")));
      File.Copy(
          Path.Join(root, WorkspaceProgram.WindowsServerFileName),
          Path.Join(root, WorkspaceProgram.ModServerFileName));
      FileSystem fs = new();
      world.Servers = servers;
      world.CreateClient = (interaction, hookOutput, _) =>
          TestClients.CmdClient(fs, interaction, hookOutput, root);
      world.List(_servers);
      using Shot shot = run.Start(world);
      TuiHarness tui = shot.Tui;
      await tui.WaitFor("LeviLamina " + World.LoaderVersion);
      await tui.WaitFor(HomeText.StartServerDetail);

      // Step 3 starts the server and shows its console.
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.WaitFor(_ => LastRow(tui).Contains($"Enter {ServerText.HintSend.Text}"), "Send");
      WorkspaceServer server = servers.Find(world.Workspace)!;
      await Prompt(tui, server);
      Write(server, _started);
      await tui.WaitFor("Server started.");
      await shot.Capture(
          "first-server-21-running",
          Note.Callout(
              1, At.Text("● " + CommonText.ServerRunning, 2),
              shot.T(
                  "The server runs, and for how long. The top bar and the sidebar say so too.",
                  "服务器正在运行，以及已运行的时间。顶栏和侧边栏也有同样的标记。",
                  "伺服器正在執行，以及已執行的時間。頂端列和側邊欄也有同樣的標記。")),
          Note.Callout(
              2, At.Text("Server started."),
              shot.T(
                  "What the server writes. \"Server started.\": players can join.",
                  "服务器的输出。看到“Server started.”，玩家就可以进服了。",
                  "伺服器的輸出。看到「Server started.」，玩家就可以加入了。"),
              Side.Right),
          Note.Callout(
              3, At.Text("> " + ServerText.InputExample),
              shot.T(
                  "The input line: what you type here goes to the server.",
                  "输入行：在这里输入的内容会发给服务器。",
                  "輸入列：在這裡輸入的內容會傳給伺服器。")));

      tui.Type("list");
      await tui.WaitFor("> list");
      await shot.Capture(
          "first-server-22-command",
          Note.Box(At.Text("> list")),
          Note.Keys("l", "i", "s", "t", "Enter"));

      // cmd.exe answers that it knows no such command; the game answers with the players.
      tui.Press(ConsoleKey.Enter);
      await Answered(tui, server, root);
      Write(server, _started, _listed);
      await tui.WaitFor("players online");
      await shot.Capture(
          "first-server-23-answer",
          Note.Callout(
              1, At.Text("> list"),
              shot.T("The command that you sent.", "你发出的命令。", "你送出的命令。")),
          Note.Callout(
              2, At.Text("There are 0/10 players online:"),
              shot.T(
                  "The answer of the server: nobody has joined yet.",
                  "服务器的回答：还没有玩家加入。", "伺服器的回答：還沒有玩家加入。"),
              Side.Right),
          Note.Keys("Ctrl+C"));

      // Ctrl+C asks lip to stop the server, which then reads "stop"; cmd.exe exits on "exit".
      tui.Interrupt();
      await tui.WaitFor("● " + CommonText.ServerStopping);
      server.Send("exit 0");
      await tui.WaitFor($"[ {ServerText.Start} ]");
      Write(server, _started, [.. _listed, .. _stopped]);
      await tui.WaitFor("Quit correctly");
      await shot.Capture(
          "first-server-24-stopped",
          Note.Callout(
              1, At.Text("○ " + ServerText.ExitedWith(0)),
              shot.T(
                  "The server has stopped. Code 0 means that it ended normally.",
                  "服务器已停止。退出码 0 表示正常结束。", "伺服器已停止。結束代碼 0 表示正常結束。")),
          Note.Box(At.Button(ServerText.Start)));
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  /// The question that the interface asks when it does not reach GitHub.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task GithubUnreachable(Language language) {
    World world = new(language, height: 22, workspace: _servers) {
      GithubProbe = new GithubProbe((_, _) => Task.FromException<int>(
          new HttpRequestException("Connection timed out (github.com:443)"))),
    };
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor(HomeText.GithubQuestion);
    await tui.WaitFor(HomeText.GithubProxyDetail);

    await shot.Capture(
        "first-server-25-github",
        Note.Focus(At.Panel(HomeText.GithubTitle)),
        Note.Callout(
            1, At.Row(HomeText.GithubProxy),
            shot.T(
                "You have the address of a GitHub proxy: lip tests it, then uses it.",
                "你有 GitHub 代理的网址：lip 会先测试，然后使用它。",
                "你有 GitHub 代理伺服器的網址：lip 會先測試，然後使用它。")),
        Note.Callout(
            2, At.Row(CommonText.ConnectDirectly),
            shot.T(
                "Your network reaches GitHub after all: lip does not ask again.",
                "你的网络其实能连上 GitHub：lip 不会再问。",
                "你的網路其實能連上 GitHub：lip 不會再問。")),
        Note.Callout(
            3, At.Row(CommonText.Later),
            shot.T(
                "Decide another time: lip asks again when it next starts.",
                "以后再决定：lip 下次启动时会再问。", "之後再決定：lip 下次啟動時會再問。")));
  }

  /// A line that LeviLamina writes, with its colors.
  private static string Loader(string time, string message) =>
      $"\x1b[90m{time}\x1b[0m \x1b[36mINFO\x1b[0m \x1b[1m[LeviLamina]\x1b[0m {message}";

  private static string LastRow(TuiHarness tui) => tui.Screen.Split('\n')[^1];

  /// Waits until cmd.exe has written its prompt, the last that it writes before it reads.
  private static Task Prompt(TuiHarness tui, WorkspaceServer server) =>
      tui.WaitFor(
          _ => {
            lock (server.Gate) {
              return server.Log.Lines[^1].PlainText.EndsWith('>');
            }
          },
          "the prompt of cmd.exe");

  /// Waits until cmd.exe has answered "list": it has written that it knows no such command,
  /// to its standard error, and its prompt, to its standard output, in either order.
  private static Task Answered(TuiHarness tui, WorkspaceServer server, string root) =>
      tui.WaitFor(
          _ => {
            lock (server.Gate) {
              string prompt = root + ">";
              List<string> lines = [.. server.Log.Lines.Select(line => line.PlainText)];
              List<string> after = lines[(lines.LastIndexOf("> list") + 1)..];
              return lines.Contains("> list")
                  && after.Any(line => line.Contains(prompt, StringComparison.OrdinalIgnoreCase))
                  && after.Any(line => line.Replace(prompt, "", StringComparison.OrdinalIgnoreCase)
                      .Trim() is not ("" or "list"));
            }
          },
          "the answer of cmd.exe");

  /// Replaces what the server wrote by the lines given, as the game writes them. The lines
  /// that the user sent, which the interface shows as it does, stay between them and the
  /// answers.
  private static void Write(WorkspaceServer server, string[] lines, string[]? answers = null) {
    lock (server.Gate) {
      List<StyledLine> sent = [
        .. server.Log.Lines.Where(
            line => line.PlainText == "> list"),
      ];
      server.Log.Append("\n");
      server.Log.Lines.Clear();
      server.Log.Lines.Add(new StyledLine());
      foreach (string line in lines) {
        server.Log.Append(line + "\n");
      }

      foreach (StyledLine line in sent) {
        server.Log.AddLine(line);
      }

      foreach (string line in answers ?? []) {
        server.Log.Append(line + "\n");
      }
    }
  }

  /// Makes the archives of the world hold LeviOptimize, so that lip can install it: its
  /// package file, as its repository has it, and the archive of the mod.
  private static void OfferMod(World world) {
    SpecVersion version = new(SemVersion.Parse(ModVersion), "v" + ModVersion);
    ArchiveHandle package = new(
        $"{Catalogue.Optimize}@{ModVersion}",
        new Files(new() {
          ["lip.toml"] = $$"""
              format = 4
              id = "{{Catalogue.Optimize}}"
              version = "{{ModVersion}}"

              [info]
              name = "{{ModName}}"
              description = "A performance optimization mod for LeviLamina"

              [[variants.""]]
              side = "server"
              platform = "win-x64"
              assets = [{ type = "mcmod", urls = ["{{AssetUrl}}"] }]
              """,
        }),
        World.Commit);
    Files mod = new(new() {
      ["manifest.json"] =
          $$"""{ "name": "{{ModName}}", "entry": "{{ModName}}.dll", "type": "native" }""",
      [ModName + ".dll"] = "",
      [ModName + ".pdb"] = "",
      ["lang/en_US.json"] = "{}",
      ["lang/zh_CN.json"] = "{}",
    });
    world.Archives.Setup(a => a.ListVersions(Catalogue.Optimize)).ReturnsAsync([version]);
    world.Archives.Setup(a => a.ResolveGitRef(Catalogue.Optimize, It.IsAny<GitRef>()))
        .ReturnsAsync(World.Commit);
    world.Archives.Setup(a => a.OpenVersion(Catalogue.Optimize, It.IsAny<SpecVersion>()))
        .ReturnsAsync(package);
    world.Archives.Setup(a => a.OpenCommit(Catalogue.Optimize, World.Commit))
        .ReturnsAsync(package);
    world.Archives.Setup(a => a.OpenAsset(It.IsAny<IReadOnlyList<string>>())).ReturnsAsync(mod);
  }

  /// The files of an archive, held in memory.
  private sealed class Files(Dictionary<string, string> files) : ISource {
    public IEnumerable<string> Keys => files.Keys;

    public Task<Stream> OpenRead(string key) =>
        Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(files[key])));
  }

  private sealed class Released(TaskCompletionSource release) : IDisposable {
    public void Dispose() => release.TrySetResult();
  }
}
