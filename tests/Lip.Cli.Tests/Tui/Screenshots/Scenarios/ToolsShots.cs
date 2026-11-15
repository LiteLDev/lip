using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;
using Lip.Cli.Tui.Widgets;
using static Lip.Cli.Tests.Tui.Screenshots.Scenarios.ConfigPagesText;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Tools page as its page of the documentation walks through it: the four actions of
/// someone who makes a package, in the folder of a mod of theirs.
public sealed class ToolsShots(ShotRun run) {
  private const string PackageId = "github.com/steve/WelcomeMod";

  /// The folder of a mod that its author works on.
  private static string Project { get; } = World.PathOf(@"D:\projects\WelcomeMod");

  /// The tooth.json of the mod as lip 0.x read it: its variant names no platform, so lip
  /// pkg migrate asks for one, and it keeps a file by a pattern that lip.toml writes
  /// otherwise, which lip pkg migrate notes.
  private const string Tooth = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "github.com/steve/WelcomeMod",
        "version": "1.2.0",
        "info": {
          "name": "WelcomeMod",
          "description": "Greets players when they join",
          "tags": ["utility"]
        },
        "variants": [
          { "preserve_files": ["messages.json"], "assets": [ { "type": "self", "placements": [
            { "type": "dir", "src": "WelcomeMod/", "dest": "plugins/WelcomeMod/" } ] } ] }
        ]
      }
      """;

  /// A tooth.json that its author broke: it lost its version.
  private const string ToothWithoutVersion = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "github.com/steve/WelcomeMod",
        "info": { "name": "WelcomeMod", "description": "Greets players when they join" }
      }
      """;

  private const string LipToml = """
      format = 4
      id = "github.com/steve/WelcomeMod"
      version = "1.2.0"

      [info]
      name = "WelcomeMod"
      description = "Greets players when they join"
      tags = ["utility"]

      [[variants.""]]
      side = "server"
      platform = "win-x64"

      [variants."".deps]
      "github.com/LiteLDev/LeviLamina" = "26.60.*"

      [[variants."".files]]
      src = "WelcomeMod/"
      dest = "mods/WelcomeMod/"
      """;

  /// The folder of the mod: its files, and whatever else the scenario adds.
  private static World Mod(Language language, int height = 22) {
    World world = new(language, height: height, workspace: Project);
    world.File(Path.Join("WelcomeMod", "manifest.json"), "{ \"name\": \"WelcomeMod\" }\n")
        .File(Path.Join("WelcomeMod", "WelcomeMod.dll"), "MZ")
        .File("README.md", "# WelcomeMod\n");
    return world;
  }

  /// Waits for the path picker with the title, and until it has read its first folder.
  private static async Task Picker(TuiHarness tui, string title) {
    await tui.WaitFor($"─ {title} ─");
    await tui.WaitUntilGone(PathPickerText.Reading);
  }

  /// The message of a failure followed by those of its causes that add something, as the
  /// summary of ErrorReporter.Describe has them, which writes a report to the disk.
  private static string WithCauses(Exception exception) {
    string shown = "";
    for (Exception? cause = exception; cause is not null; cause = cause.InnerException) {
      string message = cause.Message.Trim();
      if (message.Length > 0 && !shown.Contains(message, StringComparison.Ordinal)) {
        shown += shown.Length == 0 ? message : $"\n  caused by: {message}";
      }
    }

    return shown;
  }

  /// The two rows of the status area that say whether the workspace has a lip.toml and a
  /// tooth.json: the rows of what the first and the second say, which may be the same word.
  private static Target Files(string lipToml, string toothJson) =>
      At.Row(lipToml).To(At.Row(toothJson, lipToml == toothJson ? 2 : 1));

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Init(Language language) {
    using Shot shot = run.Start(Mod(language));
    shot.Scrub(Spinner(), "/");
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);

    await shot.Capture(
        "tools-05-status",
        Note.Callout(
            1, At.Text(ToolsText.InitAbout),
            shot.T(
                "What the selected action does.", "所选操作的作用。", "所選操作的作用。")),
        Note.Callout(
            2, At.Text(Project, 2),
            shot.T(
                "The selected workspace: the folder that the actions work in.",
                "当前工作区：这些操作所在的文件夹。", "目前的工作區：這些操作所在的資料夾。")),
        Note.Callout(
            3, Files(ToolsText.Missing, ToolsText.Missing),
            shot.T(
                "Whether the folder has a lip.toml and a tooth.json.",
                "文件夹里有没有 lip.toml 和 tooth.json。", "資料夾裡有沒有 lip.toml 和 tooth.json。")),
        Note.Callout(
            4, At.Row(ToolsText.Migrate),
            shot.T(
                "Dimmed: an action that cannot work in this folder.",
                "灰色：在这个文件夹里做不了的操作。", "灰色：在這個資料夾裡做不了的操作。")),
        Note.Keys("Enter"));

    // An action that the files of the folder make fail says why, and Enter does nothing.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.NoFile("tooth.json"));
    await shot.Capture(
        "tools-06-migrate-disabled",
        Note.Box(At.Row(ToolsText.Migrate)),
        Note.Callout(
            1, At.Text(ToolsText.NoFile("tooth.json")),
            shot.T(
                "Why the action cannot be chosen now.", "这个操作现在不能选择的原因。",
                "這個操作現在不能選擇的原因。")));
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitUntilGone(ToolsText.NoFile("tooth.json"));

    // Create lip.toml asks for the package id.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
    tui.Type(PackageId);
    await shot.Capture(
        "tools-07-init-question", Note.Focus(At.Panel(CommonText.Question)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Created("lip.toml"));
    await shot.Capture(
        "tools-08-init-result", Note.Box(At.Text(ToolsText.Created("lip.toml"))),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ToolsText.Created("lip.toml"));

    // The file is there now: the action that writes it is off.
    await tui.WaitFor(ToolsText.Exists("lip.toml"));
    await shot.Capture(
        "tools-09-init-disabled",
        Note.Box(At.Row(ToolsText.Init)),
        Note.Callout(
            1, At.Text(ToolsText.Exists("lip.toml")),
            shot.T(
                "lip does not replace a lip.toml.", "lip 不会替换已有的 lip.toml。",
                "lip 不會取代已有的 lip.toml。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Migrate(Language language) {
    World world = Mod(language).File("tooth.json", Tooth);
    using Shot shot = run.Start(world);
    shot.Scrub(Spinner(), "/");
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ToolsText.MigrateAbout);
    await shot.Capture(
        "tools-10-migrate",
        Note.Box(At.Row(ToolsText.Migrate)),
        Note.Callout(
            1, Files(ToolsText.Missing, ToolsText.Found),
            shot.T(
                "The folder has a tooth.json and no lip.toml: it can be migrated.",
                "文件夹里有 tooth.json、没有 lip.toml：可以迁移。",
                "資料夾裡有 tooth.json、沒有 lip.toml：可以移轉。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Question);
    tui.Type("win-x64");
    await shot.Capture(
        "tools-11-migrate-question", Note.Focus(At.Panel(CommonText.Question)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Migrated);
    await shot.Capture(
        "tools-12-migrate-result",
        Note.Callout(
            1, At.Text("⚠"),
            shot.T(
                "A note of the command: something that the new file says in another way.",
                "命令的提示：新文件里换了一种写法的内容。", "命令的提示：新檔案裡換了一種寫法的內容。")),
        Note.Callout(
            2, At.Text("✓ " + ToolsText.Migrated),
            shot.T(
                "lip.toml is written, and tooth.json is deleted.", "lip.toml 已写入，tooth.json 已删除。",
                "lip.toml 已寫入，tooth.json 已刪除。")),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ToolsText.Migrated);
    await tui.WaitFor(ToolsText.Exists("lip.toml"));
    await shot.Capture(
        "tools-13-migrated", Note.Box(Files(ToolsText.Found, ToolsText.Missing)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Pack(Language language) {
    // The archive of the last release is still in the folder.
    World world = Mod(language).File("lip.toml", LipToml).File("package.zip", "PK");
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackFolder);
    await shot.Capture(
        "tools-14-pack-folder",
        Note.Callout(
            1, At.Text(PathPickerText.Folder + "  "),
            shot.T(
                "The folder that is packed: the one with lip.toml.", "要打包的文件夹：放着 lip.toml 的那个。",
                "要封裝的資料夾：放著 lip.toml 的那個。")),
        Note.Callout(
            2, At.Button(PathPickerText.SelectFolder),
            shot.T("Packs the folder shown.", "打包当前显示的文件夹。", "封裝目前顯示的資料夾。"),
            Side.Bottom),
        Note.Keys("Tab", "Enter"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.PackOutput);
    await tui.WaitFor("package.zip");
    await shot.Capture(
        "tools-15-pack-name",
        Note.Callout(
            1, At.Text(PathPickerText.Folder + "  "),
            shot.T(
                "The folder that the ZIP is written to.", "ZIP 写入的文件夹。", "ZIP 寫入的資料夾。")),
        Note.Callout(
            2, At.Text(PathPickerText.FileName + "  "),
            shot.T(
                "The name of the ZIP: Tab goes here, and you can type another.",
                "ZIP 的文件名：按 Tab 到这里，可以改成别的名字。",
                "ZIP 的檔案名稱：按 Tab 到這裡，可以改成別的名稱。")),
        Note.Keys("Tab", "Enter"));

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {ToolsText.Replace} ]");
    await shot.Capture(
        "tools-16-pack-replace",
        Note.Focus(At.Panel(ToolsText.PackTitle)),
        Note.Box(At.Button(ToolsText.Replace)),
        Note.Keys("←", "Enter"));

    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Ok} ]");
    await shot.Capture("tools-17-pack-result", Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task View(Language language) {
    World world = Worlds.Survival(language, height: 24).WithRepositories();
    world.File("LeviAntiCheat-windows-x64.zip", "PK");
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);

    // An installed mod.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {ToolsText.ViewInstalledTitle} ─");
    await tui.WaitFor(World.Game);
    await shot.Capture(
        "tools-18-view-installed",
        Note.Focus(At.Panel(ToolsText.ViewInstalledTitle)),
        Note.Callout(
            1, At.Text("0.13.1"),
            shot.T("The installed version.", "已安装的版本。", "已安裝的版本。"),
            Side.Right),
        Note.Callout(
            2, At.Text(Catalogue.RemoteCall),
            shot.T(
                "Dimmed: installed because another package needs it.", "灰色：因为别的包需要才安装的。",
                "灰色：因為別的套件需要才安裝的。")),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("\"manifest\"");
    await shot.Capture(
        "tools-19-view-result",
        Note.Callout(
            1, At.Text("\"manifest\""),
            shot.T(
                "The package file of that version, as JSON.", "该版本的包文件（JSON）。",
                "該版本的套件檔（JSON）。")),
        Note.KeysAfter(shot.T("Scroll, close", "滚动、关闭", "捲動、關閉"), "↑", "↓", "Esc"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("\"manifest\"");

    // A package that is typed.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.Package);
    tui.Type(Catalogue.Optimize + "@1.8.1");
    await shot.Capture(
        "tools-20-view-typed", Note.Focus(At.Panel(ToolsText.ViewTitle)), Note.Keys("Enter"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(ToolsText.ViewTypedAbout);

    // A ZIP file or a folder on the disk.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await Picker(tui, ToolsText.ViewLocal);
    await tui.WaitFor("LeviAntiCheat-windows-x64.zip");
    await shot.Capture(
        "tools-21-view-browse",
        Note.Callout(
            1, At.Row("LeviAntiCheat-windows-x64.zip"),
            shot.T(
                "A ZIP file: Enter shows it.", "ZIP 文件：按 Enter 查看。", "ZIP 檔案：按 Enter 檢視。")),
        Note.Callout(
            2, At.Button(PathPickerText.SelectFolder),
            shot.T(
                "Shows the folder in the bar as a package.", "把地址栏里的文件夹当作包来查看。",
                "把路徑列裡的資料夾當作套件來檢視。"),
            Side.Bottom),
        Note.Callout(
            3, At.Text(PathPickerText.Files(".zip")),
            shot.T(
                "The files that the list shows.", "列表显示的文件类型。", "清單顯示的檔案類型。"),
            Side.Right));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ViewWithoutPackages(Language language) {
    using Shot shot = run.Start(Mod(language, height: 18));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);

    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.ViewInstalledAbout);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ToolsText.NothingInstalled);
    await shot.Capture(
        "tools-22-view-nothing", Note.Focus(At.Panel(ToolsText.ViewInstalledTitle)));
  }

  /// An action that the page lets through and the command refuses: the failure dialog.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Failure(Language language) {
    World world = Mod(language).File("tooth.json", ToothWithoutVersion);
    // As lip describes a failure: its message, then the messages of its causes.
    world.DescribeFailure = (exception, _, cancelled, _) => new Failure(
        WithCauses(exception), ErrorReporter.Hints(exception),
        Path.Join(".lip", "logs", "20261114T201502Z-4120.log"), null, "", cancelled);
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Found);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"{ToolsText.Migrate} · {CommonText.Failed}");
    await shot.Capture(
        "tools-23-failed",
        Note.Callout(
            1, At.Text(CommonText.Error + ":"),
            shot.T(
                "Why the action did nothing, in the words of the command.",
                "操作没有执行的原因（命令的原话）。", "操作沒有執行的原因（命令的原話）。")),
        Note.Callout(
            2, At.Text(CommonText.FullReport),
            shot.T(
                "The file to attach when you report a problem.", "报告问题时要附上的文件。",
                "回報問題時要附上的檔案。")));
  }
}
