using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.ModConfig;

/// The text of the Mod config page.
public static class ModConfigText {
  public static string Title => L.T("Mod config", "模组配置", "模組設定");

  // The list of files.
  public static string Files => L.T("Files", "文件", "檔案");
  public static string CannotList =>
      L.T("Cannot read the workspace", "无法读取工作区", "無法讀取工作區");

  // What is shown in place of a file.
  public static string NoFile =>
      L.T(
          "No config file yet. Start the server once.",
          "还没有配置文件，请先启动一次服务器。",
          "還沒有設定檔，請先啟動一次伺服器。");
  public static string Gone => L.T("The file is gone.", "文件已不存在。", "檔案已不存在。");
  public static string CannotRead => L.T("Cannot read the file", "无法读取文件", "無法讀取檔案");

  // The line above a file that is only shown.
  public static string ReadOnly =>
      L.T("Read-only. Use a text editor.", "只读，请用文本编辑器修改。", "唯讀，請用文字編輯器修改。");
  public static string InvalidJson =>
      L.T("Read-only: not valid JSON.", "只读：不是有效的 JSON。", "唯讀：不是有效的 JSON。");

  // The tree of a JSON file.
  public static string Unsaved => L.T("Unsaved", "未保存", "未儲存");
  public static string AddValue => L.T("Add value…", "添加值…", "新增值…");
  public static string AddTitle => L.T("Add value", "添加值", "新增值");
  public static string Edit => L.T("Edit…", "编辑…", "編輯…");
  public static string Open => L.T("Open", "打开", "開啟");
  public static string Toggle => L.T("Toggle", "切换", "切換");
  public static string Remove => L.T("Remove", "移除", "移除");
  public static string Value => L.T("Value", "值", "值");
  public static string Text => L.T("Text", "文本", "文字");
  public static string Number => L.T("Number", "数字", "數字");
  public static string Boolean => L.T("True or false", "真或假", "真或假");
  public static string Null => L.T("Empty: takes any value", "空值：可填任意值", "空值：可填任意值");

  public static string Items(int count) =>
      L.T(count == 1 ? "1 item" : $"{count} items", $"{count} 项", $"{count} 項");

  // One sentence about the selected row.
  public static string VersionDetail =>
      L.T("The mod keeps this version itself.", "此版本号由模组自行维护。", "此版本號由模組自行維護。");
  public static string AddDetail =>
      L.T(
          "JSON, or plain text for a string.",
          "填 JSON；其他内容视为字符串。",
          "填 JSON；其他內容視為字串。");

  // Why a value is not taken.
  public static string NotNumber => L.T("Enter a number.", "请输入数字。", "請輸入數字。");
  public static string NotScalar =>
      L.T(
          "Enter text, a number, true, false or null.",
          "请输入文本、数字、true、false 或 null。",
          "請輸入文字、數字、true、false 或 null。");

  // Saving.
  public static string SaveTitle(string file) =>
      L.T($"Save {file}", $"保存 {file}", $"儲存 {file}");
}
