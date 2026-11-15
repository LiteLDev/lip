using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Workspaces;

/// The text of the Workspaces page.
public static class WorkspacesText {
  public static string Title => L.T("Workspaces", "工作区", "工作區");

  // The action rows above the list.
  public static string NewServerDetail =>
      L.T(
          "Creates a folder and installs the game in it.",
          "创建文件夹并在其中安装游戏。",
          "建立資料夾並在其中安裝遊戲。");
  public static string AddFolder => L.T("Add existing folder…", "添加已有文件夹…", "加入現有資料夾…");
  public static string AddFolderDetail =>
      L.T(
          "Adds a folder that already holds a server.",
          "添加一个已有服务器的文件夹。",
          "加入已有伺服器的資料夾。");

  // The list.
  public static string Empty => L.T("No workspaces.", "没有工作区。", "沒有工作區。");
  public static string Missing => L.T("missing", "已丢失", "已遺失");
  public static string ServerRunning => L.T("server running", "服务器运行中", "伺服器執行中");
  public static string CurrentDetail => L.T("The selected workspace.", "当前工作区。", "目前的工作區。");
  public static string MissingDetail =>
      L.T("This folder no longer exists.", "此文件夹已不存在。", "此資料夾已不存在。");
  public static string RunningDetail =>
      L.T("Its server is running.", "它的服务器正在运行。", "它的伺服器正在執行。");
  public static string CurrentRunningDetail =>
      L.T(
          "The selected workspace, with its server running.",
          "当前工作区，服务器正在运行。",
          "目前的工作區，伺服器正在執行。");
  public static string ListUnreadable =>
      L.T("Cannot read the workspace list", "无法读取工作区列表", "無法讀取工作區清單");

  // The action menu of a workspace.
  public static string Switch => L.T("Switch", "切换", "切換");
  public static string ChangeVersions =>
      L.T("Change game or loader version…", "更改游戏或加载器版本…", "變更遊戲或載入器版本…");
  public static string Remove => L.T("Remove from list", "从列表移除", "從清單移除");
  public static string RemoveQuestion =>
      L.T(
          "Remove it from the list? No file is deleted.",
          "要从列表中移除吗？不会删除任何文件。",
          "要從清單中移除嗎？不會刪除任何檔案。");
  public static string Removed => L.T("Removed from the list.", "已从列表移除。", "已從清單移除。");

  // Adding a folder.
  public static string AddTitle => L.T("Add folder", "添加文件夹", "加入資料夾");

  // The new server.
  public static string NewServerTitle => L.T("New server", "新建服务器", "建立伺服器");
  public static string Here => L.T("This folder", "当前文件夹", "目前的資料夾");
  public static string Elsewhere => L.T("Another folder…", "其他文件夹…", "其他資料夾…");
  public static string ElsewhereDetail =>
      L.T(
          "A folder that does not exist is created.",
          "文件夹不存在时会自动创建。",
          "資料夾不存在時會自動建立。");
  public static string IsFile => L.T("That is a file", "这是一个文件", "這是一個檔案");
  public static string InvalidPath => L.T("Not a valid path", "路径无效", "路徑無效");
  public static string CannotCreate =>
      L.T("Cannot create the folder", "无法创建文件夹", "無法建立資料夾");

  // The choices of a setup.
  public static string ReadingGames =>
      L.T("Reading game versions", "正在读取游戏版本", "正在讀取遊戲版本");
  public static string GameVersion => L.T("Game version", "游戏版本", "遊戲版本");
  public static string NoGames =>
      L.T("No game version is available.", "没有可用的游戏版本。", "沒有可用的遊戲版本。");
  public static string Newest => L.T("newest", "最新", "最新");
  public static string Installed => L.T("installed", "已安装", "已安裝");
  public static string Prerelease => L.T("preview", "预览版", "預覽版");
  public static string HasLoader => "LeviLamina ✓";
  public static string HasNoLoader => L.T("No LeviLamina", "无 LeviLamina", "無 LeviLamina");
  public static string HasLoaderDetail =>
      L.T(
          "Mods need LeviLamina, which supports this version.",
          "模组需要 LeviLamina，它支持此版本。",
          "模組需要 LeviLamina，它支援此版本。");
  public static string HasNoLoaderDetail =>
      L.T(
          "Mods need LeviLamina, which this version lacks.",
          "模组需要 LeviLamina，而此版本没有。",
          "模組需要 LeviLamina，而此版本沒有。");

  /// "windows only": the one build of a game version, where others have more.
  public static string OnlyBuild(string build) =>
      L.T($"{build} only", $"仅 {build}", $"僅 {build}");
  public static string Build => L.T("Build", "构建", "組建");
  public static string WindowsDetail =>
      OperatingSystem.IsWindows()
          ? L.T(
              "Supports LeviLamina and its mods.",
              "支持 LeviLamina 及其模组。",
              "支援 LeviLamina 及其模組。")
          : L.T(
              "Supports LeviLamina and its mods, through Wine.",
              "通过 Wine 支持 LeviLamina 及其模组。",
              "透過 Wine 支援 LeviLamina 及其模組。");
  public static string LinuxDetail =>
      L.T(
          "No loader; for Linux on x64 only.",
          "没有加载器；仅适用于 x64 的 Linux。",
          "沒有載入器；僅適用於 x64 的 Linux。");
  public static string ReadingLoaders =>
      L.T("Finding loader versions", "正在查找加载器版本", "正在尋找載入器版本");
  public static string Loader => L.T("Loader", "加载器", "載入器");
  public static string LoaderDetail =>
      L.T("Lets the server run mods.", "让服务器可以运行模组。", "讓伺服器可以執行模組。");
  public static string NoLoader => L.T("No loader", "不安装加载器", "不安裝載入器");
  public static string NoLoaderDetail =>
      L.T("A plain server, without mods.", "纯净服务器，不带模组。", "純淨伺服器，不含模組。");
  public static string NoLoaderFits =>
      L.T(
          "No LeviLamina version supports this game version.",
          "没有支持此游戏版本的 LeviLamina。",
          "沒有支援此遊戲版本的 LeviLamina。");
  public static string SetUpTitle => L.T("Set up server", "安装服务器", "安裝伺服器");
}
