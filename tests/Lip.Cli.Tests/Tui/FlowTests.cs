using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Pages.Home;
using Lip.Cli.Tui.Pages.Mods;
using Lip.Cli.Tui.Pages.Server;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Services;

namespace Lip.Cli.Tests.Tui;

/// What leads from one page of the interface to another, with the pages of lip, driven only
/// by the arrow keys, Enter, Esc, Tab and text.
public sealed class FlowTests {
  private const string Proxy = "https://proxy.test/";

  // A catalogue of one mod for a server.
  private const string Index = """
      { "mods": [ {
        "id": "github.com/acme/Alpha", "name": "Alpha", "owner": "acme",
        "description": "An economy core", "tags": ["money"], "side": ["server"],
        "platform": ["win-x64"], "stars": 7, "updated": "2026-11-01T00:00:00Z"
      } ] }
      """;

  private const string Detail = """
      { "id": "github.com/acme/Alpha", "latest": {"variant": "", "version": "1.2.0"},
        "versions": [ {"version": "1.2.0"} ] }
      """;

  private static string Catalog(string url) =>
      url == ModCatalogClient.BaseUrl + "index.json" ? Index : Detail;

  /// A file system whose working directory has a game when game is set, and nothing otherwise.
  private static MockFileSystem Workspace(bool game) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    if (game) {
      fs.AddFile(
          Path.Join(TuiHarness.DefaultRoot, ".lip", "state.json"),
          new MockFileData(TuiHarness.StateWithGame));
    }

    return fs;
  }

  /// The interface with the pages of lip, as on a system that is not Linux on x64, a
  /// catalogue of one mod, and a fake server setup.
  private static TuiHarness Start(
      out Func<FakeServerSetup> setup,
      bool game,
      Language language = Language.En,
      GithubProbe? probe = null) {
    FakeServerSetup? fake = null;
    TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      FileSystem = Workspace(game),
      Pages = ServerSetupTests.Pages(linux: false),
      Catalog = Catalog,
      GithubProbe = probe,
      ServerSetup = harness => fake = new FakeServerSetup(harness),
    });
    setup = () => fake!;
    return tui;
  }

  private static Task<PageId> Page(TuiHarness tui) => tui.Invoke(() => tui.Context.CurrentPage);

  /// Waits until the page is shown with the focus in its content area.
  private static Task Shown(TuiHarness tui, PageId page) =>
      tui.WaitUntil(
          () => tui.Context.CurrentPage == page && tui.Context.ContentFocused, $"the page {page}");

  /// The bottom bar.
  private static string LastRow(TuiHarness tui) => tui.Screen.Split('\n')[^1];

  [Fact]
  public async Task Home_SetUpTheServer_GoesThroughTheSetupOfTheServerPage_InTraditionalChinese() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, game: false, Language.ZhHant);
    await tui.WaitFor(HomeText.CreateServerDetail);
    await tui.WaitFor(HomeText.NotInstalled);

    // Act: Enter on the step that the workspace, which has no game, needs first.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    Assert.Equal(PageId.Server, await Page(tui));
    tui.Snapshot("flow-home-set-up");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);

    // Assert: the server was set up in the workspace, and the Server page is shown.
    lock (setup().Calls) {
      Assert.Equal(
          [
            "1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False",
            "1.21.50 Windows True 1.1.0 dryRun=False acceptEula=True",
          ],
          setup().Calls);
      // The versions were read, and the setup was carried out, in the session of its dry run.
      Assert.NotNull(setup().Sessions[0]);
      Assert.All(setup().Sessions, session => Assert.Same(setup().Sessions[0], session));
    }

    await Shown(tui, PageId.Server);
    Assert.Equal([tui.Root], tui.Workspaces.Read());
    tui.Snapshot("flow-home-set-up-done");

    // Esc goes back to the sidebar, and from there the Home page is one key away.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    await tui.GoTo(PageId.Home);
    await tui.WaitFor(HomeText.NextSteps);
  }

  [Fact]
  public async Task Home_SetUpTheServer_EscInTheSetup_ChangesNothing_AndStaysOnServer() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, game: true);
    await tui.WaitFor(HomeText.InstallModsDetail);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(HomeText.CreateServerDoneDetail);

    // A workspace that has a game: the setup changes its versions.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    Assert.Equal(PageId.Server, await Page(tui));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {SetupText.GameVersion} ─");

    await Shown(tui, PageId.Server);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Empty(setup().Calls);
  }

  [Fact]
  public async Task Home_ChooseAWorkspace_OpensTheWorkspacePicker() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, game: true);
    await tui.WaitFor(HomeText.InstallModsDetail);
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(HomeText.ChooseWorkspaceDetail);
    Assert.Contains("✓ 1  " + HomeText.ChooseWorkspace, tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"─ {WorkspacesText.Title} ─");
    await tui.WaitFor("● " + tui.Root);
    Assert.Equal(PageId.Home, await Page(tui));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {WorkspacesText.Title} ─");
    Assert.Empty(setup().Calls);
  }

  [Fact]
  public async Task Home_InstallMods_OpensGetMods_WithTheModsReadyForEnter() {
    using TuiHarness tui = Start(out _, game: true);
    await tui.WaitFor(HomeText.InstallModsDetail);

    // Act: the second step is selected in a workspace with a game and no mod.
    tui.Press(ConsoleKey.Enter);

    await Shown(tui, PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    await tui.WaitFor(_ => LastRow(tui).Contains("Enter Actions"), "the mods");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Snapshot("flow-home-get-mods");
    Assert.Contains("─ Alpha ─", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GetModsText.Install);

    // Esc goes back to the sidebar, on the entry of the Get mods page.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    Assert.Equal(PageId.GetMods, await Page(tui));
  }

  [Fact]
  public async Task Home_StartTheServer_StartsIt_OnTheServerPage_WithTheInputLineReady() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Servers = servers,
      });
      await tui.WaitFor(HomeText.InstallModsDetail);

      // Act: ↓ to the third step, and Enter.
      tui.Press(ConsoleKey.DownArrow);
      await tui.WaitFor(HomeText.StartServerDetail);
      tui.Press(ConsoleKey.Enter);

      await Shown(tui, PageId.Server);
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.WaitFor(_ => LastRow(tui).Contains("Enter Send"), "the input line");
      tui.Type("echo from home");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("> echo from home");
      tui.Snapshot("flow-home-start-server");

      // Esc goes back a level at a time: the output, then the sidebar. There the Home page
      // ticks the step while the server runs, and Enter on it comes back to the server.
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone("Enter Send");
      Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
      await tui.GoTo(PageId.Home);
      await tui.WaitFor($"✓ 4  {HomeText.StartServer}");
      await tui.WaitFor(HomeText.InstallModsDetail);
      tui.Press(ConsoleKey.DownArrow);
      await tui.WaitFor(HomeText.OpenServerDetail);
      tui.Press(ConsoleKey.Enter);
      await Shown(tui, PageId.Server);
      await tui.WaitFor(_ => LastRow(tui).Contains("Enter Send"), "the input line");
      tui.Type("exit 0");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("○ " + ServerText.ExitedWith(0));
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Fact]
  public async Task Home_GithubUseAProxy_AsksForItOnTheSettingsPage_AndSavesIt_InSimplifiedChinese() {
    // GitHub is reached through the proxy only.
    GithubProbe probe = new((url, _) => Task.FromResult(
        url.ToString().StartsWith(Proxy, StringComparison.Ordinal) ? 200 : 503));
    using TuiHarness tui = Start(out _, game: false, Language.ZhHans, probe);
    await tui.WaitFor(HomeText.GithubTitle);

    // Act: "Use a proxy" is selected: Enter, the URL, Enter.
    await tui.WaitFor(HomeText.GithubProxyDetail);
    tui.Snapshot("flow-github-question");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SettingsText.ProxyPlaceholder);
    Assert.Equal(PageId.Settings, await Page(tui));
    Assert.Contains($"─ {SettingsText.GithubProxy} ─", tui.Screen);
    Assert.DoesNotContain(HomeText.GithubQuestion, tui.Screen);
    tui.Snapshot("flow-github-proxy-prompt");
    tui.Type(Proxy);
    tui.Press(ConsoleKey.Enter);

    // Assert: tested, saved, and shown in the row of the proxy, which is the selected one.
    await tui.WaitFor(CommonText.Saved);
    Assert.Equal(Proxy, await tui.Config.Get(ConfigService.GithubProxyKey));
    await Shown(tui, PageId.Settings);
    await tui.WaitFor(SettingsText.GithubProxyDetail);
    Assert.Matches($@"{SettingsText.GithubProxy}\s+{Proxy}", tui.Screen);

    // The question does not come back.
    await tui.Settle();
    await Task.Delay(700, TestContext.Current.CancellationToken);
    await tui.Settle();
    Assert.DoesNotContain(HomeText.GithubTitle, tui.Screen);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task Mods_SetUpServer_StartsTheSetupOfTheServerPage() {
    using TuiHarness tui = Start(out _, game: false);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(CommonText.NoMods);
    await tui.WaitFor(CommonText.SetUpServer);

    // Act: the first action row of a workspace without a game.
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    Assert.Equal(PageId.Server, await Page(tui));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {SetupText.GameVersion} ─");
    await Shown(tui, PageId.Server);
  }

  [Fact]
  public async Task Mods_GetMods_OpensTheCatalogue() {
    using TuiHarness tui = Start(out _, game: true);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ModsText.Installed(1));
    await tui.WaitFor(ModsText.InstallFromSourceDetail);

    // Act: the action row below "Install from source…" of a workspace without mods.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ModsText.GetModsDetail);
    tui.Press(ConsoleKey.Enter);

    await Shown(tui, PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    await tui.WaitFor(_ => LastRow(tui).Contains("Enter Actions"), "the mods");
  }

  [Fact]
  public async Task Server_SetUpServer_StartsTheSetup_AndEscGoesBackAStepAtATime() {
    using TuiHarness tui = Start(out _, game: false);
    await tui.GoTo(PageId.Server);
    await tui.WaitFor(ServerText.NoServer);
    Assert.Contains($"[ {CommonText.SetUpServer} ]", tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");

    // Esc goes back one step at a time, and out of the first one to the page.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {SetupText.GameVersion} ─");
    await Shown(tui, PageId.Server);
    Assert.Contains($"[ {CommonText.SetUpServer} ]", tui.Screen);
  }

  [Fact]
  public async Task ModConfig_GetMods_OpensTheCatalogue() {
    using TuiHarness tui = Start(out _, game: true);
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor(Lip.Cli.Tui.Pages.ModConfig.ModConfigText.NoMods);
    Assert.Contains($"[ {CommonText.GetMods} ]", tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await Shown(tui, PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    await tui.WaitFor(_ => LastRow(tui).Contains("Enter Actions"), "the mods");
  }
}
