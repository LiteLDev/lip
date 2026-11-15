using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.GameConfig;

/// The text of the Game config page.
public static class GameConfigText {
  public static string Title => L.T("Game config", "游戏配置", "遊戲設定");

  // The sections.
  public static string Properties => L.T("Properties", "属性", "屬性");
  public static string Allowlist => L.T("Allowlist", "白名单", "白名單");
  public static string Permissions => L.T("Permissions", "权限", "權限");

  // A file that cannot be edited.
  public static string Missing(string file) =>
      L.T(
          $"{file} does not exist yet. Start the server once to create it.",
          $"{file} 尚未生成。启动一次服务器即可创建。",
          $"{file} 尚未產生。啟動一次伺服器即可建立。");
  public static string Unreadable(string file) =>
      L.T(
          $"lip cannot read {file}. It is shown as is and is not saved.",
          $"lip 无法读取 {file}。这里只显示原文，不会保存。",
          $"lip 無法讀取 {file}。這裡只顯示原文，不會儲存。");
  public static string NoProperties => L.T("No properties.", "没有属性。", "沒有屬性。");

  // The bottom of the page.
  public static string AppliesOnStart =>
      L.T("Applies on next server start.", "下次启动服务器时生效。", "下次啟動伺服器時生效。");
  public static string SaveTitle => L.T("Save game config", "保存游戏配置", "儲存遊戲設定");
  public static string ChangedOnDisk =>
      L.T(
          "Changed on disk since it was read. Overwrite?",
          "读取之后，磁盘上的文件已被修改。要覆盖吗？",
          "讀取之後，磁碟上的檔案已被修改。要覆寫嗎？");
  public static string Overwrite => L.T("Overwrite", "覆盖", "覆寫");

  // A property.
  public static string Edit => L.T("Edit…", "编辑…", "編輯…");
  public static string Toggle => L.T("Toggle", "切换", "切換");
  public static string Choose => L.T("Choose…", "选择…", "選擇…");
  public static string Reset => L.T("Reset value", "恢复原值", "還原為原值");
  public static string Original(string value) =>
      L.T($"Original: {value}", $"原值：{value}", $"原值：{value}");
  public static string EmptyValue => L.T("(empty)", "（空）", "（空）");
  public static string Value => L.T("Value", "值", "值");
  public static string ValueBetween(long min, long max) =>
      L.T($"Value ({min}–{max})", $"值（{min}–{max}）", $"值（{min}–{max}）");
  public static string NotAnInteger => L.T("Enter an integer.", "请输入整数。", "請輸入整數。");
  public static string NotBetween(long min, long max) =>
      L.T(
          $"Enter an integer from {min} to {max}.",
          $"请输入 {min} 到 {max} 之间的整数。",
          $"請輸入 {min} 到 {max} 之間的整數。");
  public static string NotAtLeast(long min) =>
      L.T(
          $"Enter an integer of at least {min}.",
          $"请输入不小于 {min} 的整数。",
          $"請輸入不小於 {min} 的整數。");
  public static string NotOneLine => L.T("Enter one line.", "只能输入一行。", "只能輸入一行。");

  // The players.
  public static string AddPlayer => L.T("Add player…", "添加玩家…", "新增玩家…");
  public static string AddPlayerTitle => L.T("Add player", "添加玩家", "新增玩家");
  public static string AllowlistDetail =>
      L.T(
          "Only these players can join while allow-list is on.",
          "开启 allow-list 后，只有这些玩家可以加入。",
          "開啟 allow-list 後，只有這些玩家可以加入。");
  public static string PermissionsDetail =>
      L.T(
          "These players get their own permission instead of the default one.",
          "这些玩家使用各自的权限，而不是默认权限。",
          "這些玩家會使用各自的權限，而非預設權限。");
  public static string PlayerName => L.T("Name", "玩家名称", "玩家名稱");
  public static string PlayerNameExample => "Steve";
  public static string Xuid => "XUID";
  public static string XuidOptional => L.T("XUID (optional)", "XUID（可选）", "XUID（選填）");
  public static string XuidExample => "2535416409681153";
  public static string NotAXuid =>
      L.T("An XUID is digits only.", "XUID 只能是数字。", "XUID 只能是數字。");
  public static string AlreadyListed => L.T("Already on the list", "已在列表中", "已在清單中");
  public static string IgnoresLimit =>
      L.T("ignores player limit", "无视人数上限", "無視人數上限");
  public static string IgnoreLimit =>
      L.T("Ignore player limit", "无视人数上限", "無視人數上限");
  public static string ApplyLimit => L.T("Apply player limit", "遵守人数上限", "遵守人數上限");
  public static string Remove => L.T("Remove", "移除", "移除");
  public static string ChangePermission => L.T("Change permission…", "更改权限…", "變更權限…");
  public static string Permission => L.T("Permission", "权限", "權限");
  public static string OperatorDetail =>
      L.T("Can use every command.", "可以使用所有命令。", "可以使用所有指令。");
  public static string MemberDetail =>
      L.T("Can play and build.", "可以正常游玩和建造。", "可以正常遊玩和建造。");
  public static string VisitorDetail =>
      L.T("Can only look around.", "只能观看，不能改动世界。", "只能觀看，不能更動世界。");
}
