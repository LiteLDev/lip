using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Tools;

/// The text of the Tools page.
public static class ToolsText {
  public static string Title => L.T("Tools", "工具", "工具");

  // The actions: what the list shows, with … when the action asks first.
  public static string Init => L.T("Create lip.toml", "创建 lip.toml", "建立 lip.toml");
  public static string Migrate => L.T("Migrate tooth.json", "迁移 tooth.json", "移轉 tooth.json");
  public static string Pack => L.T("Pack ZIP…", "打包 ZIP…", "封裝 ZIP…");
  public static string View => L.T("View package…", "查看包信息…", "檢視套件資訊…");

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
  public static string PackAbout =>
      L.T(
          "Packs a directory into the ZIP archive of a package.",
          "将目录打包为包的 ZIP 压缩包。",
          "將目錄封裝成套件的 ZIP 壓縮檔。");
  public static string ViewAbout =>
      L.T(
          "Shows the package file and the versions of a package.",
          "显示包的包文件和可用版本。",
          "顯示套件的套件檔和可用版本。");

  // The titles of the dialogs.
  public static string PackTitle => L.T("Pack ZIP", "打包 ZIP", "封裝 ZIP");
  public static string ViewTitle => L.T("View package", "查看包信息", "檢視套件資訊");

  // The fields.
  public static string Directory => L.T("Directory", "目录", "目錄");
  public static string Output => L.T("Output file", "输出文件", "輸出檔案");
  public static string Package => L.T("Package", "包", "套件");
  public static string OutputRequired =>
      L.T("Enter the output file.", "请填写输出文件。", "請填寫輸出檔案。");
  public static string PackageRequired => L.T("Enter a package.", "请填写包。", "請填寫套件。");

  // The results.
  public static string Created(string file) =>
      L.T($"Created {file}.", $"已创建 {file}。", $"已建立 {file}。");
  public static string Migrated =>
      L.T(
          "Migrated tooth.json to lip.toml.",
          "已将 tooth.json 迁移为 lip.toml。",
          "已將 tooth.json 移轉為 lip.toml。");
  public static string Packed(string file) =>
      L.T($"Packed {file}.", $"已打包到 {file}。", $"已封裝至 {file}。");

  // The status area.
  public static string Found => L.T("Found", "已存在", "已存在");
  public static string Missing => L.T("None", "不存在", "不存在");
}
