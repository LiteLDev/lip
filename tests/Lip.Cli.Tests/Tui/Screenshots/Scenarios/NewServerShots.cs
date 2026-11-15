using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Home;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The new server wizard, from the folder to the result.
public sealed class NewServerShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Wizard(Language language) {
    // lip was started in a folder that holds no server yet.
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    World world = new(language, workspace: World.PathOf(@"D:\servers"));
    world.ServerSetup = harness => new SetupOfShots(harness, world) { Release = release.Task };
    world.Fs.AddDirectory(Path.Join(world.Workspace, "creative"));
    world.Fs.AddDirectory(Path.Join(world.Workspace, "backups"));
    using Shot shot = run.Start(world);
    // Whatever becomes of the scenario, the setup does not wait for ever.
    using IDisposable released = new Released(release);
    TuiHarness tui = shot.Tui;
    await tui.WaitFor(HomeText.CreateServerDetail);

    // The first step of the Home page starts the wizard: here, or in another folder.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Elsewhere);
    await shot.Capture(
        "new-server-01-where", Note.Box(At.Row(WorkspacesText.Elsewhere)),
        Note.Keys("↓", "Enter"));

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Cancel} ]");
    await tui.WaitUntilGone(PathPickerText.Reading);
    await shot.Capture(
        "new-server-02-folder",
        Note.Callout(
            1, At.Text(PathPickerText.Folder),
            shot.T(
                "The folder shown: the server goes here unless you open another.",
                "当前显示的文件夹：不打开别的文件夹，服务器就装在这里。",
                "目前顯示的資料夾：不開啟別的資料夾，伺服器就裝在這裡。")),
        Note.Callout(
            2, At.Button(PathPickerText.NewFolder),
            shot.T(
                "Makes a folder for the server inside the one shown.",
                "在当前文件夹里为服务器新建一个文件夹。",
                "在目前資料夾裡為伺服器建立一個資料夾。"),
            Side.Bottom),
        Note.Keys("Tab", "→", "Enter"));

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {PathPickerText.NewFolderTitle} ─");
    tui.Type("survival");
    await shot.Capture(
        "new-server-03-folder-name", Note.Focus(At.Panel(PathPickerText.NewFolderTitle)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor("1.26.60");
    await shot.Capture(
        "new-server-04-game-version",
        Note.Box(At.Text("LeviLamina ✓")),
        Note.Arrow(
            At.Text("LeviLamina ✓"),
            shot.T("Mods work on this version", "此版本可以装模组", "此版本可以裝模組"),
            Side.Right),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Loader} ─");
    await shot.Capture("new-server-05-loader", Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    await shot.Capture(
        "new-server-06-plan",
        Note.Box(At.Button(CommonText.Apply)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    await shot.Capture(
        "new-server-07-eula", Note.Box(At.Button(CommonText.Accept)), Note.Keys("Enter"));

    // The setup waits in the middle of its second download for the screenshot.
    shot.Scrub(@"(?<=│ )[|/\\-](?= Downloading)", "/");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(" 62%");
    await shot.Capture("new-server-08-progress");

    release.TrySetResult();
    await tui.WaitFor("✓ " + CommonText.Done);
    await shot.Capture("new-server-09-result", Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor("● " + Path.Join(world.Workspace, "survival"));
    await shot.Capture(
        "new-server-10-workspace",
        Note.Box(At.Row("● " + Path.Join(world.Workspace, "survival"))));
  }

  private sealed class Released(TaskCompletionSource release) : IDisposable {
    public void Dispose() => release.TrySetResult();
  }
}
