using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Mods;
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

  private readonly Mock<IArchiveProvider> _archives = new();

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

  /// An installed package without files; all but the game are of a commit of a repository.
  private static string Package(string id, string version, bool isExplicit) => $$"""
      { "manifest": { "format": 4, "id": "{{id}}", "version": "{{version}}",
          "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
        "variant": "", "explicit": {{(isExplicit ? "true" : "false")}},
        "ref": "{{id}}@{{version}}", {{(id == Game ? "" : CommitField)}}"files": [] }
      """;

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

    return TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      FileSystem = fs,
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
    await tui.WaitFor(ModsText.Empty);
    tui.Snapshot("mods-empty");

    // Without a game, the first row leads to a new server; the list offers nothing to update.
    string screen = tui.Screen;
    int newServer = screen.IndexOf(ModsText.NewServer, StringComparison.Ordinal);
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
    await tui.WaitFor(ModsText.Empty);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(_ => tui.Context.CurrentPage == PageId.GetMods, "the Get mods page");

    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ModsText.Empty);
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(_ => tui.Context.CurrentPage == PageId.Workspaces, "the Workspaces page");
  }

  [Fact]
  public async Task WithAGameOnly_OffersGetMods_ButNoNewServer() {
    using TuiHarness tui = Start(State(Package(Game, "1.21.0", true)));

    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Game);

    Assert.DoesNotContain(ModsText.NewServer, tui.Screen);
    Assert.DoesNotContain(ModsText.UpdateAll, tui.Screen);
    Assert.Contains(ModsText.InstallFromSourceDetail, tui.Screen);
    Assert.DoesNotContain(ModsText.Empty, tui.Screen);
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
  public async Task Uninstall_ShowsThePlan_AndCancelChangesNothing() {
    using TuiHarness tui = Start(Server);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Snapshot("mods-menu");
    tui.Press(ConsoleKey.DownArrow, 2);
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

  [Fact]
  public async Task Uninstall_Applied_RemovesThePackage_AndReadsTheListAgain() {
    using TuiHarness tui = Start(Server, Language.ZhHans);
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Alpha);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.Uninstall);
    tui.Snapshot("mods-menu");
    tui.Press(ConsoleKey.DownArrow, 2);
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
    Assert.DoesNotContain(ModsText.UpdateTo, tui.Screen);
    Assert.DoesNotContain(ModsText.Uninstall, tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(_ => tui.Context.CurrentPage == PageId.Workspaces, "the Workspaces page");
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
    tui.Press(ConsoleKey.DownArrow);
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
    tui.Press(ConsoleKey.DownArrow);
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
    // The update began with the first mod, whose archive the test does not have.
    Assert.Contains(
        Asked(), call => call.StartsWith($"OpenCommit({Alpha},", StringComparison.Ordinal));
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
  public async Task Request_InstallFromSource_OpensThePrompt_AndEscClosesIt() {
    using TuiHarness tui = Start(state: null);
    await tui.Settle();

    await tui.Invoke(() => tui.Context.Navigate(PageId.Mods, PageRequest.InstallFromSource));
    await tui.WaitFor(ModsText.SourcePlaceholder);
    Assert.Contains($"─ {ModsText.InstallTitle} ─", tui.Screen);

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.SourcePlaceholder);
    Assert.Empty(Asked());
    Assert.Equal(PageId.Mods, tui.Context.CurrentPage);
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
    Assert.Contains(ModsText.NewServer, tui.Screen);
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
    Assert.DoesNotContain(ModsText.Empty, tui.Screen);
    Assert.DoesNotContain(ModsText.NewServer, tui.Screen);
  }
}
