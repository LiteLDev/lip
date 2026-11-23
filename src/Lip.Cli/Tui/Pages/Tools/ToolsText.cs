using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Tools;

/// The text of the Tools page.
public static class ToolsText {
  public static string Title => L.T("Develop Tools", "开发工具", "開發工具");

  // The actions: what the list shows, with … when the action asks first.
  public static string Init => L.T("Create lip.toml", "创建 lip.toml", "建立 lip.toml");
  public static string Migrate => L.T("Migrate tooth.json", "迁移 tooth.json", "移轉 tooth.json");

  // One sentence about the selected action, for the details area.
  public static string InitAbout =>
      L.T(
          "Writes a lip.toml for a new package in the workspace.",
          "在工作区中为新包创建 lip.toml。",
          "在工作區中為新套件建立 lip.toml。");
  public static string MigrateAbout =>
      L.T(
          "Converts the tooth.json in the workspace to a lip.toml.",
          "将工作区中的 tooth.json 转换为 lip.toml。",
          "將工作區中的 tooth.json 轉換為 lip.toml。");

  // Why an action cannot be done in the selected workspace.
  public static string Exists(string file) =>
      L.T($"{file} already exists here.", $"这里已有 {file}。", $"這裡已有 {file}。");
  public static string NoFile(string file) =>
      L.T($"There is no {file} here.", $"这里没有 {file}。", $"這裡沒有 {file}。");

  // The results.
  public static string Created(string file) =>
      L.T($"Created {file}.", $"已创建 {file}。", $"已建立 {file}。");
  public static string Migrated =>
      L.T(
          "Migrated tooth.json to lip.toml.",
          "已将 tooth.json 迁移为 lip.toml。",
          "已將 tooth.json 移轉為 lip.toml。");

  // The status area.
  public static string Found => L.T("Found", "已存在", "已存在");
  public static string Missing => L.T("None", "不存在", "不存在");
}
