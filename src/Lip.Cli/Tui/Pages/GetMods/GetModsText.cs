using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;

namespace Lip.Cli.Tui.Pages.GetMods;

/// The text of the Get mods page.
public static class GetModsText {
  public static string Title => L.T("Get mods", "获取模组", "取得模組");

  // The controls above the mods.
  public static string Search => L.T("Search", "搜索", "搜尋");
  public static string SearchExample => L.T("economy", "经济", "經濟");
  public static string Sort => L.T("Sort", "排序", "排序");
  public static string Filters => L.T("Filters", "筛选", "篩選");

  public static string FiltersActive(int count) =>
      L.T($"{count} active", $"已选 {count} 项", $"已選 {count} 項");

  /// The names of the orders, as levimc.org/mods names them.
  public static string SortName(CatalogSort sort) => sort switch {
    CatalogSort.Popularity => L.T("Popularity", "热度", "熱度"),
    CatalogSort.Updated => L.T("Updated", "最近更新", "最近更新"),
    CatalogSort.Newest => L.T("Newest", "最新发布", "最新發佈"),
    _ => L.T("Relevance", "相关性", "相關性"),
  };

  // The mods.
  public static string Count(int count) =>
      L.T(count == 1 ? "1 mod" : $"{count} mods", $"{count} 个模组", $"{count} 個模組");

  public static string Installed => L.T("✓ Installed", "✓ 已安装", "✓ 已安裝");
  public static string NoMatch => L.T("No mods match.", "没有匹配的模组。", "沒有符合的模組。");
  public static string Unavailable =>
      L.T("Cannot read the catalogue.", "无法读取模组目录。", "無法讀取模組目錄。");
  public static Hint HintRetry => new("Enter", CommonText.Retry);
  public static Hint HintResults => new("Enter", L.T("Results", "结果", "結果"));

  // The filters.
  public static string FiltersTitle(int count) =>
      count == 0 ? Filters : $"{Filters} ({count})";

  public static string ClearFilters => L.T("Clear filters", "清除筛选", "清除篩選");

  public static string Section(FilterSection section) => section switch {
    FilterSection.GameVersion => L.T("Game version", "游戏版本", "遊戲版本"),
    FilterSection.Tags => ModDetailsText.Tags,
    FilterSection.License => ModDetailsText.License,
    _ => L.T("Depends on", "依赖于", "相依於"),
  };

  public static string Selected(int count) =>
      L.T($"{count} selected", $"已选 {count}", $"已選 {count}");

  public static string State(FilterState state) => state switch {
    FilterState.Included => L.T("Included", "包含", "包含"),
    FilterState.Excluded => L.T("Excluded", "排除", "排除"),
    _ => L.T("Any", "不限", "不限"),
  };

  /// The name of a license family of the catalogue, by its id; other when lip knows no such
  /// family.
  public static string LicenseFamily(string id, string other) => id switch {
    "permissive" => L.T("Permissive", "宽松许可", "寬鬆授權"),
    "weak-copyleft" => L.T("Weak copyleft", "弱 Copyleft", "弱 Copyleft"),
    "copyleft" => L.T("Copyleft", "Copyleft", "Copyleft"),
    "other" => L.T("Other", "其他", "其他"),
    "none" => L.T("No license", "无许可证", "無授權條款"),
    _ => other,
  };

  // The actions of a mod.
  public static string Install => L.T("Install…", "安装…", "安裝…");

  public static string NoServer => L.T(
      "This folder has no server yet. Set one up first.",
      "此文件夹还没有服务器，请先安装。",
      "此資料夾還沒有伺服器，請先安裝。");

  public static string InstallTitle(string package) =>
      L.T($"Install {package}", $"安装 {package}", $"安裝 {package}");

  // The versions of a mod.
  public static string Variant => L.T("Variant", "变体", "變體");
  public static string DefaultVariant => L.T("default", "默认", "預設");
  public static string Prerelease => L.T("prerelease", "预发布", "預發行");
  public static string Custom => L.T("Custom…", "自定义…", "自訂…");
  public static string CustomTitle => L.T("Custom version", "自定义版本", "自訂版本");
  public static Hint HintVariant => new("←→", Variant);

  public static string NoVersionForLoader(string loader) => L.T(
      $"No version fits LeviLamina {loader}.",
      $"没有适用于 LeviLamina {loader} 的版本。",
      $"沒有適用於 LeviLamina {loader} 的版本。");

  public static string NoVersionForGame(string game) => L.T(
      $"No version fits Minecraft {game}.",
      $"没有适用于 Minecraft {game} 的版本。",
      $"沒有適用於 Minecraft {game} 的版本。");

  public static string NoVersionWithoutLoader(string game) => L.T(
      $"No version fits Minecraft {game} without LeviLamina.",
      $"没有适用于 Minecraft {game} 且无需 LeviLamina 的版本。",
      $"沒有適用於 Minecraft {game} 且不需 LeviLamina 的版本。");

  public static string CustomExample => L.T("branch:main", "branch:main", "branch:main");
}
