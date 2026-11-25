using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.GetMods;

/// The text of the dialog that a lip: link opens (LinkDialog).
public static class LinkText {
  public static string Title => L.T("Install from a link", "从链接安装", "從連結安裝");

  public static string Asked => L.T(
      "A link asks lip to install this package. Install it only if you trust where it comes"
          + " from.",
      "一个链接请求 lip 安装此包。请只在信任其来源时安装。",
      "一個連結要求 lip 安裝此套件。請只在信任其來源時安裝。");

  public static string Package => L.T("Package", "包", "套件");
  public static string Name => L.T("Name", "名称", "名稱");
  public static string Author => L.T("Author", "作者", "作者");
  public static string Version => L.T("Version", "版本", "版本");
  public static string Variant => L.T("Variant", "变体", "變體");
  public static string Dependencies => L.T("Needs", "依赖", "相依");
  public static string Workspace => L.T("Into", "安装到", "安裝到");
  public static string Unknown => L.T("unknown", "未知", "未知");
  public static string None => L.T("none", "无", "無");

  public static string Latest(string? version) => version is null
      ? L.T("latest", "最新", "最新")
      : L.T($"latest ({version})", $"最新（{version}）", $"最新（{version}）");

  public static string Listed => L.T(
      "Listed in the LeviMC catalogue.",
      "已收录于 LeviMC 模组目录。",
      "已收錄於 LeviMC 模組目錄。");

  public static string NotListed => L.T(
      "Not in the LeviMC catalogue. lip knows nothing about this package or who made it.",
      "不在 LeviMC 模组目录中。lip 对此包及其作者一无所知。",
      "不在 LeviMC 模組目錄中。lip 對此套件及其作者一無所知。");

  public static string CatalogueUnreadable(string error) => L.T(
      "The LeviMC catalogue cannot be read, so lip cannot tell whether it lists this package: "
          + error,
      $"无法读取 LeviMC 模组目录，lip 无法判断其中是否收录此包：{error}",
      $"無法讀取 LeviMC 模組目錄，lip 無法判斷其中是否收錄此套件：{error}");

  public static string VersionNotListed(string version) => L.T(
      $"The catalogue lists no version {version} of this mod.",
      $"目录中没有此模组的 {version} 版本。",
      $"目錄中沒有此模組的 {version} 版本。");

  public static string NoWorkspace => L.T(
      "No folder chosen: choose where to install it.",
      "尚未选择文件夹：请选择安装位置。",
      "尚未選擇資料夾：請選擇安裝位置。");

  public static string PlanFollows => L.T(
      "Install shows what would change, and asks once more before it changes anything.",
      "安装前会显示将要进行的更改，并在更改前再次确认。",
      "安裝前會顯示將要進行的變更，並在變更前再次確認。");

  public static string InstallButton => L.T("Install…", "安装…", "安裝…");
  public static string ChooseWorkspace =>
      L.T("Choose folder…", "选择文件夹…", "選擇資料夾…");
}
