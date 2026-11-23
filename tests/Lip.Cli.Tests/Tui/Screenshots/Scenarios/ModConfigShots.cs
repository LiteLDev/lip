using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.ModConfig;
using static Lip.Cli.Tests.Tui.Screenshots.Scenarios.ConfigPagesText;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Mod config page as its page of the documentation walks through it: the files, the
/// tree of a JSON file, the files that are only shown, saving, and the narrow layout.
public sealed class ModConfigShots(ShotRun run) {
  /// The start of the row of the file of LeviOptimize, whose end the list may cut.
  private const string OptimizeRow = "LeviOptimize · ";
  private const string GmlibFile = "GMLIB · config/config.json";

  /// The start of the row of the file of GMLIB, and of the row of ignoredDimensions, whose
  /// ends the page cuts at the default width.
  private const string GmlibRow = "GMLIB · ";
  private const string IgnoredRow = "ignoredDimensio";
  private const string MoneyFile = "LegacyMoney · money.json";
  private const string RankingFile = "LegacyMoney · ranking.yml";

  /// A file that is not JSON, next to the JSON file of the same mod.
  private const string Ranking = """
      # The board of the richest players.
      title: "Richest players"
      size: 10
      update_minutes: 5
      show_offline: false
      """;

  /// A money.json that someone edited by hand and left a colon out of.
  private const string BrokenMoney = """
      {
          "language": "en",
          "def_money" 100,
          "pay_tax": 0.05,
          "currency_symbol": "$",
          "enable_ranking": true
      }
      """;

  /// The server of Worlds.Survival whose mods the page lists, one of them with a file that
  /// is not JSON.
  private static World Mods(
      Language language, int width = World.DefaultWidth, int height = World.DefaultHeight) =>
      Worlds.Survival(language, width, height, configurable: true)
          .File("mods/LegacyMoney/ranking.yml", Ranking);

  /// Goes to the page, to the file of LeviOptimize, into its tree, and switches a value of
  /// its features.
  private static async Task ChangeOptimize(TuiHarness tui) {
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor(OptimizeRow);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(IgnoredRow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.VersionDetail);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("optSeenPercent");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModConfigText.Unsaved);
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Tree(Language language) {
    using Shot shot = run.Start(Mods(language));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor(OptimizeRow);
    await tui.WaitFor("placeholderApi");

    await shot.Capture(
        "mod-config-03-files",
        Note.Callout(
            1, At.Row(GmlibRow),
            shot.T(
                "A file: the mod, then the path of the file in the folder of the mod.",
                "一个文件：模组名，后面是文件在模组文件夹里的路径。",
                "一個檔案：模組名稱，後面是檔案在模組資料夾裡的路徑。")),
        Note.Callout(
            2, At.Row("legacy-script-engine-lua"),
            shot.T(
                "A mod without a configuration file.", "没有配置文件的模组。", "沒有設定檔的模組。")),
        Note.Callout(
            3, At.Panel("GMLIB"),
            shot.T("The selected file.", "选中的文件。", "選取的檔案。"),
            Side.Top),
        Note.Keys("↑", "↓", "Enter"));

    // A mod without a file.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(Head(ModConfigText.NoFile));
    await shot.Capture(
        "mod-config-04-no-file",
        Note.Box(At.Row("LegacyRemoteCall")),
        Note.Callout(
            1, At.Text(Head(ModConfigText.NoFile)),
            shot.T(
                "lip found no file of this mod where it looks.", "lip 在它查找的位置没有找到这个模组的文件。",
                "lip 在它尋找的位置沒有找到這個模組的檔案。")));

    // The tree of LeviOptimize: the version that the mod keeps to itself.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(IgnoredRow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.VersionDetail);
    await shot.Capture(
        "mod-config-05-version",
        Note.Box(At.Row("version")),
        Note.Callout(
            1, At.Text(ModConfigText.VersionDetail),
            shot.T(
                "Why Enter does nothing on this row.", "在这一行按 Enter 没有反应的原因。",
                "在這一列按 Enter 沒有反應的原因。")));

    // Two levels down, where the top line says where the rows are.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("optSeenPercent");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("maxPushTimes");
    await shot.Capture(
        "mod-config-06-location",
        Note.Callout(
            1, At.Text("features › optPushEntity"),
            shot.T("Where you are in the file.", "当前所在的位置。", "目前所在的位置。"),
            Side.Top),
        Note.Callout(
            2, At.Text("[x] true"),
            shot.T(
                "True or false: Space or Enter switches it.", "真或假：按空格或 Enter 切换。",
                "真或假：按空白鍵或 Enter 切換。"),
            Side.Right),
        Note.Callout(
            3, At.Text("maxPushTimes"),
            shot.T(
                "A number: Enter asks for another.", "数字：按 Enter 输入新值。",
                "數字：按 Enter 輸入新值。")),
        Note.KeysAfter(shot.T("Up one level", "返回上一层", "返回上一層"), "Esc"));

    // An array: its values, and the row that adds one.
    tui.Press(ConsoleKey.Escape, 2);
    await tui.WaitFor(IgnoredRow);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.AddValue);
    await shot.Capture(
        "mod-config-07-array",
        Note.Callout(
            1, At.Text("[0]"),
            shot.T(
                "A value of the list, with its position.", "列表中的一个值及其序号。",
                "清單中的一個值及其序號。")),
        Note.Callout(
            2, At.Text("+ " + ModConfigText.AddValue),
            shot.T("Adds a value at the end.", "在末尾添加一个值。", "在末尾新增一個值。")),
        Note.Keys("↓", "Enter"));

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {ModConfigText.AddTitle} ─");
    tui.Type("nether");
    await shot.Capture(
        "mod-config-08-add-value", Note.Focus(At.Panel(ModConfigText.AddTitle)),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("\"nether\"");

    // The actions of a value of a list; removing the new one leaves the file as it was.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.Remove);
    await shot.Capture(
        "mod-config-09-element-menu", Note.Focus(At.Panel("[1]")), Note.Keys("↓", "Enter"));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("\"nether\"");

    // The file of LegacyMoney: a number takes a number only.
    tui.Press(ConsoleKey.Escape, 2);
    await tui.WaitFor($"Enter {CommonText.HintOpen.Text}");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("def_money");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ def_money ─");
    tui.Press(ConsoleKey.Backspace, 3);
    tui.Type("a lot");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.NotNumber);
    await shot.Capture(
        "mod-config-10-number",
        Note.Focus(At.Panel("def_money")),
        Note.Callout(
            1, At.Text(ModConfigText.Number),
            shot.T(
                "The kind of value that the row holds.", "这一行的值的类型。", "這一列的值的類型。")),
        Note.Callout(
            2, At.Text(ModConfigText.NotNumber),
            shot.T(
                "Why the value is not taken: the dialog stays open.",
                "不接受这个值的原因；对话框不会关闭。", "不接受這個值的原因；對話方塊不會關閉。")));
    tui.Press(ConsoleKey.Backspace, 5);
    tui.Type("250");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.Unsaved);

    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[ ] false");
    await shot.Capture(
        "mod-config-11-unsaved",
        Note.Callout(
            1, At.Text(ModConfigText.Unsaved),
            shot.T(
                "The file has changes that are not written yet.", "文件有尚未写入的更改。",
                "檔案有尚未寫入的變更。"),
            Side.Right),
        Note.Callout(
            2, At.Text("●"),
            shot.T("The same, in the list.", "列表里也有同样的标记。", "清單裡也有同樣的標記。")),
        Note.Callout(
            3, At.Button(CommonText.Save).To(At.Button(CommonText.Discard)),
            shot.T(
                "Save writes the file; Discard reads it again.", "“保存”写入文件；“放弃”重新读取文件。",
                "「儲存」寫入檔案；「捨棄」重新讀取檔案。"),
            Side.Right));

    // Saving.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    await shot.Capture(
        "mod-config-12-save", Note.Box(At.Button(CommonText.Save)), Note.Keys("Tab", "Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Saved);
    await tui.WaitUntilGone(ModConfigText.Unsaved);
    await shot.Capture("mod-config-13-saved", Note.Box(At.Text(CommonText.Saved)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task FilesThatAreOnlyShown(Language language) {
    World world = Mods(language, height: 22);
    world.File("mods/LegacyMoney/money.json", BrokenMoney);
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor(RankingFile);

    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor(ModConfigText.ReadOnly);
    await shot.Capture("mod-config-14-read-only", Note.Box(At.Text(ModConfigText.ReadOnly)));

    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ModConfigText.InvalidJson);
    await shot.Capture(
        "mod-config-15-invalid-json",
        Note.Callout(
            1, At.Text(ModConfigText.InvalidJson),
            shot.T(
                "Why the file is not a tree, and where it is broken, in English.",
                "文件不能显示为树的原因，以及出错的位置（英文）。",
                "檔案不能顯示為樹狀的原因，以及出錯的位置（英文）。")),
        Note.Callout(
            2, At.Text("\"def_money\" 100"),
            shot.T(
                "The broken line: a colon is missing.", "出错的一行：少了冒号。",
                "出錯的一行：少了冒號。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Questions(Language language) {
    using Shot shot = run.Start(Mods(language, height: 24));
    TuiHarness tui = shot.Tui;
    await ChangeOptimize(tui);

    // The mod wrote its file while the page had it open.
    string file = Path.Join(shot.World.Workspace, "mods", "LeviOptimize", "config", "config.json");
    tui.Fs.File.WriteAllText(
        file, tui.Fs.File.ReadAllText(file).Replace("\"version\": 4", "\"version\": 5"));
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.ChangedOnDisk);
    await shot.Capture(
        "mod-config-16-changed-on-disk",
        Note.Focus(At.Panel(ModConfigText.SaveTitle("config/config.json"))),
        Note.Callout(
            1, At.Button(CommonText.Overwrite),
            shot.T(
                "Writes your version over the one on disk.", "用你的版本覆盖磁盘上的文件。",
                "用你的版本覆寫磁碟上的檔案。"),
            Side.Bottom),
        Note.Callout(
            2, At.Button(ModConfigText.Reload),
            shot.T(
                "Shows the file as it is on disk now, without your changes.",
                "显示磁盘上现在的文件，你的更改会丢失。", "顯示磁碟上現在的檔案，你的變更會遺失。"),
            Side.Bottom),
        Note.Callout(
            3, At.Button(CommonText.Cancel),
            shot.T(
                "Writes nothing and keeps your changes on the screen.",
                "什么也不写，你的更改仍留在屏幕上。", "什麼也不寫，你的變更仍留在螢幕上。"),
            Side.Bottom));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.ChangedOnDisk);

    // Another file asks what becomes of the changes.
    tui.Press(ConsoleKey.Escape, 3);
    await tui.WaitFor($"Enter {CommonText.HintOpen.Text}");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.UnsavedQuestion);
    await shot.Capture(
        "mod-config-17-unsaved-question", Note.Focus(At.Panel(CommonText.UnsavedTitle)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task WhileTheServerRuns(Language language) {
    using Shot shot = run.Start(Mods(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.SetServerRunning(true);
    await ChangeOptimize(tui);
    await tui.WaitFor(ModConfigText.MayNeedRestart);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"←→ {CommonText.HintSwitch.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.SavedWhileRunning);

    await shot.Capture(
        "mod-config-18-server-running",
        Note.Callout(
            1, At.Text(ModConfigText.MayNeedRestart),
            shot.T(
                "Shown for as long as the server runs.", "服务器运行期间一直显示。",
                "伺服器執行期間一直顯示。")),
        Note.Callout(
            2, At.Text(ModConfigText.SavedWhileRunning),
            shot.T("The file is written all the same.", "文件照样写入。", "檔案照樣寫入。"),
            Side.Top));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Narrow(Language language) {
    using Shot shot = run.Start(Mods(language, width: 72, height: 20));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor(MoneyFile);
    // The file of the selected row is read in the background: until then Enter does nothing.
    await tui.WaitUntilGone(CommonText.Loading);
    await shot.Capture(
        "mod-config-19-narrow-files", Note.Box(At.Row(GmlibFile)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("placeholderApi");
    await shot.Capture(
        "mod-config-20-narrow-file",
        Note.Box(At.Text(GmlibFile)),
        Note.KeysAfter(shot.T("Back to the files", "回到文件列表", "回到檔案清單"), "Esc"));
  }
}
