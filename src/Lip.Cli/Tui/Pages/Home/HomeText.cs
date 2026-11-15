using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Home;

/// The text of the Home page.
public static class HomeText {
  public static string Title => L.T("Home", "主页", "首頁");

  // The overview of the selected workspace.
  public static string Workspace => L.T("Workspace", "工作区", "工作區");
  public static string Game => L.T("Game", "游戏", "遊戲");
  public static string Loader => L.T("Loader", "加载器", "載入器");
  public static string Mods => L.T("Mods", "模组", "模組");
  public static string Server => L.T("Server", "服务器", "伺服器");
  public static string NotInstalled => L.T("Not installed", "未安装", "未安裝");
  public static string None => L.T("None", "无", "無");

  // The next steps.
  public static string NextSteps => L.T("Next steps", "下一步", "後續步驟");
  public static string CreateServer => L.T("Create a server", "创建服务器", "建立伺服器");
  public static string InstallMods => L.T("Install mods", "安装模组", "安裝模組");
  public static string StartServer => L.T("Start the server", "启动服务器", "啟動伺服器");

  public static string CreateServerDetail =>
      L.T(
          "Pick a folder, a game and a loader.",
          "选择文件夹、游戏版本和加载器。",
          "選擇資料夾、遊戲版本和載入器。");

  public static string CreateServerDoneDetail =>
      L.T(
          "Done. Enter sets up another server.",
          "已完成。按 Enter 再建一个。",
          "已完成。按 Enter 再建一個。");

  public static string InstallModsDetail =>
      L.T(
          "Browse the mods of levimc.org.",
          "浏览 levimc.org 上的模组。",
          "瀏覽 levimc.org 上的模組。");

  public static string StartServerDetail =>
      L.T(
          "Start the server and open its console.",
          "启动服务器并打开控制台。",
          "啟動伺服器並開啟主控台。");

  public static string OpenServerDetail =>
      L.T(
          "Running. Enter opens its console.",
          "运行中。按 Enter 打开控制台。",
          "執行中。按 Enter 開啟主控台。");

  public static string NeedServer =>
      L.T("Create a server first.", "请先创建服务器。", "請先建立伺服器。");

  // GitHub reachability (protocol/lip-tui.md, "GitHub reachability").
  public static string GithubTitle => L.T("GitHub unreachable", "无法连接 GitHub", "無法連線 GitHub");
  public static string GithubQuestion =>
      L.T("How should lip reach GitHub?", "lip 应如何连接 GitHub？", "lip 要如何連線 GitHub？");
  public static string GithubProxy => L.T("Use a proxy", "使用代理", "使用代理伺服器");

  public static string GithubProxyDetail =>
      L.T(
          "Enter the URL of a GitHub proxy; lip tests it first.",
          "输入 GitHub 代理的网址，lip 会先测试。",
          "輸入 GitHub 代理伺服器的網址，lip 會先測試。");

  public static string GithubDirectDetail =>
      L.T(
          "Keep connecting directly, and do not ask again.",
          "保持直接连接，不再询问。",
          "維持直接連線，不再詢問。");

  public static string GithubLaterDetail =>
      L.T(
          "Change nothing, and ask again next time.",
          "不做更改，下次启动时再问。",
          "不做變更，下次啟動時再問。");

  public static string GithubDirectSaved =>
      L.T("Direct connection saved.", "已设为直接连接。", "已設為直接連線。");
}
