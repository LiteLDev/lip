using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GameConfig;
using static Lip.Cli.Tests.Tui.Screenshots.Scenarios.ConfigPagesText;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Game config page as its page of the documentation walks through it: the properties,
/// the players, saving, and what the page does while the server runs.
public sealed class GameConfigShots(ShotRun run) {
  /// A player who is not on the lists of Worlds.Survival yet.
  private const string NewPlayer = "Sunny";
  private const string NewXuid = "2535405219876543";

  /// Which place of a text is the row of a menu, when a row of the list behind the menu may
  /// show the same text: "ignores player limit" is one text in Chinese, on the row of a player
  /// and in the menu of another.
  private static int MenuRow(string text) => text == GameConfigText.IgnoresLimit ? 2 : 1;

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Properties(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"Enter {CommonText.HintEdit.Text}");

    // A property that is true or false.
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor($"Space {CommonText.HintToggle.Text}");
    await shot.Capture(
        "game-config-05-boolean",
        Note.Box(At.Row("allow-cheats")),
        Note.Callout(
            1, At.Text("[ ]", 1),
            shot.T(
                "An empty box is false, a checked one is true.",
                "空框表示 false，打勾表示 true。", "空框表示 false，打勾表示 true。"),
            Side.Right),
        Note.Callout(
            2, At.Text("If true then cheats like commands can be used."),
            shot.T(
                "What the file says about the selected property.",
                "文件中对所选属性的说明。", "檔案中對所選屬性的說明。")),
        Note.Keys("Space"));

    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats");
    await shot.Capture(
        "game-config-06-changed",
        Note.Callout(
            1, At.Text("* allow-cheats"),
            shot.T(
                "A star marks a property that you changed.", "星号标出你改过的属性。",
                "星號標出你改過的屬性。")),
        Note.Callout(
            2, At.Text(GameConfigText.Properties + " *"),
            shot.T(
                "The section has unsaved changes.", "这个分区有未保存的更改。",
                "這個分區有未儲存的變更。")),
        Note.Callout(
            3, At.Text(GameConfigText.Original("false")),
            shot.T("The value in the file.", "文件里现在的值。", "檔案裡現在的值。")),
        Note.Callout(
            4, At.Button(CommonText.Save).To(At.Button(CommonText.Discard)),
            shot.T(
                "Save and Discard can be chosen now.", "现在可以选择“保存”和“放弃”了。",
                "現在可以選擇「儲存」和「捨棄」了。"),
            Side.Top));

    // Enter on a changed property offers to take the change back.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Reset);
    await shot.Capture(
        "game-config-07-reset",
        Note.Focus(At.Panel("allow-cheats")),
        Note.Box(At.Row(GameConfigText.Reset)),
        Note.Keys("↓", "Enter"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GameConfigText.Reset);

    // A number with a range: what is out of it is not taken.
    tui.Press(ConsoleKey.DownArrow, 4);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.ValueBetween(1, 65535));
    tui.Press(ConsoleKey.Backspace, 5);
    tui.Type("70000");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.NotBetween(1, 65535));
    await shot.Capture(
        "game-config-08-number",
        Note.Focus(At.Panel("server-port")),
        Note.Callout(
            1, At.Text(GameConfigText.ValueBetween(1, 65535)),
            shot.T(
                "The range that the file allows.", "文件允许的范围。", "檔案允許的範圍。")),
        Note.Callout(
            2, At.Text(GameConfigText.NotBetween(1, 65535)),
            shot.T(
                "Why the value is not taken: the dialog stays open.",
                "不接受这个值的原因；对话框不会关闭。", "不接受這個值的原因；對話方塊不會關閉。")));
    tui.Press(ConsoleKey.Backspace, 5);
    tui.Type("19140");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("* server-port");

    // A text.
    tui.Press(ConsoleKey.UpArrow, 7);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ server-name ─");
    tui.Type(" SMP");
    await shot.Capture(
        "game-config-09-text", Note.Focus(At.Panel("server-name")), Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("* server-name");

    // The buttons.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    await shot.Capture(
        "game-config-10-save",
        Note.Box(At.Button(CommonText.Save)),
        Note.Callout(
            1, At.Text(GameConfigText.AppliesOnStart),
            shot.T(
                "The server reads the files when it starts.", "服务器启动时才读取这些文件。",
                "伺服器啟動時才讀取這些檔案。"),
            Side.Top),
        Note.Keys("Tab", "Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Saved);
    await tui.WaitUntilGone("* server-name");
    await shot.Capture("game-config-11-saved", Note.Box(At.Text(CommonText.Saved)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Players(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Steve");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");

    // Add player…: a name, and the XUID when you know it.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.XuidOptional);
    tui.Type(NewPlayer);
    tui.Press(ConsoleKey.Tab);
    tui.Type(NewXuid);
    await shot.Capture(
        "game-config-12-add-player",
        Note.Focus(At.Panel(GameConfigText.AddPlayerTitle)),
        Note.Callout(
            1, At.Text(GameConfigText.PlayerName),
            shot.T(
                "The gamertag of the player.", "玩家的游戏名（Gamertag）。",
                "玩家的遊戲名稱（Gamertag）。")),
        Note.Callout(
            2, At.Text(GameConfigText.XuidOptional),
            shot.T(
                "The number of the Xbox account: leave it empty when you do not know it.",
                "Xbox 账号的数字编号；不知道可以留空。", "Xbox 帳號的數字編號；不知道可以留空。")),
        Note.Keys("Tab", "Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Allowlist + " *");
    await tui.WaitFor($"Enter {CommonText.HintActions.Text}");

    // The actions of a player.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Remove);
    await shot.Capture(
        "game-config-13-player-menu",
        Note.Focus(At.Panel(NewPlayer)),
        Note.Callout(
            1, At.Row(GameConfigText.IgnoreLimit, MenuRow(GameConfigText.IgnoreLimit)),
            shot.T(
                "Lets the player join a full server.", "服务器满员时也让该玩家加入。",
                "伺服器滿員時也讓該玩家加入。")),
        Note.Callout(
            2, At.Row(GameConfigText.Remove),
            shot.T(
                "Takes the player off the list.", "把该玩家从名单中移除。",
                "把該玩家從名單中移除。")));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(screen => screen.Contains(NewXuid + "  " + GameConfigText.IgnoresLimit));
    await shot.Capture(
        "game-config-14-ignores-limit", Note.Box(At.Row(NewPlayer)));

    // The permissions.
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("operator");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Head(GameConfigText.PermissionsDetail));
    await shot.Capture(
        "game-config-15-permissions",
        Note.Callout(
            1, At.Text("2535416409681153"),
            shot.T(
                "A player, by XUID: this file has no names.", "玩家的 XUID；这个文件不记录名字。",
                "玩家的 XUID；這個檔案不記錄名稱。")),
        Note.Callout(
            2, At.Text("operator"),
            shot.T("The permission of the player.", "该玩家的权限。", "該玩家的權限。"),
            Side.Right),
        Note.Callout(
            3, At.Text(Head(GameConfigText.PermissionsDetail)),
            shot.T(
                "Everyone else gets default-player-permission-level of the properties.",
                "其他玩家使用属性里的 default-player-permission-level。",
                "其他玩家使用屬性裡的 default-player-permission-level。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {GameConfigText.AddPlayerTitle} ─");
    tui.Type(NewXuid);
    await shot.Capture(
        "game-config-16-add-permission",
        Note.Focus(At.Panel(GameConfigText.AddPlayerTitle)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.MemberDetail);
    await shot.Capture(
        "game-config-17-permission",
        Note.Focus(At.Panel(NewXuid)), Note.Keys("↑", "↓", "Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.Permissions + " *");
    await tui.WaitFor($"Enter {CommonText.HintActions.Text}");

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameConfigText.ChangePermission);
    await shot.Capture(
        "game-config-18-permission-menu", Note.Focus(At.Panel(NewXuid)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(GameConfigText.ChangePermission);

    // Discard asks first.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.DiscardQuestion);
    await shot.Capture(
        "game-config-19-discard", Note.Focus(At.Panel(CommonText.DiscardTitle)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.DiscardQuestion);

    // Another page asks what becomes of the changes.
    tui.Press(ConsoleKey.Escape, 3);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.UnsavedQuestion);
    await shot.Capture(
        "game-config-20-unsaved",
        Note.Focus(At.Panel(CommonText.UnsavedTitle)),
        Note.Callout(
            1, At.Text("allowlist.json").To(At.Text("permissions.json")),
            shot.T(
                "The files that have changes.", "有更改的文件。", "有變更的檔案。")));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.UnsavedQuestion);

    // The server wrote the allowlist while the page had it open: saving asks first.
    tui.Fs.File.WriteAllText(
        Path.Join(shot.World.Workspace, "allowlist.json"),
        "[{\"ignoresPlayerLimit\":false,\"name\":\"Steve\",\"xuid\":\"2535416409681153\"}]\n");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Tab, 2);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.ChangedOnDisk);
    await shot.Capture(
        "game-config-21-changed-on-disk",
        Note.Focus(At.Panel(GameConfigText.SaveTitle)),
        Note.Callout(
            1, At.Text("allowlist.json", 1),
            shot.T(
                "The file that is no longer what lip read.", "内容已经和 lip 读取时不同的文件。",
                "內容已經和 lip 讀取時不同的檔案。")),
        Note.Callout(
            2, At.Button(CommonText.Overwrite),
            shot.T(
                "Writes your version over the one on disk.", "用你的版本覆盖磁盘上的文件。",
                "用你的版本覆寫磁碟上的檔案。"),
            Side.Bottom));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task FilesThatCannotBeEdited(Language language) {
    // A server that never ran has no server.properties, and someone broke the allowlist.
    World world = new World(language, height: 22).InstallServer()
        .File(
            "allowlist.json",
            """
            [
              { "ignoresPlayerLimit": true, "name": "Steve", "xuid": "2535416409681153" },
              { "ignoresPlayerLimit": false, "name": "Alex" "xuid": "2535428104317920" }
            ]
            """);
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor(Head(GameConfigText.Missing("server.properties")));
    await shot.Capture(
        "game-config-22-missing",
        Note.Callout(
            1, At.Text(Head(GameConfigText.Missing("server.properties"))),
            shot.T(
                "The server writes the file when it first starts.", "服务器第一次启动时会写出这个文件。",
                "伺服器第一次啟動時會寫出這個檔案。")));

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(Head(GameConfigText.Unreadable("allowlist.json")));
    await shot.Capture(
        "game-config-23-unreadable",
        Note.Callout(
            1, At.Text(Head(GameConfigText.Unreadable("allowlist.json"))),
            shot.T(
                "lip does not write a file that it cannot read.", "lip 不会写入它读不懂的文件。",
                "lip 不會寫入它讀不懂的檔案。")),
        Note.Callout(
            2, At.Text("LineNumber"),
            shot.T(
                "Where the file is broken, in English.", "文件出错的位置（英文）。",
                "檔案出錯的位置（英文）。")),
        Note.Callout(
            3, At.Text("\"Alex\""),
            shot.T("The file as it is.", "文件的原文。", "檔案的原文。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task WhileTheServerRuns(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.GameConfig);
    await tui.WaitFor("server-name");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("* allow-cheats");

    // The server starts while a change is not saved.
    await tui.SetServerRunning(true);
    await tui.WaitFor(GameConfigText.StopToSave);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    await shot.Capture(
        "game-config-24-running-unsaved",
        Note.Callout(
            1, At.Text(GameConfigText.StopToSave),
            shot.T(
                "The change is kept, and can be saved once the server has stopped.",
                "更改会保留，服务器停止后就能保存。", "變更會保留，伺服器停止後就能儲存。"),
            Side.Top),
        Note.Callout(
            2, At.Button(CommonText.Discard),
            shot.T(
                "The only button that works meanwhile.", "这期间唯一可用的按钮。",
                "這期間唯一可用的按鈕。"),
            Side.Top));

    // Leaving cannot save.
    tui.Press(ConsoleKey.Escape, 3);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(GameConfigText.CannotSaveNow);
    await shot.Capture(
        "game-config-25-running-leave", Note.Focus(At.Panel(CommonText.UnsavedTitle)));
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage == PageId.ModConfig);

    // The page while the server runs: Enter shows what could be done, and why not now.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(GameConfigText.StopToEdit);
    await tui.WaitFor("server-name");
    tui.Press(ConsoleKey.Enter, 3);
    await tui.WaitFor(CommonText.StopServerFirst);
    await shot.Capture(
        "game-config-26-running-menu",
        Note.Focus(At.Panel("allow-cheats")),
        Note.Box(At.Text(CommonText.StopServerFirst)));
  }
}
