using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Moq;

namespace Lip.Cli.Tests.Tui;

/// The Get mods page of the interface, driven by the keys of a fake terminal.
public sealed class GetModsPageTests {
  // Two mods for a server, one of them with everything a mod can have, and one for clients.
  private const string Index = """
      {
        "mods": [
          {
            "id": "github.com/acme/Alpha", "name": "Alpha", "owner": "acme",
            "description": "An economy core", "tags": ["type:mod", "money"],
            "side": ["server"], "platform": ["win-x64"], "gameVersions": ["1.26.60"],
            "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}, "stars": 7, "license": "MIT",
            "updated": "2026-11-01T00:00:00Z", "created": "2025-01-01T00:00:00Z",
            "url": "https://levimc.org/mods/github.com/acme/Alpha/", "requiredBy": 2
          },
          {
            "id": "github.com/acme/Beta", "name": "Beta", "owner": "acme",
            "description": "领地系统", "tags": ["land"],
            "side": ["server", "client"], "platform": ["win-x64"], "stars": 40,
            "updated": "2026-10-01T00:00:00Z", "created": "2026-01-01T00:00:00Z"
          },
          {
            "id": "github.com/acme/Shaders", "name": "Shaders", "owner": "acme",
            "description": "Client only", "tags": ["shader"],
            "side": ["client"], "platform": ["win-x64"], "stars": 99
          }
        ]
      }
      """;

  private const string AlphaDetail = """
      {
        "id": "github.com/acme/Alpha", "license": "MIT",
        "latest": {"variant": "", "version": "1.2.0"},
        "versions": [
          {
            "version": "1.2.0", "prerelease": false, "released": "2026-11-01T00:00:00Z",
            "variants": {
              "": [{"side": "server"}], "lua": [{"side": "server"}], "client": [{"side": "client"}]
            }
          },
          {"version": "1.1.0", "prerelease": true, "variants": {"": [{"side": "server"}]}}
        ],
        "readmes": [{"lang": "en", "text": "# Alpha\nThe readme of Alpha."}]
      }
      """;

  private const string BetaDetail = """
      { "id": "github.com/acme/Beta", "latest": {"variant": "", "version": "0.3.0"},
        "versions": [ {"version": "0.3.0"} ] }
      """;

  private static string Catalog(string url) =>
      url == ModCatalogClient.BaseUrl + "index.json" ? Index
          : url == ModCatalogClient.DetailUrl("github.com/acme/Alpha") ? AlphaDetail
          : BetaDetail;

  private static TuiHarness Start(
      Language language = Language.En,
      int width = 120,
      int height = 36,
      Func<string, string>? catalog = null,
      MockFileSystem? fs = null,
      Mock<IArchiveProvider>? archives = null) {
    fs ??= new MockFileSystem(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    return TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
      FileSystem = fs,
      Catalog = catalog ?? Catalog,
      // A client whose archives are those of the test: nothing is read from the network.
      CreateClient = archives is null ? null : (interaction, hookOutput, root) => new LipClient(
          fs, interaction, new CacheService(fs, interaction), new ConfigService(fs, interaction),
          new McmodService(
              fs, interaction, new LockStore(fs), archives.Object, new ArchiveReader(),
              new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => root)),
    });
  }

  /// The row of the screen that shows the text.
  private static int RowOf(TuiHarness tui, string text) =>
      Array.FindIndex(tui.Screen.Split('\n'), row => row.Contains(text, StringComparison.Ordinal));

  [Theory]
  [InlineData(Language.En, "Get mods")]
  [InlineData(Language.ZhHans, "获取模组")]
  [InlineData(Language.ZhHant, "取得模組")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.GetMods);

    Assert.Equal(title, tui.Text(() => GetModsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Fact]
  public async Task Mods_ForAServer_AreListedWithTheDetailsOfTheSelectedOne() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(
        Path.Join(TuiHarness.DefaultRoot, ".lip", "state.json"),
        new MockFileData(
            TuiHarness.StateWithGame.Replace("github.com/LiteLDev/bds", "github.com/acme/Alpha")));
    using TuiHarness tui = Start(fs: fs);

    await tui.GoTo(PageId.GetMods);

    // The mods that have a build for a server; the installed one is marked.
    await tui.WaitFor("✓ Alpha · acme");
    Assert.Contains("  Beta · acme", tui.Screen);
    Assert.DoesNotContain("Shaders", tui.Screen);
    Assert.Contains("2 mods · ✓ Installed", tui.Screen);
    Assert.Contains("★ 7  2026-11-01", tui.Screen);

    // The details of the selected mod; its file is read once the selection rests.
    Assert.Contains("An economy core", tui.Screen);
    Assert.Contains("github.com/acme/Alpha", tui.Screen);
    Assert.Contains("win-x64", tui.Screen);
    Assert.Contains("1.26.60", tui.Screen);
    Assert.Contains("Economy", tui.Screen);
    Assert.Contains("MIT", tui.Screen);
    Assert.Contains("LeviLamina 26.60.*", tui.Screen);
    await tui.WaitFor("1.2.0");
    Assert.Contains("↑↓ Move  Enter Actions  Tab Next  Esc Back", tui.Screen);

    // ↓ selects the next mod, whose details follow.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("领地系统");
    await tui.WaitFor("0.3.0");
    Assert.DoesNotContain("An economy core", tui.Screen);
  }

  [Fact]
  public async Task Search_FiltersTheModsAsTheUserTypes_InSimplifiedChinese() {
    using TuiHarness tui = Start(Language.ZhHans);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("2 个模组");
    Assert.Contains("搜索", tui.Screen);
    Assert.Contains("经济", tui.Screen);

    // ↑ from the first mod goes to the filters, the order, and the search.
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Type("领地");

    await tui.WaitFor("1 个模组");
    Assert.Contains("Beta · acme", tui.Screen);
    Assert.DoesNotContain("Alpha · acme", tui.Screen);
    Assert.Contains("Enter 结果", tui.Screen);

    // Enter goes on to the mods that were found.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter 操作");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("安装最新版");
    Assert.Contains("选择版本…", tui.Screen);
    Assert.Contains("在浏览器中打开", tui.Screen);
    tui.Press(ConsoleKey.Escape);

    // Erasing the search shows every mod again.
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Press(ConsoleKey.Backspace, 2);
    await tui.WaitFor("2 个模组");
  }

  [Fact]
  public async Task Search_ThatMatchesNothing_SaysSo() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("2 mods");

    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Type("nothing-like-this");

    await tui.WaitFor("No mods match.");
    Assert.Contains("0 mods", tui.Screen);
  }

  [Fact]
  public async Task Sort_IsChangedWithLeftAndRight() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("‹ Relevance ›");
    Assert.True(RowOf(tui, "Alpha · acme") < RowOf(tui, "Beta · acme"));

    // The order is the second control from the mods.
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor("←→ Change");
    tui.Press(ConsoleKey.RightArrow);

    await tui.WaitFor("‹ Stars ›");
    Assert.True(RowOf(tui, "Beta · acme") < RowOf(tui, "Alpha · acme"));

    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("‹ Relevance ›");
    Assert.True(RowOf(tui, "Alpha · acme") < RowOf(tui, "Beta · acme"));
  }

  [Fact]
  public async Task Filters_AreChosenInThePanel_AndCleared_InTraditionalChinese() {
    using TuiHarness tui = Start(Language.ZhHant);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("2 個模組");

    // Enter on the row of the filters opens the panel: a row that clears, and the sections.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("Enter 開啟");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("清除篩選");
    Assert.Contains("▸ 遊戲版本", tui.Screen);
    Assert.Contains("▸ 標籤", tui.Screen);
    Assert.Contains("▸ 授權條款", tui.Screen);
    Assert.Contains("▸ 相依於", tui.Screen);
    Assert.DoesNotContain("[ ]", tui.Screen);

    // Enter on a heading shows its values; Space on a value chooses it.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] 1.26.60");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Space 切換");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+] 1.26.60");
    await tui.WaitFor("1 個模組");
    Assert.Contains("篩選 (1)", tui.Screen);
    Assert.Contains("已選 1", tui.Screen);
    Assert.Contains("包含", tui.Screen);

    // The row that clears is the first one.
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] 1.26.60");
    await tui.WaitFor("2 個模組");

    // Esc closes the panel; the row of the filters says that none is chosen.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("清除篩選");
    Assert.Contains("無", tui.Screen);
  }

  [Fact]
  public async Task Filters_OfTagsAndDependencies_IncludeThenExclude() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("2 mods");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Clear filters");

    // The dependencies are the last section: ↑ from the first row goes round to it.
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] LeviLamina");
    tui.Press(ConsoleKey.DownArrow);

    // Space: the mods that depend on it.
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+] LeviLamina");
    await tui.WaitFor("1 mod ");
    Assert.Contains("Included", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Clear filters");
    Assert.Contains("Alpha · acme", tui.Screen);
    Assert.DoesNotContain("Beta · acme", tui.Screen);

    // The panel opens as it was left. Space again: the mods that do not depend on it.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[+] LeviLamina");
    tui.Press(ConsoleKey.DownArrow, 4);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[-] LeviLamina");
    Assert.Contains("Excluded", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Clear filters");
    Assert.Contains("Beta · acme", tui.Screen);
    Assert.DoesNotContain("Alpha · acme", tui.Screen);

    // And again: all of them.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[-] LeviLamina");
    tui.Press(ConsoleKey.DownArrow, 4);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[ ] LeviLamina");
    await tui.WaitFor("2 mods");

    // A license family is chosen or not, with nothing to exclude; the page says how many
    // filters are chosen once the panel is closed.
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] Permissive");
    Assert.Contains("[ ] No license", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+] Permissive");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[ ] Permissive");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+] Permissive");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("1 active");
    Assert.Contains("1 mod ", tui.Screen);
  }

  [Fact]
  public async Task InstallLatest_PlansTheInstallOfTheMod() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");

    // Enter on a mod shows its actions; the first installs the latest version.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install latest");
    Assert.Contains("Choose version…", tui.Screen);
    Assert.Contains("README", tui.Screen);
    Assert.Contains("Open in browser", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    // The archives of the test give nothing: the dry run of lip install fails, with the
    // package argument of the mod.
    await tui.WaitFor("Install github.com/acme/Alpha · Failed");
    Assert.NotEmpty(archives.Invocations);
  }

  [Fact]
  public async Task ChooseVersion_OffersTheVariantsTheVersionsAndACustomOne() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Choose version…");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // The newest version of the default variant is selected; the last row says what the
    // selected one installs.
    await tui.WaitFor("Variant  ‹ default ›");
    Assert.Contains("1.2.0", tui.Screen);
    Assert.Contains("prerelease", tui.Screen);
    Assert.Contains("Custom…", tui.Screen);
    Assert.Contains("github.com/acme/Alpha@1.2.0", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Select  Esc Close", tui.Screen);

    // The variant "lua" has a build for a server in 1.2.0 only; "client" has none at all.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("←→ Change");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("Variant  ‹ lua ›");
    await tui.WaitUntilGone("1.1.0");
    Assert.Contains("github.com/acme/Alpha#lua", tui.Screen);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("Variant  ‹ default ›");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("Variant  ‹ lua ›");

    // The last row asks for what follows the @; nothing is not taken, and Esc comes back.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("github.com/acme/Alpha#lua@…");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Custom version");
    Assert.Contains("github.com/acme/Alpha#lua@", tui.Screen);
    Assert.Contains("branch:main", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Variant  ‹ lua ›");

    // A version of the variant is installed as the package argument says.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("github.com/acme/Alpha#lua@1.2.0");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install github.com/acme/Alpha#lua@1.2.0 · Failed");
  }

  [Fact]
  public async Task ChooseVersion_Custom_InstallsWhatTheUserWrote() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Choose version…");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    // Beta has one variant: no row for it. ↓ from its only version is the custom one.
    await tui.WaitFor("github.com/acme/Beta@0.3.0");
    Assert.DoesNotContain("‹", tui.Screen.Split('\n').First(row => row.Contains("0.3.0")));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/acme/Beta@");
    tui.Type("branch:main");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Install github.com/acme/Beta@branch:main · Failed");
  }

  [Fact]
  public async Task Readme_IsShownInAViewer_AndTheBrowserOpensThePageOfTheMod() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Open in browser");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("The readme of Alpha.");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("The readme of Alpha.");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Open in browser");
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Open in browser");
    await tui.Settle();
    lock (tui.OpenedUrls) {
      Assert.Equal(["https://levimc.org/mods/github.com/acme/Alpha/"], tui.OpenedUrls);
    }

    // Beta has no README and no page: the viewer says so, and the last action is skipped.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Open in browser");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("No README.");
  }

  [Fact]
  public async Task Actions_OfAModWhoseFileCannotBeRead_StillWork() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(
        catalog: url => url == ModCatalogClient.DetailUrl("github.com/acme/Alpha")
            ? throw new IOException("offline")
            : Catalog(url),
        archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");

    // The README cannot be shown: the bottom bar says why.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Open in browser");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Could not read the mod catalogue");
    Assert.DoesNotContain("Failed", tui.Screen);

    // No version is listed, and the user can still write one.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Choose version…");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/acme/Alpha@…");
    Assert.DoesNotContain("1.2.0", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Custom…");

    // The latest version is installed in the default variant.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install latest");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install github.com/acme/Alpha · Failed");
  }

  [Fact]
  public async Task Catalogue_ThatCannotBeRead_OffersToRetry() {
    bool offline = true;
    using TuiHarness tui = Start(
        catalog: url => offline ? throw new IOException("offline") : Catalog(url));

    await tui.GoTo(PageId.GetMods);

    // Not a failure of a command: a short message, why, and a button.
    await tui.WaitFor("Cannot read the catalogue.");
    Assert.Contains("offline", tui.Screen);
    Assert.Contains("[ Retry ]", tui.Screen);
    Assert.Contains("Enter Retry  Esc Back", tui.Screen);
    Assert.DoesNotContain("Failed", tui.Screen);

    // Enter reads it again.
    offline = false;
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Alpha · acme");
    Assert.DoesNotContain("[ Retry ]", tui.Screen);
    Assert.Equal(0, await tui.Quit());
  }

  [Fact]
  public async Task Catalogue_ReadWhileAnotherWorkspaceIsSelected_IsShown() {
    using ManualResetEventSlim release = new();
    using TuiHarness tui = Start(catalog: url => {
      release.Wait(TimeSpan.FromSeconds(30));
      return Catalog(url);
    });
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddDirectory(other);
    try {
      await tui.GoTo(PageId.GetMods);
      await tui.WaitFor(CommonText.Loading);

      // The catalogue is of no workspace: what is read of it counts whichever one is
      // selected when it arrives.
      Assert.True(await tui.Invoke(() => tui.Context.AddWorkspace(other, out _)));
      await tui.WaitFor("lip 1.2.3 · " + other);
      release.Set();

      await tui.WaitFor("Alpha · acme");
      Assert.DoesNotContain(CommonText.Loading, tui.Screen.Split('\n')[2]);
    }
    finally {
      release.Set();
    }
  }

  [Fact]
  public async Task Tab_GoesRoundTheSearchTheModsAndTheSidebar_AndTheBarNamesTheKeys() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    Assert.Contains("↑↓ Move  Enter Actions  Tab Next  Esc Back", tui.Screen);

    // The details fit: Tab goes on from the mods to the sidebar, and from there to the
    // search, the first area of the page.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Results  Tab Next  Esc Back");
    tui.Type("beta");
    await tui.WaitUntilGone("Alpha · acme");
    tui.Press(ConsoleKey.Tab, 3);
    await tui.WaitFor("Enter Actions  Tab Next  Esc Back");

    // Shift+Tab from the sidebar comes back to the last area: the mods.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Actions  Tab Next  Esc Back");
  }

  [Fact]
  public async Task Details_OnASmallTerminal_AreBelowTheModsAndScroll() {
    using TuiHarness tui = Start(width: 60, height: 16);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    Assert.True(RowOf(tui, "─ Alpha ─") > RowOf(tui, "Beta · acme"));
    Assert.Contains("An economy core", tui.Screen);

    // Tab goes from the mods to the details, which have more rows than fit.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("↑↓ Scroll");
    tui.Press(ConsoleKey.End);
    await tui.WaitFor("Updated  ");
    await tui.WaitUntilGone("An economy core");

    // ↑ at their start goes back to the mods, and ↓ on the last mod to the details again.
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor("An economy core");
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("Enter Actions");
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor("↑↓ Scroll");
    Assert.Contains("─ Beta ─", tui.Screen);

    // Esc goes back to the mods, and then to the sidebar.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Enter Actions");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Enter Actions");
  }

  [Fact]
  public async Task Tab_GoesRoundTheAreasOfThePage() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");

    // Shift+Tab from the mods goes to the filters, the order, and the search.
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Open");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("←→ Change");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("Enter Results");

    // Tab goes the other way, and from the mods on to the sidebar: the details all fit.
    tui.Press(ConsoleKey.Tab, 3);
    await tui.WaitFor("Enter Actions");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntilGone("Enter Actions");
    Assert.False(await tui.Invoke(() => tui.Context.ContentFocused));
  }

  /// The frames of the page, for visual review (TuiHarness.Snapshot), in every language and at
  /// both sizes.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Frames_ShowEveryPartOfThePage(Language language, int width, int height) {
    using TuiHarness tui = Start(language, width, height);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    await tui.WaitFor(screen => !screen.Contains(CommonText.Loading, StringComparison.Ordinal));
    tui.Snapshot("getmods-list");

    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Type("a");
    await tui.WaitFor($"Enter {GetModsText.HintResults.Text}");
    tui.Snapshot("getmods-search");
    tui.Press(ConsoleKey.Backspace);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.HintChange.Text);
    tui.Snapshot("getmods-sort");

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.ClearFilters);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor($"[+] {GetModsText.Tag("economy", "Economy")}");
    tui.Snapshot("getmods-filters");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Spacebar, 2);
    await tui.WaitFor($"[-] {GetModsText.Tag("management", "Server management")}");
    tui.Snapshot("getmods-filters-excluded");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GetModsText.ClearFilters);
    tui.Snapshot("getmods-filtered");

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.InstallLatest);
    tui.Snapshot("getmods-actions");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Custom);
    tui.Snapshot("getmods-versions");
    tui.Press(ConsoleKey.UpArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.CustomTitle);
    tui.Snapshot("getmods-custom");
    tui.Press(ConsoleKey.Escape, 2);
    await tui.WaitUntilGone(GetModsText.Custom);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.InstallLatest);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("The readme of Alpha.");
    tui.Snapshot("getmods-readme");
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Frames_ShowTheCatalogueThatCannotBeRead(
      Language language, int width, int height) {
    using TuiHarness tui = Start(
        language, width, height,
        _ => throw new IOException("No such host is known. (levimc.org:443)"));

    await tui.GoTo(PageId.GetMods);

    await tui.WaitFor($"[ {CommonText.Retry} ]");
    Assert.Contains(GetModsText.Unavailable, tui.Screen);
    tui.Snapshot("getmods-unavailable");
  }
}
