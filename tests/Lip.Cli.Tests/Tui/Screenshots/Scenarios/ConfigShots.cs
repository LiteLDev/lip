using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GameConfig;
using Lip.Cli.Tui.Pages.ModConfig;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Game config and the Mod config pages.
public sealed class ConfigShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task GameConfig(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name");

    // Enter on the section goes to its rows.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"Enter {CommonText.HintEdit.Text}");
    await shot.Capture(
        "game-config-01-properties",
        Note.Callout(
            1, At.Text(GameConfigText.Properties).To(At.Text(GameConfigText.Permissions)),
            shot.T(
                "The files of the game: server.properties, allowlist.json, permissions.json.",
                "游戏的文件：server.properties、allowlist.json、permissions.json。",
                "遊戲的檔案：server.properties、allowlist.json、permissions.json。")),
        Note.Callout(
            2, At.Text("server-name").To(At.Text("Survival")),
            shot.T(
                "A property and its value: Enter changes it.", "属性及其值：按 Enter 修改。",
                "屬性及其值：按 Enter 修改。")),
        Note.Callout(
            3, At.Text("Used as the server name"),
            shot.T(
                "What the game says about the selected property.", "游戏对所选属性的说明。",
                "遊戲對所選屬性的說明。")),
        Note.Callout(
            4, At.Button(CommonText.Save),
            shot.T(
                "Nothing is written until you save.", "保存之前不会写入任何内容。",
                "儲存之前不會寫入任何內容。"),
            Side.Top));

    // The gamemode is one of a few values.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    await shot.Capture("game-config-02-choose");
    tui.Press(ConsoleKey.Escape);

    // The allowlist.
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Steve");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    await shot.Capture(
        "game-config-03-allowlist", Note.Box(At.Row(GameConfigText.AddPlayer)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task GameConfigWhileTheServerRuns(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name");
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToEdit);

    await shot.Capture(
        "game-config-04-server-running", Note.Box(At.Text(GameConfigText.StopToEdit)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ModConfig(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, configurable: true));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("LeviOptimize");

    // The file of LeviOptimize, then into its tree.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("features");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("optPushEntity");
    await shot.Capture(
        "mod-config-01-tree",
        Note.Callout(
            1, At.Panel(ModConfigText.Files),
            shot.T(
                "The mods and their JSON files.", "模组及其 JSON 文件。", "模組及其 JSON 檔案。"),
            Side.Top),
        Note.Callout(
            2, At.Text("optPushEntity"),
            shot.T(
                "The values of the file as a tree: Enter opens a group or changes a value.",
                "文件中的值以树形显示：按 Enter 展开一组或修改一个值。",
                "檔案中的值以樹狀顯示：按 Enter 展開一組或修改一個值。")),
        Note.Callout(
            3, At.Button(CommonText.Save),
            shot.T(
                "Nothing is written until you save.", "保存之前不会写入任何内容。",
                "儲存之前不會寫入任何內容。"),
            Side.Top));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ModConfigWithoutMods(Language language) {
    using Shot shot = run.Start(Worlds.Server(language, height: 18));
    await shot.Tui.GoTo(PageId.ModConfig);
    await shot.Tui.WaitFor(ModConfigText.NoMods);

    await shot.Capture("mod-config-02-empty");
  }
}
