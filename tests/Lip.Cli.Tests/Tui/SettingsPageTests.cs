using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;
using Semver;

namespace Lip.Cli.Tests.Tui;

/// The Settings page of the interface, driven by the keys of a fake terminal.
public sealed class SettingsPageTests {
  private const string Proxy = "https://proxy.test/";

  // A check of GitHub that only a proxy whose URL holds "bad" fails.
  private static readonly GithubProbe _probe = new((url, _) =>
      Task.FromResult(url.ToString().Contains("bad", StringComparison.Ordinal) ? 500 : 200));

  [Theory]
  [InlineData(Language.En, "Settings")]
  [InlineData(Language.ZhHans, "设置")]
  [InlineData(Language.ZhHant, "設定")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Settings);

    Assert.Equal(title, tui.Text(() => SettingsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Fact]
  public async Task Rows_ShowTheirValues_AndTheSelectedOneItsSentence() {
    using TuiHarness tui = TuiHarness.Start();

    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor(SettingsText.NotRunning);
    await tui.WaitFor($"[ ] {SettingsText.Off}");

    Assert.Matches($@"{SettingsText.Language}\s+{SettingsText.FollowSystem}", tui.Screen);
    Assert.Matches($@"{SettingsText.GithubProxy}\s+{SettingsText.NotSet}", tui.Screen);
    Assert.Matches($@"{SettingsText.CheckUpdates}\s+1\.2\.3", tui.Screen);
    Assert.Matches($@"{SettingsText.About}\s+lip 1\.2\.3", tui.Screen);
    Assert.Contains(SettingsText.ClearCache, tui.Screen);
    Assert.DoesNotContain(SettingsText.InstallUpdate, tui.Screen);
    Assert.Contains(SettingsText.LanguageDetail, tui.Screen);
    Assert.DoesNotContain(SettingsText.GithubProxyDetail, tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("A URL prefix that lip puts before GitHub URLs");
    Assert.DoesNotContain(SettingsText.LanguageDetail, tui.Screen);
  }

  [Fact]
  public async Task Hints_ListTheKeysOfTheSelectedRow() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"Enter {CommonText.HintActions.Text}");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"Enter/Space {CommonText.HintToggle.Text}");

    // About does nothing: no Enter in the bottom bar, and Enter opens nothing.
    tui.Press(ConsoleKey.End);
    await tui.WaitUntilGone("Enter");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
  }

  [Fact]
  public async Task Language_IsChosenFromAList_ShownAtOnce_AndConfigured() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("繁體中文");
    Assert.Contains("English", tui.Screen);
    Assert.Contains("简体中文", tui.Screen);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("─ 设置 ─");
    await tui.WaitFor(screen => screen.Contains("语言") && screen.Contains("简体中文"));
    await Configured(tui, ConfigService.LanguageKey, "zh-Hans");

    // The dialog opens on the language in use; the first option follows the system again.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("跟随系统");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 設定 ─");
    await Configured(tui, ConfigService.LanguageKey, "zh-Hant");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("跟隨系統");
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Settings ─");
    await tui.WaitFor("Follow system");
    await Configured(tui, ConfigService.LanguageKey, null);
  }

  [Fact]
  public async Task Language_Escape_ChangesNothing() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("繁體中文");
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("繁體中文");

    Assert.Contains("─ Settings ─", tui.Screen);
    Assert.Null(await tui.Config.Get(ConfigService.LanguageKey));
  }

  [Fact]
  public async Task Proxy_ThatReachesGithub_IsSaved() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { GithubProbe = _probe });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    Assert.Contains(SettingsText.ConnectDirectly, tui.Screen);
    Assert.Contains(SettingsText.Reset, tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    Assert.Contains(SettingsText.ProxyPlaceholder, tui.Screen);

    // An empty proxy is not taken.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);

    tui.Type(Proxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.Saved);
    await Configured(tui, ConfigService.GithubProxyKey, Proxy);
    Assert.Matches($@"{SettingsText.GithubProxy}\s+{Proxy}", tui.Screen);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task Proxy_ThatFails_IsEnteredAgain_SavedAnyway_OrDropped() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { GithubProbe = _probe });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    tui.Press(ConsoleKey.DownArrow);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("https://bad.test/");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyFailed);
    Assert.Contains("GitHub answered with the HTTP status 500.", tui.Screen);
    Assert.Contains($"[ {SettingsText.EnterAnother} ]", tui.Screen);
    Assert.Contains($"[ {SettingsText.SaveAnyway} ]", tui.Screen);
    Assert.Contains($"[ {CommonText.Cancel} ]", tui.Screen);

    // Enter another: the prompt comes back with what was entered.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    Assert.Contains("https://bad.test/", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyFailed);

    // Esc is Cancel: nothing is saved.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(SettingsText.ProxyFailed);
    await tui.Settle();
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
    Assert.Contains(SettingsText.NotSet, tui.Screen);

    // Save anyway.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("https://bad.test/");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyFailed);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.Saved);
    await Configured(tui, ConfigService.GithubProxyKey, "https://bad.test/");
  }

  [Fact]
  public async Task Proxy_ConnectDirectly_SetsTheEmptyValue_AndResetRemovesIt() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { GithubProbe = _probe });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    tui.Press(ConsoleKey.DownArrow);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ConnectDirectly);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.Saved);
    await Configured(tui, ConfigService.GithubProxyKey, "");
    await tui.WaitFor(screen => !screen.Contains(SettingsText.NotSet));
    Assert.Matches($@"{SettingsText.GithubProxy}\s+{SettingsText.Direct}", tui.Screen);

    // Connect directly is what is configured, so ↓ from Set proxy… goes on to Reset.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.Reset);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.NotSet);
    await Configured(tui, ConfigService.GithubProxyKey, null);
  }

  [Fact]
  public async Task Proxy_WithoutACheck_IsSavedAsEntered() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    tui.Press(ConsoleKey.DownArrow);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type(Proxy);
    tui.Press(ConsoleKey.Enter);

    await Configured(tui, ConfigService.GithubProxyKey, Proxy);
  }

  [Fact]
  public async Task Proxy_ThatIsNoUri_IsNotTaken() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    tui.Press(ConsoleKey.DownArrow);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("not a uri");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(SettingsText.NotAUrl);
    Assert.Contains(SettingsText.ProxyUrl, tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(SettingsText.ProxyUrl);
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
    Assert.Contains(SettingsText.NotSet, tui.Screen);
  }

  [Fact]
  public async Task Proxy_WhoseTestIsCancelled_IsAskedForAgain() {
    TaskCompletionSource<int> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      GithubProbe = new GithubProbe((_, token) => answer.Task.WaitAsync(token)),
    });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type(Proxy);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.TestingProxy);

    tui.Press(ConsoleKey.Escape);

    await tui.WaitFor(SettingsText.ProxyUrl);
    Assert.Contains(Proxy, tui.Screen);
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
  }

  [Fact]
  public async Task ProxyRequest_OfAnotherPage_OpensThePrompt_InTraditionalChinese() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = Language.ZhHant,
      GithubProbe = _probe,
    });
    await tui.Settle();

    await tui.Invoke(() => tui.Context.Navigate(PageId.Settings, PageRequest.GithubProxy));
    await tui.WaitFor("代理伺服器網址");
    tui.Snapshot("settings-proxy-prompt");
    tui.Type(Proxy);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("已儲存。");
    await Configured(tui, ConfigService.GithubProxyKey, Proxy);
    Assert.Equal(PageId.Settings, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Matches($@"GitHub 代理伺服器\s+{Proxy}", tui.Screen);
    // The row of the proxy is the selected one.
    Assert.Contains("GitHub 連線緩慢或受阻時", tui.Screen);
  }

  [Fact]
  public async Task AutoRestart_IsSwitchedBySpaceAndEnter_InTheWorkspaceConfiguration() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor($"[ ] {SettingsText.Off}");
    tui.Press(ConsoleKey.DownArrow, 2);

    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor($"[x] {SettingsText.On}");
    await Configured(tui, ConfigService.RestartKey, ConfigService.RestartOnFailure, true);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ ] {SettingsText.Off}");
    await Configured(tui, ConfigService.RestartKey, ConfigService.RestartNever, true);

    // Switched faster than it is written, the last state is the one configured.
    tui.Press(ConsoleKey.Spacebar, 5);
    await tui.WaitFor($"[x] {SettingsText.On}");
    await Configured(tui, ConfigService.RestartKey, ConfigService.RestartOnFailure, true);
    await tui.Settle();
    Assert.Contains($"[x] {SettingsText.On}", tui.Screen);
  }

  [Fact]
  public async Task ClearCache_AsksFirst_ThenDeletesTheCache_InSimplifiedChinese() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    string cached = Path.Join(LipDirectories.Cache, "archive.zip");
    fs.AddFile(cached, new MockFileData("zip"));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = Language.ZhHans,
      FileSystem = fs,
    });
    await tui.GoTo(PageId.Settings);
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor("删除已下载的文件，不影响工作区和设置。");

    // Esc keeps the cache.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("要删除所有已下载的文件吗？");
    Assert.Contains("[ 清理 ]", tui.Screen);
    Assert.Contains("[ 取消 ]", tui.Screen);
    tui.Snapshot("settings-clear-cache");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("要删除所有已下载的文件吗？");
    Assert.True(fs.FileExists(cached));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("要删除所有已下载的文件吗？");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("缓存已清理。");
    Assert.False(fs.FileExists(cached));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("缓存已清理。");
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task Updates_WithoutANewerRelease_SayUpToDate() {
    List<bool> checks = [];
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      SelfUpdate = (_, checkOnly) => {
        lock (checks) {
          checks.Add(checkOnly);
        }

        return Task.FromResult(new SelfUpdateResult(SelfUpdateOutcome.UpToDate, null));
      },
    });
    await tui.GoTo(PageId.Settings);
    tui.Press(ConsoleKey.DownArrow, 4);
    await tui.WaitFor(SettingsText.CheckUpdatesDetail);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"1.2.3 · {SettingsText.UpToDate}");
    await tui.Settle();

    Assert.Equal([true], checks);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.DoesNotContain(SettingsText.InstallUpdate, tui.Screen);
  }

  [Fact]
  public async Task Updates_WithANewerRelease_OfferToInstallIt_AndTellToRestart() {
    List<bool> checks = [];
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      SelfUpdate = (_, checkOnly) => {
        lock (checks) {
          checks.Add(checkOnly);
        }

        return Task.FromResult(new SelfUpdateResult(
            checkOnly ? SelfUpdateOutcome.Available : SelfUpdateOutcome.Replaced,
            SemVersion.Parse("1.3.0")));
      },
    });
    await tui.GoTo(PageId.Settings);
    tui.Press(ConsoleKey.DownArrow, 4);

    // Later: nothing is installed, and Install update is a row from now on.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.UpdateAvailable("1.3.0"));
    Assert.Contains($"[ {SettingsText.Install} ]", tui.Screen);
    Assert.Contains($"[ {CommonText.Later} ]", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(SettingsText.UpdateAvailable("1.3.0"));
    await tui.WaitFor("1.2.3 → 1.3.0");
    Assert.Matches($@"{SettingsText.InstallUpdate}\s+1\.3\.0", tui.Screen);
    Assert.Equal([true], checks);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SettingsText.InstallUpdateDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.InstallQuestion("1.3.0"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("lip 1.3.0 is installed. Restart lip to use it.");
    Assert.Equal([true, false], checks);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("lip 1.3.0 is installed.");

    // The row is gone, and the one that checks says what to do.
    await tui.WaitFor($"1.3.0 · {SettingsText.RestartLip}");
    Assert.DoesNotContain(SettingsText.InstallUpdate, tui.Screen);
    Assert.Contains(SettingsText.CheckUpdatesDetail, tui.Screen);

    // lip that runs is still the old one: Enter says so again instead of checking again.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("lip 1.3.0 is installed. Restart lip to use it.");
    Assert.Equal([true, false], checks);
  }

  [Fact]
  public async Task Updates_InstalledByAnInstaller_TellToQuit() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      SelfUpdate = (_, checkOnly) => Task.FromResult(new SelfUpdateResult(
          checkOnly ? SelfUpdateOutcome.Available : SelfUpdateOutcome.InstallerStarted,
          SemVersion.Parse("1.3.0"))),
    });
    await tui.GoTo(PageId.Settings);
    tui.Press(ConsoleKey.DownArrow, 4);

    // Install, straight from the question of the check.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.UpdateAvailable("1.3.0"));
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("The installer of lip 1.3.0 is running. Quit lip so that it can finish.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"1.3.0 · {SettingsText.InstallerRunning}");
  }

  [Fact]
  public async Task Updates_ThatFail_ShowWhy() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      SelfUpdate = (_, _) => Task.FromException<SelfUpdateResult>(
          new SelfUpdateException("No release of lip was found.")),
    });
    await tui.GoTo(PageId.Settings);
    tui.Press(ConsoleKey.DownArrow, 4);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("No release of lip was found.");
    Assert.Contains($"{SettingsText.CheckUpdates} · {CommonText.Failed}", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("No release of lip was found.");

    Assert.Matches($@"{SettingsText.CheckUpdates}\s+1\.2\.3\s", tui.Screen);
    Assert.DoesNotContain(SettingsText.UpToDate, tui.Screen);
  }

  [Fact]
  public async Task Daemon_ShowsItsStatus_AndEnterAsksAgain() {
    int asked = 0;
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      ManagedServers = true,
      DaemonStatus = _ => Task.FromResult<DaemonState?>(
          new DaemonState("1.2.3", 4242, Interlocked.Increment(ref asked))),
    });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor($"{CommonText.ServerRunning} · 1.2.3 · PID 4242");
    Assert.DoesNotContain(SettingsText.DaemonCommands(1), tui.Screen);

    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(SettingsText.DaemonDetail);
    await tui.WaitFor($"{SettingsText.DaemonCommands(1)} · {SettingsText.DaemonServers(0)}");
    await tui.WaitFor($"Enter {SettingsText.Refresh}");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.DaemonCommands(2));
    Assert.Contains($"{CommonText.ServerRunning} · 1.2.3 · PID 4242", tui.Screen);
  }

  [Fact]
  public async Task Daemon_ThatDoesNotListen_IsNotRunning_AndNothingStopsIt() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotRunning);

    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(SettingsText.DaemonDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();

    await tui.WaitFor(SettingsText.NotRunning);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task Workspace_Changed_ReadsItsRestartPolicyAgain() {
    using TuiHarness tui = TuiHarness.Start();
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddDirectory(other);
    await new ConfigService(tui.Fs, Mock.Of<IUserInteraction>(), () => other).Set(
        ConfigService.RestartKey, ConfigService.RestartOnFailure, workspace: true);
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor($"[ ] {SettingsText.Off}");

    await tui.Invoke(() => {
      Assert.True(tui.Context.AddWorkspace(other, out _));
    });

    await tui.WaitFor($"[x] {SettingsText.On}");
  }

  [Fact]
  public async Task Configuration_ThatIsBroken_ReadsAsEmpty_AndSwitchingRewritesIt() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(
        Path.Join(TuiHarness.DefaultRoot, ".lip", "config.toml"), new MockFileData("restart = 1"));
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = fs });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor($"[ ] {SettingsText.Off}");

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);

    await tui.WaitFor($"[x] {SettingsText.On}");
    await Configured(tui, ConfigService.RestartKey, ConfigService.RestartOnFailure, true);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  /// The frames of the page for visual review (LIP_TUI_DUMP), in every language, on a large
  /// and on the smallest terminal: every row, the details and the hints fit.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Frames_FitTheTerminal(Language language, int width, int height) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      GithubProbe = _probe,
      ManagedServers = true,
      DaemonStatus = _ => Task.FromResult<DaemonState?>(new DaemonState("1.2.3", 4242, 2)),
      SelfUpdate = (_, checkOnly) => Task.FromResult(new SelfUpdateResult(
          checkOnly ? SelfUpdateOutcome.Available : SelfUpdateOutcome.Replaced,
          SemVersion.Parse("1.3.0"))),
    });
    await tui.GoTo(PageId.Settings);
    await tui.WaitFor(SettingsText.NotSet);
    await tui.WaitFor(CommonText.ServerRunning);
    tui.Snapshot("settings-1-language");
    foreach (string label in (string[])[
      SettingsText.Language, SettingsText.GithubProxy, SettingsText.AutoRestart,
      SettingsText.ClearCache, SettingsText.CheckUpdates, SettingsText.Daemon,
      SettingsText.About,
    ]) {
      Assert.Contains(label, tui.Screen);
    }

    Assert.Contains(SettingsText.LanguageDetail, tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("繁體中文");
    tui.Snapshot("settings-2-language-choice");
    tui.Press(ConsoleKey.Escape);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.SetProxy);
    tui.Snapshot("settings-3-proxy-menu");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyUrl);
    tui.Type("https://bad.test/a/long/path/of/a/proxy/that/does/not/fit/the/row/");
    await tui.Settle();
    tui.Snapshot("settings-4-proxy-prompt");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyFailed);
    tui.Snapshot("settings-5-proxy-failed");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.Saved);
    tui.Snapshot("settings-6-proxy-saved");

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor($"[x] {SettingsText.On}");
    tui.Snapshot("settings-7-restart");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SettingsText.Clear);
    tui.Snapshot("settings-8-cache");

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {SettingsText.Install} ]");
    tui.Snapshot("settings-9-update-available");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {SettingsText.Install} ]");
    tui.Press(ConsoleKey.DownArrow);
    await tui.Settle();
    tui.Snapshot("settings-10-install-row");
    Assert.Contains(SettingsText.InstallUpdate, tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"Enter {SettingsText.Refresh}");
    tui.Snapshot("settings-11-daemon");
    Assert.Contains(SettingsText.DaemonCommands(2), tui.Screen);

    // With one row more, the list of the smallest terminal scrolls to its last row.
    tui.Press(ConsoleKey.DownArrow);
    await tui.Settle();
    tui.Snapshot("settings-12-about");
    Assert.Matches($@"{SettingsText.About}\s+lip 1\.2\.3", tui.Screen);
    Assert.Contains(SettingsText.AboutDetail, tui.Screen);

    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.InstallQuestion("1.3.0"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Close} ]");
    tui.Snapshot("settings-13-installed");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.RestartLip);
    tui.Snapshot("settings-14-restart-lip");

    // The sidebar has the focus: on a narrow terminal the page is narrower still.
    tui.Press(ConsoleKey.Escape);
    await tui.Settle();
    tui.Snapshot("settings-15-sidebar");
  }

  /// Waits until the configuration key has the value, null for none.
  private static async Task Configured(
      TuiHarness tui, string key, string? value, bool workspace = false) {
    for (int i = 0; i < 400 && await tui.Config.Get(key, workspace) != value; i++) {
      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Equal(value, await tui.Config.Get(key, workspace));
  }
}
