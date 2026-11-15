using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Get mods page.
public sealed class GetModsShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ListSearchAndFilters(Language language) {
    using Shot shot = run.Start(Worlds.Server(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("LegacyScriptEngine · LiteLDev");
    await tui.WaitFor("0.13.2 #lua");

    await shot.Capture(
        "get-mods-01-list",
        Note.Callout(
            1, At.Text(GetModsText.Search).To(At.Text(GetModsText.Filters)),
            shot.T(
                "Search, order and filter the mods of levimc.org.",
                "搜索、排序和筛选 levimc.org 上的模组。", "搜尋、排序和篩選 levimc.org 上的模組。")),
        Note.Callout(
            2, At.Row("LegacyScriptEngine · LiteLDev"),
            shot.T(
                "The mods, with their downloads, stars and last update. Enter opens the actions.",
                "模组及其下载量、星标数和最近更新日期。按 Enter 打开操作菜单。",
                "模組及其下載量、星標數和最近更新日期。按 Enter 開啟操作選單。")),
        Note.Callout(
            3, At.Text("─ LegacyScriptEngine ─"),
            shot.T(
                "The details of the selected mod.", "所选模组的详情。", "所選模組的詳細資料。")));

    // ↑ from the first mod goes to the filters, the order, and the search.
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Type("legacy");
    await tui.WaitFor(GetModsText.Count(3));
    await shot.Capture(
        "get-mods-02-search",
        Note.Box(At.Text("legacy")),
        Note.Arrow(
            At.Text(GetModsText.Count(3)),
            shot.T("Mods that match", "匹配的模组", "符合的模組"), Side.Right),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Backspace, 6);
    await tui.WaitFor(GetModsText.Count(8));
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.ClearFilters);
    // The tags: Enter on the heading shows them, Space on one chooses it.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ ] ");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[+]");
    await shot.Capture("get-mods-03-filters", Note.Box(At.Row("[+]")), Note.Keys("Space"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Versions(Language language) {
    using Shot shot = run.Start(Worlds.Server(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GetMods);
    await tui.WaitFor("LegacyScriptEngine · LiteLDev");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GetModsText.Install);
    await shot.Capture(
        "get-mods-04-menu", Note.Focus(At.Panel("LegacyScriptEngine")), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[lua] quickjs");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(" lua [quickjs]");
    await shot.Capture(
        "get-mods-05-versions",
        Note.Callout(
            1, At.Text("lua [quickjs]").To(At.Text("python")),
            shot.T(
                "The variants of the mod: ← and → choose one.", "模组的变体：按 ← 和 → 选择。",
                "模組的變體：按 ← 和 → 選擇。")),
        Note.Callout(
            2, At.Row("0.13.2"),
            shot.T(
                "The versions that fit the game and LeviLamina of this workspace.",
                "与此工作区的游戏和 LeviLamina 相配的版本。",
                "與此工作區的遊戲和 LeviLamina 相符的版本。")),
        Note.Callout(
            3, At.Text(Catalogue.ScriptEngine + "#quickjs@0.13.2"),
            shot.T(
                "What lip installs when you press Enter.", "按 Enter 后 lip 安装的内容。",
                "按 Enter 後 lip 安裝的內容。")),
        Note.Keys("←", "→", "Enter"));
  }
}
