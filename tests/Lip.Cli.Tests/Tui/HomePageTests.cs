using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Home;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Moq;
using TuiContext = Lip.Cli.Tui.AppContext;

namespace Lip.Cli.Tests.Tui;

/// The Home page of the interface, driven by the keys of a fake terminal.
public sealed class HomePageTests {
  /// A lock file with a game, a loader, a mod that the user installed and one of its
  /// dependencies.
  private const string StateWithMods = """
      { "format": 4, "side": "server", "platform": "win-x64", "packages": [
        { "manifest": { "format": 4, "id": "github.com/LiteLDev/bds", "version": "1.21.0",
            "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "", "explicit": true, "ref": "github.com/LiteLDev/bds@1.21.0",
          "files": [] },
        { "manifest": { "format": 4, "id": "github.com/LiteLDev/LeviLamina", "version": "1.1.0",
            "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "", "explicit": true, "ref": "github.com/LiteLDev/LeviLamina@1.1.0",
          "files": [] },
        { "manifest": { "format": 4, "id": "github.com/acme/lib", "version": "0.3.0",
            "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "", "explicit": false, "ref": "github.com/acme/lib@0.3.0",
          "files": [] },
        { "manifest": { "format": 4, "id": "github.com/acme/alpha", "version": "2.0.0",
            "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "", "explicit": true, "ref": "github.com/acme/alpha@2.0.0",
          "files": [] } ] }
      """;

  /// A page that stands for one of another unit: it shows the requests it gets.
  private sealed class OtherPage(TuiContext context, PageId id) : Page(context) {
    private string _request = "none";

    /// Whether the page has unsaved changes, which the user is asked to discard.
    public bool Dirty { get; set; }

    public override string Title => id.ToString();

    public override bool CanLeave => !Dirty;

    public override void ConfirmLeave(Action leave) =>
        Context.Confirm(
            CommonText.UnsavedTitle, "Discard the changes?", CommonText.Discard, () => {
              Dirty = false;
              leave();
            });

    public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
        screen.Write(area.X, area.Y, $"page {id}, request {_request}");

    public override void OnRequest(string request) => _request = request;
  }

  /// The Home page among pages that only show what they are asked.
  private static IReadOnlyList<PageEntry> Pages(Dictionary<PageId, OtherPage>? created = null) => [
    new(PageId.Home, "⌂", context => new HomePage(context)),
    .. new[] { PageId.Server, PageId.GetMods, PageId.Workspaces, PageId.Settings }.Select(
        id => new PageEntry(id, "•", context => {
          OtherPage page = new(context, id);
          created?.Add(id, page);
          return page;
        })),
  ];

  /// A file system whose working directory has the lock file.
  private static MockFileSystem Workspace(string? state) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    if (state is not null) {
      fs.AddFile(Path.Join(TuiHarness.DefaultRoot, ".lip", "state.json"), new MockFileData(state));
    }

    return fs;
  }

  /// A reachability check that gets the status once release completes, and counts its requests.
  private sealed class Probe(int status, Task? release = null) {
    public int Requests;

    public GithubProbe Check => new(async (_, _) => {
      Interlocked.Increment(ref Requests);
      if (release is not null) {
        await release;
      }

      return status;
    });
  }

  private static TuiHarness Start(
      string? state = null,
      Language language = Language.En,
      Probe? probe = null,
      MockFileSystem? fs = null,
      int width = 120,
      int height = 36,
      Dictionary<PageId, OtherPage>? pages = null) =>
      TuiHarness.Start(new TuiHarnessOptions {
        Language = language,
        Width = width,
        Height = height,
        FileSystem = fs ?? Workspace(state),
        GithubProbe = probe?.Check,
        Pages = Pages(pages),
      });

  [Theory]
  [InlineData(Language.En, "Home")]
  [InlineData(Language.ZhHans, "主页")]
  [InlineData(Language.ZhHant, "首頁")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Home);

    Assert.Equal(title, tui.Text(() => HomeText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Fact]
  public async Task Start_FocusesTheSteps_SoThatEnterCreatesAServerInAnEmptyWorkspace() {
    using TuiHarness tui = Start();
    await tui.WaitFor(HomeText.CreateServerDetail);
    await tui.WaitFor(HomeText.NotInstalled);

    // The overview says that nothing is there, and no step is ticked.
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
    Assert.Contains(tui.Root, tui.Screen);
    Assert.Contains(HomeText.NotInstalled, tui.Screen);
    Assert.Contains(CommonText.ServerStopped, tui.Screen);
    Assert.DoesNotContain("✓", tui.Screen);
    Assert.Contains($"↑↓ {CommonText.HintMove.Text}  Enter {CommonText.HintOpen.Text}", tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Workspaces, request {PageRequest.NewServer}");
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
  }

  [Fact]
  public async Task Steps_WithAServerButNoMods_PreselectInstallMods_AndEnterOpensGetMods() {
    using TuiHarness tui = Start(TuiHarness.StateWithGame);
    await tui.WaitFor(HomeText.InstallModsDetail);

    Assert.Contains($"✓ 1  {HomeText.CreateServer}", tui.Screen);
    Assert.DoesNotContain($"✓ 2  {HomeText.InstallMods}", tui.Screen);
    Assert.Contains("1.21.0", tui.Screen);
    Assert.Contains(HomeText.None, tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("page GetMods, request none");
  }

  [Fact]
  public async Task Steps_WithAServerAndMods_PreselectStart_AndEnterStartsTheServer() {
    using TuiHarness tui = Start(StateWithMods);
    await tui.WaitFor(HomeText.StartServerDetail);

    // The dependency is not counted as a mod of the user.
    Assert.Contains("LeviLamina 1.1.0", tui.Screen);
    Assert.Matches($@"{HomeText.Mods}\s+1\b", tui.Screen);
    Assert.Contains($"✓ 1  {HomeText.CreateServer}", tui.Screen);
    Assert.Contains($"✓ 2  {HomeText.InstallMods}", tui.Screen);
    Assert.DoesNotContain("✓ 3", tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Server, request {PageRequest.StartServer}");
  }

  [Fact]
  public async Task Overview_KnowsTheGameOfTheLinuxBuild() {
    // The identity of that game has a variant, and it is still the game, not a mod.
    string state = TuiHarness.StateWithGame
        .Replace("\"variant\": \"\"", "\"variant\": \"linux\"")
        .Replace("\"variants\": { \"\":", "\"variants\": { \"linux\":")
        .Replace("bds@", "bds#linux@");
    using TuiHarness tui = Start(state);

    await tui.WaitFor(HomeText.InstallModsDetail);

    Assert.Contains($"✓ 1  {HomeText.CreateServer}", tui.Screen);
    Assert.Matches($@"{HomeText.Game}\s+1\.21\.0", tui.Screen);
    Assert.Matches($@"{HomeText.Mods}\s+0\b", tui.Screen);
  }

  [Fact]
  public async Task Overview_OfAStateFileThatCannotBeRead_StaysUnknown_AndRefusesNoStep() {
    using TuiHarness tui = Start("not a state file");
    // Why it was not read is said in the bottom bar: it names the file.
    await tui.WaitFor(".lip");
    tui.Snapshot("home-unreadable");

    // Nothing is said to be missing, and no step is ticked.
    Assert.DoesNotContain(HomeText.NotInstalled, tui.Screen);
    Assert.DoesNotContain("✓", tui.Screen);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(HomeText.StartServerDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Server, request {PageRequest.StartServer}");
  }

  [Fact]
  public async Task Steps_AreWalkedWithTheArrows_AndADoneStepStillOpensItsFlow() {
    using TuiHarness tui = Start(StateWithMods);
    await tui.WaitFor(HomeText.StartServerDetail);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(HomeText.InstallModsDetail);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(HomeText.CreateServerDoneDetail);
    tui.Press(ConsoleKey.UpArrow);
    await tui.Settle();
    Assert.Contains(HomeText.CreateServerDoneDetail, tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Workspaces, request {PageRequest.NewServer}");
  }

  [Fact]
  public async Task StartStep_WithoutAServer_SendsTheUserBackToTheFirstStep() {
    using TuiHarness tui = Start();
    await tui.WaitFor(HomeText.NotInstalled);
    await tui.WaitFor(HomeText.CreateServerDetail);

    tui.Press(ConsoleKey.DownArrow, 2);
    // The third step says what it needs once both keys are handled.
    await tui.WaitFor(HomeText.NeedServer);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(HomeText.CreateServerDetail);
    Assert.Contains(HomeText.NeedServer, tui.Screen);
    Assert.Equal(PageId.Home, await tui.Invoke(() => tui.Context.CurrentPage));
  }

  [Fact]
  public async Task Overview_FollowsTheSelectedWorkspace() {
    MockFileSystem fs = Workspace(null);
    string other = Path.Join(Path.GetPathRoot(TuiHarness.DefaultRoot), "other");
    fs.AddFile(Path.Join(other, ".lip", "state.json"), new MockFileData(StateWithMods));
    using TuiHarness tui = Start(fs: fs);
    await tui.WaitFor(HomeText.CreateServerDetail);

    string? error = null;
    Assert.True(await tui.Invoke(() => tui.Context.AddWorkspace(other, out error)), error);

    await tui.WaitFor(HomeText.StartServerDetail);
    Assert.Contains("LeviLamina 1.1.0", tui.Screen);
    Assert.DoesNotContain(HomeText.NotInstalled, tui.Screen);
  }

  [Fact]
  public async Task Steps_WhileTheServerRuns_TickStart_AndEnterOpensTheServerPage() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Pages = Pages(),
      });
      await tui.WaitFor(HomeText.InstallModsDetail);
      await tui.Invoke(() => tui.Context.StartServer("Start server"));
      await tui.WaitFor($"✓ 3  {HomeText.StartServer}");
      // It says Starting until lip run has started the server.
      await tui.WaitFor(
          screen => Regex.IsMatch(screen, $@"{HomeText.Server}\s+● {CommonText.ServerRunning}"),
          "the running server");

      tui.Press(ConsoleKey.DownArrow);
      await tui.WaitFor(HomeText.OpenServerDetail);
      tui.Press(ConsoleKey.Enter);

      await tui.WaitFor("page Server, request none");
      tui.Servers.Find(tui.Root)!.Send("exit");
      await tui.WaitUntilGone($"● {CommonText.ServerRunning}");
    }
    finally {
      TestClients.Remove(root);
    }
  }

  [Fact]
  public async Task Github_Reached_AsksNothing() {
    Probe probe = new(200);
    using TuiHarness tui = Start(probe: probe);
    await tui.WaitFor(HomeText.CreateServerDetail);
    await tui.WaitFor(_ => Volatile.Read(ref probe.Requests) == 1, "the check");

    await Ticks(tui);

    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Theory]
  [InlineData("")]
  [InlineData("https://proxy.example/")]
  public async Task Github_WithAConfiguredProxy_IsNotChecked(string proxy) {
    MockFileSystem fs = Workspace(null);
    await new ConfigService(fs, Mock.Of<IUserInteraction>())
        .Set(ConfigService.GithubProxyKey, proxy);
    Probe probe = new(503);
    using TuiHarness tui = Start(probe: probe, fs: fs);
    await tui.WaitFor(HomeText.CreateServerDetail);

    await Ticks(tui);

    Assert.Equal(0, Volatile.Read(ref probe.Requests));
    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
  }

  [Fact]
  public async Task Github_NotReached_PreselectsAProxy_SoThatEnterAloneLeadsToIt() {
    using TuiHarness tui = Start(probe: new Probe(503), language: Language.ZhHant);
    await tui.WaitFor(HomeText.GithubTitle);

    // The answer that a user who does not reach GitHub needs is the selected one.
    await tui.WaitFor(HomeText.GithubProxyDetail);
    Assert.DoesNotContain(HomeText.GithubLaterDetail, tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Settings, request {PageRequest.GithubProxy}");
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
  }

  [Fact]
  public async Task Github_NotReached_Later_ChangesNothing_AndIsAskedOnce() {
    Probe probe = new(503);
    using TuiHarness tui = Start(probe: probe);
    await tui.WaitFor(HomeText.GithubTitle);

    // The question says why, and offers the three answers.
    Assert.Contains("503", tui.Screen);
    Assert.Contains(HomeText.GithubProxy, tui.Screen);
    Assert.Contains(CommonText.ConnectDirectly, tui.Screen);
    Assert.Contains(CommonText.Later, tui.Screen);
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));

    // ↑ from the first answer goes round to the last one, "later".
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(HomeText.GithubLaterDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(HomeText.GithubTitle);
    await Ticks(tui);

    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
    Assert.Equal(1, Volatile.Read(ref probe.Requests));
    Assert.Equal(PageId.Home, await tui.Invoke(() => tui.Context.CurrentPage));
  }

  [Fact]
  public async Task Github_NotReached_Esc_AnswersLater() {
    using TuiHarness tui = Start(probe: new Probe(503));
    await tui.WaitFor(HomeText.GithubTitle);

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(HomeText.GithubTitle);
    await Ticks(tui);

    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
    Assert.Contains(HomeText.CreateServerDetail, tui.Screen);
  }

  [Fact]
  public async Task Github_NotReached_Direct_SetsTheProxyToTheEmptyString() {
    using TuiHarness tui = Start(probe: new Probe(503));
    await tui.WaitFor(HomeText.GithubTitle);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(HomeText.GithubDirectDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(HomeText.GithubDirectSaved);
    Assert.Equal("", await tui.Config.Get(ConfigService.GithubProxyKey));
    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task Github_NotReached_Proxy_HandsOverToTheSettingsPage() {
    using TuiHarness tui = Start(probe: new Probe(503));
    await tui.WaitFor(HomeText.GithubTitle);

    await tui.WaitFor(HomeText.GithubProxyDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Settings, request {PageRequest.GithubProxy}");
    await Ticks(tui);
    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
    Assert.Null(await tui.Config.Get(ConfigService.GithubProxyKey));
  }

  [Fact]
  public async Task Github_Proxy_IsAskedAgain_WhenAPageWithUnsavedChangesKeepsTheUser() {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Dictionary<PageId, OtherPage> pages = [];
    using TuiHarness tui = Start(probe: new Probe(503, release.Task), pages: pages);
    await tui.GoTo(PageId.Workspaces);
    await tui.Invoke(() => pages[PageId.Workspaces].Dirty = true);
    release.SetResult();
    await tui.WaitFor(HomeText.GithubTitle);

    // The user asks for a proxy, and then stays on the page with the changes.
    await tui.WaitFor(HomeText.GithubProxyDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Discard the changes?");
    tui.Press(ConsoleKey.Escape);

    // The question is not lost: this time the user discards the changes.
    await tui.WaitFor(HomeText.GithubTitle);
    Assert.Contains("page Workspaces", tui.Screen);
    await tui.WaitFor(HomeText.GithubProxyDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Discard the changes?");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"page Settings, request {PageRequest.GithubProxy}");
    await Ticks(tui);
    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
  }

  [Fact]
  public async Task Github_NotReached_WaitsUntilNoOtherQuestionIsOpen() {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Probe probe = new(503, release.Task);
    using TuiHarness tui = Start(probe: probe);
    await tui.WaitFor(HomeText.CreateServerDetail);
    tui.Interrupt();
    await tui.WaitFor(CommonText.QuitQuestion);

    // The check fails while the interface asks whether to quit.
    release.SetResult();
    await Ticks(tui);
    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
    Assert.Contains(CommonText.QuitQuestion, tui.Screen);

    tui.Press(ConsoleKey.Escape);

    await tui.WaitFor(HomeText.GithubTitle);
    Assert.DoesNotContain(CommonText.QuitQuestion, tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Flow_InEveryLanguageAndSize_GoesFromTheQuestionToTheSteps(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        TuiHarness.StateWithGame, language, new Probe(503), width: width, height: height);
    string direct = language switch {
      Language.ZhHans => "直接连接",
      Language.ZhHant => "直接連線",
      _ => "Connect directly",
    };
    string install = language switch {
      Language.ZhHans => "安装模组",
      Language.ZhHant => "安裝模組",
      _ => "Install mods",
    };

    // The GitHub question, answered with the arrows and Enter.
    await tui.WaitFor(HomeText.GithubTitle);
    await tui.WaitFor(direct);
    // The overview is read in the background, and shows "…" until it is.
    await tui.WaitFor(
        screen => !string.Join('\n', screen.Split('\n')[1..^1]).Contains('…'), "the overview");
    await tui.WaitFor(HomeText.GithubProxyDetail);
    tui.Snapshot("home-github");
    AssertFits(tui);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(HomeText.GithubLaterDetail);
    tui.Snapshot("home-github-later");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(HomeText.GithubDirectDetail);
    tui.Snapshot("home-github-direct");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(HomeText.GithubDirectSaved);
    Assert.Equal("", await tui.Config.Get(ConfigService.GithubProxyKey));

    // The steps: the first is done, the second is selected.
    await tui.WaitFor($"✓ 1  {HomeText.CreateServer}");
    await tui.WaitFor(HomeText.InstallModsDetail);
    Assert.Contains($"2  {install}", tui.Screen);
    tui.Snapshot("home-steps");
    AssertFits(tui);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(HomeText.StartServerDetail);
    tui.Snapshot("home-steps-start");
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(HomeText.CreateServerDoneDetail);
    tui.Snapshot("home-steps-done");
    AssertFits(tui);

    // The sidebar has the focus: the page is narrower on a small terminal.
    tui.Press(ConsoleKey.Escape);
    await tui.Settle();
    tui.Snapshot("home-sidebar");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(HomeText.InstallModsDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("page GetMods, request none");
  }

  /// No row of the screen is cut by the right edge of the content box: each ends with the
  /// border, or is one of the two bars.
  private static void AssertFits(TuiHarness tui) {
    string[] rows = tui.Screen.Split('\n');
    Assert.All(rows[1..^1], row => Assert.Matches("[│┐┘]$", row));
    Assert.DoesNotContain("…", string.Join('\n', rows[1..^1]));
  }

  /// Waits for more than a tick of the interface, in which the Home page would have asked.
  private static async Task Ticks(TuiHarness tui) {
    await tui.Settle();
    await Task.Delay(700, TestContext.Current.CancellationToken);
    await tui.Settle();
  }
}
