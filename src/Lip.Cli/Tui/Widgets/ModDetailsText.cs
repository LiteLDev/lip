using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// The text of the details of a mod of the catalogue (ModDetails, ModDetailsDialog).
public static class ModDetailsText {
  public static string Details => L.T("Details", "详情", "詳細資料");
  public static string Author => L.T("Author", "作者", "作者");
  public static string Stars => L.T("Stars", "星标", "星號");
  public static string Downloads => L.T("Downloads", "下载量", "下載次數");
  public static string Id => L.T("ID", "ID", "ID");
  public static string Latest => L.T("Latest", "最新版本", "最新版本");
  public static string Platform => L.T("Platform", "平台", "平台");
  public static string Game => L.T("Game", "游戏", "遊戲");
  public static string Tags => L.T("Tags", "标签", "標籤");
  public static string License => L.T("License", "许可证", "授權條款");
  public static string Depends => L.T("Depends", "依赖", "相依");
  public static string Updated => L.T("Updated", "更新日期", "更新日期");
  public static string None => L.T("None", "无", "無");

  /// The name of a featured tag of the catalogue, by its id; other for any other tag.
  public static string Tag(string id, string other) => id switch {
    "library" => L.T(other, "库 / API", "函式庫 / API"),
    "utility" => L.T(other, "实用工具", "實用工具"),
    "management" => L.T(other, "服务器管理", "伺服器管理"),
    "game-mechanics" => L.T(other, "游戏机制", "遊戲機制"),
    "adventure" => L.T(other, "内容与冒险", "內容與冒險"),
    "visual" => L.T(other, "视觉与 HUD", "視覺與 HUD"),
    "economy" => L.T(other, "经济", "經濟"),
    "teleport" => L.T(other, "传送", "傳送"),
    "anticheat" => L.T(other, "反作弊", "反作弊"),
    "optimization" => L.T(other, "优化", "最佳化"),
    "bugfix" => L.T(other, "错误修复", "錯誤修正"),
    "technology" => L.T(other, "技术与红石", "技術與紅石"),
    "protocol" => L.T(other, "协议与多版本", "通訊協定與多版本"),
    _ => other,
  };

  // What a mod offers besides its details.
  public static string Readme => L.T("README", "README", "README");
  public static string OpenInBrowser =>
      L.T("Open in browser", "在浏览器中打开", "在瀏覽器中開啟");

  /// Why the page of a mod cannot be opened.
  public static string NoPage =>
      L.T("This mod has no page.", "此模组没有页面。", "此模組沒有頁面。");
  public static string NoReadme => L.T("No README.", "没有 README。", "沒有 README。");
  public static string NoFile => L.T(
      "Cannot read the details of this mod.", "无法读取此模组的详情。", "無法讀取此模組的詳細資料。");
}
