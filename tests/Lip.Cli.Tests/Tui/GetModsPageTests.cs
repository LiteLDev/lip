using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Pages.Server;
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
            "downloads": 55912,
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

  // The default variant has a version for each of three LeviLamina; "lua" has two of them, and
  // "client" has no build for a server.
  private const string AlphaDetail = """
      {
        "id": "github.com/acme/Alpha", "license": "MIT",
        "latest": {"variant": "", "version": "1.3.0"},
        "versions": [
          {
            "version": "1.3.0", "prerelease": false, "released": "2026-11-10T00:00:00Z",
            "variants": {
              "": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.70.*"}}]
            }
          },
          {
            "version": "1.2.0", "prerelease": false, "released": "2026-11-01T00:00:00Z",
            "variants": {
              "": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}}],
              "lua": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}}],
              "client": [{"side": "client"}]
            }
          },
          {
            "version": "1.1.0", "prerelease": true,
            "variants": {
              "": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}}]
            }
          },
          {
            "version": "1.0.0", "prerelease": false,
            "variants": {
              "": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.50.*"}}],
              "lua": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.50.*"}}]
            }
          }
        ],
        "readmes": [{"lang": "en", "text": "# Alpha\nThe readme of Alpha."}]
      }
      """;

  private const string BetaDetail = """
      { "id": "github.com/acme/Beta", "latest": {"variant": "", "version": "0.3.0"},
        "versions": [ {"version": "0.3.0"} ] }
      """;

  // The variants of a mod that has many, with long names; none depends on anything.
  private static readonly string[] ManyVariants = [
    "", "lua", "nodejs", "python", "quickjs-engine", "dotnet-runtime-hosted",
    "experimental-native", "legacy-script-engine",
  ];

  private static readonly string ManyDetail = $$"""
      {
        "id": "github.com/acme/Alpha", "latest": {"variant": "", "version": "2.0.0"},
        "versions": [ { "version": "2.0.0", "variants": {
          {{string.Join(", ", ManyVariants.Select(v => $$"""
              "{{v}}": [{"side": "server"}]
              """))}}
        } } ]
      }
      """;

  private static string CatalogOfMany(string url) =>
      url == ModCatalogClient.DetailUrl("github.com/acme/Alpha") ? ManyDetail : Catalog(url);

  /// A state file with the packages, each as an id and a version.
  private static string State(params (string Id, string Version)[] packages) =>
      """{ "format": 4, "side": "server", "platform": "win-x64", "packages": [ """
          + string.Join(", ", packages.Select(package => $$"""
              { "manifest": { "format": 4, "id": "{{package.Id}}",
                  "version": "{{package.Version}}",
                  "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
                "variant": "", "explicit": true,
                "ref": "{{package.Id}}@{{package.Version}}", "files": [] }
              """))
          + " ] }";

  /// A file system whose working directory has the game and LeviLamina at the versions given,
  /// each unless null, and the other packages.
  private static MockFileSystem Workspace(
      string? game = "1.26.60", string? loader = "26.60.7", params string[] others) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    List<(string, string)> packages = [];
    if (game is not null) {
      packages.Add(("github.com/LiteLDev/bds", game));
    }

    if (loader is not null) {
      packages.Add(("github.com/LiteLDev/LeviLamina", loader));
    }

    packages.AddRange(others.Select(id => (id, "1.0.0")));
    if (packages.Count > 0) {
      fs.AddFile(
          Path.Join(TuiHarness.DefaultRoot, ".lip", "state.json"),
          new MockFileData(State([.. packages])));
    }

    return fs;
  }

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
    // A workspace with a game and a LeviLamina, unless the test has its own.
    fs ??= Workspace();
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
    using TuiHarness tui = Start(fs: Workspace(others: "github.com/acme/Alpha"));

    await tui.GoTo(PageId.GetMods);

    // The mods that have a build for a server; the installed one is marked.
    await tui.WaitFor("✓ Alpha · acme");
    Assert.Contains("  Beta · acme", tui.Screen);
    Assert.DoesNotContain("Shaders", tui.Screen);
    Assert.Contains("2 mods · ✓ Installed", tui.Screen);
    // Its downloads, its stars and the day; Beta has no count of downloads.
    Assert.Contains("↓ 55.9k     ★ 7  2026-11-01", tui.Screen);
    Assert.Contains("★ 40  2026-10-01", tui.Screen);
    Assert.DoesNotContain("↓", tui.Screen.Split('\n')[RowOf(tui, "Beta · acme")]);

    // The details of the selected mod; its file is read once the selection rests.
    Assert.Contains("An economy core", tui.Screen);
    Assert.Contains("github.com/acme/Alpha", tui.Screen);
    Assert.Contains("win-x64", tui.Screen);
    Assert.Contains("1.26.60", tui.Screen);
    Assert.Contains("Economy", tui.Screen);
    Assert.Contains("MIT", tui.Screen);
    Assert.Contains("LeviLamina 26.60.*", tui.Screen);
    Assert.Contains("Downloads  55,912", tui.Screen);
    await tui.WaitFor("1.3.0");
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
    await tui.WaitFor("安装…");
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

    await tui.WaitFor("‹ Popularity ›");
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
  public async Task Install_AlwaysAsksForTheVersion_AndPlansTheInstallOfIt() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");

    // Enter on a mod shows its actions; none installs without asking for the version.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install…");
    Assert.Contains("README", tui.Screen);
    Assert.Contains("Open in browser", tui.Screen);
    Assert.DoesNotContain("Install latest", tui.Screen);
    Assert.DoesNotContain("Choose version", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    // The newest version that fits LeviLamina 26.60.7 is selected.
    await tui.WaitFor("github.com/acme/Alpha@1.2.0");
    tui.Press(ConsoleKey.Enter);

    // The archives of the test give nothing: the dry run of lip install fails, with the
    // package argument of the version.
    await tui.WaitFor("Install github.com/acme/Alpha@1.2.0 · Failed");
    Assert.NotEmpty(archives.Invocations);
  }

  [Fact]
  public async Task Versions_AreThoseThatFitTheWorkspace_UnderTheVariantsAsTabs() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install…");
    tui.Press(ConsoleKey.Enter);

    // Every variant with a build for a server is a tab, the chosen one in brackets; "client"
    // has none. Only the versions for LeviLamina 26.60 are listed: not 1.3.0, not 1.0.0.
    await tui.WaitFor("[default] lua");
    Assert.DoesNotContain("client", tui.Screen);
    Assert.Contains("1.2.0", tui.Screen);
    Assert.Contains("1.1.0", tui.Screen);
    Assert.Contains("prerelease", tui.Screen);
    Assert.DoesNotContain("2026-11-10", tui.Screen);
    Assert.DoesNotContain("1.0.0 ", tui.Screen);
    Assert.Contains("Custom…", tui.Screen);
    Assert.Contains("github.com/acme/Alpha@1.2.0", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Select  ←→ Variant  Tab Next  Esc Close", tui.Screen);

    // → chooses the next variant from the versions, and → again goes round.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(" default [lua]");
    await tui.WaitUntilGone("1.1.0");
    Assert.Contains("github.com/acme/Alpha#lua@1.2.0", tui.Screen);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("[default] lua");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor(" default [lua]");

    // ↑ on the first version and Tab go to the tabs; ↓ and Tab come back.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("←→ Change  ↓/Tab Next  Esc Close");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("[default] lua");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(" default [lua]");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter Select");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Change");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Select");

    // The last row asks for what follows the @; nothing is not taken, and Esc comes back.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("github.com/acme/Alpha#lua@…");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Custom version");
    Assert.Contains("github.com/acme/Alpha#lua@", tui.Screen);
    Assert.Contains("branch:main", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(" default [lua]");

    // A version of the variant is installed as the package argument says.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("github.com/acme/Alpha#lua@1.2.0");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install github.com/acme/Alpha#lua@1.2.0 · Failed");
  }

  [Fact]
  public async Task Versions_Custom_InstallsWhatTheUserWrote() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(archives: archives);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install…");
    tui.Press(ConsoleKey.Enter);

    // Beta has the default variant only: no tabs. ↓ from its only version is the custom one.
    await tui.WaitFor("github.com/acme/Beta@0.3.0");
    Assert.DoesNotContain("[default]", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Select  Esc Close", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/acme/Beta@");
    tui.Type("branch:main");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Install github.com/acme/Beta@branch:main · Failed");
  }

  [Theory]
  [InlineData(Language.En, "26.40.0", "No version fits LeviLamina 26.40.0.")]
  [InlineData(Language.ZhHans, "26.40.0", "没有适用于 LeviLamina 26.40.0 的版本。")]
  [InlineData(Language.ZhHant, null, "沒有適用於 Minecraft 1.26.60 且不需 LeviLamina 的版本。")]
  public async Task Versions_WhenNoneFits_SayWhatTheWorkspaceHas(
      Language language, string? loader, string message) {
    using TuiHarness tui = Start(language, fs: Workspace(loader: loader));
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);

    // One line says so; the row that asks for a version is all that is left.
    await tui.WaitFor(message);
    Assert.Contains(GetModsText.Custom, tui.Screen);
    Assert.Contains("github.com/acme/Alpha@…", tui.Screen);
    Assert.DoesNotContain("1.2.0", tui.Screen);
    Assert.DoesNotContain("2026-11-10", tui.Screen);
    tui.Snapshot("getmods-versions-none");
  }

  [Theory]
  [InlineData(Language.En, 60)]
  [InlineData(Language.ZhHans, 60)]
  [InlineData(Language.ZhHant, 60)]
  [InlineData(Language.En, 120)]
  public async Task Variants_ThatDoNotFit_ScrollOnOneRow(Language language, int width) {
    using TuiHarness tui = Start(language, width, width == 60 ? 16 : 36, CatalogOfMany);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);
    string first = $"[{GetModsText.DefaultVariant}]";
    await tui.WaitFor(first);
    tui.Snapshot("getmods-variants-first");

    // The row of the tabs starts at the first variant: more follow at the right only.
    string[] rows = tui.Screen.Split('\n');
    int tabs = Array.FindIndex(rows, row => row.Contains(first, StringComparison.Ordinal));
    int custom = RowOf(tui, GetModsText.Custom);
    // The columns of the frame of the dialog, at the left and at the right of the tabs.
    static (int, int) Frame(string row) {
      int tab = row.IndexOf('[');
      return (
          TextWidth.Of(row[..row.LastIndexOf('│', tab)]),
          TextWidth.Of(row[..row.IndexOf('│', tab)]));
    }

    (int, int) frame = Frame(rows[tabs]);
    Assert.Equal(custom, tabs + 3);
    Assert.DoesNotContain("‹", rows[tabs]);
    Assert.Contains("›", rows[tabs]);

    // → goes through every variant: each is shown whole, in brackets, on that same row,
    // which stays inside the frame; nothing below it moves.
    foreach (string variant in ManyVariants.Skip(1)) {
      tui.Press(ConsoleKey.RightArrow);
      await tui.WaitFor($"[{variant}]");
      rows = tui.Screen.Split('\n');
      Assert.Contains($"[{variant}]", rows[tabs]);
      Assert.Equal(1, rows.Count(row => row.Contains($"[{variant}]", StringComparison.Ordinal)));
      Assert.Equal(frame, Frame(rows[tabs]));
      Assert.Equal(custom, RowOf(tui, GetModsText.Custom));
      Assert.Contains($"github.com/acme/Alpha#{variant}@2.0.0", tui.Screen);
    }

    // At the last variant, more are at the left only.
    tui.Snapshot("getmods-variants-last");
    Assert.Contains("‹", rows[tabs]);
    Assert.DoesNotContain("›", rows[tabs]);

    // ← from there scrolls back; → from the last goes round to the first.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor($"[{ManyVariants[^2]}]");
    tui.Snapshot("getmods-variants-middle");
    tui.Press(ConsoleKey.RightArrow, 2);
    await tui.WaitFor(first);
    Assert.DoesNotContain("‹", tui.Screen.Split('\n')[tabs]);
  }

  [Fact]
  public async Task Install_InAWorkspaceWithoutAGame_OffersANewServerInstead() {
    MockFileSystem fs = Workspace(game: null, loader: null);
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      FileSystem = fs,
      Catalog = Catalog,
      ServerSetup = harness => new FakeServerSetup(harness),
    });
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install…");
    tui.Press(ConsoleKey.Enter);

    // No version is asked for: a short message, and a button that leads to a server.
    await tui.WaitFor("This folder has no server yet. Create one first.");
    Assert.Contains("[ New server… ]", tui.Screen);
    Assert.DoesNotContain("Custom…", tui.Screen);
    tui.Snapshot("getmods-no-server");

    // Esc changes nothing.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Create one first.");
    Assert.Equal(PageId.GetMods, await tui.Invoke(() => tui.Context.CurrentPage));

    // Enter on the button starts the new server wizard.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install…");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ New server… ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.Workspaces, "the workspaces");
  }

  [Fact]
  public async Task Install_WhileTheServerRuns_CannotBeChosen() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Servers = servers,
        Catalog = Catalog,
      });
      await tui.Settle();
      await tui.Invoke(() => tui.Context.Navigate(PageId.Server, PageRequest.StartServer));
      // The client of the test reads the configuration of the user: the language may be any.
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.Invoke(() => tui.Context.Navigate(PageId.GetMods));
      await tui.WaitFor("Alpha · acme");

      // The action says why it cannot be chosen; the README is the first that can.
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(GetModsText.InstallWhileRunning);
      tui.Press(ConsoleKey.UpArrow, 2);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("The readme of Alpha.");
      Assert.DoesNotContain("Custom…", tui.Screen);
    }
    finally {
      foreach (WorkspaceServer server in servers.Running()) {
        server.Send("exit");
        for (int i = 0; i < 200 && server.Running; i++) {
          await Task.Delay(25, TestContext.Current.CancellationToken);
        }
      }

      try {
        Directory.Delete(root, recursive: true);
      }
      catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
        // The server still runs.
      }
    }
  }

  // Three mods whose four orders all differ: Zeta has many downloads and no star, Eta many
  // stars and no count of downloads, Theta a few stars and no download.
  private const string OrderedIndex = """
      { "mods": [
        { "id": "github.com/acme/Zeta", "name": "Zeta", "owner": "acme", "side": ["server"],
          "stars": 0, "downloads": 1500000,
          "updated": "2026-01-01T00:00:00Z", "created": "2024-01-01T00:00:00Z" },
        { "id": "github.com/acme/Eta", "name": "Eta", "owner": "acme", "side": ["server"],
          "stars": 1200,
          "updated": "2026-03-01T00:00:00Z", "created": "2023-01-01T00:00:00Z" },
        { "id": "github.com/acme/Theta", "name": "Theta", "owner": "acme", "side": ["server"],
          "stars": 3, "downloads": 0,
          "updated": "2026-02-01T00:00:00Z", "created": "2026-01-15T00:00:00Z" }
      ] }
      """;

  /// The names of the mods of the list, from the first row.
  private static string Order(TuiHarness tui) =>
      string.Join(',', new[] { "Zeta", "Eta", "Theta" }.OrderBy(name => RowOf(tui, name + " · ")));

  [Theory]
  [InlineData(Language.En, 120, "Relevance", "Popularity", "Updated", "Newest")]
  [InlineData(Language.ZhHans, 120, "相关性", "热度", "最近更新", "最新发布")]
  [InlineData(Language.ZhHant, 60, "相關性", "熱度", "最近更新", "最新發佈")]
  public async Task Sort_OffersTheOrdersOfLevimcOrg_AndTheRowsShowTheCounts(
      Language language, int width, string relevance, string popularity, string updated,
      string newest) {
    using TuiHarness tui = Start(
        language, width, width == 60 ? 16 : 36,
        url => url == ModCatalogClient.BaseUrl + "index.json" ? OrderedIndex : BetaDetail);
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor($"‹ {relevance} ›");
    await tui.WaitFor("Theta · acme");
    Assert.Equal("Zeta,Eta,Theta", Order(tui));

    // Downloads and stars are short counts in columns of their own; what is unknown or
    // none is left out, and the days stay in one column.
    string[] rows = tui.Screen.Split('\n');
    string zeta = rows[RowOf(tui, "Zeta · ")];
    string eta = rows[RowOf(tui, "Eta · ")];
    string theta = rows[RowOf(tui, "Theta · ")];
    Assert.Contains("↓ 1.5M          2026-01-01", zeta);
    Assert.DoesNotContain("★", zeta);
    Assert.Contains("★ 1.2k  2026-03-01", eta);
    Assert.DoesNotContain("↓", eta);
    Assert.Contains("★ 3  2026-02-01", theta);
    Assert.DoesNotContain("↓", theta);
    static int Column(string row, string text) =>
        TextWidth.Of(row[..row.IndexOf(text, StringComparison.Ordinal)]);
    Assert.Equal(Column(zeta, "2026-01-01"), Column(eta, "2026-03-01"));
    Assert.Equal(Column(zeta, "2026-01-01"), Column(theta, "2026-02-01"));
    tui.Snapshot("getmods-counts");

    // → on the order goes through the four of them.
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(CommonText.HintChange.Text);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor($"‹ {popularity} ›");
    Assert.Equal("Eta,Zeta,Theta", Order(tui));
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor($"‹ {updated} ›");
    Assert.Equal("Eta,Theta,Zeta", Order(tui));
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor($"‹ {newest} ›");
    Assert.Equal("Theta,Zeta,Eta", Order(tui));
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor($"‹ {relevance} ›");
    Assert.Equal("Zeta,Eta,Theta", Order(tui));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task ModDetailsDialog_ShowsTheDetailsOfAMod_WithItsReadme(
      Language language, int width, int height) {
    using TuiHarness tui = Start(language, width, height);
    CatalogIndex index = await new ModCatalogClient((url, _) => Task.FromResult(Catalog(url)))
        .GetIndex(TestContext.Current.CancellationToken);
    await tui.Settle();

    // Act: what a page does to show the details of a mod of the catalogue.
    await tui.Invoke(() => tui.Context.Show(new ModDetailsDialog(index.Mods[0])));

    // The same fields as on the Get mods page; the dialog reads the file of the mod itself.
    await tui.WaitFor("1.3.0");
    Assert.Contains("─ Alpha ─", tui.Screen);
    Assert.Contains("An economy core", tui.Screen);
    Assert.Contains("55,912", tui.Screen);
    Assert.Contains("github.com/acme/Alpha", tui.Screen);
    Assert.Contains($"[ README ] [ {ModDetailsText.OpenInBrowser} ] [ {CommonText.Close} ]",
        tui.Screen);
    Assert.Contains($"←→/Tab {CommonText.HintSwitch.Text}", tui.Screen);
    tui.Snapshot("moddetails-dialog");

    // Enter on the first button shows the README, and Esc comes back.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("The readme of Alpha.");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("The readme of Alpha.");

    // → goes to the button that opens the page of the mod; Tab goes on, round the buttons.
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(
        () => {
          lock (tui.OpenedUrls) {
            return tui.OpenedUrls.Count == 1;
          }
        },
        "the browser");
    tui.Press(ConsoleKey.Tab, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("The readme of Alpha.");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("The readme of Alpha.");

    // Esc closes the dialog.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[ README ]");
  }

  [Fact]
  public async Task ModDetailsDialog_OfAModWhoseFileCannotBeRead_HasNoReadme() {
    using TuiHarness tui = Start(
        catalog: url => url == ModCatalogClient.DetailUrl("github.com/acme/Alpha")
            ? throw new IOException("offline")
            : Catalog(url));
    CatalogIndex index = await new ModCatalogClient((url, _) => Task.FromResult(Catalog(url)))
        .GetIndex(TestContext.Current.CancellationToken);
    await tui.Settle();

    await tui.Invoke(() => tui.Context.Show(new ModDetailsDialog(index.Mods[0])));

    // The latest version is a dash, and the README cannot be shown: the bottom bar says so.
    await tui.WaitFor(screen => screen.Contains("—") && !screen.Contains(CommonText.Loading));
    Assert.Contains("An economy core", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Cannot read the details of this mod.");

    // The page of the mod still opens.
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(
        () => {
          lock (tui.OpenedUrls) {
            return tui.OpenedUrls.Count == 1;
          }
        },
        "the browser");
    Assert.DoesNotContain("No README.", tui.Screen);
  }

  [Fact]
  public async Task Readme_IsShownInAViewer_AndTheBrowserOpensThePageOfTheMod() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("Alpha · acme");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Open in browser");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("The readme of Alpha.");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("The readme of Alpha.");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Open in browser");
    tui.Press(ConsoleKey.DownArrow, 2);
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
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Could not read the mod catalogue");
    Assert.DoesNotContain("Failed", tui.Screen);

    // No version is listed, which the dialog says, and the user can still write one.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install…");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("github.com/acme/Alpha@…");
    Assert.Contains("Cannot read the details of this mod.", tui.Screen);
    Assert.DoesNotContain("1.2.0", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Custom version");
    tui.Type("1.2.0");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install github.com/acme/Alpha@1.2.0 · Failed");
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
    await tui.WaitFor($"[+] {ModDetailsText.Tag("economy", "Economy")}");
    tui.Snapshot("getmods-filters");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Spacebar, 2);
    await tui.WaitFor($"[-] {ModDetailsText.Tag("management", "Server management")}");
    tui.Snapshot("getmods-filters-excluded");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GetModsText.ClearFilters);
    tui.Snapshot("getmods-filtered");

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Snapshot("getmods-actions");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Custom);
    tui.Snapshot("getmods-versions");
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor($"↓/Tab {CommonText.HintNext.Text}");
    tui.Snapshot("getmods-versions-tabs");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.CustomTitle);
    tui.Snapshot("getmods-custom");
    tui.Press(ConsoleKey.Escape, 2);
    await tui.WaitUntilGone(GetModsText.Custom);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.DownArrow);
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
