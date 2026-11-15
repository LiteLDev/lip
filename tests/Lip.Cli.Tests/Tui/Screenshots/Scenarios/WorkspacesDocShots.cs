using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;
using static Lip.Cli.Tests.Tui.Screenshots.Scenarios.ConfigPagesText;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Workspaces page as its page of the documentation walks through it: the list and its
/// marks, the menus, adding and removing a folder, and the choices of the new server wizard
/// and of a change of versions that NewServerShots does not show.
public sealed class WorkspacesDocShots(ShotRun run) {
  private static string Lobby { get; } = World.PathOf(@"D:\servers\lobby");

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ListAndMenus(Language language) {
    // One workspace runs its server, and the folder of another was deleted.
    World world = Worlds.Survival(language, height: 22);
    world.Fs.Directory.Delete(Worlds.Skyblock);
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.SetServerRunning(true, Worlds.Creative);
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(WorkspacesText.Missing);
    await tui.WaitFor(WorkspacesText.ServerRunning);

    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(WorkspacesText.CurrentDetail);
    await shot.Capture(
        "workspaces-03-marks",
        Note.Callout(
            1, At.Text("●", 1),
            shot.T(
                "The selected workspace.", "当前工作区。", "目前的工作區。")),
        Note.Callout(
            2, At.Text(WorkspacesText.ServerRunning),
            shot.T(
                "The server of this workspace runs.", "这个工作区的服务器正在运行。",
                "這個工作區的伺服器正在執行。"),
            Side.Right),
        Note.Callout(
            3, At.Text(WorkspacesText.Missing),
            shot.T(
                "The folder no longer exists.", "文件夹已经不存在。", "資料夾已經不存在。"),
            Side.Right),
        Note.Callout(
            4, At.Text(WorkspacesText.CurrentDetail),
            shot.T(
                "The whole path of the row with the focus, and what it is.",
                "焦点所在行的完整路径和说明。", "焦點所在列的完整路徑和說明。")));

    // The selected workspace is neither switched to nor removed.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    await shot.Capture("workspaces-04-menu-selected", Note.Focus(At.Panel("survival")));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(WorkspacesText.ChangeVersions);

    // A workspace whose server runs is not set up again meanwhile.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Switch);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.StopServerFirst);
    await shot.Capture(
        "workspaces-05-menu-running",
        Note.Focus(At.Panel("creative")),
        Note.Box(At.Text(CommonText.StopServerFirst)));
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(WorkspacesText.Switch);

    // A folder that is gone can only be taken off the list.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(WorkspacesText.MissingDetail);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Remove);
    await shot.Capture(
        "workspaces-06-menu-missing", Note.Focus(At.Panel("skyblock")), Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Head(WorkspacesText.RemoveQuestion));
    await shot.Capture(
        "workspaces-07-remove", Note.Focus(At.Panel("skyblock")),
        Note.Box(At.Button(WorkspacesText.Remove)));

    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Removed);
    await tui.WaitUntilGone(Worlds.Skyblock);
    await shot.Capture("workspaces-08-removed", Note.Box(At.Text(WorkspacesText.Removed)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Switch(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 22));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Worlds.Skyblock);

    tui.Press(ConsoleKey.DownArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Switch);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("● " + Worlds.Creative);
    await tui.WaitFor(WorkspacesText.CurrentDetail);
    await tui.WaitFor($"{CommonText.WorkspaceSelected}: {Worlds.Creative}");
    await shot.Capture(
        "workspaces-09-switched",
        Note.Callout(
            1, At.Text(Worlds.Creative, 1),
            shot.T(
                "The top bar names the workspace that every page now works on.",
                "顶栏显示所有页面现在操作的工作区。", "頂端列顯示所有頁面現在操作的工作區。"),
            Side.Bottom),
        Note.Callout(
            2, At.Text("● " + Worlds.Creative),
            shot.T("The mark moved to it.", "标记移到了它上面。", "標記移到了它上面。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task AddFolder(Language language) {
    // A server that was set up without this list knowing of it.
    World world = Worlds.Survival(language, height: 22);
    foreach (string folder in new[] { "behavior_packs", "mods", "resource_packs", "worlds" }) {
      world.Fs.AddDirectory(Path.Join(Lobby, folder));
    }

    world.InstallIn(
        Lobby, new Installed(World.Game, World.GameVersion),
        new Installed(World.Loader, World.LoaderVersion));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(Worlds.Skyblock);

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(WorkspacesText.AddFolderDetail);
    await shot.Capture(
        "workspaces-10-add-row", Note.Box(At.Row(WorkspacesText.AddFolder)), Note.Keys("Enter"));

    // The browser starts in the selected workspace: up one level are the other servers.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.AddTitle} ─");
    await tui.WaitUntilGone(PathPickerText.Reading);
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("lobby" + Path.DirectorySeparatorChar);
    await tui.WaitUntilGone(PathPickerText.Reading);
    tui.Press(ConsoleKey.UpArrow, 2);
    await shot.Capture(
        "workspaces-11-browse",
        Note.Callout(
            1, At.Text(PathPickerText.Folder + "  "),
            shot.T(
                "The folder that the list shows.", "列表当前显示的文件夹。", "清單目前顯示的資料夾。")),
        Note.Callout(
            2, At.Row("lobby" + Path.DirectorySeparatorChar),
            shot.T(
                "A folder inside it: Enter or → opens it.", "其中的一个文件夹：按 Enter 或 → 打开。",
                "其中的一個資料夾：按 Enter 或 → 開啟。")),
        Note.Keys("←", "↑", "↓", "Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Lobby);
    await tui.WaitUntilGone(PathPickerText.Reading);
    await shot.Capture(
        "workspaces-12-select-folder",
        Note.Box(At.Button(PathPickerText.SelectFolder)), Note.Keys("Tab", "Enter"));

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("● " + Lobby);
    await tui.WaitFor($"{CommonText.WorkspaceSelected}: {Lobby}");
    await shot.Capture("workspaces-13-added", Note.Box(At.Row("● " + Lobby)));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task NewServerChoices(Language language) {
    // lip was started in an empty folder: the server may go there.
    World world = Worlds.Empty(language, height: 24);
    world.ServerSetup = harness => new SetupOfVersions(harness, new SetupOfShots(harness, world));
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(WorkspacesText.NewServerDetail);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Elsewhere);
    await shot.Capture(
        "new-server-11-this-folder",
        Note.Focus(At.Panel(WorkspacesText.NewServerTitle)),
        Note.Callout(
            1, At.Row(WorkspacesText.Here),
            shot.T(
                "The selected workspace, which has no server yet.", "当前工作区，里面还没有服务器。",
                "目前的工作區，裡面還沒有伺服器。")),
        Note.Callout(
            // The top bar, the row of the list behind the dialog, and the line of the dialog.
            2, At.Text(world.Workspace, 3),
            shot.T("Its path.", "它的路径。", "它的路徑。")),
        Note.Keys("Enter"));

    // The game versions: which have a LeviLamina, and which do not.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor(WorkspacesText.HasNoLoader);
    tui.Press(ConsoleKey.DownArrow, 3);
    await tui.WaitFor(WorkspacesText.HasNoLoaderDetail);
    await shot.Capture(
        "new-server-12-no-levilamina",
        Note.Focus(At.Panel(WorkspacesText.GameVersion)),
        Note.Callout(
            1, At.Text(WorkspacesText.HasLoader, 1),
            shot.T(
                "A LeviLamina supports this version: mods work on it.",
                "有支持此版本的 LeviLamina：可以装模组。", "有支援此版本的 LeviLamina：可以裝模組。"),
            Side.Right),
        Note.Callout(
            2, At.Text(WorkspacesText.HasNoLoader, 1),
            shot.T(
                "No LeviLamina supports this version: a plain server only.",
                "没有支持此版本的 LeviLamina：只能是纯净服务器。",
                "沒有支援此版本的 LeviLamina：只能是純淨伺服器。"),
            Side.Right),
        Note.Callout(
            3, At.Text(WorkspacesText.HasNoLoaderDetail),
            shot.T(
                "The same, in words, for the selected version.", "对所选版本的文字说明。",
                "對所選版本的文字說明。")));

    // The loader step finds none for it, and is no dead end.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Loader} ─");
    await shot.Capture(
        "new-server-13-no-loader-fits",
        Note.Focus(At.Panel(WorkspacesText.Loader)),
        Note.Callout(
            1, At.Row(WorkspacesText.NoLoader),
            shot.T(
                "Goes on with a server that cannot run mods.", "继续安装不能运行模组的服务器。",
                "繼續安裝不能執行模組的伺服器。")),
        Note.Callout(
            2, At.Row(WorkspacesText.Back),
            shot.T(
                "Back to the game versions; Esc does the same.", "回到游戏版本列表；按 Esc 也一样。",
                "回到遊戲版本清單；按 Esc 也一樣。")));

    // Back, and the newest game: its loaders, the newest first, then "No loader".
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    tui.Press(ConsoleKey.UpArrow, 3);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("LeviLamina 26.60.3");
    tui.Press(ConsoleKey.DownArrow, 4);
    await tui.WaitFor(WorkspacesText.NoLoaderDetail);
    await shot.Capture(
        "new-server-14-no-loader",
        Note.Focus(At.Panel(WorkspacesText.Loader)),
        Note.Box(At.Row(WorkspacesText.NoLoader)));

    // Declining the license installs nothing.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(GameSetup.EulaUrl);
    tui.Press(ConsoleKey.RightArrow);
    await shot.Capture(
        "new-server-15-eula-decline",
        Note.Focus(At.Panel(CommonText.EulaTitle)),
        Note.Box(At.Button(CommonText.Decline)));
  }

  /// The steps that only Linux on x64 has. The picture is made on Windows, as all are: the
  /// path of the top bar is written as Linux writes it.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task NewServerOnLinux(Language language) {
    World world = Worlds.Empty(language, height: 24);
    world.Pages = WorkspacesPageTests.Pages(linux: true);
    using Shot shot = run.Start(world);
    shot.Scrub(@"D:\\servers\\survival|/D/servers/survival", "/home/alex/survival");
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor(WorkspacesText.NewServerDetail);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.Elsewhere);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor(WorkspacesText.OnlyBuild("windows"));
    await shot.Capture(
        "new-server-16-linux-games",
        Note.Focus(At.Panel(WorkspacesText.GameVersion)),
        Note.Callout(
            1, At.Text(WorkspacesText.OnlyBuild("windows")),
            shot.T(
                "This version has no Linux server: the build is not asked for.",
                "这个版本没有 Linux 服务器，不会询问构建。", "這個版本沒有 Linux 伺服器，不會詢問組建。"),
            Side.Right),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Build} ─");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(WorkspacesText.LinuxDetail);
    await shot.Capture(
        "new-server-17-build",
        Note.Focus(At.Panel(WorkspacesText.Build)),
        Note.Callout(
            1, At.Row("windows"),
            shot.T(
                "The Windows server, run through Wine: the one that LeviLamina and mods need.",
                "Windows 服务器，通过 Wine 运行：LeviLamina 和模组需要它。",
                "Windows 伺服器，透過 Wine 執行：LeviLamina 和模組需要它。")),
        Note.Callout(
            2, At.Row("linux"),
            shot.T(
                "The Linux server: runs natively, without a loader and without mods.",
                "Linux 服务器：原生运行，没有加载器，也不能装模组。",
                "Linux 伺服器：原生執行，沒有載入器，也不能裝模組。")),
        Note.Keys("↑", "↓", "Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ChangeVersions(Language language) {
    World world = Worlds.Survival(language, height: 24);
    world.ServerSetup = harness => new SetupOfVersions(harness, new SetupOfShots(harness, world)) {
      Plan = [
        $"Dry run: would uninstall {World.Loader} {World.LoaderVersion} (12 files)",
        $"Dry run: would install {World.Loader} 26.60.2 (12 files)",
      ],
    };
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + world.Workspace);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor(WorkspacesText.Installed);
    await shot.Capture(
        "workspaces-14-change-game",
        Note.Focus(At.Panel(WorkspacesText.GameVersion)),
        Note.Box(At.Text(WorkspacesText.Installed)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("LeviLamina 26.60.2");
    tui.Press(ConsoleKey.DownArrow);
    await shot.Capture(
        "workspaces-15-change-loader",
        Note.Focus(At.Panel(WorkspacesText.Loader)),
        Note.Box(At.Row("LeviLamina 26.60.2")),
        Note.Keys("↓", "Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    await shot.Capture(
        "workspaces-16-change-plan",
        Note.Callout(
            1, At.Text("Dry run: would uninstall"),
            shot.T(
                "What lip would remove and install, in the words of the command.",
                "lip 将要移除和安装的内容（命令的原话）。", "lip 將要移除和安裝的內容（命令的原話）。")),
        Note.Callout(
            2, At.Button(CommonText.Apply),
            shot.T(
                "Nothing changes before you choose this.", "选择它之前不会有任何更改。",
                "選擇它之前不會有任何變更。"),
            Side.Bottom));
  }

  /// A workspace that was set up with the Linux server, opened on a system that does not run
  /// it: the wizard says that it replaces the game with the Windows server.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task ChangeFromLinuxBuild(Language language) {
    World world = new World(language, height: 24)
        .Install(new Installed(World.Game, World.GameVersion, Variant: "linux"));
    world.ServerSetup = harness => new SetupOfVersions(harness, new SetupOfShots(harness, world)) {
      Plan = [
        $"Dry run: would uninstall {World.Game}#linux {World.GameVersion} (3 files)",
        $"Dry run: would install {World.Game} {World.GameVersion} (3 files)",
        $"Dry run: would install {World.Loader} {World.LoaderVersion} (12 files)",
        GameSetup.EulaDryRunMessage + $" ({GameSetup.EulaUrl}) and the Privacy Policy"
            + $" ({GameSetup.PrivacyPolicyUrl}), and download the game.",
      ],
    };
    using Shot shot = run.Start(world);
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Workspaces);
    await tui.WaitFor("● " + world.Workspace);

    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(WorkspacesText.ChangeVersions);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.GameVersion} ─");
    await tui.WaitFor(Head(WorkspacesText.LinuxNotHere));
    await shot.Capture(
        "workspaces-17-linux-build",
        Note.Focus(At.Panel(WorkspacesText.GameVersion)),
        Note.Callout(
            1, At.Text(Head(WorkspacesText.LinuxNotHere)),
            shot.T(
                "Said before you choose anything.", "在你做任何选择之前就会说明。",
                "在你做任何選擇之前就會說明。")));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("LeviLamina 26.60.3");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    await shot.Capture(
        "workspaces-18-switch-to-windows",
        Note.Box(At.Text(WorkspacesText.SetUpBuildTitle("windows"))));
  }

  /// The server setup of the scenarios that change versions: the LeviLamina versions that fit
  /// a game are those made for it, and none for a game that has none; and the dry run of a
  /// setup says what Plan holds when it is given.
  private sealed class SetupOfVersions(TuiHarness harness, SetupOfShots setup) : IServerSetup {
    private static readonly Dictionary<string, string[]> _loaders = new() {
      ["1.26.60"] = ["26.60.3", "26.60.2", "26.60.1", "26.60.0"],
      ["1.26.50"] = ["26.50.2", "26.50.1", "26.50.0"],
      ["1.26.40"] = ["26.40.1", "26.40.0"],
      ["1.26.20"] = ["26.20.4", "26.20.3"],
      ["1.26.10"] = ["26.10.2", "26.10.1"],
    };

    /// The lines of the dry run, as lip setup would say them; those of SetupOfShots when null.
    public string[]? Plan { get; init; }

    public Task<IReadOnlyList<GameVersion>> GameVersions(
        LipClient client, OperationSession? session = null) =>
        setup.GameVersions(client, session);

    public Task<IReadOnlyList<SemVersion>> LoaderVersions(
        LipClient client,
        SemVersion game,
        CancellationToken cancellationToken,
        OperationSession? session = null) =>
        Task.FromResult<IReadOnlyList<SemVersion>>([
          .. (_loaders.GetValueOrDefault(game.ToString()) ?? [])
              .Select(version => SemVersion.Parse(version)),
        ]);

    public async Task<OperationResult> Setup(
        LipClient client, SetupRequest request, OperationRun run) {
      if (!run.DryRun || Plan is null) {
        return await setup.Setup(client, request, run);
      }

      foreach (string line in Plan) {
        await harness.InteractionOf(client).PrintInfo(line);
      }

      return OperationResult.Changed;
    }
  }
}
