using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;
using Lip.Core.Mcmod;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The setup of a server on the Server page, from the button to the result.
public sealed class NewServerShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Wizard(Language language) {
    // lip was started in a folder that holds no server yet.
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    World world = new(language, workspace: World.PathOf(@"D:\servers\survival"));
    world.ServerSetup = harness => new SetupOfShots(harness, world) { Release = release.Task };
    using Shot shot = run.Start(world);
    // Whatever becomes of the scenario, the setup does not wait for ever.
    using IDisposable released = new Released(release);
    TuiHarness tui = shot.Tui;

    // The Server page of a folder without a server offers to set one up.
    await tui.GoTo(PageId.Server);
    await tui.WaitFor(ServerText.NoServer);
    await shot.Capture(
        "new-server-01-where",
        Note.Box(At.Button(CommonText.SetUpServer)),
        Note.Callout(
            1, At.Text(ServerText.SetUpDetail),
            shot.T(
                "The server goes into the selected workspace, which the top bar names.",
                "服务器会装进顶栏显示的当前工作区。",
                "伺服器會裝進頂端列顯示的目前工作區。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
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
    await tui.WaitFor($"─ {SetupText.Loader} ─");
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

    // The Server page now shows the server, ready to start.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor($"[ {ServerText.Start} ]");
    await shot.Capture(
        "new-server-10-workspace",
        Note.Box(At.Button(ServerText.Start)),
        Note.Callout(
            1, At.Button(SetupText.ChangeVersions),
            shot.T(
                "Sets it up again with other versions.", "用其他版本重新安装。",
                "用其他版本重新安裝。"),
            Side.Bottom));
  }

  private sealed class Released(TaskCompletionSource release) : IDisposable {
    public void Dispose() => release.TrySetResult();
  }
}
