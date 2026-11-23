using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tests.Tui;

/// The setup of the server on the Server page (protocol/lip-tui.md, "Server setup"), driven by
/// the keys of a fake terminal: in a workspace without a game, and to change the versions of
/// one that has one.
public sealed class ServerSetupTests {
  private static readonly string Root = TuiHarness.DefaultRoot;

  /// A file system whose workspace list names the working directory, which has the lock file
  /// state, or no game when it is null.
  private static MockFileSystem Files(string? state = null) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), Root);
    fs.AddDirectory(Root);
    if (state is not null) {
      fs.AddFile(Path.Join(Root, ".lip", "state.json"), new MockFileData(state));
    }

    fs.AddFile(
        Path.Join(OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName),
        new MockFileData(System.Text.Json.JsonSerializer.Serialize(new[] { Root })));
    return fs;
  }

  /// The file of LeviLamina in the catalogue: its newest version is for the game 1.21.50, and
  /// none is for 1.21.40.
  private const string Loader = """
      { "id": "github.com/LiteLDev/LeviLamina", "versions": [ {
        "version": "1.1.0",
        "variants": { "": [ {
          "side": "server", "platform": "win-x64",
          "deps": {"github.com/LiteLDev/bds": "1.21.50"}
        } ] }
      } ] }
      """;

  /// The pages of lip, with a Server page that takes lip to run on Linux on x64, or not,
  /// whatever system the tests run on.
  internal static IReadOnlyList<PageEntry> Pages(bool linux) => [
    .. PageRegistry.All.Select(entry => entry.Id == PageId.Server
        ? entry with { Create = context => new ServerPage(context, () => linux) }
        : entry),
  ];

  /// The interface on a system that is not Linux on x64 unless linux is set, with a catalogue
  /// that tells which games have a LeviLamina unless another is given, and the fake server
  /// setup, or what wrap makes of it.
  private static TuiHarness Start(
      out Func<FakeServerSetup> setup,
      MockFileSystem? fs = null,
      Language language = Language.En,
      int width = 120,
      int height = 36,
      bool linux = false,
      Func<string, string>? catalog = null,
      Func<FakeServerSetup, IServerSetup>? wrap = null) {
    FakeServerSetup? fake = null;
    TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      FileSystem = fs ?? Files(),
      Pages = Pages(linux),
      Catalog = catalog ?? (_ => Loader),
      ServerSetup = harness => {
        fake = new FakeServerSetup(harness);
        return wrap?.Invoke(fake) ?? fake;
      },
    });
    setup = () => fake!;
    return tui;
  }

  /// On the Server page: the button that sets up a workspace without a game, or that changes
  /// the versions of one that has one; Enter on it.
  private static async Task OpenSetup(TuiHarness tui) {
    await tui.GoTo(PageId.Server);
    string setUp = $"[ {tui.Text(() => CommonText.SetUpServer)} ]";
    string change = $"[ {tui.Text(() => SetupText.ChangeVersions)} ]";
    await tui.WaitFor(screen => screen.Contains(setUp) || screen.Contains(change), "a button");
    if (tui.Screen.Contains(change)) {
      tui.Press(ConsoleKey.RightArrow, 2);
      await tui.WaitFor(tui.Text(() => ServerText.ChangeVersionsDetail));
    }

    tui.Press(ConsoleKey.Enter);
  }

  private static List<OperationSession?> Sessions(FakeServerSetup setup) {
    lock (setup.Calls) {
      return [.. setup.Sessions];
    }
  }

  private static List<string> Calls(FakeServerSetup setup) {
    lock (setup.Calls) {
      return [.. setup.Calls];
    }
  }

  /// Applies the plan, accepts the license, and closes the result.
  private static async Task ApplyAndAccept(TuiHarness tui) {
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
  }

  /// The text without the line breaks, the borders and the spaces that wrapping it in a
  /// dialog puts into it.
  private static string Flat(string text) =>
      Regex.Replace(text, @"[\s│]+", "");

  /// A lock file with a game of a version and a variant, and LeviLamina of a version unless
  /// that is null.
  private static string State(string game, string variant, string? loader) {
    static string Package(string id, string variant, string version) => $$"""
        { "manifest": { "format": 4, "id": "{{id}}", "version": "{{version}}",
            "variants": { "{{variant}}": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "{{variant}}", "explicit": true,
          "ref": "{{id}}{{(variant.Length > 0 ? "#" + variant : "")}}@{{version}}", "files": [] }
        """;
    string[] packages = [
      Package("github.com/LiteLDev/bds", variant, game),
      .. loader is null ? (string[])[] : [Package(GameSetup.LoaderRepository, "", loader)],
    ];
    return $$"""
        { "format": 4, "side": "server", "platform": "win-x64",
          "packages": [ {{string.Join(", ", packages)}} ] }
        """;
  }

  /// A server setup that installs the game it is asked for: once it is carried out, the
  /// workspace has the game of the request.
  private sealed class InstallingSetup(FakeServerSetup setup, MockFileSystem fs) : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        setup.LoaderVersions(client, game, cancellationToken, session);

    public async Task<OperationResult> Setup(
        LipClient client, SetupRequest request, OperationRun run) {
      OperationResult result = await setup.Setup(client, request, run);
      if (!run.DryRun) {
        fs.AddFile(
            Path.Join(Root, ".lip", "state.json"),
            new MockFileData(State(
                request.Game.ToString(), request.Build == GameBuild.Linux ? "linux" : "",
                request.LoaderVersion?.ToString())));
      }

      return result;
    }
  }

  /// The setup of a workspace without a game, from the Server page, which then shows the
  /// controls of the server.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 80, 24)]
  [InlineData(Language.ZhHans, 60, 16)]
  public async Task Setup_OfAWorkspaceWithoutAGame_SetsItUp_AndThePageShowsTheServer(
      Language language, int width, int height) {
    MockFileSystem fs = Files();
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, fs, language, width, height,
        wrap: fake => new InstallingSetup(fake, fs));
    await tui.GoTo(PageId.Server);

    // The page says that there is no server, and offers to set one up.
    await tui.WaitFor(tui.Text(() => ServerText.NoServer));
    Assert.Contains($"[ {tui.Text(() => CommonText.SetUpServer)} ]", tui.Screen);
    Assert.Contains(Flat(tui.Text(() => ServerText.SetUpDetail)), Flat(tui.Screen));
    tui.Snapshot("setup-none");
    tui.Press(ConsoleKey.Enter);

    // The game version: the newest first, each with whether LeviLamina supports it. Lip does
    // not run on Linux here, so no build is named.
    await tui.WaitFor($"─ {tui.Text(() => SetupText.GameVersion)} ─");
    Assert.Contains(
        $"1.21.50  LeviLamina ✓   {tui.Text(() => SetupText.Newest)}", tui.Screen);
    Assert.Contains($"1.21.40  {tui.Text(() => SetupText.HasNoLoader)}", tui.Screen);
    Assert.DoesNotContain("linux", tui.Screen);
    tui.Snapshot("setup-game");
    tui.Press(ConsoleKey.Enter);

    // The loader: the build is not asked for, since windows is the only one.
    await tui.WaitFor($"─ {tui.Text(() => SetupText.Loader)} ─");
    Assert.Contains($"LeviLamina 1.1.0  {tui.Text(() => SetupText.Newest)}", tui.Screen);
    Assert.Contains("LeviLamina 1.0.0", tui.Screen);
    Assert.Contains(tui.Text(() => SetupText.NoLoader), tui.Screen);
    tui.Snapshot("setup-loader");
    tui.Press(ConsoleKey.Enter);

    // The plan, the license, the result.
    await tui.WaitFor($"[ {tui.Text(() => CommonText.Apply)} ] [ {tui.Text(() => CommonText.Cancel)} ]");
    Assert.Contains($"─ {tui.Text(() => SetupText.SetUpTitle)} ─", tui.Screen);
    tui.Snapshot("setup-plan");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    tui.Snapshot("setup-license");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + tui.Text(() => CommonText.Done));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + tui.Text(() => CommonText.Done));

    Assert.Equal(
        [
          "1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False",
          "1.21.50 Windows True 1.1.0 dryRun=False acceptEula=True",
        ],
        Calls(setup()));
    Assert.Equal(Root, tui.WorkspaceOf(await tui.Invoke(() => tui.Context.Client)));
    Assert.Equal([Root], tui.Workspaces.Read());

    // The game versions, the loaders, the dry run and the setup were all in one session.
    List<OperationSession?> sessions = Sessions(setup());
    Assert.Equal(4, sessions.Count);
    Assert.NotNull(sessions[0]);
    Assert.All(sessions, session => Assert.Same(sessions[0], session));

    // The page now shows the server, with its controls.
    await tui.WaitFor($"[ {tui.Text(() => ServerText.Start)} ]");
    Assert.Contains($"[ {tui.Text(() => SetupText.ChangeVersions)} ]", tui.Screen);
    Assert.DoesNotContain(tui.Text(() => ServerText.NoServer), tui.Screen);
    tui.Snapshot("setup-done");
  }

  /// Changing the versions of a workspace that has a game, from the button of the Server page.
  [Fact]
  public async Task ChangeVersions_FromTheServerPage_InTraditionalChinese_SetsItUpAgain() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(State("1.21.50", "", "1.1.0")), Language.ZhHant);
    await tui.GoTo(PageId.Server);
    await tui.WaitFor("[ 變更版本… ]");
    tui.Press(ConsoleKey.RightArrow, 2);
    await tui.WaitFor("更換遊戲或載入器。");
    Assert.Contains("Enter 選擇", tui.Screen);
    tui.Snapshot("change-button");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("─ 遊戲版本 ─");
    Assert.Contains("1.21.50  LeviLamina ✓   最新  已安裝", tui.Screen);
    Assert.Contains("1.21.40  無 LeviLamina", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("模組需要 LeviLamina，而此版本沒有。");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 載入器 ─");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ 套用 ] [ 取消 ]");
    Assert.Contains("─ 安裝伺服器 ─", tui.Screen);
    await ApplyAndAccept(tui);

    Assert.Equal(
        [
          "1.21.40 Windows True 1.0.0 dryRun=True acceptEula=False",
          "1.21.40 Windows True 1.0.0 dryRun=False acceptEula=True",
        ],
        Calls(setup()));
    List<OperationSession?> sessions = Sessions(setup());
    Assert.Equal(4, sessions.Count);
    Assert.All(sessions, session => Assert.Same(sessions[0], session));
  }

  /// While the server runs, the button is disabled and says why; so is a request.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 80, 24)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ChangeVersions_WhileTheServerRuns_IsDisabled_AndSaysWhy(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(TuiHarness.StateWithGame), language, width,
        height);
    await tui.GoTo(PageId.Server);
    await tui.SetServerRunning(true);
    string change = $"[ {tui.Text(() => SetupText.ChangeVersions)} ]";
    await tui.WaitFor(change);
    Assert.Equal(Theme.Muted, await tui.StyleOf(change));
    tui.Press(ConsoleKey.RightArrow, 2);
    await tui.Settle();
    tui.Snapshot("change-running");
    if (width >= 80) {
      Assert.Contains(tui.Text(() => CommonText.StopServerFirst), tui.Screen);
    }

    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.DoesNotContain($"─ {tui.Text(() => SetupText.GameVersion)} ─", tui.Screen);

    // Asked for by another page, the setup says that the server runs.
    await tui.Invoke(() => tui.Context.Navigate(PageId.Server, PageRequest.SetUpServer));
    await tui.WaitFor($"─ {tui.Text(() => SetupText.SetUpTitle)} ─");
    Assert.Contains(tui.Text(() => CommonText.StopServerFirst), tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone($"─ {tui.Text(() => SetupText.SetUpTitle)} ─");
    Assert.Empty(Sessions(setup()));
  }

  [Fact]
  public async Task Setup_OnLinux_OffersTheLinuxBuild_WhichHasNoLoader() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, linux: true);
    await OpenSetup(tui);

    await tui.WaitFor("─ Game version ─");
    // A game version says its build only when it lacks one that others have.
    Assert.Contains("1.21.50  LeviLamina ✓   newest", tui.Screen);
    Assert.Contains("1.21.40  No LeviLamina  windows only", tui.Screen);
    tui.Snapshot("setup-game-linux");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Build ─");
    Assert.Contains("Supports LeviLamina and its mods", tui.Screen);
    tui.Snapshot("setup-build");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("No loader; for Linux on x64 only.");
    tui.Press(ConsoleKey.Enter);

    await ApplyAndAccept(tui);
    Assert.Equal(0, setup().LoaderLists);
    Assert.Equal(
        [
          "1.21.50 Linux False  dryRun=True acceptEula=False",
          "1.21.50 Linux False  dryRun=False acceptEula=True",
        ],
        Calls(setup()));
  }

  [Fact]
  public async Task Setup_OnLinux_AGameWithOneBuild_IsNotAskedForTheBuild() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, linux: true);
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SetupText.HasNoLoaderDetail);
    tui.Press(ConsoleKey.Enter);

    // The game 1.21.40 has the build windows only.
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("A plain server, without mods.");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(["1.21.40 Windows False  dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task Setup_NotOnLinux_ListsNoGameThatHasOnlyTheLinuxBuild() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, wrap: fake => new LinuxOnlyGame(fake));
    await OpenSetup(tui);

    await tui.WaitFor("─ Game version ─");
    Assert.DoesNotContain("1.21.60", tui.Screen);
    Assert.DoesNotContain("linux", tui.Screen);
    // The first version that is listed is the newest one that can be set up here.
    Assert.Contains("1.21.50  LeviLamina ✓   newest", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(["1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task Setup_OnLinux_AGameThatHasOnlyTheLinuxBuild_IsSetUpWithoutMoreChoices() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, linux: true, wrap: fake => new LinuxOnlyGame(fake));
    await OpenSetup(tui);

    await tui.WaitFor("─ Game version ─");
    Assert.Contains("1.21.60  No LeviLamina  linux only  newest", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Equal(0, setup().LoaderLists);
    Assert.Equal(["1.21.60 Linux False  dryRun=True acceptEula=False"], Calls(setup()));
  }

  /// A server setup whose newest game has the build linux only.
  private sealed class LinuxOnlyGame(FakeServerSetup setup) : IServerSetup {
    public async Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) => [
          new GameVersion(SemVersion.Parse("1.21.60"), Windows: false, Linux: true),
          .. await setup.GameVersions(client, session),
        ];

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        setup.LoaderVersions(client, game, cancellationToken, session);

    public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
        setup.Setup(client, request, run);
  }

  [Fact]
  public async Task Setup_WhenTheCatalogueCannotBeRead_ListsTheGamesWithoutTheColumn() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup,
        catalog: _ => throw new InvalidOperationException("The catalogue is down."));
    await OpenSetup(tui);

    await tui.WaitFor("─ Game version ─");
    Assert.Contains("1.21.50  newest", tui.Screen);
    Assert.DoesNotContain("LeviLamina", tui.Screen);
    tui.Snapshot("setup-game-no-catalogue");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Single(Calls(setup()));
  }

  [Fact]
  public async Task Setup_WhenTheCatalogueIsSlow_ListsTheGamesWithoutWaitingForIt() {
    using ManualResetEventSlim answered = new();
    try {
      using TuiHarness tui = Start(out Func<FakeServerSetup> setup, catalog: _ => {
        answered.Wait(TestContext.Current.CancellationToken);
        return Loader;
      });
      await OpenSetup(tui);

      // A few seconds after the game versions are read, they are listed as they are.
      await tui.WaitFor("─ Game version ─");
      Assert.Contains("1.21.50  newest", tui.Screen);
      Assert.DoesNotContain("LeviLamina", tui.Screen);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("─ Loader ─");

      // Once the catalogue has answered, the list of the next setup has its column.
      answered.Set();
      tui.Press(ConsoleKey.Escape);
      await tui.WaitFor("─ Game version ─");
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone("─ Game version ─");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("─ Game version ─");
      await tui.WaitFor("1.21.50  LeviLamina ✓   newest");
      Assert.Empty(Calls(setup()));
    }
    finally {
      answered.Set();
    }
  }

  [Fact]
  public async Task Setup_ReadsTheCatalogueOnce_AndEverythingInOneSession() {
    List<string> urls = [];
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup, catalog: url => {
      lock (urls) {
        urls.Add(url);
      }

      return Loader;
    });
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Back to the game, and on again: the loaders are asked for again, in the same session,
    // and the catalogue is not.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await ApplyAndAccept(tui);

    lock (urls) {
      Assert.Single(urls, ModCatalogClient.DetailUrl(GameSetup.LoaderRepository));
    }

    List<OperationSession?> sessions = Sessions(setup());
    Assert.Equal(5, sessions.Count);
    Assert.NotNull(sessions[0]);
    Assert.All(sessions, session => Assert.Same(sessions[0], session));
  }

  [Fact]
  public async Task Setup_EscGoesBackAStep_AndOutOfTheFirstChangesNothing() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SetupText.HasNoLoaderDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Back to the game, whose row is still the chosen one, and out of the setup.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    Assert.Contains(SetupText.HasNoLoaderDetail, tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Game version ─");
    await tui.Settle();

    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Empty(Calls(setup()));
    Assert.Contains($"[ {CommonText.SetUpServer} ]", tui.Screen);
  }

  /// A setup whose plan is not applied, or whose license is declined, changes nothing and says
  /// nothing more: the page offers it again.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Setup_NotAppliedOrLicenseDeclined_SetsNothingUp(bool decline) {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Apply ] [ Cancel ]");

    if (decline) {
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {CommonText.Accept} ] [ {CommonText.Decline} ]");
      tui.Press(ConsoleKey.RightArrow);
      tui.Press(ConsoleKey.Enter);
    } else {
      tui.Press(ConsoleKey.Escape);
    }

    await tui.WaitUntilGone("[ Apply ] [ Cancel ]");
    await tui.WaitUntilGone($"[ {CommonText.Accept} ]");
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Equal(["1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False"], Calls(setup()));
    Assert.Contains($"[ {CommonText.SetUpServer} ]", tui.Screen);
  }

  [Fact]
  public async Task Setup_ThatFails_ShowsTheFailure() {
    using TuiHarness tui = Start(out _, wrap: fake => new FailingSetup(fake));
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"{SetupText.SetUpTitle} · {CommonText.Failed}");
    Assert.Contains("The builds list cannot be read.", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone(CommonText.Failed);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  /// A server setup whose setup fails.
  private sealed class FailingSetup(FakeServerSetup setup) : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client, SemVersion game, CancellationToken cancellationToken,
        OperationSession? session = null) =>
        setup.LoaderVersions(client, game, cancellationToken, session);

    public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
        throw new InvalidOperationException("The builds list cannot be read.");
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Setup_WhenTheLoaderStepDisagreesWithTheList_SaysSo_AndIsNoDeadEnd(
      Language language, int width, int height) {
    // The catalogue marks 1.21.50 as having a LeviLamina and 1.21.40 as having none; what a
    // setup finds is the other way round.
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, language: language, width: width, height: height,
        wrap: fake => new OtherLoaders(fake));
    await OpenSetup(tui);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    await tui.WaitFor("1.21.50  LeviLamina ✓");

    // A row marked ✓ for which no LeviLamina fits: the step says so, and offers to go on
    // without a loader, or back.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");
    tui.Snapshot("setup-no-loader-fits");
    Assert.Contains(Flat(SetupText.NoLoaderFits), Flat(tui.Screen));
    Assert.Contains(SetupText.NoLoader, tui.Screen);
    Assert.Contains(SetupText.NoLoaderDetail, tui.Screen);
    Assert.DoesNotContain("LeviLamina 1.", tui.Screen);

    // "Back" is a row of its own, and leads to the game versions, on the row that was chosen.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SetupText.BackDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    await tui.WaitFor(SetupText.HasLoaderDetail);

    // The reverse: a row marked as having none, for which a LeviLamina fits, offers it.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(SetupText.HasNoLoaderDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("LeviLamina 1.0.0");
    Assert.DoesNotContain(Flat(SetupText.NoLoaderFits), Flat(tui.Screen));
    Assert.DoesNotContain(SetupText.BackDetail, tui.Screen);

    // Esc goes back too; then the first game again, without a loader.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(SetupText.HasLoaderDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(SetupText.NoLoaderDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Assert.Equal(["1.21.50 Windows False  dryRun=True acceptEula=False"], Calls(setup()));
  }

  /// A server setup whose loader step finds no LeviLamina for the newest game, and one for
  /// the game before it: the opposite of what the catalogue of the tests tells.
  private sealed class OtherLoaders(FakeServerSetup setup) : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        Task.FromResult<IReadOnlyList<SemVersion>>(
            game.ToString() == "1.21.50" ? [] : [SemVersion.Parse("1.0.0")]);

    public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
        setup.Setup(client, request, run);
  }

  [Fact]
  public async Task Setup_WhenTheLoadersCannotBeRead_ShowsWhy_AndIsBackAtTheGame() {
    using TuiHarness tui = Start(out _, wrap: fake => new NoLoaders(fake));
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Finding loader versions · Failed");
    Assert.Contains("GitHub is not reached.", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("Finding loader versions · Failed");
    Assert.Contains("─ Game version ─", tui.Screen);
  }

  /// A server setup that cannot read the versions of the loader.
  private sealed class NoLoaders(FakeServerSetup setup) : IServerSetup {
    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        Task.FromException<IReadOnlyList<SemVersion>>(
            new InvalidOperationException("GitHub is not reached."));

    public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
        setup.Setup(client, request, run);
  }

  [Fact]
  public async Task Setup_WhenTheServerStartsBeforeTheSetup_IsRefused() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Another lip starts the server while the setup is at its last choice.
    await tui.SetServerRunning(true);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Stop the server first.");
    Assert.Contains("─ Set up server ─", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Stop the server first.");
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Empty(Calls(setup()));
  }

  [Fact]
  public async Task Setup_WhenTheServerStartsUnseenByTheInterface_IsRefusedToo() {
    using TuiHarness tui = Start(out Func<FakeServerSetup> setup);
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // The server starts between two looks of the interface.
    tui.SetServerLockHeld(true, Root);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Stop the server first.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Stop the server first.");
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.Empty(Calls(setup()));
  }

  [Theory]
  // The game, its build and its loader are the ones selected in each step.
  [InlineData("1.21.40", "", "1.0.0", false, true, "1.21.40 Windows True 1.0.0")]
  // A game without a loader starts on "No loader".
  [InlineData("1.21.50", "", null, true, true, "1.21.50 Windows False ")]
  // The Linux build, which has no loader to choose.
  [InlineData("1.21.50", "linux", null, true, false, "1.21.50 Linux False ")]
  // A game version that has no Linux build: the build windows, which is not asked for, with
  // the newest loader, since the Linux build could have none.
  [InlineData("1.21.40", "linux", null, false, true, "1.21.40 Windows True 1.1.0")]
  // A game version that the builds list does not name any more: the newest one.
  [InlineData("1.20.0", "", "0.9.0", true, true, "1.21.50 Windows True 1.1.0")]
  public async Task ChangeVersions_OnLinux_StartsOnWhatTheWorkspaceHas(
      string game, string variant, string? loader, bool build, bool loaders, string request) {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(State(game, variant, loader)), linux: true);
    await OpenSetup(tui);

    // Act: Enter in each step takes what it starts on.
    await tui.WaitFor("─ Game version ─");
    if (game != "1.20.0") {
      Assert.Matches($@"{Regex.Escape(game)}\s.*{SetupText.Installed}", tui.Screen);
    }

    tui.Press(ConsoleKey.Enter);
    if (build) {
      await tui.WaitFor("─ Build ─");
      tui.Press(ConsoleKey.Enter);
    }

    if (loaders) {
      await tui.WaitFor("─ Loader ─");
      tui.Press(ConsoleKey.Enter);
    }

    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Assert.Equal([$"{request} dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ChangeVersions_NotOnLinux_OfAWorkspaceOfTheLinuxBuild_SaysThatWindowsReplacesIt(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(State("1.21.50", "linux", null)), language,
        width, height);
    await OpenSetup(tui);

    // The build linux is not offered here, and the step says what happens to it before
    // anything is chosen; the game versions are all there below it.
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    tui.Snapshot("change-linux-game");
    Assert.Contains(Flat(SetupText.LinuxNotHere), Flat(tui.Screen));
    Assert.Contains("1.21.50", tui.Screen);
    Assert.Contains("1.21.40", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");
    Assert.DoesNotContain(Flat(SetupText.LinuxNotHere), Flat(tui.Screen));
    tui.Press(ConsoleKey.Enter);

    // The plan names the build that it sets up.
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Snapshot("change-linux-plan");
    Assert.Contains($"─ {SetupText.SetUpBuildTitle("windows")} ─", tui.Screen);
    Assert.Equal(["1.21.50 Windows True 1.1.0 dryRun=True acceptEula=False"], Calls(setup()));
  }

  [Fact]
  public async Task ChangeVersions_ThatKeepsTheBuild_NamesNoBuild_AndSaysNothingOfLinux() {
    using TuiHarness tui = Start(out _, Files(State("1.21.50", "", null)));
    await OpenSetup(tui);

    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    Assert.DoesNotContain("linux", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Assert.Contains($"─ {SetupText.SetUpTitle} ─", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ChangeVersions_MarksWhatIsInstalled_InEveryLanguageAndSize(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        out _, Files(State("1.21.40", "", "1.0.0")), language, width, height);
    await OpenSetup(tui);

    // The column of LeviLamina is as wide in every row, whatever the language.
    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    Assert.Contains($"1.21.50  LeviLamina ✓   {SetupText.Newest}", tui.Screen);
    Assert.Contains(
        $"1.21.40  {SetupText.HasNoLoader}  {SetupText.Installed}", tui.Screen);
    Assert.Contains(SetupText.HasNoLoaderDetail, tui.Screen);
    tui.Snapshot("change-game");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Loader} ─");
    tui.Snapshot("change-loader");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");

    // Back on the Server page, on the button.
    await tui.WaitFor(ServerText.ChangeVersionsDetail);
    tui.Snapshot("change-cancelled");
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ChangeVersions_OnLinux_NamesTheOnlyBuild_InEveryLanguageAndSize(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        out _, Files(State("1.21.50", "", "1.1.0")), language, width, height, linux: true);
    await OpenSetup(tui);

    await tui.WaitFor($"─ {SetupText.GameVersion} ─");
    Assert.Contains(
        $"1.21.50  LeviLamina ✓   {SetupText.Newest}  {SetupText.Installed}",
        tui.Screen);
    Assert.Contains(
        $"1.21.40  {SetupText.HasNoLoader}  {SetupText.OnlyBuild("windows")}",
        tui.Screen);
    tui.Snapshot("change-game-linux");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {SetupText.Build} ─");
    tui.Snapshot("change-build-linux");
  }

  [Fact]
  public async Task ChangeVersions_OnLinux_BackFromTheLoaders_KeepsTheBuildThatWasChosen() {
    using TuiHarness tui = Start(
        out Func<FakeServerSetup> setup, Files(State("1.21.50", "linux", null)), linux: true);
    await OpenSetup(tui);
    await tui.WaitFor("─ Game version ─");
    tui.Press(ConsoleKey.Enter);

    // The build starts on linux, which the workspace has; the user chooses windows.
    await tui.WaitFor(SetupText.LinuxDetail);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(SetupText.WindowsDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ Loader ─");

    // Esc goes back to the build, on what the user chose there.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(SetupText.WindowsDetail);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("─ Game version ─");
    Assert.Empty(Calls(setup()));
  }

  /// Asked for by another page, as Home asks for it, the setup starts on the Server page.
  [Fact]
  public async Task SetUpServerRequest_ShowsTheServerPage_AndStartsTheSetup() {
    using TuiHarness tui = Start(out _);
    await tui.Settle();

    await tui.Invoke(() => tui.Context.Navigate(PageId.Server, PageRequest.SetUpServer));

    await tui.WaitFor("─ Game version ─");
    Assert.Equal(PageId.Server, await tui.Invoke(() => tui.Context.CurrentPage));
  }
}
