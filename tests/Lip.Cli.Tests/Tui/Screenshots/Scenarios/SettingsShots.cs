using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Core.Infrastructure;
using Semver;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Settings page, and the dialogs of the shell: a failure, and the question before lip
/// quits.
public sealed class SettingsShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Settings(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((_, _) => Task.FromResult(200));
    world.SelfUpdate = (_, checkOnly) => Task.FromResult(new SelfUpdateResult(
        checkOnly ? SelfUpdateOutcome.Available : SelfUpdateOutcome.Replaced,
        SemVersion.Parse("0.42.0")));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor(SettingsText.NotRunning);

    await shot.Capture(
        "settings-01-list",
        Note.Callout(
            1, At.Row(SettingsText.Language),
            shot.T(
                "A setting and its value: Enter changes it.", "设置项及其值：按 Enter 修改。",
                "設定項目及其值：按 Enter 修改。")),
        Note.Callout(
            2, At.Text(SettingsText.LanguageDetail),
            shot.T(
                "What the selected setting does.", "所选设置项的作用。", "所選設定項目的作用。")));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("繁體中文");
    await shot.Capture(
        "settings-02-language", Note.Focus(At.Panel(SettingsText.Language)),
        Note.Keys("↑", "↓", "Enter"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("繁體中文");

    // The GitHub proxy: its actions, then its URL.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("https://proxy.example/");
    await shot.Capture(
        "settings-03-proxy", Note.Focus(At.Panel(SettingsText.GithubProxy)),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(SettingsText.ProxyUrl);
    await tui.WaitUntilGone(SettingsText.SetProxy);

    // A newer release.
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.UpdateAvailable("0.42.0"));
    await shot.Capture(
        "settings-04-update-available", Note.Box(At.Button(SettingsText.Install)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("0.41.0 → 0.42.0");
    await shot.Capture("settings-05-update-row", Note.Box(At.Row(SettingsText.InstallUpdate)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Failure(Language language) {
    // The workspace has the game in its state file, and no file of it.
    using Shot shot = run.Start(Worlds.Survival(language, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Server);
    await tui.WaitFor($"[ {ServerText.Start} ]");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"{ServerText.StartTitle} · {CommonText.Failed}");
    await shot.Capture(
        "dialog-01-failure",
        Note.Callout(
            1, At.Text(CommonText.Error + ":"),
            shot.T(
                "What went wrong, as lip says it.", "出了什么问题（lip 的原话）。",
                "出了什麼問題（lip 的原話）。")),
        Note.Callout(
            2, At.Text(CommonText.FullReport),
            shot.T(
                "The file to attach when you report the failure.", "报告问题时要附上的文件。",
                "回報問題時要附上的檔案。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Quit(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 18));
    TuiHarness tui = shot.Tui;
    await tui.WaitFor("LeviLamina " + World.LoaderVersion);

    tui.Interrupt();
    await tui.WaitFor(CommonText.QuitQuestion);
    await shot.Capture(
        "dialog-02-quit", Note.Focus(At.Panel(CommonText.QuitTitle)),
        Note.KeysAfter(shot.T("Opened with", "打开方式", "開啟方式"), "Ctrl+C"));
  }
}
