using System.Globalization;
using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Server;

/// The text of the Server page.
public static class ServerText {
  public static string Title => L.T("Server", "服务器", "伺服器");

  // The buttons and the toggle.
  public static string Start => L.T("Start", "启动", "啟動");
  public static string Stop => L.T("Stop", "停止", "停止");
  public static string AutoRestartDetail =>
      L.T("Restarts after a crash.", "崩溃后重新启动。", "當機後重新啟動。");

  // The state of the server.
  public static string ExitedWith(int code) {
    string number = code.ToString(CultureInfo.InvariantCulture);
    return L.T($"Exited with code {number}", $"已退出，退出码 {number}", $"已結束，結束代碼 {number}");
  }

  // The output and the input line.
  public static string Output => L.T("Output", "输出", "輸出");
  public static string NoOutput => L.T("No output yet.", "还没有输出。", "還沒有輸出。");
  public static string NoServer =>
      L.T("No server here yet.", "这里还没有服务器。", "這裡還沒有伺服器。");

  /// The example that the empty input line shows: a command of the server.
  public static string InputExample => "list";

  /// The title of the failure of a server that did not start.
  public static string StartTitle => L.T("Start server", "启动服务器", "啟動伺服器");

  // The hints of the bottom bar.
  public static Hint HintSend => new("Enter", L.T("Send", "发送", "傳送"));
  public static Hint HintHistory => new("↑↓", L.T("History", "历史", "歷史"));
}
