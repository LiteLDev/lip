using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;
using static Lip.Cli.Tests.Tui.Screenshots.Scenarios.ConfigPagesText;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Tools page as its page of the documentation walks through it: the two actions of
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

  /// The folder of the mod: its files, and whatever else the scenario adds.
  private static World Mod(Language language, int height = 22) {
    World world = new(language, height: height, workspace: Project);
    world.File(Path.Join("WelcomeMod", "manifest.json"), "{ \"name\": \"WelcomeMod\" }\n")
        .File(Path.Join("WelcomeMod", "WelcomeMod.dll"), "MZ")
        .File("README.md", "# WelcomeMod\n");
    return world;
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

    // Create lip.toml opens the form, which marks the package id until it is typed.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.Problems(1));
    tui.Type(PackageId);
    await tui.WaitFor(ManifestEditorText.AllValid);
    await shot.Capture(
        "tools-07-init-form",
        Note.Callout(
            1, At.Text(PackageId),
            shot.T(
                "Each value is checked as it is typed: ✓ valid, ✗ not, ⋯ being checked.",
                "每个值在输入时就会检查：✓ 合法，✗ 不合法，⋯ 检查中。",
                "每個值在輸入時就會檢查：✓ 合法，✗ 不合法，⋯ 檢查中。")),
        Note.Callout(
            2, At.Text(ManifestEditorText.AllValid),
            shot.T(
                "Whether the whole file is valid.", "整个文件是否合法。", "整個檔案是否合法。")),
        Note.Keys("Ctrl+S"));

    // Saving asks first, with what is expected on the BDS and the LeviLamina chosen.
    tui.Press(ConsoleKey.S, modifiers: ConsoleModifiers.Control);
    await tui.WaitFor(ManifestEditorText.Prediction);
    await tui.WaitUntilGone(ManifestEditorText.LoadingVersions);
    await shot.Capture(
        "tools-08-init-save", Note.Focus(At.Panel(ManifestEditorText.SaveTitle)),
        Note.Keys("←", "→", "Enter"));
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.Saved("lip.toml"));

    // The file is there now: the form stays open on it, and the action edits it.
    await tui.WaitFor(ManifestEditorText.EditTitle + "  ");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(ToolsText.Found);
    await shot.Capture(
        "tools-09-edit",
        Note.Box(At.Row(ManifestEditorText.Edit)),
        Note.Callout(
            1, Files(ToolsText.Found, ToolsText.Missing),
            shot.T(
                "The folder has a lip.toml now: the same form edits it.",
                "文件夹里现在有 lip.toml 了：用同一个表单编辑它。",
                "資料夾裡現在有 lip.toml 了：用同一個表單編輯它。")));
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

  /// An action that the page lets through and the command refuses: the failure dialog.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Failure(Language language) {
    World world = Mod(language).File("tooth.json", ToothWithoutVersion);
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
