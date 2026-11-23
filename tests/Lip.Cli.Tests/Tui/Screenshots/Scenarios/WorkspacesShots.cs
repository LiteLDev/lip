using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Core.Mcmod;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Workspaces and the Tools pages.
public sealed class WorkspacesShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Workspaces(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.Fs.Directory.Delete(Worlds.Skyblock);
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Worlds.Skyblock);

    await shot.Capture(
        "workspaces-01-list",
        Note.Callout(
            1, At.Row(CommonText.NewServer).To(At.Row(WorkspacesText.AddFolder)),
            shot.T(
                "Create a server, or add a folder that has one.",
                "新建服务器，或添加已有服务器的文件夹。", "建立伺服器，或加入已有伺服器的資料夾。")),
        Note.Callout(
            2, At.Text("● " + world.Workspace),
            shot.T(
                "The selected workspace: every page works on it.",
                "当前工作区：所有页面都针对它操作。", "目前的工作區：所有頁面都針對它操作。")),
        Note.Callout(
            3, At.Text(WorkspacesText.Missing),
            shot.T(
                "A folder that no longer exists.", "已不存在的文件夹。", "已不存在的資料夾。"),
            Side.Right));

    // The row of another workspace, and its actions.
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Remove);
    await shot.Capture(
        "workspaces-02-menu", Note.Focus(At.Panel("creative")), Note.Keys("Enter"));
  }

  /// The plan of an operation that would change nothing: the versions that the workspace
  /// has already are chosen again.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task PlanWithoutChanges(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.ServerSetup = harness =>
        new SetupOfShots(harness, world) { Result = OperationResult.Unchanged };
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + world.Workspace);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor(World.GameVersion);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Loader} ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Close} ]");
    await shot.Capture(
        "plan-02-no-changes", Note.Box(At.Text(CommonText.NothingWouldChange)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Tools(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.File("lip.toml", "format = 4\nid = \"github.com/steve/survival-pack\"\n");
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    await shot.Capture("tools-01-list");
  }
}
