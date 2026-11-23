using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Server;

/// The text of the setup of the server (ServerWizard).
public static class SetupText {
  public static string ChangeVersions =>
      L.T("Change versions…", "更改版本…", "變更版本…");

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

  /// The title of the plan of a setup that changes the build of the workspace, which names
  /// the build that it sets up.
  public static string SetUpBuildTitle(string build) =>
      L.T($"Switch to {build}", $"改用 {build} 构建", $"改用 {build} 組建");

  /// Said in the game version step for a workspace of the build linux on a system where lip
  /// does not run it.
  public static string LinuxNotHere =>
      L.T(
          "This workspace has the linux build, which does not run on this system. The setup"
              + " replaces it with the windows build.",
          "此工作区使用 linux 构建，它无法在本系统上运行。安装时会改用 windows 构建。",
          "此工作區使用 linux 組建，它無法在本系統上執行。安裝時會改用 windows 組建。");
  public static string Back => L.T("Back", "返回", "返回");
  public static string BackDetail =>
      L.T("Go back and choose again.", "返回重新选择。", "返回重新選擇。");
}
