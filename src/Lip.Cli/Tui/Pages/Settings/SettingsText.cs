using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Settings;

/// The text of the Settings page.
public static class SettingsText {
  public static string Title => L.T("Settings", "设置", "設定");

  // What several rows show.
  public static string Unreadable => L.T("Unreadable", "无法读取", "無法讀取");
  public static string Saved => L.T("Saved.", "已保存。", "已儲存。");

  // The language.
  public static string Language => L.T("Language", "语言", "語言");
  public static string LanguageDetail =>
      L.T("The language of the interface.", "界面使用的语言。", "介面使用的語言。");
  public static string FollowSystem => L.T("Follow system", "跟随系统", "跟隨系統");

  // The GitHub proxy.
  public static string GithubProxy => L.T("GitHub proxy", "GitHub 代理", "GitHub 代理伺服器");
  public static string GithubProxyDetail =>
      L.T(
          "A URL prefix that lip puts before GitHub URLs where GitHub is slow or blocked.",
          "GitHub 访问缓慢或受阻时，lip 加在 GitHub 网址前面的前缀。",
          "GitHub 連線緩慢或受阻時，lip 加在 GitHub 網址前面的前置網址。");
  public static string NotSet => L.T("Not set", "未设置", "未設定");
  public static string Direct => L.T("Direct", "直连", "直接連線");
  public static string SetProxy => L.T("Set proxy…", "设置代理…", "設定代理伺服器…");
  public static string ConnectDirectly => L.T("Connect directly", "直接连接", "直接連線");
  public static string Reset => L.T("Reset", "重置", "重設");
  public static string ProxyUrl => L.T("Proxy URL", "代理网址", "代理伺服器網址");
  public static string ProxyPlaceholder => "https://example.com/";
  public static string NotAUrl => L.T("Not a URL", "不是网址", "不是網址");
  public static string TestingProxy => L.T("Testing proxy", "测试代理", "測試代理伺服器");
  public static string ProxyFailed => L.T("Proxy failed", "代理不可用", "代理伺服器無法使用");
  public static string EnterAnother => L.T("Enter another", "重新输入", "重新輸入");
  public static string SaveAnyway => L.T("Save anyway", "仍然保存", "仍要儲存");

  // The restart policy of the workspace.
  public static string AutoRestart => L.T("Auto restart", "自动重启", "自動重新啟動");
  public static string AutoRestartDetail =>
      L.T(
          "Restarts the server of this workspace when it crashes.",
          "此工作区的服务器崩溃时自动重启。",
          "此工作區的伺服器當機時自動重新啟動。");
  public static string On => L.T("On", "开", "開");
  public static string Off => L.T("Off", "关", "關");

  // The cache.
  public static string ClearCache => L.T("Clear cache", "清理缓存", "清除快取");
  public static string ClearCacheDetail =>
      L.T(
          "Deletes the downloaded files; workspaces and settings stay.",
          "删除已下载的文件，不影响工作区和设置。",
          "刪除已下載的檔案，不影響工作區和設定。");
  public static string ClearCacheQuestion =>
      L.T("Delete all downloaded files?", "要删除所有已下载的文件吗？", "要刪除所有已下載的檔案嗎？");
  public static string Clear => L.T("Clear", "清理", "清除");
  public static string CacheCleared => L.T("Cache cleared.", "缓存已清理。", "快取已清除。");

  // The updates of lip.
  public static string CheckUpdates => L.T("Check for updates", "检查更新", "檢查更新");
  public static string CheckUpdatesDetail =>
      L.T("Looks for a newer release of lip.", "查看 lip 是否有新版本。", "查看 lip 是否有新版本。");
  public static string UpToDate => L.T("Up to date", "已是最新", "已是最新版本");
  public static string InstallUpdate => L.T("Install update", "安装更新", "安裝更新");
  public static string InstallUpdateDetail =>
      L.T(
          "Replaces lip with its newest release.",
          "将 lip 更新到最新版本。",
          "將 lip 更新到最新版本。");
  public static string Install => L.T("Install", "安装", "安裝");
  public static string RestartLip => L.T("Restart lip", "请重启 lip", "請重新啟動 lip");
  public static string InstallerRunning =>
      L.T("Installer running", "安装程序运行中", "安裝程式執行中");

  public static string UpdateAvailable(string version) =>
      L.T($"lip {version} is available.", $"有新版本 lip {version}。", $"有新版本 lip {version}。");

  public static string InstallQuestion(string version) =>
      L.T($"Install lip {version}?", $"要安装 lip {version} 吗？", $"要安裝 lip {version} 嗎？");

  public static string Installed(string version) =>
      L.T(
          $"lip {version} is installed. Restart lip to use it.",
          $"lip {version} 已安装。请重启 lip 以使用新版本。",
          $"lip {version} 已安裝。請重新啟動 lip 以使用新版本。");

  public static string InstallerStarted(string version) =>
      L.T(
          $"The installer of lip {version} is running. Quit lip so that it can finish.",
          $"lip {version} 的安装程序正在运行。请退出 lip 以完成安装。",
          $"lip {version} 的安裝程式正在執行。請結束 lip 以完成安裝。");

  // The daemon.
  public static string Daemon => L.T("Daemon", "守护进程", "常駐程式");
  public static string DaemonDetail =>
      L.T(
          "The background process of lip that runs commands and servers.",
          "在后台执行命令并运行服务器的 lip 进程。",
          "在背景執行命令並執行伺服器的 lip 處理程序。");
  public static string NotRunning => L.T("Not running", "未运行", "未執行");
  public static string Refresh => L.T("Refresh", "刷新", "重新整理");

  public static string DaemonCommands(int commands) =>
      L.T(
          $"Commands in progress: {commands}",
          $"进行中的命令：{commands}",
          $"進行中的命令：{commands}");

  public static string DaemonServers(int servers) =>
      L.T($"Servers running: {servers}", $"运行中的服务器：{servers}", $"執行中的伺服器：{servers}");

  // lip itself.
  public static string About => L.T("About", "关于", "關於");
  public static string AboutDetail =>
      L.T("The version of lip that is running.", "当前运行的 lip 版本。", "目前執行的 lip 版本。");
}
