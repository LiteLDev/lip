using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Tools;

/// The text of the Tools page.
public static class ToolsText {
  public static string Title => L.T("Develop Tools", "开发工具", "開發工具");

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
          "Packs a folder into a package ZIP, saved in the workspace unless you choose elsewhere.",
          "将文件夹打包为包的 ZIP，默认保存在工作区中。",
          "將資料夾封裝成套件的 ZIP，預設儲存在工作區中。");
  public static string ViewAbout =>
      L.T(
          "Shows the package file and the versions of a package.",
          "显示包的包文件和可用版本。",
          "顯示套件的套件檔和可用版本。");

  // Why an action cannot be done in the selected workspace.
  public static string Exists(string file) =>
      L.T($"{file} already exists here.", $"这里已有 {file}。", $"這裡已有 {file}。");
  public static string NoFile(string file) =>
      L.T($"There is no {file} here.", $"这里没有 {file}。", $"這裡沒有 {file}。");

  // The titles of the dialogs.
  public static string PackTitle => L.T("Pack ZIP", "打包 ZIP", "封裝 ZIP");
  public static string PackFolder => L.T("Folder to pack", "要打包的文件夹", "要封裝的資料夾");
  public static string PackOutput => L.T("Save ZIP", "保存 ZIP", "儲存 ZIP");
  public static string ViewTitle => L.T("View package", "查看包信息", "檢視套件資訊");
  public static string ViewInstalledTitle =>
      L.T("Installed mods", "已安装的模组", "已安裝的模組");
  public static string ViewLocal => L.T("Choose package", "选择包", "選擇套件");

  // Pack ZIP: the question about an archive that exists.
  public static string ReplaceQuestion(string file) =>
      L.T(
          $"{file} already exists. Replace it?",
          $"{file} 已存在。要替换它吗？",
          $"{file} 已存在。要取代它嗎？");
  public static string Replace => L.T("Replace", "替换", "取代");

  // View package: where the package comes from, and one sentence about each.
  public static string ViewInstalled => L.T("Installed mod…", "已安装的模组…", "已安裝的模組…");
  public static string ViewTyped => L.T("Type a package…", "输入包…", "輸入套件…");
  public static string ViewBrowse => L.T("Browse…", "浏览…", "瀏覽…");
  public static string ViewInstalledAbout =>
      L.T(
          "Shows the installed version of a package of the workspace.",
          "显示工作区中某个包的已安装版本。",
          "顯示工作區中某個套件的已安裝版本。");
  public static string ViewTypedAbout =>
      L.T(
          "Shows a repository, a URL or a path that you type.",
          "显示你输入的仓库、网址或路径。",
          "顯示你輸入的儲存庫、網址或路徑。");
  public static string ViewBrowseAbout =>
      L.T(
          "Shows a local ZIP file or the folder of a package.",
          "显示本地的 ZIP 文件或包的文件夹。",
          "顯示本機的 ZIP 檔案或套件的資料夾。");
  public static string NothingInstalled =>
      L.T("Nothing is installed here.", "这里还没有安装任何包。", "這裡尚未安裝任何套件。");

  // The field of a package that is typed.
  public static string Package => L.T("Package", "包", "套件");
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
