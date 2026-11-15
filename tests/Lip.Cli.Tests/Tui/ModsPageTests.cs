using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Mods;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Moq;

namespace Lip.Cli.Tests.Tui;

/// The Mods page of the interface, driven by the keys of a fake terminal. The workspace is a
/// state file on a mock file system; the client is the real one over archives that have
/// nothing, so that no test reaches the network: what the archives were asked for is what a
/// command was given.
public sealed class ModsPageTests {
  private const string Alpha = "github.com/acme/Alpha";
  private const string Beta = "github.com/acme/Beta";
  private const string Lib = "github.com/acme/Lib";
  private const string Game = "github.com/LiteLDev/bds";
  private const string CommitField =
      "\"commit\": \"0123456789abcdef0123456789abcdef01234567\", ";

  // The catalogue has Alpha, under an id in other letters, and Lib; it has neither Beta nor
  // the game.
  private const string Catalogue = """
      {
        "mods": [
          {
            "id": "github.com/Acme/alpha", "name": "Alpha", "owner": "acme",
            "description": "An economy core", "tags": ["type:mod", "economy"],
            "side": ["server"], "platform": ["win-x64"], "gameVersions": ["1.26.60"],
            "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}, "stars": 7, "license": "MIT",
            "downloads": 55912,
            "updated": "2026-11-01T00:00:00Z", "created": "2025-01-01T00:00:00Z",
            "url": "https://levimc.org/mods/github.com/acme/Alpha/"
          },
          {
            "id": "github.com/acme/Lib", "name": "Lib", "owner": "acme",
            "description": "一个依赖库", "tags": ["library"],
            "side": ["server"], "platform": ["win-x64"], "stars": 3
          }
        ]
      }
      """;

  private const string AlphaFile = """
      { "id": "github.com/Acme/alpha", "latest": {"variant": "", "version": "1.3.0"},
        "versions": [ {"version": "1.3.0"} ],
        "readmes": [{"lang": "en", "text": "The readme of Alpha."}] }
      """;

  private readonly Mock<IArchiveProvider> _archives = new();
  // What a URL of the catalogue holds; a test replaces it to fail, or to wait.
  private Func<string, string> _catalogue = url =>
      url == ModCatalogClient.BaseUrl + "index.json" ? Catalogue : AlphaFile;

  // The request for a URL of the catalogue, whose token is cancelled when the request is
  // abandoned; used in place of _catalogue by a test that sets it.
  private Func<string, CancellationToken, Task<string>>? _catalogueRequest;

  public ModsPageTests() {
    ArchiveUnavailableException nothing = new("The archives of the test have nothing.");
    _archives.Setup(a => a.ListVersions(It.IsAny<string>())).ReturnsAsync([]);
    _archives.Setup(a => a.ResolveGitRef(It.IsAny<string>(), It.IsAny<GitRef>()))
        .ThrowsAsync(nothing);
    _archives.Setup(a => a.OpenCommit(It.IsAny<string>(), It.IsAny<string>()))
        .ThrowsAsync(nothing);
    _archives.Setup(a => a.OpenVersion(It.IsAny<string>(), It.IsAny<SpecVersion>()))
        .ThrowsAsync(nothing);
    _archives.Setup(a => a.Open(It.IsAny<Ref>())).ThrowsAsync(nothing);
  }

  /// The start of a sentence of the details area, which is wrapped where the page is narrow.
  private static string StartOf(string sentence) => sentence[..Math.Min(sentence.Length, 10)];

  /// An installed package without files. All but the game are of a commit of a repository,
  /// and of its version tags unless a source is given; a source that is no repository has no
  /// commit.
  private static string Package(
      string id, string version, bool isExplicit, string? source = null) => $$"""
      { "manifest": { "format": 4, "id": "{{id}}", "version": "{{version}}",
          "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
        "variant": "", "explicit": {{(isExplicit ? "true" : "false")}},
        "ref": "{{source ?? (id == Game ? $"{id}@{version}" : id)}}", {{(
            id == Game || source?.Contains("://", StringComparison.Ordinal) == true
                ? "" : CommitField)}}"files": [] }
      """;

  /// A folder of the file system of the test, which a package was installed from.
  private static readonly string _local =
      OperatingSystem.IsWindows() ? @"C:\packages\gamma" : "/packages/gamma";

  private const string Gamma = "github.com/acme/Gamma";
  private const string Delta = "github.com/acme/Delta";
  private const string BetaUrl = "https://example.com/beta.zip";

  /// Mods of a branch, of a URL, of a local folder and of one version, a dependency and the
  /// game.
  private static readonly string _sourced = State(
      Package(Lib, "2.0.0", false), Package(Game, "1.21.0", true),
      Package(Alpha, "1.2.0", true, $"{Alpha}@branch:main"),
      Package(Beta, "0.3.1", true, BetaUrl),
      Package(Gamma, "0.1.0", true, Ref.FromFile(_local).ToString()),
      Package(Delta, "3.0.0", true, $"{Delta}@3.0.0"));

  private static string State(params string[] packages) => $$"""
      { "format": 4, "side": "server", "platform": "win-x64",
        "packages": [ {{string.Join(", ", packages)}} ] }
      """;

  /// A dependency, the game and two mods, in an order that is not that of the page.
  private static readonly string Server = State(
      Package(Lib, "2.0.0", false), Package(Game, "1.21.0", true),
      Package(Alpha, "1.2.0", true), Package(Beta, "0.3.1", true));

  private static string StatePath(string workspace) => Path.Join(workspace, ".lip", "state.json");

  private TuiHarness Start(
      string? state, Language language = Language.En, int width = 120, int height = 36) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    if (state is not null) {
      fs.AddFile(StatePath(TuiHarness.DefaultRoot), new MockFileData(state));
    }

    fs.AddDirectory(_local);

    return TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      FileSystem = fs,
      Catalog = url => _catalogue(url),
      CatalogRequest = _catalogueRequest,
      CreateClient = (interaction, hookOutput, workspace) => new LipClient(
          fs, interaction, new CacheService(fs, interaction), new ConfigService(fs, interaction),
          new McmodService(
              fs, interaction, new LockStore(fs), _archives.Object, new ArchiveReader(),
              new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => workspace)),
    });
  }

  /// What the archives were asked for: the name of each call with its arguments.
  private List<string> Asked() {
    lock (_archives.Invocations) {
      return [
        .. _archives.Invocations.Select(
            i => $"{i.Method.Name}({string.Join(", ", i.Arguments)})"),
      ];
    }
  }

  /// The local paths that the archives were asked to open.
  private List<string> OpenedPaths() {
    lock (_archives.Invocations) {
      return [
        .. _archives.Invocations.SelectMany(i => i.Arguments.OfType<Ref>())
            .Where(r => r.Kind == RefKind.File).Select(r => r.LocalPath),
      ];
    }
  }

  private static string HintOf(Hint hint) => $"{hint.Key} {hint.Text}";

  private static string Installed(TuiHarness tui) => tui.Fs.File.ReadAllText(StatePath(tui.Root));

  [Theory]
  [InlineData(Language.En, "Mods")]
  [InlineData(Language.ZhHans, "模组")]
  [InlineData(Language.ZhHant, "模組")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Mods);

    Assert.Equal(title, tui.Text(() => ModsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Empty_ShowsALineAndTheRowsToStartWith(
      Language language, int width, int height) {
    using TuiHarness tui = Start(state: null, language, width, height);

    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(CommonText.NoMods);
    tui.Snapshot("mods-empty");

    // Without a game, the first row leads to a new server; the list offers nothing to update.
    string screen = tui.Screen;
    int newServer = screen.IndexOf(CommonText.NewServer, StringComparison.Ordinal);
    int install = screen.IndexOf(ModsText.InstallFromSource, StringComparison.Ordinal);
    Assert.True(newServer >= 0 && newServer < install, screen);
    Assert.DoesNotContain(ModsText.UpdateAll, screen);
    Assert.Contains(ModsText.NewServerDetail, screen);
    Assert.Contains(
        $"{CommonText.HintMove.Key} {CommonText.HintMove.Text}  "
            + $"{CommonText.HintSelect.Key} {CommonText.HintSelect.Text}  "
            + $"{CommonText.HintBack.Key} {CommonText.HintBack.Text}",
        screen);
    Assert.DoesNotContain(CommonText.HintNext.Text, screen);
  }

  [Fact]
  public async Task Empty_NewServerAndGetMods_LeadToTheirPages() {
    using TuiHarness tui = Start(state: null);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(CommonText.NoMods);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.GetMods, "the Get mods page");

    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(CommonText.NoMods);
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.Workspaces, "the Workspaces page");
  }

  [Fact]
  public async Task WithAGameOnly_OffersGetMods_ButNoNewServer() {
    using TuiHarness tui = Start(State(Package(Game, "1.21.0", true)));

    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Game);

    Assert.DoesNotContain(CommonText.NewServer, tui.Screen);
    Assert.DoesNotContain(ModsText.UpdateAll, tui.Screen);
    Assert.Contains(ModsText.InstallFromSourceDetail, tui.Screen);
    Assert.DoesNotContain(CommonText.NoMods, tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task List_ShowsTheModsFirst_ThenDependencies_ThenTheGame_WithDetails(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);

    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);
    tui.Snapshot("mods-list");

    // The list has the focus, on the first mod.
    string screen = tui.Screen;
    int alpha = screen.IndexOf("[ ] " + Alpha, StringComparison.Ordinal);
    int beta = screen.IndexOf("[ ] " + Beta, StringComparison.Ordinal);
    int lib = screen.IndexOf("    " + Lib, StringComparison.Ordinal);
    int game = screen.IndexOf("    " + Game, StringComparison.Ordinal);
    Assert.True(alpha >= 0 && alpha < beta && beta < lib && lib < game, screen);
    Assert.Contains(ModsText.Installed(4), screen);
    Assert.Contains(ModsText.UpdateAll, screen);
    Assert.Contains(ModsText.ExplicitDetail, screen);
    Assert.Contains(
        $"{CommonText.HintMove.Key} {CommonText.HintMove.Text}  "
            + $"{CommonText.HintCheck.Key} {CommonText.HintCheck.Text}  "
            + $"{CommonText.HintActions.Key} {CommonText.HintActions.Text}  "
            + $"{CommonText.HintNext.Key} {CommonText.HintNext.Text}  "
            + $"{CommonText.HintBack.Key} {CommonText.HintBack.Text}",
        screen);

    // The details follow the selection: a dependency, then the game.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ModsText.DependencyDetail);
    tui.Snapshot("mods-list-dependency");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ModsText.GameDetail);

    // ↑ from the first row goes to the action rows, whose details are shown then.
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(StartOf(ModsText.UpdateAllDetail));
    Assert.Contains($"{CommonText.HintNext.Key} {CommonText.HintNext.Text}", tui.Screen);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(StartOf(ModsText.InstallFromSourceDetail));
    tui.Snapshot("mods-actions");

    // Tab goes back to the list.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(ModsText.ExplicitDetail);
  }

  [Fact]
  public async Task Tab_GoesRoundTheActionsTheListAndTheSidebar() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);
    await tui.WaitFor("Enter Actions  Tab Next  Esc Back");

    // The list is the last area: Tab goes on to the sidebar, and from there to the actions.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" ↑↓ Move  Enter Select  Tab Next  Esc Back");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Actions  Tab Next  Esc Back");

    // Shift+Tab goes the other way, and from the sidebar to the list.
    tui.Press(ConsoleKey.Tab, 2, ConsoleModifiers.Shift);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Actions  Tab Next  Esc Back");
  }

  [Fact]
  public async Task Uninstall_ShowsThePlan_AndCancelChangesNothing() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Snapshot("mods-menu");
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    tui.Snapshot("mods-uninstall-plan");
    Assert.Contains(ModsText.UninstallOperation(Alpha), tui.Screen);
    Assert.Contains("Dry run:", tui.Screen);

    // Cancel is the dry run only.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ]");

    Assert.Contains("[ ] " + Alpha, tui.Screen);
    Assert.Contains(Alpha, Installed(tui));
  }

  /// The list has the mods by identity, whatever the order of the state file, which every
  /// operation changes; and after an operation the highlight is on the package it was on.
  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHant)]
  public async Task List_IsByIdentity_AndTheHighlightStaysOnItsPackageAfterAnOperation(
      Language language) {
    using TuiHarness tui = Start(
        State(
            Package(Game, "1.21.0", true), Package(Delta, "3.0.0", true),
            Package(Beta, "0.3.1", true), Package(Alpha, "1.2.0", true)),
        language);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);
    string screen = tui.Screen;
    int[] rows = [
      screen.IndexOf("[ ] " + Alpha, StringComparison.Ordinal),
      screen.IndexOf("[ ] " + Beta, StringComparison.Ordinal),
      screen.IndexOf("[ ] " + Delta, StringComparison.Ordinal),
      screen.IndexOf("    " + Game, StringComparison.Ordinal),
    ];
    Assert.DoesNotContain(-1, rows);
    Assert.Equal(rows.Order(), rows);

    // Alpha is checked, and the highlight goes to Delta, the third row.
    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(Delta + "  3.0.0");

    // Act: the checked mod, Alpha, is uninstalled.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UninstallChecked(1));
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Ok} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone($"[ {CommonText.Ok} ]");

    // Assert: Delta is the second row now, and still the highlighted one.
    await tui.WaitFor(ModsText.Installed(3));
    Assert.DoesNotContain(Alpha, tui.Screen);
    Assert.Contains(Delta + "  3.0.0", tui.Screen);
    Assert.DoesNotContain(Beta + "  0.3.1", tui.Screen);
    Assert.Equal(Theme.Selected, await tui.StyleOf("[ ] " + Delta));
    Assert.True(
        tui.Screen.IndexOf("[ ] " + Beta, StringComparison.Ordinal)
            < tui.Screen.IndexOf("[ ] " + Delta, StringComparison.Ordinal));
  }

  [Fact]
  public async Task Uninstall_Applied_RemovesThePackage_AndReadsTheListAgain() {
    using TuiHarness tui = Start(Server, Language.ZhHans);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.Uninstall);
    tui.Snapshot("mods-menu");
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    tui.Snapshot("mods-uninstall-plan");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Ok} ]");
    tui.Snapshot("mods-uninstall-result");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone($"[ {CommonText.Ok} ]");
    // The dependency, which nothing needs any more, left with it.
    await tui.WaitFor(ModsText.Installed(2));
    Assert.DoesNotContain(Alpha, tui.Screen);
    Assert.Contains("[ ] " + Beta, tui.Screen);
    Assert.DoesNotContain(Alpha, Installed(tui));
  }

  [Fact]
  public async Task Dependency_OffersNoUninstall_AndCannotBeChecked() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ModsText.DependencyDetail);
    Assert.DoesNotContain(CommonText.HintCheck.Text, tui.Screen);
    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);

    Assert.Contains($"─ {Lib} ─", tui.Screen);
    Assert.DoesNotContain(ModsText.Uninstall, tui.Screen);
    Assert.DoesNotContain("[x]", tui.Screen);
    Assert.DoesNotContain(ModsText.UncheckAll, tui.Screen);
  }

  [Fact]
  public async Task Game_OffersOnlyTheWayToWorkspaces() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.GoToWorkspaces);
    Assert.Contains(ModsText.Details, tui.Screen);
    Assert.DoesNotContain(ModsText.UpdateTo, tui.Screen);
    Assert.DoesNotContain(ModsText.Uninstall, tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.Workspaces, "the Workspaces page");
  }

  [Theory]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Checked_UninstallActsOnAllOfThem(Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModsText.Checked(2));
    tui.Snapshot("mods-checked");

    // The menu is that of the checked rows, wherever the selection is.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UninstallChecked(2));
    tui.Snapshot("mods-checked-menu");
    Assert.DoesNotContain(ModsText.UpdateTo, tui.Screen);
    // The first action shows the details of the row that is highlighted.
    Assert.Contains(ModsText.DetailsOf("Lib"), tui.Screen);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    tui.Snapshot("mods-checked-plan");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Ok} ]");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone(Beta);
    Assert.DoesNotContain(Alpha, tui.Screen);
    Assert.DoesNotContain("[x]", tui.Screen);
    string state = Installed(tui);
    Assert.DoesNotContain(Alpha, state);
    Assert.DoesNotContain(Beta, state);
    Assert.Contains(Game, state);
  }

  [Fact]
  public async Task Checked_UncheckAll_ClearsTheChecks() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[x] " + Alpha);

    // While a row is checked, the menu of any row is that of the checked ones.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UncheckAll);
    Assert.Contains($"─ {ModsText.Checked(1)} ─", tui.Screen);
    Assert.Contains(ModsText.UninstallChecked(1), tui.Screen);
    Assert.DoesNotContain(ModsText.UpdateTo, tui.Screen);
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("[x]");
    Assert.DoesNotContain(ModsText.Checked(1), tui.Screen);
  }

  [Fact]
  public async Task Update_UpdatesTheSelectedPackage() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // The archives have no version of it, which is why the dry run fails.
    await tui.WaitFor($"{ModsText.UpdateOperation(Beta)} · {CommonText.Failed}");
    Assert.Contains($"ListVersions({Beta})", Asked());
    Assert.DoesNotContain($"ListVersions({Alpha})", Asked());
  }

  [Theory]
  [InlineData("1.5.0", "1.5.0")]
  [InlineData("@branch:main", "branch:main")]
  public async Task UpdateTo_AsksForTheRef_AndUpdatesToIt(string typed, string reference) {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Alpha + "@");
    Assert.Contains(ModsText.RefPlaceholder, tui.Screen);

    // Something that is no ref keeps the prompt open and says what a ref is.
    tui.Type("tag:");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.RefInvalid);
    tui.Snapshot("mods-update-to");
    tui.Press(ConsoleKey.Backspace, 4);
    await tui.WaitUntilGone(ModsText.RefInvalid);
    tui.Type(typed);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"{ModsText.UpdateOperation($"{Alpha}@{reference}")} · {CommonText.Failed}");
    Assert.Contains(Asked(), call => call.Contains(Alpha, StringComparison.Ordinal));
  }

  /// Opens the menu of the highlighted mod and chooses Update, whose dry run fails, since the
  /// archives of the test have nothing; then closes the failure.
  private static async Task UpdateAndClose(TuiHarness tui) {
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains(CommonText.Failed, tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone($"[ {CommonText.Close} ]");
  }

  /// protocol/lip-update.md: the request of a package is made again by its source. Its
  /// identity names the version tags of a repository, which a package of a URL or of a local
  /// path has none of, and which a package of a branch would be moved back to.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Update_AsksAgainForTheSourceThatTheModWasInstalledFrom(
      Language language, int width, int height) {
    using TuiHarness tui = Start(_sourced, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    // A branch: the details area names it, and the update stays on it.
    await tui.WaitFor($"{ModsText.Source}  branch:main");
    Assert.Contains(ModsText.ExplicitDetail, tui.Screen);
    tui.Snapshot("mods-source-branch");
    await UpdateAndClose(tui);
    Assert.Contains($"ResolveGitRef({Alpha}, branch:main)", Asked());
    Assert.DoesNotContain($"ListVersions({Alpha})", Asked());

    // A URL: it has no other versions to name, which the menu says; it is downloaded again.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"{ModsText.Source}  {BetaUrl}");
    tui.Snapshot("mods-source-url");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ModsText.UpdateToNeedsRepository);
    Assert.DoesNotContain(HintOf(CommonText.HintSelect), tui.Screen);
    tui.Snapshot("mods-source-url-menu");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains(ModsText.UpdateToNeedsRepository, tui.Screen);
    Assert.DoesNotContain(ModsText.UpdateToTitle + " ", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.UpdateToNeedsRepository);
    await UpdateAndClose(tui);
    Assert.Contains($"Forget({BetaUrl})", Asked());
    Assert.Contains($"Open({BetaUrl})", Asked());
    Assert.DoesNotContain($"ListVersions({Beta})", Asked());

    // One version, as Get mods installs: the update asks for the version tags of the
    // repository, of which the highest that fits is installed, not for that version again.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(Delta + "  3.0.0");
    Assert.Contains(ModsText.ExplicitDetail, tui.Screen);
    Assert.DoesNotContain(ModsText.Source + "  ", tui.Screen);
    tui.Snapshot("mods-source-version");
    await UpdateAndClose(tui);
    Assert.Contains($"ListVersions({Delta})", Asked());
    Assert.DoesNotContain($"ResolveGitRef({Delta}, 3.0.0)", Asked());

    // A local folder: it is read again.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(Gamma + "  0.1.0");
    if (width >= 120) {
      Assert.Contains($"{ModsText.Source}  {_local}", tui.Screen);
    }

    tui.Snapshot("mods-source-local");
    Assert.Empty(OpenedPaths());
    await UpdateAndClose(tui);
    Assert.Equal([_local], OpenedPaths().Distinct());
    Assert.DoesNotContain($"ListVersions({Gamma})", Asked());
  }

  private const string Commit = "0123456789abcdef0123456789abcdef01234567";

  /// Gives the archives the repository of Delta, whose only version is the one installed, at
  /// the commit that the state file records; and writes the state file as lip writes it, so
  /// that an operation that changes nothing leaves it with the value it has.
  private async Task WithDelta(TuiHarness tui) {
    LockStore store = new(tui.Fs);
    await store.Save(tui.Root, (await store.Load(tui.Root))!);
    string folder = Path.Join(Path.GetPathRoot(tui.Root), "archives", "delta");
    tui.Fs.AddFile(
        Path.Join(folder, "lip.toml"),
        new MockFileData($$"""
            format = 4
            id = "{{Delta}}"
            version = "3.0.0"

            [[variants.""]]
            side = "server"
            platform = "win-x64"

            """));
    ArchiveHandle Handle() => new(
        $"{Delta}@v3.0.0", new Lip.Core.Sources.DirectorySource(tui.Fs, folder), Commit);
    _archives.Setup(a => a.ListVersions(Delta)).ReturnsAsync([
      new SpecVersion(Semver.SemVersion.Parse("3.0.0"), "v3.0.0"),
    ]);
    _archives.Setup(a => a.OpenVersion(Delta, It.IsAny<SpecVersion>())).ReturnsAsync(Handle);
    _archives.Setup(a => a.ResolveGitRef(Delta, It.IsAny<GitRef>())).ReturnsAsync(Commit);
    _archives.Setup(a => a.OpenCommit(Delta, Commit)).ReturnsAsync(Handle);
  }

  /// Update means the newest version that fits. A mod that was installed at one version, as
  /// Get mods installs, and is at its newest version: lip would only record that it was
  /// asked for without that version, which is nothing to confirm. The plan says that nothing
  /// would change, and has nothing to apply, however often Update is chosen; the state file
  /// stays as it is.
  [Theory]
  [InlineData(Language.En, false)]
  [InlineData(Language.ZhHans, false)]
  [InlineData(Language.ZhHant, true)]
  public async Task Update_OfAModAtItsNewestVersion_HasNothingToApply_HoweverOftenItIsAsked(
      Language language, bool all) {
    using TuiHarness tui = Start(
        State(Package(Game, "1.21.0", true), Package(Delta, "3.0.0", true, $"{Delta}@3.0.0")),
        language);
    await WithDelta(tui);
    string state = Installed(tui);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Delta);

    for (int i = 0; i < 2; i++) {
      if (all) {
        // Update all, the action row above the list.
        tui.Press(ConsoleKey.UpArrow);
        await tui.WaitFor(ModsText.UpdateAllDetail);
        tui.Press(ConsoleKey.Enter);
      } else {
        tui.Press(ConsoleKey.Enter);
        await tui.WaitFor(ModsText.UpdateTo);
        tui.Press(ConsoleKey.DownArrow);
        tui.Press(ConsoleKey.Enter);
      }

      await tui.WaitFor(CommonText.NothingWouldChange);
      Assert.Contains($"[ {CommonText.Close} ]", tui.Screen);
      Assert.DoesNotContain($"[ {CommonText.Apply} ]", tui.Screen);
      Assert.DoesNotContain(CommonText.OnlyRecordChanges, tui.Screen);
      Assert.DoesNotContain(CommonText.Failed, tui.Screen);
      tui.Snapshot("mods-update-newest");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitUntilGone(CommonText.NothingWouldChange);
      if (all) {
        tui.Press(ConsoleKey.DownArrow);
        await tui.WaitFor(Delta + "  3.0.0");
      }
    }

    Assert.Contains($"ListVersions({Delta})", Asked());
    Assert.Equal(state, Installed(tui));
  }

  /// Update to version… is what the user named: for a mod that is at that version, and of
  /// the version tags, the plan says that only the record would change, and applying it
  /// records the version.
  [Fact]
  public async Task UpdateTo_TheInstalledVersion_OffersToRecordIt() {
    using TuiHarness tui = Start(
        State(Package(Game, "1.21.0", true), Package(Delta, "3.0.0", true)));
    await WithDelta(tui);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Delta);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Delta + "@");
    tui.Type("3.0.0");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(CommonText.OnlyRecordChanges);
    Assert.Contains($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]", tui.Screen);
    Assert.DoesNotContain($"\"ref\": \"{Delta}@3.0.0\"", Installed(tui));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    Assert.Contains($"{Delta}@3.0.0", Installed(tui));
  }

  /// The checked mods are updated as each is by itself: one installed at a version leaves it
  /// for the version tags.
  [Fact]
  public async Task Checked_UpdateOfModsInstalledAtOneVersion_AsksForTheirVersionTags() {
    using TuiHarness tui = Start(
        State(
            Package(Game, "1.21.0", true), Package(Alpha, "1.2.0", true, $"{Alpha}@1.2.0"),
            Package(Beta, "0.3.1", true, $"{Beta}@0.3.1")),
        Language.ZhHant);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModsText.Checked(2));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateChecked(2));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains($"ListVersions({Alpha})", Asked());
    Assert.DoesNotContain(
        Asked(), call => call.StartsWith("ResolveGitRef(", StringComparison.Ordinal));
  }

  /// Update all updates each mod as Update does, and keeps a mod whose local path is gone,
  /// which the plan says.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task UpdateAll_KeepsAModWhoseLocalPathIsGone_AndSaysSo(
      Language language, int width, int height) {
    string gone = OperatingSystem.IsWindows() ? @"C:\gone" : "/gone";
    using TuiHarness tui = Start(
        State(
            Package(Game, "1.21.0", true),
            Package(Gamma, "0.1.0", true, Ref.FromFile(gone).ToString())),
        language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Gamma);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(StartOf(ModsText.UpdateAllDetail));
    tui.Press(ConsoleKey.Enter);

    // Nothing would change, so the plan can only be closed.
    await tui.WaitFor($"[ {CommonText.Close} ]");
    await tui.WaitFor("Keeping package");
    tui.Snapshot("mods-update-all-gone");
    Assert.DoesNotContain(CommonText.Failed, tui.Screen);
    Assert.DoesNotContain($"[ {CommonText.Apply} ]", tui.Screen);
    Assert.Empty(OpenedPaths());
    Assert.DoesNotContain(
        Asked(), call => call.StartsWith("ListVersions(", StringComparison.Ordinal));
  }

  /// Update all asks for the version tags of a mod that was installed at one version, which
  /// lip update without operands would keep there, and for the branch of a mod of a branch.
  [Fact]
  public async Task UpdateAll_UpdatesAModInstalledAtOneVersion_AndNeverTheGame() {
    using TuiHarness tui = Start(
        State(
            Package(Game, "1.21.0", true), Package(Delta, "3.0.0", true, $"{Delta}@3.0.0")),
        Language.ZhHans);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Delta);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ModsText.UpdateAllDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains($"ListVersions({Delta})", Asked());
    Assert.DoesNotContain(Asked(), call => call.Contains(Game, StringComparison.Ordinal));
  }

  [Fact]
  public async Task UpdateTo_OfAModOfABranch_NamesItsRepositoryAtTheRef() {
    using TuiHarness tui = Start(_sourced, Language.ZhHant);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Alpha + "@");
    // A commit that is not written in full is no ref.
    tui.Type("rev:0123");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.RefInvalid);
    tui.Press(ConsoleKey.Backspace, 8);
    tui.Type("tag:nightly");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains(ModsText.UpdateOperation($"{Alpha}@tag:nightly"), tui.Screen);
    Assert.Contains($"ResolveGitRef({Alpha}, tag:nightly)", Asked());
    Assert.DoesNotContain($"ResolveGitRef({Alpha}, branch:main)", Asked());
  }

  [Fact]
  public async Task Checked_UpdateAsksForTheSourceOfEach() {
    using TuiHarness tui = Start(_sourced, Language.ZhHans);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModsText.Checked(2));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateChecked(2));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains(
        $"{CommonText.Preview} · {ModsText.UpdateChecked(2)} · {CommonText.Failed}", tui.Screen);
    Assert.Contains($"Forget({BetaUrl})", Asked());
    Assert.Contains($"ResolveGitRef({Alpha}, branch:main)", Asked());
    Assert.DoesNotContain(
        Asked(), call => call.StartsWith("ListVersions(", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Checked_DetailsAreThoseOfTheHighlightedRow(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    // Alpha is checked, and the dependency is highlighted.
    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ModsText.DependencyDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.DetailsOf("Lib"));
    Assert.Contains(HintOf(CommonText.HintSelect), tui.Screen);
    tui.Snapshot("mods-checked-details");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("一个依赖库");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("一个依赖库");
    // The check stays.
    Assert.Contains("[x] " + Alpha, tui.Screen);
    Assert.Empty(Asked());
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Menu_ThatIsOpenWhenTheServerStopsOrStarts_IsMadeAgain(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);
    await tui.SetServerRunning(true);
    await tui.WaitFor(ModsText.ServerRuns);

    // The menu says why Update cannot be chosen.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.StopServerFirst);

    // The server stops: the menu stays open, says so no more, and Update, which is still
    // highlighted, can be chosen.
    await tui.SetServerRunning(false);
    await tui.WaitUntilGone(CommonText.StopServerFirst);
    Assert.Contains(ModsText.UpdateTo, tui.Screen);
    Assert.Contains(HintOf(CommonText.HintSelect), tui.Screen);
    tui.Snapshot("mods-menu-server-stopped");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains($"ListVersions({Alpha})", Asked());
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone($"[ {CommonText.Close} ]");
    int asked = Asked().Count;

    // The server starts while the menu is open, with Uninstall highlighted: the menu says why
    // that cannot be chosen, and Enter changes nothing.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.UpArrow);
    await tui.Settle();
    Assert.DoesNotContain(CommonText.StopServerFirst, tui.Screen);
    await tui.SetServerRunning(true);
    await tui.WaitFor(CommonText.StopServerFirst);
    Assert.Contains(ModsText.UpdateTo, tui.Screen);
    Assert.DoesNotContain(HintOf(CommonText.HintSelect), tui.Screen);
    tui.Snapshot("mods-menu-server-started");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains(ModsText.UpdateTo, tui.Screen);
    Assert.DoesNotContain($"[ {CommonText.Apply} ]", tui.Screen);
    Assert.Equal(asked, Asked().Count);
    Assert.Contains(Alpha, Installed(tui));

    // The highlight is where it was: one step on is Details, which can be chosen.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitUntilGone(CommonText.StopServerFirst);
    Assert.Contains(HintOf(CommonText.HintSelect), tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.UpdateTo);
  }

  [Fact]
  public async Task UpdateAll_UpdatesEveryMod() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ModsText.UpdateAllDetail);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    tui.Snapshot("mods-update-all");
    Assert.Contains(
        $"{CommonText.Preview} · {ModsText.UpdateAll} · {CommonText.Failed}", tui.Screen);
    // The update began with the first mod, whose repository has no version in the test.
    Assert.Contains($"ListVersions({Alpha})", Asked());
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task InstallFromSource_AsksForPackages_AndInstallsThem(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor(CommonText.HintNext.Text);
    tui.Press(ConsoleKey.Enter);

    // Two ways to name what to install; the second is to type it.
    await tui.WaitFor(ModsText.TypeSource);
    tui.Snapshot("mods-install-choice");
    Assert.Contains($"─ {ModsText.InstallTitle} ─", tui.Screen);
    Assert.Contains(StartOf(ModsText.BrowseDetail), tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(StartOf(ModsText.TypeSourceDetail));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.SourcePlaceholder);
    tui.Snapshot("mods-install-prompt");

    // Nothing to install keeps the prompt open.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Type("github.com/acme/Gamma  github.com/acme/Delta@2.0.0");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    tui.Snapshot("mods-install-failed");
    Assert.Contains(
        $"{CommonText.Preview} · {ModsText.InstallOperation("github.com/acme/G")}", tui.Screen);
    Assert.Contains("ListVersions(github.com/acme/Gamma)", Asked());
  }

  [Fact]
  public async Task Request_InstallFromSource_AsksHow_AndEscGoesBackOneLevel() {
    using TuiHarness tui = Start(state: null);
    await tui.Settle();

    await tui.Invoke(() => tui.Context.Navigate(PageId.Mods, PageRequest.InstallFromSource));
    await tui.WaitFor(ModsText.TypeSource);
    Assert.Contains($"─ {ModsText.InstallTitle} ─", tui.Screen);

    // Esc in the prompt goes back to the choice, with the way just left highlighted.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.SourcePlaceholder);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.SourcePlaceholder);
    await tui.WaitFor(StartOf(ModsText.TypeSourceDetail));

    // And so does Esc in the file browser.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {ModsText.InstallLocal} ─");
    await tui.WaitUntilGone(PathPickerText.Reading);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {ModsText.InstallLocal} ─");
    await tui.WaitFor(StartOf(ModsText.BrowseDetail));

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.TypeSource);
    Assert.Empty(Asked());
    Assert.Equal(PageId.Mods, tui.Context.CurrentPage);
  }

  /// Goes from the list of the page to the file browser of "Install from source…".
  private static async Task Browse(TuiHarness tui) {
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor(StartOf(ModsText.InstallFromSourceDetail));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.TypeSource);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {PathPickerText.SelectFolder} ] [ {CommonText.Cancel} ]");
    await tui.WaitUntilGone(PathPickerText.Reading);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task InstallFromSource_Browse_InstallsTheZipFileChosen_AsOneArgument(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    // A path with a space and Chinese characters.
    string folder = Path.Join(tui.Root, "my mods");
    string zip = Path.Join(folder, "领地 系统.zip");
    tui.Fs.AddFile(zip, new MockFileData("zip"));
    tui.Fs.AddFile(Path.Join(folder, "notes.txt"), new MockFileData("text"));
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    // The browser starts at the workspace, and shows folders and .zip files.
    await Browse(tui);
    await tui.WaitFor($"my mods{Path.DirectorySeparatorChar}");
    tui.Snapshot("mods-install-browse");
    Assert.Contains($"─ {ModsText.InstallLocal} ─", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("领地 系统.zip");
    tui.Snapshot("mods-install-browse-folder");
    Assert.DoesNotContain("notes.txt", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // The archives of the test have nothing, which is why the dry run fails; the title names
    // the file.
    await tui.WaitFor($"[ {CommonText.Close} ]");
    tui.Snapshot("mods-install-browse-failed");
    Assert.Contains($"{CommonText.Preview} · {ModsText.InstallOperation("领地 系统.zip")}", tui.Screen);
    Assert.Equal([zip], OpenedPaths());
  }

  [Fact]
  public async Task InstallFromSource_Browse_InstallsTheFolderShown() {
    using TuiHarness tui = Start(Server, Language.ZhHant);
    string folder = Path.Join(tui.Root, "我的 模組", "領地 系統");
    tui.Fs.AddFile(Path.Join(folder, "manifest.json"), new MockFileData("{}"));
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    await Browse(tui);
    await tui.WaitFor($"我的 模組{Path.DirectorySeparatorChar}");

    // The workspace, which the browser starts at, is no package.
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.IsWorkspace);
    tui.Snapshot("mods-install-browse-workspace");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"領地 系統{Path.DirectorySeparatorChar}");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"{PathPickerText.Folder}  {folder}");
    await tui.WaitUntilGone(PathPickerText.Reading);

    // The folder shown is chosen with its button.
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.Contains($"{CommonText.Preview} · {ModsText.InstallOperation("領地 系統")}", tui.Screen);
    Assert.Equal([folder], OpenedPaths());
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Details_ShowWhatTheCatalogueSaysOfTheMod(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    // Details is the first action of the menu.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    Assert.True(
        tui.Screen.IndexOf(ModsText.Details, StringComparison.Ordinal)
            < tui.Screen.IndexOf(ModsText.UpdateTo, StringComparison.Ordinal),
        tui.Screen);
    tui.Press(ConsoleKey.Enter);

    // The same details as on the Get mods page, with the file of the mod once it is read.
    await tui.WaitFor("An economy core");
    await tui.WaitFor("1.3.0");
    tui.Snapshot("mods-details");
    string screen = tui.Screen;
    Assert.Contains("─ Alpha ─", screen);
    Assert.Contains(ModDetailsText.Author, screen);
    Assert.Contains("55,912", screen);
    Assert.Contains($"[ {ModDetailsText.Readme} ]", screen);
    Assert.Contains($"[ {CommonText.Close} ]", screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("The readme of Alpha.");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("The readme of Alpha.");

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("An economy core");
    Assert.Contains("[ ] " + Alpha, tui.Screen);
    Assert.Empty(Asked());
  }

  [Fact]
  public async Task Details_OfADependency_AreShownToo() {
    using TuiHarness tui = Start(Server, Language.ZhHans);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ModsText.DependencyDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("一个依赖库");
    Assert.Contains("─ Lib ─", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Details_OfWhatTheCatalogueDoesNotHave_SayThat(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    // Beta, which the catalogue does not have.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.NotInCatalogue);
    tui.Snapshot("mods-details-missing");
    Assert.Contains($"─ {ModsText.Details} ─", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ModsText.NotInCatalogue);

    // The game, whose menu has Details too.
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.GoToWorkspaces);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.NotInCatalogue);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.NotInCatalogue);
    Assert.Equal(PageId.Mods, tui.Context.CurrentPage);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Details_WhenTheCatalogueCannotBeRead_SayThat_AndReadItAgainWhenAsked(
      Language language, int width, int height) {
    int reads = 0;
    bool broken = true;
    _catalogue = url => {
      if (url != ModCatalogClient.BaseUrl + "index.json") {
        return AlphaFile;
      }

      Interlocked.Increment(ref reads);
      return Volatile.Read(ref broken)
          ? throw new CatalogUnavailableException("The catalogue of the test is away.")
          : Catalogue;
    };
    using TuiHarness tui = Start(Server, language, width, height);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.CatalogueUnreadable);
    tui.Snapshot("mods-details-unreadable");
    Assert.DoesNotContain(ModsText.NotInCatalogue, tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ModsText.CatalogueUnreadable);
    Assert.Contains("[ ] " + Alpha, tui.Screen);

    // Asked again, the catalogue is read again; once read, it is kept.
    Volatile.Write(ref broken, false);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("An economy core");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("An economy core");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("An economy core");
    Assert.Equal(2, Volatile.Read(ref reads));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Details_WaitForTheCatalogue_AndEscAbandonsTheRequest(
      Language language, int width, int height) {
    TaskCompletionSource arrives = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource abandoned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int reads = 0;
    _catalogueRequest = async (url, token) => {
      if (url != ModCatalogClient.BaseUrl + "index.json") {
        return AlphaFile;
      }

      Interlocked.Increment(ref reads);
      token.Register(() => abandoned.TrySetResult());
      await arrives.Task.WaitAsync(token);
      return Catalogue;
    };
    using TuiHarness tui = Start(Server, language, width, height);
    try {
      await tui.GoTo(PageId.Mods);
      await tui.WaitFor(Alpha);

      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(ModsText.UpdateTo);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(ModsText.ReadingCatalogue);
      tui.Snapshot("mods-details-wait");
      Assert.Contains($" {HintOf(CommonText.HintCancel)}", tui.Screen);
      Assert.DoesNotContain(CommonText.HintSelect.Text, tui.Screen);

      // Esc gives up, and the request is abandoned: nothing is shown, and nothing is said of
      // a catalogue that cannot be read.
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone(ModsText.ReadingCatalogue);
      await abandoned.Task.WaitAsync(
          TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
      await tui.Settle();
      Assert.DoesNotContain(ModsText.CatalogueUnreadable, tui.Screen);
      Assert.Contains("[ ] " + Alpha, tui.Screen);

      // Asked again, the catalogue is read again, and shown when it arrives.
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(ModsText.UpdateTo);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(ModsText.ReadingCatalogue);
      await tui.WaitUntil(() => Volatile.Read(ref reads) == 2, "the catalogue");
      arrives.SetResult();
      await tui.WaitFor("An economy core");
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone("An economy core");

      // What arrived is kept, by the catalogue that every page shares, for the next time.
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(ModsText.UpdateTo);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("An economy core");
      Assert.Equal(2, Volatile.Read(ref reads));
      Assert.Same(
          await tui.Context.Catalog.GetIndex(TestContext.Current.CancellationToken),
          await tui.Context.Catalog.GetIndex(TestContext.Current.CancellationToken));
      Assert.Equal(2, Volatile.Read(ref reads));
    }
    finally {
      arrives.TrySetResult();
    }
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task WhileTheServerRuns_NothingChangesTheWorkspace_UntilItStops(
      Language language, int width, int height) {
    using TuiHarness tui = Start(Server, language, width, height);
    string select = HintOf(CommonText.HintSelect);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);
    Assert.DoesNotContain(ModsText.ServerRuns, tui.Screen);

    // The page follows the server: a line says that it runs.
    await tui.SetServerRunning(true);
    await tui.WaitFor(ModsText.ServerRuns);
    tui.Snapshot("mods-running");

    // The menu of a mod: Details can be chosen, the rest says why it cannot.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    Assert.Contains(select, tui.Screen);
    Assert.DoesNotContain(CommonText.StopServerFirst, tui.Screen);
    for (int i = 0; i < 3; i++) {
      tui.Press(ConsoleKey.DownArrow);
      await tui.Settle();
      Assert.Contains(CommonText.StopServerFirst, tui.Screen);
      Assert.DoesNotContain(select, tui.Screen);
      tui.Press(ConsoleKey.Enter);
      await tui.Settle();
      Assert.Contains(ModsText.UpdateTo, tui.Screen);
    }

    tui.Snapshot("mods-running-menu");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitUntilGone(CommonText.StopServerFirst);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("An economy core");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("An economy core");

    // The action rows: Update all, then Install from source.
    // The details area says why each does nothing; the title of the second has no "…".
    foreach (string shown in (string[])[CommonText.StopServerFirst, ModsText.InstallTitle + "  "]) {
      tui.Press(ConsoleKey.UpArrow);
      await tui.WaitFor(shown);
      Assert.Contains(CommonText.StopServerFirst, tui.Screen);
      Assert.DoesNotContain(select, tui.Screen);
      tui.Press(ConsoleKey.Enter);
      await tui.Settle();
      Assert.DoesNotContain(ModsText.TypeSource, tui.Screen);
      Assert.DoesNotContain(CommonText.Preview, tui.Screen);
    }

    tui.Snapshot("mods-running-actions");

    // Checked mods: only the checks can be taken off.
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[x] " + Alpha);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UncheckAll);
    tui.Snapshot("mods-running-checked");
    Assert.Contains(select, tui.Screen);
    foreach (string action in (string[])[ModsText.UpdateChecked(1), ModsText.UninstallChecked(1)]) {
      Assert.Contains(action, tui.Screen);
      tui.Press(ConsoleKey.DownArrow);
      await tui.WaitFor(CommonText.StopServerFirst);
      tui.Press(ConsoleKey.Enter);
      await tui.Settle();
      Assert.DoesNotContain(select, tui.Screen);
    }

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitUntilGone(CommonText.StopServerFirst);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("[x]");
    Assert.Empty(Asked());
    Assert.Contains(Alpha, Installed(tui));

    // Once the server stopped, everything can be chosen again.
    await tui.SetServerRunning(false);
    await tui.WaitUntilGone(ModsText.ServerRuns);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    Assert.DoesNotContain(CommonText.StopServerFirst, tui.Screen);
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ]");
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(StartOf(ModsText.InstallFromSourceDetail));
    Assert.Contains(select, tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.TypeSource);
  }

  [Fact]
  public async Task Request_InstallFromSource_WhileTheServerRuns_SaysToStopIt() {
    using TuiHarness tui = Start(Server);
    await tui.Settle();
    await tui.SetServerRunning(true);

    await tui.Invoke(() => tui.Context.Navigate(PageId.Mods, PageRequest.InstallFromSource));
    await tui.WaitFor(CommonText.StopServerFirst);

    Assert.Contains($"─ {ModsText.InstallTitle} ─", tui.Screen);
    Assert.DoesNotContain(ModsText.TypeSource, tui.Screen);
  }

  [Fact]
  public async Task AnotherWorkspace_ShowsItsOwnPackages() {
    using TuiHarness tui = Start(Server);
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddFile(StatePath(other), new MockFileData(State(Package(Beta, "9.9.9", true))));
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[x]");

    await tui.Invoke(() => {
      Assert.True(tui.Context.AddWorkspace(other, out string? error), error);
    });

    await tui.WaitFor("9.9.9");
    Assert.DoesNotContain(Alpha, tui.Screen);
    Assert.DoesNotContain("[x]", tui.Screen);
    Assert.Contains(ModsText.Installed(1), tui.Screen);
    Assert.Contains(CommonText.NewServer, tui.Screen);
  }

  [Fact]
  public async Task ChangedFromAnotherPage_IsReadAgainWhenShown() {
    using TuiHarness tui = Start(
        State(Package(Game, "1.21.0", true), Package(Lib, "2.0.0", false)));
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Lib);

    // Only what the user did not install: the action rows keep the focus, and lead to mods.
    Assert.DoesNotContain(ModsText.UpdateAll, tui.Screen);
    Assert.Contains(ModsText.InstallFromSourceDetail, tui.Screen);

    // Another page installs a mod.
    await tui.GoTo(PageId.Home);
    tui.Fs.File.WriteAllText(StatePath(tui.Root), Server);
    await tui.Invoke(() => tui.Context.NotifyWorkspaceChanged());
    await tui.GoTo(PageId.Mods);

    await tui.WaitFor("[ ] " + Alpha);
    Assert.Contains(ModsText.UpdateAll, tui.Screen);
    Assert.Contains(ModsText.ExplicitDetail, tui.Screen);
  }

  [Fact]
  public async Task UnreadableState_IsSaid_AndTheActionRowsStay() {
    using TuiHarness tui = Start("{ not json");

    await tui.GoTo(PageId.Mods);
    await tui.WaitUntilGone(CommonText.Loading);

    Assert.Contains(ModsText.InstallFromSource, tui.Screen);
    Assert.DoesNotContain(CommonText.NoMods, tui.Screen);
    Assert.DoesNotContain(CommonText.NewServer, tui.Screen);
  }
}
