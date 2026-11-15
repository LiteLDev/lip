using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// The text of the PathPicker.
public static class PathPickerText {
  // Buttons.
  public static string SelectFolder =>
      L.T("Select this folder", "选择此文件夹", "選擇此資料夾");
  public static string NewFolder => L.T("New folder…", "新建文件夹…", "新增資料夾…");

  // Labels.
  public static string NewFolderTitle => L.T("New folder", "新建文件夹", "新增資料夾");
  public static string Name => L.T("Name", "名称", "名稱");
  public static string Folder => L.T("Folder", "文件夹", "資料夾");
  public static string FileName => L.T("File name", "文件名", "檔案名稱");
  public static string Computer => L.T("Computer", "此电脑", "本機");

  // The line above the buttons.

  /// At the roots, where no folder is shown to select or to make a folder in.
  public static string AtRoots =>
      L.T("Open one of these first.", "请先打开其中一项。", "請先開啟其中一項。");
  public static string Reading => L.T("Reading…", "读取中…", "讀取中…");
  public static string Unreadable =>
      L.T("Cannot read this folder.", "无法读取此文件夹。", "無法讀取此資料夾。");
  public static string NotFound => L.T("Not found", "找不到", "找不到");
  public static string InvalidName => L.T("Invalid name", "名称无效", "名稱無效");
  public static string CannotCreate =>
      L.T("Cannot create the folder", "无法创建文件夹", "無法建立資料夾");

  /// "Only the first 5000 are listed."
  public static string Capped(int count) =>
      L.T(
          $"Only the first {count} are listed.", $"只列出前 {count} 项。", $"只列出前 {count} 項。");

  /// "Files: .zip": the files that the list shows.
  public static string Files(string extensions) =>
      L.T($"Files: {extensions}", $"文件：{extensions}", $"檔案：{extensions}");

  /// "Not a .zip file": a file that was typed is of another kind than the list shows.
  public static string WrongType(string extensions) =>
      L.T($"Not a {extensions} file", $"不是 {extensions} 文件", $"不是 {extensions} 檔案");

  // Hints.
  private static string Up => L.T("Up", "上一级", "上一層");
  public static Hint HintGo => new("Enter", L.T("Go", "前往", "前往"));
  public static Hint HintUp => new("←", Up);
  public static Hint HintUpEither => new("Enter/←", Up);
  public static Hint HintOpenEither => new("Enter/→", CommonText.HintOpen.Text);
  public static Hint HintSave => new("Enter", CommonText.Save);

  /// The keys that go to another part of the dialog, such as "↓/Tab".
  public static Hint HintFocus(string keys) => new(keys, CommonText.HintNext.Text);
}
