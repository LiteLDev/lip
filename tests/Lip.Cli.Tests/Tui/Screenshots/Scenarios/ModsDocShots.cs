using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Mods;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Mods page, for docs/lip/interface/mods.md of liteldev.github.io: every state of it that
/// ModsShots does not show.
public sealed class ModsDocShots(ShotRun run) {
  private const string ScriptEngine = Catalogue.ScriptEngine + "#lua";
  private const string WelcomeKit = "github.com/steve-builds/WelcomeKit";
  private const string SpawnTweaks = "github.com/steve-builds/SpawnTweaks";
  private const string WelcomeKitUrl =
      "https://github.com/steve-builds/WelcomeKit/releases/latest/download/WelcomeKit.zip";

  private static string Downloads => World.PathOf(@"D:\downloads");
  private static string SpawnTweaksFolder => Path.Join(Downloads, "SpawnTweaks");
  private static string WelcomeKitZip => Path.Join(Downloads, "WelcomeKit-1.2.0.zip");

  /// A server with mods of every kind of source: one version, a branch, a URL, a local folder.
  private static World Sourced(Language language, int height = World.DefaultHeight) {
    World world = new World(language, height: height).InstallServer(
        new Installed(
            Catalogue.ScriptEngine, "0.13.1", Variant: "lua",
            Files: ["mods/legacy-script-engine-lua/manifest.json"]),
        new Installed(
            Catalogue.RemoteCall, "0.13.0", Explicit: false,
            Files: ["mods/LegacyRemoteCall/manifest.json"]));
    return world.WithRepositories()
        .InstallFrom(
            Catalogue.Optimize, "1.8.0", Catalogue.Optimize + "@branch:main",
            Repositories.CommitOf(Catalogue.Optimize, "1.8.0"),
            "mods/LeviOptimize/manifest.json")
        .InstallFrom(WelcomeKit, "1.2.0", WelcomeKitUrl, null, "mods/WelcomeKit/manifest.json")
        .InstallFrom(
            SpawnTweaks, "0.3.0", Ref.FromFile(SpawnTweaksFolder).ToString(), null,
            "mods/SpawnTweaks/manifest.json")
        .UrlPackage(WelcomeKitUrl, WelcomeKit, "1.2.1", "manifest.json", "WelcomeKit.dll")
        .LocalPackage(SpawnTweaksFolder, SpawnTweaks, "0.3.0", "manifest.json", "SpawnTweaks.js");
  }

  /// The server of most screenshots, with repositories to update from and files to browse to.
  private static World Survival(Language language, int height = World.DefaultHeight) {
    World world = Worlds.Survival(language, height: height).WithRepositories()
        .LocalPackage(SpawnTweaksFolder, SpawnTweaks, "0.3.0", "manifest.json", "SpawnTweaks.js")
        .ZipPackage(WelcomeKitZip, WelcomeKit, "1.2.0", "manifest.json", "WelcomeKit.dll");
    world.Fs.AddFile(Path.Join(Downloads, "world-backup-2026-11-01.zip"), new("backup"));
    world.Fs.AddFile(Path.Join(Downloads, "resource-pack.mcpack"), new("pack"));
    return world;
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task WithoutMods(Language language) {
    using (Shot shot = run.Start(Worlds.Empty(language, height: 18))) {
      await shot.Tui.GoTo(PageId.Mods);
      await shot.Tui.WaitFor(CommonText.NoMods);
      await shot.Capture(
          "mods-05-no-server",
          Note.Callout(
              1, At.Row(CommonText.SetUpServer),
              shot.T(
                  "Shown only while the folder has no server: mods need one.",
                  "只在文件夹里还没有服务器时出现：装模组之前要先有服务器。",
                  "只在資料夾裡還沒有伺服器時出現：裝模組之前要先有伺服器。")),
          Note.Callout(
              2, At.Text(ModsText.SetUpServerDetail),
              shot.T(
                  "One sentence about the highlighted row.", "关于高亮那一行的一句话说明。",
                  "關於反白那一列的一句話說明。")),
          Note.Keys("Enter"));
    }

    using (Shot shot = run.Start(Worlds.Server(language, height: 18))) {
      await shot.Tui.GoTo(PageId.Mods);
      await shot.Tui.WaitFor(World.Loader);
      shot.Tui.Press(ConsoleKey.DownArrow);
      await shot.Tui.WaitFor(ModsText.GetModsDetail);
      await shot.Capture(
          "mods-06-no-mods",
          Note.Box(At.Row(CommonText.GetMods)),
          Note.Callout(
            1, At.Row(World.Game).To(At.Row(World.Loader)),
              shot.T("The game and its loader", "游戏和加载器", "遊戲和載入器"), Side.Right),
          Note.Keys("Enter"));
    }
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task CheckedMods(Language language) {
    using Shot shot = run.Start(Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // Space checks the highlighted mod; ↓ moves on. The third and the fourth mod.
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Spacebar);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModsText.Checked(2));
    await shot.Capture(
        "mods-07-checked",
        Note.Callout(
            1, At.Text("[x]", 2),
            shot.T(
                "Space checks and unchecks the highlighted mod.", "按 Space 勾选或取消勾选高亮的模组。",
                "按 Space 勾選或取消勾選反白的模組。")),
        Note.Callout(
            2, At.Text(ModsText.Checked(2)),
            shot.T("How many mods are checked.", "已勾选的模组数量。", "已勾選的模組數量。"),
            Side.Right),
        Note.Keys("Space", "↓", "Space"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UncheckAll);
    await shot.Capture(
        "mods-08-batch-menu",
        Note.Focus(At.Panel(ModsText.Checked(2))),
        Note.Callout(
            1, At.Row(ModsText.DetailsOf("LeviOptimize")),
            shot.T(
                "About the highlighted mod only.", "只针对高亮的那个模组。", "只針對反白的那個模組。")),
        Note.Callout(
            2, At.Row(ModsText.UpdateChecked(2)).To(At.Row(ModsText.UninstallChecked(2))),
            shot.T("For every checked mod at once.", "一次处理所有勾选的模组。", "一次處理所有勾選的模組。")),
        Note.Callout(
            3, At.Row(ModsText.UncheckAll),
            shot.T("Clears the checks.", "清除所有勾选。", "清除所有勾選。")),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Sources(Language language) {
    using Shot shot = run.Start(Sourced(language, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // A mod installed from a branch.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"{ModsText.Source}  branch:main");
    await shot.Capture(
        "mods-09-source-branch",
        Note.Box(At.Row($"{ModsText.Source}  branch:main")),
        Note.Arrow(
            At.Text($"{ModsText.Source}  branch:main"),
            shot.T("Where the mod came from", "模组的来源", "模組的來源"), Side.Bottom));

    // A mod installed from a URL, the last of the mods, has no other versions to name.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor($"{ModsText.Source}  https://");
    await shot.Capture("mods-10-source-url", Note.Box(At.Row($"{ModsText.Source}  https://")));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(ModsText.UpdateToNeedsRepository);
    await shot.Capture(
        "mods-11-url-menu",
        Note.Focus(At.Panel(WelcomeKit)),
        Note.Box(At.Row(ModsText.UpdateToNeedsRepository)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(ModsText.UpdateToNeedsRepository);

    // A mod installed from a folder, the row above.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor($"{ModsText.Source}  {SpawnTweaksFolder}");
    await shot.Capture(
        "mods-12-source-local", Note.Box(At.Row($"{ModsText.Source}  {SpawnTweaksFolder}")));

    // Update asks each source again: the URL is downloaded again.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor($"{ModsText.Source}  https://");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "mods-13-update-url-plan", Note.Focus(At.Panel(ModsText.UpdateOperation(WelcomeKit))));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task InstallFromSource(Language language) {
    using Shot shot = run.Start(Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // ↑ from the first mod goes to the action rows.
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(ModsText.InstallFromSourceDetail[..20]);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.BrowseDetail);
    await shot.Capture(
        "mods-14-install-choice",
        Note.Focus(At.Panel(ModsText.InstallTitle)),
        Note.Callout(
            1, At.Row(ModsText.Browse),
            shot.T(
                "A .zip file or a folder on this computer.", "这台电脑上的 .zip 文件或文件夹。",
                "這台電腦上的 .zip 檔案或資料夾。")),
        Note.Callout(
            2, At.Row(ModsText.TypeSource),
            shot.T(
                "A repository or a URL, typed or pasted.", "输入或粘贴仓库或网址。",
                "輸入或貼上儲存庫或網址。")),
        Note.Keys("Enter"));

    // Browse: the folder browser, which lists folders and .zip files.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {ModsText.InstallLocal} ─");
    await tui.WaitUntilGone(PathPickerText.Reading);
    // ← goes up a level, with the folder just left highlighted: to D:\servers, then to D:\.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("creative" + Path.DirectorySeparatorChar);
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("downloads" + Path.DirectorySeparatorChar);
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("WelcomeKit-1.2.0.zip");
    tui.Press(ConsoleKey.DownArrow, 2);
    await shot.Capture(
        "mods-15-browse",
        Note.Callout(
            1, At.Row("WelcomeKit-1.2.0.zip"),
            shot.T(
                "A .zip file: Enter installs it.", ".zip 文件：按 Enter 安装。",
                ".zip 檔案：按 Enter 安裝。")),
        Note.Callout(
            2, At.Button(PathPickerText.SelectFolder),
            shot.T(
                "Installs the folder shown, when the package is a folder.",
                "如果包是一个文件夹，用它安装当前显示的文件夹。",
                "如果套件是一個資料夾，用它安裝目前顯示的資料夾。"),
            Side.Bottom),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "mods-16-install-local-plan",
        Note.Focus(At.Panel(ModsText.InstallOperation("WelcomeKit-1.2.0.zip"))),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ]");

    // Type a source: a repository, as on the command line.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.BrowseDetail);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.SourcePlaceholder);
    await shot.Capture(
        "mods-17-type-source-empty",
        Note.Focus(At.Panel(ModsText.InstallTitle)),
        Note.Callout(
            1, At.Text(ModsText.SourcePlaceholder),
            shot.T("An example, not a value", "这是示例，不是已填的内容", "這是範例，不是已填的內容"),
            Side.Right));
    tui.Type(Catalogue.Land + "@0.19.0");
    await tui.WaitFor(Catalogue.Land + "@0.19.0");
    await shot.Capture(
        "mods-18-type-source", Note.Focus(At.Panel(ModsText.InstallTitle)), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "mods-19-install-plan",
        Note.Focus(At.Panel(ModsText.InstallOperation(Catalogue.Land + "@0.19.0"))),
        Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Updates(Language language) {
    using Shot shot = run.Start(Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // Update all, from the action rows.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(ModsText.UpdateAllDetail);
    await shot.Capture("mods-20-update-all", Note.Box(At.Row(ModsText.UpdateAll)), Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "mods-21-update-all-plan", Note.Focus(At.Panel(ModsText.UpdateAll)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ]");

    // Update to version…, of the third mod: a prompt for what follows the @.
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor(ScriptEngine + "  0.13.1");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow, 2);
    await shot.Capture(
        "mods-22-update-to-menu", Note.Focus(At.Panel(ScriptEngine)), Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ScriptEngine + "@");
    tui.Type("tag:");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.RefInvalid);
    await shot.Capture(
        "mods-23-update-to-invalid",
        Note.Focus(At.Panel(ModsText.UpdateToTitle)),
        Note.Box(At.Text(ModsText.RefInvalid)));
    tui.Press(ConsoleKey.Backspace, 4);
    tui.Type("0.13.0");
    await tui.WaitFor("0.13.0");
    await shot.Capture(
        "mods-24-update-to", Note.Focus(At.Panel(ModsText.UpdateToTitle)), Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "mods-25-update-to-plan",
        Note.Focus(At.Panel(ModsText.UpdateOperation(ScriptEngine + "@0.13.0"))));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ]");

    // Update: the newest version that fits, and what it leaves on the page.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    await shot.Capture(
        "mods-26-update-plan",
        Note.Focus(At.Panel(ModsText.UpdateOperation(ScriptEngine))),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    await shot.Capture(
        "mods-27-update-result",
        Note.Focus(At.Panel(ModsText.UpdateOperation(ScriptEngine))),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor("0.13.2");
    await shot.Capture(
        "mods-28-updated",
        Note.Box(At.Row(ScriptEngine)),
        Note.Callout(
            1, At.Text("0.13.2"), shot.T("The new version", "新版本", "新版本"), Side.Right));

    // Update once more: the highlight is still on the mod, and there is nothing to apply.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.NothingWouldChange);
    await shot.Capture(
        "mods-30-up-to-date",
        Note.Focus(At.Panel(ModsText.UpdateOperation(ScriptEngine))),
        Note.Box(At.Text(CommonText.NothingWouldChange)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task OtherRows(Language language) {
    using Shot shot = run.Start(Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // A dependency: no box, and no Uninstall.
    tui.Press(ConsoleKey.DownArrow, 4);
    await tui.WaitFor(ModsText.DependencyDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.UpdateTo);
    await shot.Capture(
        "mods-31-dependency-menu",
        Note.Focus(At.Panel(Catalogue.RemoteCall)),
        Note.Focus(At.Row(ModsText.DependencyDetail)),
        Note.Box(At.Text(ModsText.DependencyDetail)));

    // Updating it only records that the user asked for it.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.OnlyRecordChanges);
    await shot.Capture(
        "plan-03-record-only",
        Note.Focus(At.Panel(ModsText.UpdateOperation(Catalogue.RemoteCall))),
        Note.Box(At.Text(CommonText.OnlyRecordChanges)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.OnlyRecordChanges);

    // The game and the loader lead to the setup of the Server page.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(ModsText.GameDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.ChangeVersions);
    await shot.Capture(
        "mods-32-game-menu",
        Note.Focus(At.Panel(World.Game)),
        Note.Focus(At.Row(ModsText.GameDetail)),
        Note.Box(At.Row(ModsText.ChangeVersions)));

    // The catalogue has no entry for the game.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.NotInCatalogue);
    await shot.Capture("mods-33-not-in-catalogue", Note.Focus(At.Panel(ModsText.Details)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task UninstallAndRunning(Language language) {
    using Shot shot = run.Start(Survival(language, height: 24));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(ScriptEngine);

    // Uninstall LegacyMoney, the second mod, and apply the plan.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(Catalogue.Money + "  0.16.1");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.Uninstall);
    tui.Press(ConsoleKey.DownArrow, 3);
    await shot.Capture(
        "mods-34-uninstall-menu", Note.Focus(At.Panel(Catalogue.Money)), Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ " + CommonText.Done);
    await shot.Capture(
        "mods-35-uninstall-result",
        Note.Focus(At.Panel(ModsText.UninstallOperation(Catalogue.Money))),
        Note.Keys("Enter"));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ " + CommonText.Done);
    await tui.WaitFor(ModsText.Installed(6));

    // While the server runs, the page says so, and the action rows are disabled.
    // The highlight is on the mod that took the row of the one uninstalled, the second.
    await tui.SetServerRunning(true);
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor(CommonText.StopServerFirst);
    await shot.Capture(
        "mods-36-server-running-page",
        Note.Callout(
            1, At.Text(ModsText.ServerRuns),
            shot.T(
                "The page says that the server runs.", "页面提示服务器正在运行。",
                "頁面提示伺服器正在執行。")),
        Note.Callout(
            2, At.Row(ModsText.InstallFromSource).To(At.Row(ModsText.UpdateAll)),
            shot.T(
                "What changes the mods is gray, and Enter does nothing on it.",
                "会改动模组的操作变灰，按 Enter 没有反应。",
                "會變更模組的操作變灰，按 Enter 沒有反應。")),
        Note.Callout(
            3, At.Text(CommonText.StopServerFirst),
            shot.T("Why the row is disabled.", "这一行不可用的原因。", "這一列無法使用的原因。")));
  }
}
