using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Home;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Home page, and the interface on a small terminal.
public sealed class HomeShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Empty(Language language) {
    using Shot shot = run.Start(Worlds.Empty(language, height: 22));
    await shot.Tui.WaitFor(HomeText.CreateServerDetail);
    await shot.Tui.WaitFor(HomeText.NotInstalled);

    await shot.Capture(
        "home-01-empty",
        Note.Callout(
            1, At.Text($"─ {HomeText.Title} ─"),
            shot.T(
                "The page that is shown. The sidebar at the left lists every page.",
                "当前显示的页面。左侧的侧边栏列出所有页面。",
                "目前顯示的頁面。左側的側邊欄列出所有頁面。"),
            Side.Right),
        Note.Callout(
            2, At.Row(HomeText.CreateServer),
            shot.T(
                "The next step: Enter starts it.", "下一步：按 Enter 开始。",
                "下一步：按 Enter 開始。")),
        Note.Callout(
            3, At.Text("Esc " + CommonText.HintBack.Text),
            shot.T(
                "The keys that work now.", "当前可用的按键。", "目前可用的按鍵。"),
            Side.Right),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task AllStepsDone(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 22));
    await shot.Tui.WaitFor("LeviLamina " + World.LoaderVersion);
    await shot.Tui.SetServerRunning(true);
    await shot.Tui.WaitFor("✓ 3");

    await shot.Capture(
        "home-02-all-done",
        Note.Box(At.Text("✓ 1").To(At.Text("✓ 3"))),
        Note.Arrow(
            At.Text("● " + CommonText.ServerRunning),
            shot.T("The server runs", "服务器正在运行", "伺服器正在執行"), Side.Top));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Narrow(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, 60, 16));
    await shot.Tui.WaitFor("LeviLamina " + World.LoaderVersion);

    await shot.Capture("narrow-01-home");

    // Esc goes to the sidebar, which shows the names of the pages while it has the focus.
    shot.Tui.Press(ConsoleKey.Escape);
    await shot.Tui.WaitUntil(() => !shot.Tui.Context.ContentFocused, "the sidebar");
    await shot.Capture("narrow-02-sidebar", Note.Keys("Esc"));
  }
}
