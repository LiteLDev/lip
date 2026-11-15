namespace Lip.Cli.Tui.I18n;

/// The text that the shell, the widgets and several pages share: buttons, the hints of the
/// bottom bar, and common words. A page keeps its own text in its own XxxText class.
public static class CommonText {
  // Buttons.
  public static string Ok => L.T("OK", "确定", "確定");
  public static string Cancel => L.T("Cancel", "取消", "取消");
  public static string Close => L.T("Close", "关闭", "關閉");
  public static string Apply => L.T("Apply", "应用", "套用");
  public static string Yes => L.T("Yes", "是", "是");
  public static string No => L.T("No", "否", "否");
  public static string Save => L.T("Save", "保存", "儲存");
  public static string Discard => L.T("Discard", "放弃", "捨棄");
  public static string Quit => L.T("Quit", "退出", "結束");
  public static string Accept => L.T("Accept", "接受", "接受");
  public static string Decline => L.T("Decline", "拒绝", "拒絕");
  public static string Retry => L.T("Retry", "重试", "重試");
  public static string Later => L.T("Later", "以后再说", "稍後再說");

  // What several pages name: the same words for the same thing.
  public static string NewServer => L.T("New server…", "新建服务器…", "建立伺服器…");
  public static string GetMods => L.T("Get mods", "获取模组", "取得模組");
  public static string AutoRestart => L.T("Auto restart", "自动重启", "自動重新啟動");
  public static string ConnectDirectly => L.T("Connect directly", "直接连接", "直接連線");
  public static string NoMods => L.T("No mods yet.", "还没有模组。", "還沒有模組。");
  public static string Saved => L.T("Saved.", "已保存。", "已儲存。");

  /// Asked before a file is saved that changed on the disk since it was read, with Overwrite.
  public static string ChangedOnDisk =>
      L.T(
          "Changed on disk since it was read. Overwrite?",
          "读取之后，磁盘上的文件已被修改。要覆盖吗？",
          "讀取之後，磁碟上的檔案已被修改。要覆寫嗎？");
  public static string Overwrite => L.T("Overwrite", "覆盖", "覆寫");
  public static string DiscardTitle => L.T("Discard changes", "放弃更改", "捨棄變更");
  public static string DiscardQuestion =>
      L.T("Discard the changes?", "要放弃更改吗？", "要捨棄變更嗎？");

  // The hints of the bottom bar: a key, and one or two words.
  // A hint names two keys, as in "Enter/Space", where both do the same.
  public static Hint HintMove => new("↑↓", L.T("Move", "移动", "移動"));
  public static Hint HintMoveFields => new("↑↓/Tab", HintMove.Text);
  public static Hint HintScroll => new("↑↓", L.T("Scroll", "滚动", "捲動"));
  public static Hint HintPage => new("PgUp/PgDn", L.T("Page", "翻页", "翻頁"));
  public static Hint HintEnds => new("Home/End", L.T("Ends", "首尾", "首尾"));
  public static Hint HintSwitch => new("←→", L.T("Switch", "切换", "切換"));
  public static Hint HintSwitchButtons => new("←→/Tab", HintSwitch.Text);
  public static Hint HintChange => new("←→", L.T("Change", "更改", "變更"));
  public static Hint HintNext => new("Tab", L.T("Next", "下一项", "下一項"));
  public static Hint HintOpen => new("Enter", L.T("Open", "打开", "開啟"));
  public static Hint HintSelect => new("Enter", L.T("Select", "选择", "選擇"));
  public static Hint HintConfirm => new("Enter", L.T("Confirm", "确认", "確認"));
  public static Hint HintEdit => new("Enter", L.T("Edit", "编辑", "編輯"));
  public static Hint HintActions => new("Enter", L.T("Actions", "操作", "操作"));
  public static Hint HintToggle => new("Space", L.T("Toggle", "切换", "切換"));
  public static Hint HintToggleEither => new("Enter/Space", HintToggle.Text);
  public static Hint HintCheck => new("Space", L.T("Check", "勾选", "勾選"));
  public static Hint HintBack => new("Esc", L.T("Back", "返回", "返回"));
  public static Hint HintCancel => new("Esc", L.T("Cancel", "取消", "取消"));
  public static Hint HintClose => new("Esc", L.T("Close", "关闭", "關閉"));
  public static Hint HintQuit => new("Esc", L.T("Quit", "退出", "結束"));
  public static Hint HintStop => new("Ctrl+C", L.T("Stop", "停止", "停止"));

  // The shell.
  public static string TooSmall =>
      L.T(
          "Enlarge the terminal to 60×16. Ctrl+C quits.",
          "请将终端放大到 60×16。按 Ctrl+C 退出。",
          "請將終端機放大到 60×16。按 Ctrl+C 結束。");
  public static string ServerRunning => L.T("Running", "运行中", "執行中");
  public static string ServerStarting => L.T("Starting", "正在启动", "正在啟動");
  public static string ServerRestarting => L.T("Restarting", "正在重启", "正在重新啟動");
  public static string ServerStopping => L.T("Stopping", "正在停止", "正在停止");
  public static string ServerStopped => L.T("Stopped", "未运行", "未執行");

  /// Why something cannot be done while the server of the workspace runs: the message of
  /// AppContext.RefuseWhileServerRuns, and the reason of a menu item or a row that is disabled
  /// for it.
  public static string StopServerFirst =>
      L.T("Stop the server first.", "请先停止服务器。", "請先停止伺服器。");
  public static string QuitTitle => L.T("Quit lip", "退出 lip", "結束 lip");
  public static string QuitQuestion => L.T("Quit lip?", "要退出 lip 吗？", "要結束 lip 嗎？");
  public static string QuitStopsServers =>
      L.T("Running servers stop first.", "运行中的服务器会先停止。", "執行中的伺服器會先停止。");
  public static string QuitServersKeepRunning =>
      L.T("Running servers keep running.", "运行中的服务器会继续运行。", "執行中的伺服器會繼續執行。");
  public static string StoppingServers =>
      L.T("Stopping servers…", "正在停止服务器…", "正在停止伺服器…");
  public static string UnsavedTitle => L.T("Unsaved changes", "未保存的更改", "未儲存的變更");
  public static string UnsavedQuestion =>
      L.T("Save the changes?", "要保存更改吗？", "要儲存變更嗎？");
  public static string WorkspaceSelected => L.T("Workspace", "工作区", "工作區");
  public static string WorkspaceListUnwritable =>
      L.T("Cannot write the workspace list", "无法写入工作区列表", "無法寫入工作區清單");
  public static string NotADirectory => L.T("Not a folder", "不是文件夹", "不是資料夾");

  // Operations.
  public static string Working => L.T("Working…", "处理中…", "處理中…");
  public static string Cancelling => L.T("Cancelling…", "正在取消…", "正在取消…");
  public static string Done => L.T("Done.", "完成。", "完成。");
  public static string DoneWord => L.T("done", "完成", "完成");
  public static string Preview => L.T("Preview", "预览", "預覽");
  public static string NothingWouldChange =>
      L.T("Nothing would change.", "不会有任何更改。", "不會有任何變更。");
  public static string OnlyRecordChanges =>
      L.T(
          "Only lip's record of the packages would change.",
          "只会更新 lip 的安装记录。",
          "只會更新 lip 的安裝紀錄。");
  public static string Cancelled =>
      L.T("Cancelled. Changes were undone.", "已取消，更改已撤销。", "已取消，變更已復原。");
  public static string CancelledWord => L.T("Cancelled", "已取消", "已取消");
  public static string Failed => L.T("Failed", "失败", "失敗");
  public static string Error => L.T("error", "错误", "錯誤");
  public static string HintWord => L.T("hint", "提示", "提示");
  public static string FullReport => L.T("Full report", "完整报告", "完整報告");
  public static string NoReport => L.T("No failure report", "未能写入失败报告", "未能寫入失敗報告");
  public static string Question => L.T("lip asks", "lip 询问", "lip 詢問");
  public static string Loading => L.T("Loading…", "加载中…", "載入中…");
  public static string Empty => L.T("Nothing here.", "这里什么都没有。", "這裡什麼都沒有。");
  public static string Required => L.T("Required", "必填", "必填");

  // The license of the game (protocol/lip-setup.md, "The license").
  public static string EulaTitle => L.T("Minecraft EULA", "Minecraft EULA", "Minecraft EULA");
  public static string EulaIntro =>
      L.T(
          "The Minecraft server is under the Minecraft End User License Agreement and the"
              + " Microsoft Privacy Policy:",
          "Minecraft 服务器受《Minecraft 最终用户许可协议》和《Microsoft 隐私政策》约束：",
          "Minecraft 伺服器受《Minecraft 終端使用者授權合約》和《Microsoft 隱私權政策》約束：");
  public static string EulaQuestion =>
      L.T("Accept them to install the server.", "接受后才能安装服务器。", "接受後才能安裝伺服器。");

  /// "1-10 of 42": the rows shown of a text that scrolls.
  public static string Range(int first, int last, int total) =>
      L.T($"{first}-{last} of {total}", $"{first}-{last} / {total}", $"{first}-{last} / {total}");
}
