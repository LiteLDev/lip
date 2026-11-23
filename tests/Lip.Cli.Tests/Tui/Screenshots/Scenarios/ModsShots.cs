using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Mods;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Mods page, and the plan of a workspace operation.
public sealed class ModsShots(ShotRun run) {
  private const string ScriptEngine = Catalogue.ScriptEngine + "#lua";

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ListMenuAndDetails(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // The third of the mods, which are listed by identity.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ScriptEngine + "  0.13.1");
    await shot.Capture(
        "mods-01-list",
        Note.Callout(
            1, At.Row(ModsText.InstallFromSource).To(At.Row(ModsText.UpdateAll)),
            shot.T(
                "Actions for the whole workspace.", "针对整个工作区的操作。",
                "針對整個工作區的操作。")),
        Note.Callout(
            2, At.Row(ScriptEngine),
            shot.T(
                "A mod that you installed: Space checks it, Enter opens its actions.",
                "你安装的模组：按 Space 勾选，按 Enter 打开操作菜单。",
                "你安裝的模組：按 Space 勾選，按 Enter 開啟操作選單。")),
        Note.Callout(
            3, At.Row(Catalogue.RemoteCall),
            shot.T(
                "Without a box: installed because another mod needs it.",
                "没有方框：因其他模组需要而安装。",
                "沒有方框：因其他模組需要而安裝。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    await shot.Capture("mods-02-menu", Note.Focus(At.Panel(ScriptEngine)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("README");
    await tui.WaitFor("LGPL-3.0");
    // The file of the mod is read after the dialog opens.
    await tui.WaitUntilGone(CommonText.Loading);
    await shot.Capture("mods-03-details");
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task WhileTheServerRuns(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ScriptEngine + "  0.13.1");
    await tui.SetServerRunning(true);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.Uninstall);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.StopServerFirst);
    await shot.Capture(
        "mods-04-server-running",
        Note.Box(At.Row(CommonText.StopServerFirst)),
        Note.Arrow(
            At.Text("● " + CommonText.ServerRunning),
            shot.T("The server runs", "服务器正在运行", "伺服器正在執行"), Side.Top));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Plan(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // Uninstall LegacyMoney, the second mod: its dependency, which nothing else needs, goes
    // with it.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(Catalogue.Money + "  0.16.1");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.Uninstall);
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "plan-01-changes",
        Note.Callout(
            1, At.Text("Dry run:"),
            shot.T(
                "What lip would do. Nothing has changed yet.", "lip 将要做的事。此时尚未更改任何东西。",
                "lip 將要做的事。此時尚未變更任何東西。")),
        Note.Callout(
            2, At.Button(CommonText.Apply),
            shot.T(
                "Apply carries the plan out; Cancel or Esc leaves everything as it is.",
                "「应用」执行计划；「取消」或 Esc 保持原样。",
                "「套用」執行計畫；「取消」或 Esc 保持原樣。"),
            Side.Bottom));
  }
}
