using System.Text.Json.Nodes;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Get mods page, for content/lip/pages/interface/get_mods.md of liteldev.github.io: every
/// state of it that GetModsShots does not show.
public sealed class GetModsDocShots(ShotRun run) {
  private const string ChatBridge = "github.com/steve-builds/ChatBridge";

  private static readonly string[] _bridges = [
    "discord", "telegram", "matrix", "slack", "irc", "kook", "qq", "feishu", "dingtalk",
    "webhook",
  ];

  private static string Row(CatalogueMod mod) => $"{mod.Name} · {mod.Owner}";

  private static CatalogueMod Mod(string id) => Catalogue.Mods.First(mod => mod.Id == id);

  /// The catalogue with one mod more, which has more variants than a row of tabs holds.
  private static string CatalogueWithChatBridge(string url) {
    if (url == ModCatalogClient.BaseUrl + "index.json") {
      JsonNode index = JsonNode.Parse(Catalogue.File(url))!;
      JsonNode entry = index["mods"]![0]!.DeepClone();
      entry["id"] = ChatBridge;
      entry["name"] = "ChatBridge";
      entry["owner"] = "steve-builds";
      entry["description"] = "Relays the chat of the server to other chat platforms";
      entry["tags"] = new JsonArray("type:mod", "utility", "chat");
      entry["stars"] = 203;
      entry["downloads"] = 118530;
      entry["updated"] = "2026-11-10T00:00:00Z";
      entry["deps"] = new JsonObject { [World.Loader] = "26.60.*" };
      entry["requiredBy"] = 0;
      entry["url"] = $"https://levimc.org/mods/{ChatBridge}/";
      index["mods"]!.AsArray().Insert(0, entry);
      return index.ToJsonString();
    }

    if (url != ModCatalogClient.DetailUrl(ChatBridge)) {
      return Catalogue.File(url);
    }

    JsonNode detail = JsonNode.Parse(Catalogue.File(ModCatalogClient.DetailUrl(Catalogue.Optimize)))!;
    detail["id"] = ChatBridge;
    detail["latest"] = new JsonObject { ["variant"] = _bridges[0], ["version"] = "2.4.0" };
    string[] versions = ["2.4.0", "2.3.1", "2.3.0"];
    JsonArray list = detail["versions"]!.AsArray();
    for (int i = 0; i < list.Count; i++) {
      JsonNode build = list[i]!["variants"]![""]!;
      JsonObject variants = [];
      foreach (string bridge in _bridges) {
        variants[bridge] = build.DeepClone();
      }

      list[i]!["version"] = versions[i];
      list[i]!["variants"] = variants;
    }

    detail["readmes"] = new JsonArray();
    return detail.ToJsonString();
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ListOfAServerWithMods(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor(Row(Mod(Catalogue.ScriptEngine)));
    await tui.WaitFor("✓ " + Row(Mod(Catalogue.ScriptEngine)));
    tui.Press(ConsoleKey.DownArrow, 4);
    await tui.WaitFor("─ PLand ─");
    await tui.WaitUntilGone(CommonText.Loading);

    CatalogueMod land = Mod(Catalogue.Land);
    await shot.Capture(
        "get-mods-06-columns",
        Note.Callout(
            1, At.Text("✓ " + Mod(Catalogue.ScriptEngine).Name).To(At.Text("✓ LegacyMoney")),
            shot.T(
                "✓: installed in this workspace.", "✓：已安装在当前工作区。", "✓：已安裝在目前的工作區。")),
        Note.Callout(
            2, At.Text(Row(land)),
            shot.T("The mod and its author.", "模组名称和作者。", "模組名稱和作者。"), Side.Right),
        Note.Callout(
            3, At.Text("↓ 186k"),
            shot.T("Downloads.", "下载量。", "下載次數。"), Side.Top),
        Note.Callout(
            4, At.Text("★ 412"),
            shot.T("Stars on GitHub.", "GitHub 星标数。", "GitHub 星號數。"), Side.Top),
        Note.Callout(
            5, At.Text("2026-11-08"),
            shot.T("The day of its last update.", "最近一次更新的日期。", "最近一次更新的日期。"),
            Side.Top),
        Note.Callout(
            6, At.Text($"{GetModsText.Count(8)} · {GetModsText.Installed}"),
            shot.T(
                "How many mods the list holds now, and what ✓ stands for.",
                "列表中当前的模组数量，以及 ✓ 的含义。", "清單中目前的模組數量，以及 ✓ 的意義。"),
            Side.Right));

    // The details scroll once they have the focus.
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor(_ => LastRow(tui).Contains(CommonText.HintPage.Key), "the details scroll");
    await shot.Capture(
        "get-mods-07-details-scroll",
        Note.Callout(
            1, At.Text("─ PLand ─"),
            shot.T(
                "The details have the focus: their title is highlighted.",
                "焦点在详情区：标题高亮。", "焦點在詳細資料區：標題反白。")),
        Note.Callout(
            2, At.Text("▲").To(At.Text("▼")),
            shot.T("More lines above and below.", "上方和下方还有内容。", "上方和下方還有內容。"),
            Side.Right),
        Note.Callout(
            3, At.Text($"{CommonText.HintScroll.Key} {CommonText.HintScroll.Text}")
                .To(At.Text($"{CommonText.HintEnds.Key} {CommonText.HintEnds.Text}")),
            shot.T("The keys that scroll.", "用来滚动的按键。", "用來捲動的按鍵。"), Side.Top),
        Note.Keys("Tab", "↓"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Wide(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, width: 124, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("✓ LegacyScriptEngine");
    await tui.WaitFor("LGPL-3.0");
    await tui.WaitUntilGone(CommonText.Loading);
    await shot.Capture(
        "get-mods-08-wide",
        Note.Callout(
            1, At.Text("─ LegacyScriptEngine ─"),
            shot.T(
                "On a wide terminal, the details are at the right of the list.",
                "终端够宽时，详情显示在列表右侧。", "終端機夠寬時，詳細資料顯示在清單右側。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task SearchAndSort(Language language) {
    using Shot shot = run.Start(Worlds.Server(language, width: 124, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("LegacyScriptEngine · ");
    await tui.WaitUntilGone(CommonText.Loading);

    // The search matches the names of the tags as the language of the interface has them.
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Type(GetModsText.SearchExample);
    await tui.WaitFor(GetModsText.Count(1));
    await tui.WaitFor("─ LegacyMoney ─");
    await tui.WaitUntilGone(CommonText.Loading);
    await shot.Capture(
        "get-mods-09-search-language",
        Note.Box(At.Text(GetModsText.SearchExample)),
        Note.Callout(
            1, At.Text(ModDetailsText.Tags),
            shot.T("The tag that matched", "匹配到的标签", "符合的標籤"), Side.Right));

    tui.Press(ConsoleKey.Backspace, GetModsText.SearchExample.Length);
    tui.Type("dynmap");
    await tui.WaitFor(GetModsText.NoMatch);
    await shot.Capture("get-mods-10-no-match", Note.Box(At.Text(GetModsText.NoMatch)));
    tui.Press(ConsoleKey.Backspace, 6);
    await tui.WaitFor(GetModsText.Count(8));

    // ↓ goes to the order; ← and → change it.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor($"‹ {GetModsText.SortName(CatalogSort.Popularity)} ›");
    await shot.Capture(
        "get-mods-11-sort",
        Note.Box(At.Text($"‹ {GetModsText.SortName(CatalogSort.Popularity)} ›")),
        Note.Callout(
            1, At.Text("LegacyRemoteCall"),
            shot.T(
                "61 stars, but 171k downloads: above PLand", "星标只有 61，但下载量 17.1 万：排在 PLand 前面",
                "星號只有 61，但下載次數 17.1 萬：排在 PLand 前面"),
            Side.Right),
        Note.Keys("←", "→"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Filters(Language language) {
    using Shot shot = run.Start(Worlds.Server(language, height: 26));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor(Row(Mod(Catalogue.ScriptEngine)));
    await tui.WaitUntilGone(CommonText.Loading);

    tui.Press(ConsoleKey.UpArrow);
    await shot.Capture(
        "get-mods-12-filters-row", Note.Box(At.Text(GetModsText.Filters).Pad(2)),
        Note.Keys("↑", "Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.ClearFilters);
    await shot.Capture(
        "get-mods-13-filter-sections",
        Note.Focus(At.Panel(GetModsText.Filters)),
        Note.Callout(
            1, At.Row(GetModsText.ClearFilters),
            shot.T("Removes every filter.", "清除所有筛选条件。", "清除所有篩選條件。")),
        Note.Callout(
            2, At.Row(GetModsText.Section(FilterSection.GameVersion))
                .To(At.Row(GetModsText.Section(FilterSection.Dependency))),
            shot.T(
                "The sections, each with the number of its values. Enter opens one.",
                "各个分类及其选项数量。按 Enter 展开。", "各個分類及其選項數量。按 Enter 展開。")),
        Note.Callout(
            3, At.Text(GetModsText.Count(8)),
            shot.T(
                "The mods that the filters leave.", "筛选后剩下的模组数量。",
                "篩選後剩下的模組數量。"),
            Side.Right),
        Note.Keys("Enter"));

    // Tags: Space includes a value, and Space again excludes it.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] ");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+]");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar, 2);
    await tui.WaitFor("[-]");
    await shot.Capture(
        "get-mods-14-filter-exclude",
        Note.Focus(At.Panel(GetModsText.FiltersTitle(2))),
        Note.Callout(
            1, At.Row("[+]"),
            shot.T(
                "Included: only mods that have it.", "包含：只显示带有它的模组。",
                "包含：只顯示帶有它的模組。")),
        Note.Callout(
            2, At.Row("[-]"),
            shot.T(
                "Excluded: no mod that has it.", "排除：不显示带有它的模组。",
                "排除：不顯示帶有它的模組。")),
        Note.Callout(
            3, At.Text(GetModsText.State(FilterState.Excluded)),
            shot.T(
                "The state of the highlighted value.", "高亮选项当前的状态。", "反白選項目前的狀態。")),
        Note.Keys("Space", "Space"));

    // Clear filters, close the tags, and open the two sections below them.
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Count(8));
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("[ ] ");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] ");
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] LeviLamina");
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+]");
    await shot.Capture(
        "get-mods-15-filter-depends",
        Note.Focus(At.Panel(GetModsText.FiltersTitle(1))),
        Note.Callout(
            1, At.Text(GetModsText.Section(FilterSection.License)),
            shot.T(
                "Kinds of license, not single licenses.", "按许可证的类别筛选，而不是具体的许可证。",
                "依授權條款的類別篩選，而不是個別的授權條款。")),
        Note.Callout(
            2, At.Row("[+]"),
            shot.T(
                "Only the mods that need LegacyRemoteCall.", "只显示依赖 LegacyRemoteCall 的模组。",
                "只顯示相依於 LegacyRemoteCall 的模組。")),
        Note.Callout(
            3, At.Text(GetModsText.Count(2)),
            shot.T("Two mods are left.", "还剩两个模组。", "還剩兩個模組。"), Side.Right));

    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(GetModsText.FiltersActive(1));
    await shot.Capture(
        "get-mods-16-filters-active",
        Note.Box(At.Text(GetModsText.FiltersActive(1))),
        Note.Keys("Esc"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Menus(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("✓ " + Row(Mod(Catalogue.ScriptEngine)));
    tui.Press(ConsoleKey.DownArrow, 4);
    await tui.WaitFor("─ PLand ─");
    await tui.WaitUntilGone(CommonText.Loading);

    // The README of the mod, in a window that scrolls.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("# PLand");
    await shot.Capture(
        "get-mods-17-readme",
        Note.Callout(
            1, At.Text("# PLand"),
            shot.T("The README, as the author wrote it", "作者写的 README 原文", "作者寫的 README 原文"),
            Side.Right),
        Note.Keys("Esc"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("# PLand");

    // Open in browser.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.DownArrow, 2);
    await shot.Capture(
        "get-mods-18-open-in-browser",
        Note.Focus(At.Panel("PLand")),
        Note.Box(At.Row(ModDetailsText.OpenInBrowser)),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModDetailsText.OpenInBrowser);

    // While the server runs, nothing is installed.
    await tui.SetServerRunning(true);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModDetailsText.Readme);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(CommonText.StopServerFirst);
    await shot.Capture(
        "get-mods-19-server-running",
        Note.Focus(At.Panel("PLand")),
        Note.Box(At.Row(CommonText.StopServerFirst)),
        Note.Arrow(
            At.Text("● " + CommonText.ServerRunning),
            shot.T("The server runs", "服务器正在运行", "伺服器正在執行"), Side.Top));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task WithoutAServer(Language language) {
    using Shot shot = run.Start(Worlds.Empty(language, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor(Row(Mod(Catalogue.ScriptEngine)));
    await tui.WaitUntilGone(CommonText.Loading);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.NoServer);
    await shot.Capture(
        "get-mods-20-no-server",
        Note.Focus(At.Panel("LegacyScriptEngine")),
        Note.Box(At.Button(CommonText.SetUpServer)),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task VersionsThatDoNotFit(Language language) {
    // A server of an older game: no version of the mod is for its LeviLamina.
    World world = new World(language, height: 22).Install(
        new Installed(World.Game, "1.26.40"), new Installed(World.Loader, "26.40.1"));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor(Row(Mod(Catalogue.ScriptEngine)));
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("─ LeviOptimize ─");
    await tui.WaitUntilGone(CommonText.Loading);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.NoVersionForLoader("26.40.1"));
    await shot.Capture(
        "get-mods-21-none-fits",
        Note.Focus(At.Panel("LeviOptimize")),
        Note.Callout(
            1, At.Text(GetModsText.NoVersionForLoader("26.40.1")),
            shot.T(
                "Why the list is empty: what this workspace has.", "列表为空的原因：当前工作区的版本。",
                "清單為空的原因：目前工作區的版本。")),
        Note.Callout(
            2, At.Row(GetModsText.Custom),
            shot.T(
                "Still there: name a version, a branch, a tag or a commit yourself.",
                "仍然可用：自己指定版本、分支、标签或提交。", "仍然可用：自己指定版本、分支、標籤或提交。")));

    // Custom…: what follows the @ of the package.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {GetModsText.CustomTitle} ─");
    await shot.Capture(
        "get-mods-22-custom",
        Note.Focus(At.Panel(GetModsText.CustomTitle)),
        Note.Callout(
            1, At.Text(GetModsText.CustomExample),
            shot.T("An example, not a value", "这是示例，不是已填的内容", "這是範例，不是已填的內容"),
            Side.Right));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ManyVariants(Language language) {
    World world = Worlds.Server(language, height: 22);
    world.CatalogFile = CatalogueWithChatBridge;
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("ChatBridge · steve-builds");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[discord]");
    tui.Press(ConsoleKey.RightArrow, 8);
    await tui.WaitFor("[dingtalk]");
    await shot.Capture(
        "get-mods-23-variants-overflow",
        Note.Focus(At.Panel("ChatBridge")),
        Note.Callout(
            1, At.Text("‹", 2),
            shot.T(
                "More variants at the left.", "左边还有变体。", "左邊還有變體。")),
        Note.Callout(
            2, At.Text("[dingtalk]"),
            shot.T("The chosen variant.", "当前选中的变体。", "目前選取的變體。"), Side.Top),
        Note.Callout(
            3, At.Text("›", 2),
            shot.T("More variants at the right.", "右边还有变体。", "右邊還有變體。"), Side.Right),
        Note.Keys("←", "→"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Unreachable(Language language) {
    World world = Worlds.Server(language, height: 16);
    world.CatalogFile = _ => throw new HttpRequestException(
        "No such host is known. (levimc.org:443)");
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor(GetModsText.Unavailable);
    await shot.Capture(
        "get-mods-24-unreachable",
        Note.Callout(
            1, At.Text(GetModsText.Unavailable),
            shot.T("The list could not be read.", "无法读取模组列表。", "無法讀取模組清單。")),
        Note.Callout(
            2, At.Text("Could not read"),
            shot.T("Why, as lip says it.", "原因（lip 的原话）。", "原因（lip 的原話）。")),
        Note.Callout(
            3, At.Button(CommonText.Retry),
            shot.T("Reads the list again.", "重新读取列表。", "重新讀取清單。")),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Install(Language language) {
    using Shot shot = run.Start(Worlds.Server(language, height: 24).WithRepositories());
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor(Row(Mod(Catalogue.ScriptEngine)));
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor("─ LegacyMoney ─");
    await tui.WaitUntilGone(CommonText.Loading);

    // A mod with one variant: no tabs.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Custom);
    await shot.Capture(
        "get-mods-25-versions-simple",
        Note.Focus(At.Panel("LegacyMoney")),
        Note.Callout(
            1, At.Row("0.16.1"),
            shot.T(
                "The newest version that fits is highlighted.", "默认高亮相配的最新版本。",
                "預設反白相符的最新版本。")),
        Note.Callout(
            2, At.Text(Catalogue.Money + "@0.16.1"),
            shot.T("What Enter installs.", "按 Enter 会安装的内容。", "按 Enter 會安裝的內容。")),
        Note.Keys("Enter"));

    // The plan: the mod, and what it needs.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "get-mods-26-install-plan",
        Note.Focus(At.Panel(GetModsText.InstallTitle(Catalogue.Money + "@0.16.1"))),
        Note.Callout(
            1, At.Text("LegacyRemoteCall"),
            shot.T(
                "A mod that LegacyMoney needs: lip installs it too.",
                "LegacyMoney 需要的模组：lip 会一并安装。", "LegacyMoney 需要的模組：lip 會一併安裝。")),
        Note.Callout(
            2, At.Button(CommonText.Apply),
            shot.T("Installs both.", "安装这两个模组。", "安裝這兩個模組。"), Side.Bottom),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    await shot.Capture(
        "get-mods-27-install-result",
        Note.Focus(At.Panel(GetModsText.InstallTitle(Catalogue.Money + "@0.16.1"))),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor("✓ " + Row(Mod(Catalogue.Money)));
    await shot.Capture(
        "get-mods-28-installed",
        Note.Box(At.Row("✓ " + Row(Mod(Catalogue.Money)))),
        Note.Box(At.Row("✓ " + Row(Mod(Catalogue.RemoteCall)))));
  }

  private static string LastRow(TuiHarness tui) => tui.Screen.Split('\n')[^1];
}
