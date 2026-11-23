using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Pages.Workspaces;

/// The text of the workspace picker.
public static class WorkspacesText {
  public static string Title => L.T("Workspaces", "工作区", "工作區");

  // The action row above the list.
  public static string AddFolder => L.T("Add folder…", "添加文件夹…", "加入資料夾…");
  public static string AddFolderDetail =>
      L.T(
          "Adds a folder to the list, or a new one that it creates. The Server page sets it up.",
          "把文件夹加入列表，也可以新建一个。服务器页会为它安装服务器。",
          "將資料夾加入清單，也可以新建一個。伺服器頁會為它安裝伺服器。");

  // The list.
  public static string Empty => L.T("No workspaces.", "没有工作区。", "沒有工作區。");
  public static string Missing => L.T("missing", "已丢失", "已遺失");
  public static string ServerRunning => L.T("server running", "服务器运行中", "伺服器執行中");
  public static string CurrentDetail => L.T("The selected workspace.", "当前工作区。", "目前的工作區。");
  public static string MissingDetail =>
      L.T("This folder no longer exists.", "此文件夹已不存在。", "此資料夾已不存在。");
  public static string RunningDetail =>
      L.T("Its server is running.", "它的服务器正在运行。", "它的伺服器正在執行。");
  public static string CurrentRunningDetail =>
      L.T(
          "The selected workspace, with its server running.",
          "当前工作区，服务器正在运行。",
          "目前的工作區，伺服器正在執行。");
  public static string ListUnreadable =>
      L.T("Cannot read the workspace list", "无法读取工作区列表", "無法讀取工作區清單");

  // The action menu of a workspace.
  public static string Switch => L.T("Switch", "切换", "切換");
  public static string Remove => L.T("Remove from list", "从列表移除", "從清單移除");
  public static string RemoveQuestion =>
      L.T(
          "Remove it from the list? No file is deleted.",
          "要从列表中移除吗？不会删除任何文件。",
          "要從清單中移除嗎？不會刪除任何檔案。");
  public static string Removed => L.T("Removed from the list.", "已从列表移除。", "已從清單移除。");

  // Adding a folder.
  public static string AddTitle => L.T("Add folder", "添加文件夹", "加入資料夾");
  public static string InvalidPath => L.T("Not a valid path", "路径无效", "路徑無效");
}
