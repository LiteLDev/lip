using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Mods;

/// The text of the Mods page.
public static class ModsText {
  public static string Title => L.T("Mods", "模组", "模組");

  // The action rows, each with the one sentence of the details area.
  public static string InstallFromSource =>
      L.T("Install from source…", "从来源安装…", "從來源安裝…");
  public static string InstallFromSourceDetail =>
      L.T(
          "A repository, an https:// URL of a .zip file, or a local .zip file or folder.",
          "可以是仓库、.zip 文件的 https:// 网址，或本地的 .zip 文件或文件夹。",
          "可以是儲存庫、.zip 檔案的 https:// 網址，或本機的 .zip 檔案或資料夾。");
  public static string UpdateAll => L.T("Update all", "全部更新", "全部更新");
  public static string UpdateAllDetail =>
      L.T(
          "Updates every mod that you installed.",
          "更新你安装的所有模组。",
          "更新你安裝的所有模組。");
  public static string GetModsDetail =>
      L.T(
          "Browse the mods of levimc.org.",
          "浏览 levimc.org 上的模组。",
          "瀏覽 levimc.org 上的模組。");
  public static string NewServerDetail =>
      L.T(
          "This workspace has no server yet.",
          "这个工作区还没有服务器。",
          "這個工作區還沒有伺服器。");

  // The list.
  public static string Installed(int count) =>
      L.T($"Installed ({count})", $"已安装（{count}）", $"已安裝（{count}）");
  public static string Checked(int count) =>
      L.T($"{count} checked", $"已勾选 {count} 个", $"已勾選 {count} 個");
  public static string Version => L.T("Version", "版本", "版本");

  // What a package is, in the details area.
  public static string ExplicitDetail =>
      L.T("Installed by you.", "由你安装。", "由你安裝。");
  public static string DependencyDetail =>
      L.T(
          "Installed because another mod needs it.",
          "因其他模组需要而安装。",
          "因其他模組需要而安裝。");
  public static string GameDetail =>
      L.T(
          "The game. Change its version in Workspaces.",
          "游戏本体。请在“工作区”中更改版本。",
          "遊戲本體。請在「工作區」中變更版本。");
  public static string LoaderDetail =>
      L.T(
          "The mod loader. Change its version in Workspaces.",
          "模组加载器。请在“工作区”中更改版本。",
          "模組載入器。請在「工作區」中變更版本。");

  // The action menu of a row.
  public static string Update => L.T("Update", "更新", "更新");
  public static string UpdateTo => L.T("Update to version…", "更新到指定版本…", "更新到指定版本…");
  public static string Uninstall => L.T("Uninstall", "卸载", "解除安裝");
  public static string UpdateChecked(int count) =>
      L.T(
          count == 1 ? "Update 1 mod" : $"Update {count} mods",
          $"更新 {count} 个模组", $"更新 {count} 個模組");
  public static string UninstallChecked(int count) =>
      L.T(
          count == 1 ? "Uninstall 1 mod" : $"Uninstall {count} mods",
          $"卸载 {count} 个模组", $"解除安裝 {count} 個模組");
  public static string UncheckAll => L.T("Uncheck all", "取消全部勾选", "取消全部勾選");
  public static string GoToWorkspaces => L.T("Go to Workspaces", "前往工作区", "前往工作區");

  // The prompts.
  public static string InstallTitle => L.T("Install from source", "从来源安装", "從來源安裝");
  public static string Source => L.T("Source", "来源", "來源");
  public static string SourcePlaceholder => "github.com/owner/repo@1.2.3";
  public static string UpdateToTitle => L.T("Update to version", "更新到指定版本", "更新到指定版本");
  public static string RefPlaceholder => "1.2.3";
  public static string RefInvalid =>
      L.T(
          "Give a version, branch:<name>, tag:<name> or rev:<commit>.",
          "请填写版本号、branch:<名称>、tag:<名称> 或 rev:<提交>。",
          "請填寫版本號、branch:<名稱>、tag:<名稱> 或 rev:<提交>。");

  // The titles of the operations.
  public static string InstallOperation(string packages) =>
      L.T($"Install {packages}", $"安装 {packages}", $"安裝 {packages}");
  public static string UpdateOperation(string packages) =>
      L.T($"Update {packages}", $"更新 {packages}", $"更新 {packages}");
  public static string UninstallOperation(string packages) =>
      L.T($"Uninstall {packages}", $"卸载 {packages}", $"解除安裝 {packages}");
}
