using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Home;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Home page, for docs/lip/interface/home.md of liteldev.github.io: the states of its steps
/// that HomeShots does not show, and the question about GitHub with each of its answers.
public sealed class HomeDocShots(ShotRun run) {
  /// A check of GitHub that fails as a network that does not reach it fails.
  private static GithubProbe Unreachable => new((_, _) => Task.FromException<int>(
      new HttpRequestException(
          "A connection attempt failed because the connected party did not properly respond"
              + " after a period of time. (github.com:443)")));

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ServerCreated(Language language) {
    using Shot shot = run.Start(Worlds.Server(language, height: 20));
    await shot.Tui.WaitFor("LeviLamina " + World.LoaderVersion);
    await shot.Tui.WaitFor(HomeText.InstallModsDetail);

    await shot.Capture(
        "home-03-server-created",
        Note.Callout(
            1, At.Text(HomeText.Workspace).To(At.Text(CommonText.ServerStopped)),
            shot.T(
                "The overview: what this folder holds now.", "概览：这个文件夹里现在有什么。",
                "概覽：這個資料夾裡現在有什麼。")),
        Note.Callout(
            2, At.Text("✓ 1"),
            shot.T("A step that is done.", "已完成的步骤。", "已完成的步驟。")),
        Note.Callout(
            3, At.Row(HomeText.InstallMods),
            shot.T(
                "The next step is highlighted: Enter opens it.", "下一步已高亮：按 Enter 打开。",
                "下一步已反白：按 Enter 開啟。"),
            Side.Right),
        Note.Callout(
            4, At.Text(HomeText.InstallModsDetail),
            shot.T(
                "What Enter does on the highlighted step.", "在高亮的步骤上按 Enter 会做什么。",
                "在反白的步驟上按 Enter 會做什麼。")),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ModsInstalled(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 20));
    await shot.Tui.WaitFor("LeviLamina " + World.LoaderVersion);
    await shot.Tui.WaitFor(HomeText.StartServerDetail);

    await shot.Capture(
        "home-04-mods-installed",
        Note.Box(At.Text("✓ 1").To(At.Text("✓ 3"))),
        Note.Box(At.Row(HomeText.StartServer)),
        Note.Keys("Enter"));

    // A step that is done can still be opened.
    shot.Tui.Press(ConsoleKey.UpArrow, 2);
    await shot.Tui.WaitFor(HomeText.CreateServerDoneDetail);
    await shot.Capture(
        "home-05-step-done",
        Note.Box(At.Row(HomeText.CreateServer)),
        Note.Box(At.Text(HomeText.CreateServerDoneDetail)),
        Note.Keys("↑", "↑"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task StartWithoutAServer(Language language) {
    using Shot shot = run.Start(Worlds.Empty(language, height: 20));
    await shot.Tui.WaitFor(HomeText.NotInstalled);
    await shot.Tui.WaitFor(HomeText.CreateServerDetail);

    // The last step, in a folder without a server: lip says what is missing, and goes back
    // to the step that sets the server up.
    shot.Tui.Press(ConsoleKey.DownArrow, 2);
    await shot.Tui.WaitFor(HomeText.NeedServer);
    shot.Tui.Press(ConsoleKey.Enter);
    await shot.Tui.WaitFor(HomeText.CreateServerDetail);
    await shot.Tui.WaitFor(HomeText.NeedServer);
    await shot.Capture(
        "home-06-need-server",
        Note.Callout(
            1, At.Text(HomeText.NeedServer),
            shot.T(
                "A message at the right of the bottom line, for a few seconds.",
                "底部一行右侧的提示，几秒后消失。", "底部一列右側的提示，幾秒後消失。"),
            Side.Top),
        Note.Callout(
            2, At.Row(HomeText.CreateServer),
            shot.T(
                "The highlight is back on the step that sets up the server.",
                "高亮回到了安装服务器的那一步。", "反白回到了安裝伺服器的那一步。")),
        Note.KeysAfter(shot.T("After", "按下之后", "按下之後"), "↓", "↓", "Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Github(Language language) {
    World world = Worlds.Survival(language, height: 20);
    world.GithubProbe = Unreachable;
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor(HomeText.GithubQuestion);

    await shot.Capture(
        "home-07-github",
        Note.Callout(
            1, At.Text("Could not connect to GitHub"),
            shot.T(
                "Why lip did not reach GitHub.", "lip 连不上 GitHub 的原因。",
                "lip 連不上 GitHub 的原因。")),
        Note.Callout(
            2, At.Row(HomeText.GithubProxy).To(At.Row(CommonText.Later)),
            shot.T(
                "The three answers. The first is highlighted.", "三种回答，默认高亮第一个。",
                "三種回答，預設反白第一個。")),
        Note.Callout(
            3, At.Text(HomeText.GithubProxyDetail),
            shot.T(
                "What the highlighted answer does.", "高亮的回答会做什么。", "反白的回答會做什麼。")),
        Note.Keys("↑", "↓", "Enter"));

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(HomeText.GithubDirectDetail);
    await shot.Capture(
        "home-08-github-direct",
        Note.Focus(At.Panel(HomeText.GithubTitle)),
        Note.Box(At.Row(CommonText.ConnectDirectly)),
        Note.Box(At.Text(HomeText.GithubDirectDetail)),
        Note.Keys("↓", "Enter"));

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(HomeText.GithubLaterDetail);
    await shot.Capture(
        "home-09-github-later",
        Note.Focus(At.Panel(HomeText.GithubTitle)),
        Note.Box(At.Row(CommonText.Later)),
        Note.Box(At.Text(HomeText.GithubLaterDetail)),
        Note.Keys("↓", "↓", "Enter"));

    // Connect directly: saved, and said in the bottom line.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(HomeText.GithubDirectSaved);
    // The page is no longer behind the question: it shows what the workspace has.
    await tui.WaitFor("LeviLamina " + World.LoaderVersion);
    await tui.WaitFor(HomeText.StartServerDetail);
    await shot.Capture(
        "home-10-github-direct-saved",
        Note.Arrow(
            At.Text(HomeText.GithubDirectSaved),
            shot.T("Saved: lip does not ask again", "已保存：lip 不会再问", "已儲存：lip 不會再問"),
            Side.Bottom));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task GithubProxy(Language language) {
    World world = Worlds.Survival(language, height: 20);
    world.GithubProbe = Unreachable;
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor(HomeText.GithubQuestion);

    // Use a proxy: the Settings page asks for its URL.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("https://proxy.example.com/");
    await tui.WaitFor("https://proxy.example.com/");
    await shot.Capture(
        "home-11-github-proxy",
        Note.Focus(At.Panel(SettingsText.GithubProxy)),
        Note.Arrow(
            At.Text($"─ {SettingsText.Title} ─"),
            shot.T("lip went to the Settings page", "lip 跳到了“设置”页面", "lip 跳到了「設定」頁面"),
            Side.Top),
        Note.Keys("Enter"));
  }
}
