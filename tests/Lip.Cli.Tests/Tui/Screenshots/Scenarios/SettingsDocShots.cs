using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Core.Infrastructure;
using Semver;
using static Lip.Cli.Tests.Tui.Screenshots.Scenarios.ConfigPagesText;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Settings page as its page of the documentation walks through it, row by row: what
/// SettingsShots does not show of the proxy, the restart policy, the cache, the updates, the
/// daemon and the language.
public sealed class SettingsDocShots(ShotRun run) {
  /// Proxies of no one: one that does not answer as GitHub does, and one that does.
  private const string DeadProxy = "https://old-proxy.example.com/";
  private const string LiveProxy = "https://proxy.example.com/";

  private const string Newer = "0.42.0";

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Proxy(Language language) {
    // GitHub answers directly, and the proxy when the scenario lets it.
    TaskCompletionSource<int> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((url, token) =>
        url.ToString().StartsWith(LiveProxy, StringComparison.Ordinal)
            ? answer.Task.WaitAsync(token)
            : Task.FromResult(200));
    using Shot shot = run.Start(world);
    using IDisposable answered = new Answered(answer);
    shot.Scrub(Spinner(), "/");
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor(SettingsText.NotRunning);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    await shot.Capture(
        "settings-06-proxy-menu",
        Note.Focus(At.Panel(SettingsText.GithubProxy)),
        Note.Callout(
            1, At.Row(SettingsText.SetProxy),
            shot.T(
                "Asks for the URL of a proxy, and tests it.", "输入代理网址并测试。",
                "輸入代理伺服器網址並測試。")),
        Note.Callout(
            2, At.Row(CommonText.ConnectDirectly),
            shot.T(
                "No proxy, and lip does not ask about GitHub again.", "不用代理，lip 也不再询问 GitHub 的事。",
                "不用代理伺服器，lip 也不再詢問 GitHub 的事。")),
        Note.Callout(
            3, At.Row(SettingsText.Reset),
            shot.T(
                "Back to \"Not set\": dimmed here, since nothing is set.",
                "恢复为“未设置”；现在本来就没有设置，所以是灰色的。",
                "還原為「未設定」；現在本來就沒有設定，所以是灰色的。")));

    // A proxy, while it is tested and once it is saved.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type(LiveProxy);
    await shot.Capture(
        "settings-07-proxy-url",
        Note.Focus(At.Panel(SettingsText.GithubProxy)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SettingsText.TestingProxy} ─");
    await shot.Capture(
        "settings-08-testing",
        Note.Focus(At.Panel(SettingsText.TestingProxy)),
        Note.KeysAfter(shot.T("Stops the test", "中止测试", "中止測試"), "Esc"));

    answer.TrySetResult(200);
    await tui.WaitFor(CommonText.Saved);
    await tui.WaitUntilGone($"─ {SettingsText.TestingProxy} ─");
    await shot.Capture(
        "settings-09-proxy-saved",
        Note.Box(At.Row(SettingsText.GithubProxy)),
        Note.Box(At.Text(CommonText.Saved)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Direct(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((_, _) => Task.FromResult(200));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor(SettingsText.NotRunning);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    // The menu, which names the action as the row names the value in Traditional Chinese, is
    // gone, and the change is saved.
    await tui.WaitUntilGone(SettingsText.SetProxy);
    await tui.WaitUntilGone(SettingsText.NotSet);
    await tui.WaitFor(CommonText.Saved);
    await shot.Capture(
        "settings-12-direct",
        Note.Box(At.Row(SettingsText.GithubProxy)),
        Note.Box(At.Text(CommonText.Saved)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ProxyProblems(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((url, _) => Task.FromResult(
        url.ToString().StartsWith(DeadProxy, StringComparison.Ordinal) ? 502 : 200));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor(SettingsText.NotRunning);

    // What is typed must be a URL.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("old-proxy.example.com");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.NotAUrl);
    await shot.Capture(
        "settings-10-not-a-url",
        Note.Focus(At.Panel(SettingsText.GithubProxy)),
        Note.Box(At.Text(SettingsText.NotAUrl)));

    // A proxy that does not reach GitHub.
    tui.Press(ConsoleKey.Backspace, "old-proxy.example.com".Length);
    tui.Type(DeadProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyFailed);
    await shot.Capture(
        "settings-11-proxy-failed",
        Note.Focus(At.Panel(SettingsText.ProxyFailed)),
        Note.Callout(
            1, At.Button(SettingsText.EnterAnother),
            shot.T(
                "Back to the URL, to correct it.", "回到网址，修改后再试。", "回到網址，修改後再試。"),
            Side.Bottom),
        Note.Callout(
            2, At.Button(SettingsText.SaveAnyway),
            shot.T(
                "Saves the proxy although the test failed.", "测试失败也照样保存这个代理。",
                "測試失敗也照樣儲存這個代理伺服器。"),
            Side.Bottom),
        Note.Callout(
            3, At.Button(CommonText.Cancel),
            shot.T(
                "Changes nothing.", "什么也不改。", "什麼也不改。"),
            Side.Bottom));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Rows(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((_, _) => Task.FromResult(200));
    world.SelfUpdate = (_, _) => Task.FromResult(
        new SelfUpdateResult(SelfUpdateOutcome.UpToDate, SemVersion.Parse(World.Version)));
    world.ManagedServers = true;
    world.DaemonStatus = _ => Task.FromResult<DaemonState?>(
        new DaemonState(World.Version, 4120, 1));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor("PID 4120");

    // Auto restart.
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[x] " + SettingsText.On);
    await shot.Capture(
        "settings-13-auto-restart",
        Note.Box(At.Row(CommonText.AutoRestart)),
        Note.Callout(
            1, At.Text(SettingsText.AutoRestartDetail),
            shot.T(
                "Of the selected workspace only: each workspace has its own.",
                "只针对当前工作区；每个工作区各有各的设置。", "只針對目前的工作區；每個工作區各有各的設定。")),
        Note.Keys("Space"));

    // Clear cache.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SettingsText.ClearCacheDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ClearCacheQuestion);
    await shot.Capture(
        "settings-14-clear-cache",
        Note.Focus(At.Panel(SettingsText.ClearCache)),
        Note.Box(At.Button(SettingsText.Clear)));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + SettingsText.CacheCleared);
    await shot.Capture("settings-15-cache-cleared", Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + SettingsText.CacheCleared);

    // The daemon.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(SettingsText.DaemonCommands(1));
    await shot.Capture(
        "settings-17-daemon",
        Note.Callout(
            1, At.Text(CommonText.ServerRunning + " · " + World.Version),
            shot.T(
                "A daemon runs, of this version of lip.", "守护进程正在运行，以及它的 lip 版本。",
                "常駐程式正在執行，以及它的 lip 版本。")),
        Note.Callout(
            2, At.Text("PID 4120"),
            shot.T("Its process ID.", "它的进程 ID。", "它的處理程序 ID。"),
            Side.Right),
        Note.Callout(
            3, At.Text(SettingsText.DaemonCommands(1)),
            shot.T(
                "The commands that it carries out now: this interface is one.",
                "它正在执行的命令数；这个界面也算一个。", "它正在執行的命令數；這個介面也算一個。")),
        Note.Callout(
            4, At.Text(SettingsText.DaemonServers(0)),
            shot.T(
                "The servers that it runs now, of all workspaces.", "它正在运行的服务器数（所有工作区）。",
                "它正在執行的伺服器數（所有工作區）。"),
            Side.Right),
        Note.KeysAfter(shot.T("Asks again", "重新查询", "重新查詢"), "Enter"));

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SettingsText.AboutDetail);
    await shot.Capture("settings-18-about", Note.Box(At.Row(SettingsText.About)));

    // No newer release.
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"{World.Version} · {SettingsText.UpToDate}");
    await shot.Capture(
        "settings-16-up-to-date", Note.Box(At.Row(SettingsText.CheckUpdates)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Update(Language language) {
    using Shot shot = run.Start(Updating(language, SelfUpdateOutcome.Replaced));
    TuiHarness tui = shot.Tui;
    await ToInstallUpdate(tui);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.InstallQuestion(Newer));
    await shot.Capture(
        "settings-19-install-question",
        Note.Focus(At.Panel(SettingsText.InstallUpdate)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Head(SettingsText.Installed(Newer)));
    await tui.WaitFor($"[ {CommonText.Close} ]");
    await shot.Capture(
        "settings-20-installed", Note.Focus(At.Panel(SettingsText.InstallUpdate)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"{Newer} · {SettingsText.RestartLip}");
    await shot.Capture(
        "settings-21-restart-lip", Note.Box(At.Row(SettingsText.CheckUpdates)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task UpdateByInstaller(Language language) {
    using Shot shot = run.Start(Updating(language, SelfUpdateOutcome.InstallerStarted));
    TuiHarness tui = shot.Tui;
    await ToInstallUpdate(tui);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.InstallQuestion(Newer));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Head(SettingsText.InstallerStarted(Newer)));
    await tui.WaitFor($"[ {CommonText.Close} ]");
    await shot.Capture(
        "settings-22-installer", Note.Focus(At.Panel(SettingsText.InstallUpdate)));
  }

  /// The language of the interface changes under the eyes of the user: the English interface
  /// goes to Simplified Chinese, and the two Chinese ones to English, as the Chinese page
  /// says, which the Traditional Chinese page is made from.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task LanguageChanged(Language language) {
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((_, _) => Task.FromResult(200));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotRunning);

    // The list: Follow system, English, 简体中文, 繁體中文.
    Language other = language == Language.En ? Language.ZhHans : Language.En;
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("繁體中文");
    tui.Press(ConsoleKey.DownArrow, other == Language.En ? 1 : 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.Language == other, "the other language");
    await tui.WaitFor(SettingsText.LanguageDetail);
    await shot.Capture(
        "settings-23-language-changed",
        Note.Callout(
            1, At.Row(SettingsText.Language),
            shot.T(
                "The language that you chose.", "你选择的语言。", "你選擇的語言。")),
        Note.Callout(
            2, At.Panel(SettingsText.Title),
            shot.T(
                "Every text of the interface is in it at once.", "界面上的文字立刻全部换成这种语言。",
                "介面上的文字立刻全部換成這種語言。"),
            Side.Top));
  }

  /// A world in which lip has a newer release, and installing it ends as given.
  private static World Updating(Language language, SelfUpdateOutcome installed) {
    World world = Worlds.Survival(language, height: 22);
    world.GithubProbe = new GithubProbe((_, _) => Task.FromResult(200));
    world.SelfUpdate = (_, checkOnly) => Task.FromResult(new SelfUpdateResult(
        checkOnly ? SelfUpdateOutcome.Available : installed, SemVersion.Parse(Newer)));
    return world;
  }

  /// Checks for updates, answers "Later", and goes to the row that installs the update.
  private static async Task ToInstallUpdate(TuiHarness tui) {
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotRunning);
    tui.Press(ConsoleKey.DownArrow, 4);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.UpdateAvailable(Newer));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor($"{World.Version} → {Newer}");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SettingsText.InstallUpdateDetail);
  }

  private sealed class Answered(TaskCompletionSource<int> answer) : IDisposable {
    public void Dispose() => answer.TrySetResult(200);
  }
}
