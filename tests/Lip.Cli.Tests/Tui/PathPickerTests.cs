using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Moq;

namespace Lip.Cli.Tests.Tui;

/// The path picker of the interface, driven by the keys of a fake terminal over a file system
/// in memory.
public sealed class PathPickerTests {
  private const string LongName = "这是一个名字非常非常长的服务器文件夹用来测试界面在窄终端上的截断与对齐";

  private static readonly bool _windows = OperatingSystem.IsWindows();
  private static readonly char _sep = Path.DirectorySeparatorChar;

  /// The path of the file system of the tests that the names lead to, from its first root.
  private static string P(params string[] names) =>
      (_windows ? @"C:\" : "/") + string.Join(_sep, names);

  /// A workspace with folders, packages, a hidden folder and long Chinese names; on Windows, a
  /// second drive.
  private static MockFileSystem Tree() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddDirectory(P("ws", "servers", "survival"));
    fs.AddDirectory(P("ws", "servers", "creative"));
    fs.AddFile(P("ws", "packages", "tool.zip"), new MockFileData("zip"));
    fs.AddFile(P("ws", "packages", "Other.ZIP"), new MockFileData("zip"));
    fs.AddFile(P("ws", "packages", "notes.txt"), new MockFileData("text"));
    fs.AddDirectory(P("ws", "packages", "unpacked"));
    fs.AddFile(P("ws", ".lip", "cache.txt"), new MockFileData("cache"));
    fs.AddDirectory(P("ws", LongName, "第二层的文件夹名字也很长很长很长", "生存服务器"));
    fs.AddFile(P("ws", LongName, "一个名字特别特别长的模组压缩包文件用来测试列表的截断.zip"),
        new MockFileData("zip"));
    if (_windows) {
      fs.AddDirectory(@"D:\games\bedrock");
    }

    return fs;
  }

  private static TuiHarness Start(
      Language language = Language.En,
      int width = 120,
      int height = 36,
      MockFileSystem? fs = null) =>
      TuiHarness.Start(new TuiHarnessOptions {
        Language = language,
        Width = width,
        Height = height,
        FileSystem = fs ?? Tree(),
      });

  /// Shows the picker and waits until it has read its first folder.
  private static async Task Show(TuiHarness tui, PathPicker picker) {
    await tui.Invoke(() => tui.Context.Show(picker));
    await tui.WaitFor($"[ {tui.Text(() => CommonText.Cancel)} ]");
    await tui.WaitUntilGone(tui.Text(() => PathPickerText.Reading));
  }

  /// What the path bar shows for a folder.
  private static string Bar(TuiHarness tui, string path) =>
      $"{tui.Text(() => PathPickerText.Folder)}  {path}";

  [Fact]
  public async Task Folder_IsBrowsedWithTheArrowKeys_AndTheFolderShownIsSelected() {
    using TuiHarness tui = Start();
    string? chosen = null;

    await Show(tui, PathPicker.Folder("Add folder", tui.Root, path => chosen = path));

    // "..", then the folders, each with a separator; no file, and nothing hidden.
    await tui.WaitFor(Bar(tui, tui.Root));
    Assert.Contains("─ Add folder ─", tui.Screen);
    Assert.Contains($"packages{_sep}", tui.Screen);
    Assert.Contains($"servers{_sep}", tui.Screen);
    Assert.DoesNotContain($".lip{_sep}", tui.Screen);
    Assert.Contains(
        $"[ {PathPickerText.SelectFolder} ] [ {PathPickerText.NewFolder} ] [ {CommonText.Cancel} ]",
        tui.Screen);
    Assert.Contains(" ↑↓ Move  Enter/← Up  Tab Next  Esc Cancel", tui.Screen);

    // Enter opens a folder, and so does →.
    tui.Press(ConsoleKey.DownArrow, 2);
    await tui.WaitFor(" ↑↓ Move  Enter/→ Open  ← Up  Tab Next  Esc Cancel");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "servers")));
    await tui.WaitFor($"survival{_sep}");
    Assert.DoesNotContain($"packages{_sep}", tui.Screen);
    tui.Press(ConsoleKey.DownArrow, 2);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "survival")));

    // ← goes up, and the folder just left is the one selected: → opens it again.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitUntilGone(Bar(tui, P("ws", "servers", "survival")));
    await tui.WaitFor(" ↑↓ Move  Enter/→ Open  ← Up  Tab Next  Esc Cancel");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "survival")));

    // Enter on ".." goes up too.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(Bar(tui, P("ws", "servers", "survival")));
    await tui.WaitFor($"creative{_sep}");

    // The buttons: Tab from the list, then Enter on the first one.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" ←→ Switch  Enter Select  ↑/Tab Next  Esc Cancel");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Add folder ─");
    Assert.Equal(P("ws", "servers"), chosen);
  }

  [Fact]
  public async Task Folder_GoesUpToTheRoots_AndIntoAnotherDrive() {
    using TuiHarness tui = Start();
    string? chosen = null;

    await Show(tui, PathPicker.Folder("Add folder", tui.Root, path => chosen = path));
    await tui.WaitFor(Bar(tui, tui.Root));

    // Up from the workspace to its root, and from there to the roots.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor($"ws{_sep}");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor(Bar(tui, PathPickerText.Computer));
    await tui.WaitUntilGone($"ws{_sep}");

    // Neither a folder to select nor one to go up from: the buttons for them are skipped.
    Assert.Contains(" ↑↓ Move  Enter/→ Open  Tab Next  Esc Cancel", tui.Screen);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" Enter Select  ↑/Tab Next  Esc Cancel");
    Assert.DoesNotContain("Switch", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Add folder ─");
    Assert.Null(chosen);

    if (!_windows) {
      return;
    }

    // The drives, the one just left selected; ↓ and Enter go into the other one.
    await Show(tui, PathPicker.Folder("Add folder", @"C:\", path => chosen = path));
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor(@"D:\");
    Assert.Contains(@"C:\", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, @"D:\"));
    await tui.WaitFor(@"games\");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, @"D:\games"));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Add folder ─");
    Assert.Equal(@"D:\games", chosen);
  }

  [Fact]
  public async Task PathBar_GoesToATypedPath_AndEscGoesBackOneLevel() {
    using TuiHarness tui = Start();
    bool cancelled = false;

    await Show(tui, PathPicker.Folder("Add folder", tui.Root, _ => { }, () => cancelled = true));
    await tui.WaitFor(Bar(tui, tui.Root));

    // ↑ on the first row is the path bar, which starts with the folder shown.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor(" Enter Go  ↓/Tab Next  Esc Back");

    // A path relative to the folder shown.
    tui.Press(ConsoleKey.Backspace, tui.Root.Length);
    tui.Type($"servers{_sep}creative");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "creative")));
    await tui.WaitFor(" ↑↓ Move  Enter/← Up  Tab Next  Esc Cancel");

    // An absolute one, in quotes as when it is pasted, with ".." in it.
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor(" Enter Go  ↓/Tab Next  Esc Back");
    tui.Press(ConsoleKey.Backspace, P("ws", "servers", "creative").Length);
    tui.Type($"\"{P("ws", "servers", "..", "packages")}{_sep}\"");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "packages")));
    await tui.WaitFor($"unpacked{_sep}");

    // One that does not exist, and a file: the bar keeps the focus and says why.
    tui.Press(ConsoleKey.UpArrow);
    tui.Type($"{_sep}nowhere");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(PathPickerText.NotFound);
    Assert.Contains(" Enter Go  ↓/Tab Next  Esc Back", tui.Screen);
    tui.Press(ConsoleKey.Backspace, "nowhere".Length);
    await tui.WaitUntilGone(PathPickerText.NotFound);
    tui.Type("tool.zip");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.NotADirectory);

    // Esc goes back to the list first, and cancels from there.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(Bar(tui, P("ws", "packages")));
    await tui.WaitFor(" ↑↓ Move  Enter/← Up  Tab Next  Esc Cancel");
    Assert.False(cancelled);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Add folder ─");
    Assert.True(cancelled);
  }

  [Fact]
  public async Task NewFolder_IsCreatedAndShown_AndEscInItsPromptGoesBackToThePicker() {
    using TuiHarness tui = Start();
    string? chosen = null;

    await Show(tui, PathPicker.Folder("Add folder", P("ws", "servers"), path => chosen = path));
    await tui.WaitFor(Bar(tui, P("ws", "servers")));

    // ↓ past the last row is the buttons; → is the second one.
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {PathPickerText.NewFolderTitle} ─");

    // Esc closes the prompt only.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {PathPickerText.NewFolderTitle} ─");
    Assert.Contains("─ Add folder ─", tui.Screen);

    // A name is required, and must be one.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {PathPickerText.NewFolderTitle} ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Type("..");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(PathPickerText.InvalidName);
    tui.Press(ConsoleKey.Backspace, 2);

    tui.Type("skyblock");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "skyblock")));
    Assert.True(tui.Fs.Directory.Exists(P("ws", "servers", "skyblock")));

    // The buttons still have the focus: ← is the first one.
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Add folder ─");
    Assert.Equal(P("ws", "servers", "skyblock"), chosen);
  }

  [Fact]
  public async Task Folder_ThatMayNotExist_IsChosenWithoutBeingCreated_AndValidateRefuses() {
    using TuiHarness tui = Start(Language.ZhHans);
    List<string> chosen = [];
    PathPicker Picker() =>
        PathPicker.Folder(
            "新建服务器", tui.Root, chosen.Add,
            validate: path =>
                path.EndsWith("survival", StringComparison.Ordinal) ? "已经有服务器了" : null,
            mayNotExist: true);

    // A new name: the folder of that name in the one shown, which is not created.
    await Show(tui, Picker());
    await tui.WaitFor(Bar(tui, tui.Root));
    Assert.Contains("[ 选择此文件夹 ] [ 新建文件夹… ] [ 取消 ]", tui.Screen);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 新建文件夹 ─");
    tui.Type("生存服");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ 新建服务器 ─");
    Assert.Equal([P("ws", "生存服")], chosen);
    Assert.False(tui.Fs.Directory.Exists(P("ws", "生存服")));

    // A path that is typed and does not exist is chosen as it is.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.UpArrow);
    tui.Type($"{_sep}new{_sep}deeper{_sep}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ 新建服务器 ─");
    Assert.Equal(P("ws", "new", "deeper"), chosen[^1]);
    Assert.False(tui.Fs.Directory.Exists(P("ws", "new")));

    // What the caller refuses is said in the dialog, which stays open until another is chosen.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.UpArrow);
    tui.Type($"{_sep}servers{_sep}survival");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "survival")));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("已经有服务器了");
    Assert.Equal(2, chosen.Count);
    tui.Press(ConsoleKey.Tab, 2);
    await tui.WaitUntilGone("已经有服务器了");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"survival{_sep}");
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ 新建服务器 ─");
    Assert.Equal(P("ws", "servers"), chosen[^1]);
  }

  [Fact]
  public async Task TypedPath_IsResolvedAsThePlatformDoes_AndANewFolderMustBeOneToCreate() {
    using TuiHarness tui = Start();
    string? chosen = null;

    await Show(tui, PathPicker.Folder(
        "New server", P("ws", "servers"), path => chosen = path, mayNotExist: true));
    await tui.WaitFor($"survival{_sep}");

    // A path that starts with a separator is not below the folder shown.
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.Backspace, P("ws", "servers").Length);
    tui.Type($"{_sep}ws{_sep}packages");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"unpacked{_sep}");
    Assert.Contains(Bar(tui, P("ws", "packages")), tui.Screen);

    // A new folder below a file cannot be created.
    tui.Press(ConsoleKey.UpArrow);
    tui.Type($"{_sep}tool.zip{_sep}world");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.NotADirectory);
    Assert.Null(chosen);
    if (!_windows) {
      return;
    }

    // Neither can one whose name is none.
    tui.Press(ConsoleKey.Backspace, @"\tool.zip\world".Length);
    tui.Type(@"\a*b");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(PathPickerText.InvalidName);
    Assert.Null(chosen);

    // A drive alone is its root.
    tui.Press(ConsoleKey.Backspace, P("ws", "packages").Length + @"\a*b".Length);
    tui.Type("D:");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"games{_sep}");
    Assert.Contains(Bar(tui, @"D:\"), tui.Screen);
    Assert.Null(chosen);
  }

  [Fact]
  public async Task OpenFile_ListsTheFilesOfItsKind_AndEnterChoosesOne() {
    using TuiHarness tui = Start(Language.ZhHant);
    string? chosen = null;

    await Show(tui, PathPicker.OpenFile("安裝模組", tui.Root, [".zip"], path => chosen = path));
    await tui.WaitFor(Bar(tui, tui.Root));
    Assert.Contains("資料夾  ", tui.Screen);
    Assert.Contains("檔案：.zip", tui.Screen);

    // Only a way out: a file is chosen in the list.
    Assert.Contains("[ 取消 ]", tui.Screen);
    Assert.DoesNotContain(PathPickerText.SelectFolder, tui.Screen);
    Assert.DoesNotContain(PathPickerText.NewFolder, tui.Screen);

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "packages")));

    // The folders first, then the files that end with .zip, whatever the case.
    await tui.WaitFor("tool.zip");
    Assert.Contains("Other.ZIP", tui.Screen);
    Assert.DoesNotContain("notes.txt", tui.Screen);
    string screen = tui.Screen;
    Assert.True(
        screen.IndexOf($"unpacked{_sep}", StringComparison.Ordinal)
            < screen.IndexOf("Other.ZIP", StringComparison.Ordinal));
    Assert.True(
        screen.IndexOf("Other.ZIP", StringComparison.Ordinal)
            < screen.IndexOf("tool.zip", StringComparison.Ordinal));

    // A file of another kind that is typed is refused.
    tui.Press(ConsoleKey.UpArrow);
    tui.Type($"{_sep}notes.txt");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("不是 .zip 檔案");
    Assert.Null(chosen);
    tui.Press(ConsoleKey.Escape);

    tui.Press(ConsoleKey.End);
    await tui.WaitFor(" ↑↓ 移動  Enter 選擇  ← 上一層  Tab 下一項  Esc 取消");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ 安裝模組 ─");
    Assert.Equal(P("ws", "packages", "tool.zip"), chosen);
  }

  [Fact]
  public async Task OpenFile_OrFolder_TakesATypedFile_OrTheFolderShown() {
    using TuiHarness tui = Start();
    List<string> chosen = [];
    PathPicker Picker() =>
        PathPicker.OpenFile("Install", tui.Root, ["zip"], chosen.Add, orFolder: true);

    await Show(tui, Picker());
    await tui.WaitFor(Bar(tui, tui.Root));
    Assert.Contains($"[ {PathPickerText.SelectFolder} ] [ {CommonText.Cancel} ]", tui.Screen);
    Assert.DoesNotContain(PathPickerText.NewFolder, tui.Screen);

    // A file that is typed is chosen at once.
    tui.Press(ConsoleKey.UpArrow);
    tui.Type($"{_sep}packages{_sep}other.zip");
    tui.Press(ConsoleKey.Enter);
    if (_windows) {
      await tui.WaitUntilGone("─ Install ─");
      Assert.Equal([P("ws", "packages", "other.zip")], chosen);
    } else {
      // The names of this file system differ by case: nothing was found.
      await tui.WaitFor(PathPickerText.NotFound);
      tui.Press(ConsoleKey.Escape, 2);
      await tui.WaitUntilGone("─ Install ─");
      Assert.Empty(chosen);
    }

    // A folder.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "packages")));
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(Bar(tui, P("ws", "packages", "unpacked")));
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Install ─");
    Assert.Equal(P("ws", "packages", "unpacked"), chosen[^1]);
  }

  [Fact]
  public async Task SaveFile_TakesTheFolderShownAndATypedName_WithItsExtension() {
    using TuiHarness tui = Start();
    List<string> chosen = [];
    PathPicker Picker() =>
        PathPicker.SaveFile("Pack", tui.Root, "package.zip", ".zip", chosen.Add);

    await Show(tui, Picker());
    await tui.WaitFor(Bar(tui, tui.Root));
    Assert.Contains($"{PathPickerText.FileName}  package.zip", tui.Screen);
    Assert.Contains(
        $"[ {CommonText.Save} ] [ {PathPickerText.NewFolder} ] [ {CommonText.Cancel} ]", tui.Screen);

    // Into a folder, then to the name: the extension is added to a name without it.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "packages")));
    await tui.WaitFor("tool.zip");
    Assert.DoesNotContain("notes.txt", tui.Screen);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" Enter Save  ↑↓/Tab Next  Esc Cancel");
    tui.Press(ConsoleKey.Backspace, "package.zip".Length);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.Required);
    tui.Type("release");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Pack ─");
    Assert.Equal([P("ws", "packages", "release.zip")], chosen);

    // Enter on a file takes its name; [ Save ] chooses it.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("tool.zip");
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"{PathPickerText.FileName}  tool.zip");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(" ←→ Switch  Enter Select  ↑/Tab Next  Esc Cancel");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Pack ─");
    Assert.Equal(P("ws", "packages", "tool.zip"), chosen[^1]);

    // A name that is a path leads to its folder first.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Backspace, "package.zip".Length);
    tui.Type($"servers{_sep}creative{_sep}world");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "creative")));
    await tui.WaitFor($"{PathPickerText.FileName}  world");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Pack ─");
    Assert.Equal(P("ws", "servers", "creative", "world.zip"), chosen[^1]);

    // A name that is a folder shows it, and the name is asked for again.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Backspace, "package.zip".Length);
    tui.Type($"servers{_sep}survival");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(Bar(tui, P("ws", "servers", "survival")));
    await tui.WaitFor($"{PathPickerText.FileName}  package.zip");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ Pack ─");
    Assert.Equal(P("ws", "servers", "survival", "package.zip"), chosen[^1]);

    // Esc in the name cancels.
    await Show(tui, Picker());
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Pack ─");
    Assert.Equal(4, chosen.Count);
  }

  [Fact]
  public async Task UnreadableFolder_SaysSo_AndIsLeftWithTheArrowKeys() {
    DenyingFileSystem fs = new(P("ws", "servers", "creative"));
    fs.AddDirectory(P("ws", "servers", "survival"));
    fs.AddDirectory(P("ws", "servers", "creative", "inside"));
    using TuiHarness tui = Start(fs: fs);

    await Show(tui, PathPicker.Folder("Add folder", P("ws", "servers"), _ => { }));
    await tui.WaitFor($"survival{_sep}");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(PathPickerText.Unreadable);
    Assert.Contains(Bar(tui, P("ws", "servers", "creative")), tui.Screen);
    Assert.DoesNotContain("inside", tui.Screen);
    Assert.Contains(" ↑↓ Move  Enter/← Up  Tab Next  Esc Cancel", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"survival{_sep}");
    Assert.DoesNotContain(PathPickerText.Unreadable, tui.Screen);
    Assert.Contains(Bar(tui, P("ws", "servers")), tui.Screen);
  }

  [Fact]
  public async Task MissingStart_ShowsTheNearestFolderAbove_OrTheRoots() {
    using TuiHarness tui = Start();

    await Show(tui, PathPicker.Folder("Add folder", P("ws", "servers", "gone", "deeper"), _ => { }));
    await tui.WaitFor(Bar(tui, P("ws", "servers")));
    Assert.Contains($"survival{_sep}", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Add folder ─");

    // Nothing above it exists, or it is no path at all.
    await Show(tui, PathPicker.Folder("Add folder", _windows ? @"Z:\gone\deeper" : "", _ => { }));
    await tui.WaitFor(Bar(tui, PathPickerText.Computer));
    Assert.Contains(_windows ? @"C:\" : "/", tui.Screen);
    Assert.DoesNotContain("..", tui.Screen);
  }

  [Fact]
  public async Task LargeFolder_IsCapped_AndPagedThrough() {
    MockFileSystem fs = Tree();
    for (int i = 0; i < PathPicker.MaxEntries + 500; i++) {
      fs.AddDirectory(P("ws", "many", $"world-{i:D5}"));
    }

    using TuiHarness tui = Start(fs: fs);

    await Show(tui, PathPicker.Folder("Add folder", P("ws", "many"), _ => { }));
    await tui.WaitFor("world-");
    Assert.Contains(PathPickerText.Capped(PathPicker.MaxEntries), tui.Screen);
    Assert.Contains(
        " ↑↓ Move  Enter/← Up  Tab Next  PgUp/PgDn Page  Home/End Ends  Esc Cancel", tui.Screen);

    // Sorted once, so the rows shown are in order, and End is the last row of the list.
    string[] shown = [.. Names(tui.Screen)];
    Assert.Equal(shown.Order(StringComparer.Ordinal), shown);
    tui.Press(ConsoleKey.PageDown);
    await tui.WaitUntilGone("..");
    tui.Press(ConsoleKey.End);
    await tui.WaitFor(screen => Names(screen).Count() > 1 && !screen.Contains('▼'));
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor("..");

    static IEnumerable<string> Names(string screen) =>
        screen.Split('\n')
            .Where(row => row.Contains("world-", StringComparison.Ordinal))
            .Select(row => row[row.IndexOf("world-", StringComparison.Ordinal)..][..11]);
  }

  [Theory]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task LongChineseNames_AreCutToTheirRoom_AtTheSmallestSize(Language language) {
    using TuiHarness tui = Start(language, 60, 16);

    await Show(tui, PathPicker.OpenFile(
        tui.Text(() => PathPickerText.SelectFolder),
        P("ws", LongName, "第二层的文件夹名字也很长很长很长", "生存服务器"), [".zip"], _ => { },
        orFolder: true));

    // The path bar keeps the start and the end of the path.
    string folder = tui.Text(() => PathPickerText.Folder);
    await tui.WaitFor(screen => screen.Split('\n').Any(row =>
        row.Contains($"{folder}  ", StringComparison.Ordinal)
            && row.Contains('…') && row.Contains("生存服务器 ", StringComparison.Ordinal)));
    AssertFits(tui.Screen, 60);

    // The names of the list end in an ellipsis.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor($"生存服务器{_sep}");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("第二层的文件夹");
    await tui.WaitFor(screen => screen.Split('\n').Any(row =>
        row.Contains("一个名字特别特别长", StringComparison.Ordinal) && row.Contains('…')));
    AssertFits(tui.Screen, 60);
    Assert.Contains(language == Language.ZhHans ? "文件：.zip" : "檔案：.zip", tui.Screen);
    Assert.Contains(
        language == Language.ZhHans ? "[ 选择此文件夹 ] [ 取消 ]" : "[ 選擇此資料夾 ] [ 取消 ]",
        tui.Screen);

    // The dialog is 56 columns wide from column 2: every row inside it has the borders at
    // the same columns, however wide its characters are.
    static void AssertFits(string screen, int width) {
      string[] rows = screen.Split('\n');
      Assert.All(rows, row => Assert.True(TextWidth.Of(row) <= width));
      foreach (string row in rows.Skip(2).Take(12)) {
        List<int> borders = [];
        int column = 0;
        foreach (System.Text.Rune rune in row.EnumerateRunes()) {
          if (rune.Value == '│') {
            borders.Add(column);
          }

          column += TextWidth.Of(rune);
        }

        Assert.Contains(2, borders);
        Assert.Contains(57, borders);
      }
    }
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task Frames_OfEveryMode_AtBothSizes(Language language) {
    foreach ((int width, int height) in new[] { (120, 36), (60, 16) }) {
      using TuiHarness tui = Start(language, width, height);
      string deep = P("ws", LongName, "第二层的文件夹名字也很长很长很长");
      (string Name, Func<PathPicker> Picker)[] modes = [
        ("folder", () => PathPicker.Folder(PathPickerText.SelectFolder, tui.Root, _ => { })),
        ("new-folder", () => PathPicker.Folder(
            CommonText.NewServer, deep, _ => { }, mayNotExist: true)),
        ("open-file", () => PathPicker.OpenFile(
            PathPickerText.FileName, P("ws", "packages"), [".zip"], _ => { })),
        ("file-or-folder", () => PathPicker.OpenFile(
            PathPickerText.FileName, P("ws", LongName), [".zip"], _ => { }, orFolder: true)),
        ("save-file", () => PathPicker.SaveFile(
            CommonText.Save, P("ws", "packages"), "package.zip", ".zip", _ => { })),
      ];
      foreach ((string name, Func<PathPicker> picker) in modes) {
        await Show(tui, picker());
        await tui.Settle();
        tui.Snapshot($"picker-{name}-list");
        for (int tab = 1; tab <= 3; tab++) {
          tui.Press(ConsoleKey.Tab);
          await tui.Settle();
          tui.Snapshot($"picker-{name}-tab{tab}");
          foreach (string row in tui.Screen.Split('\n')) {
            Assert.True(TextWidth.Of(row) <= width);
          }
        }

        // Round to the path bar, and out: Esc there goes back to the list first.
        string go = tui.Text(() => $"Enter {PathPickerText.HintGo.Text}  ");
        while (!tui.Screen.Contains(go, StringComparison.Ordinal)) {
          tui.Press(ConsoleKey.Tab);
          await tui.Settle();
        }

        tui.Press(ConsoleKey.Escape, 2);
        await tui.WaitUntilGone($"[ {tui.Text(() => CommonText.Cancel)} ]");
      }

      // The roots, and a folder that asks for a name.
      await Show(tui, PathPicker.Folder(PathPickerText.SelectFolder, "", _ => { }));
      await tui.Settle();
      tui.Snapshot("picker-roots");
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone($"[ {tui.Text(() => CommonText.Cancel)} ]");
      await Show(tui, PathPicker.Folder(PathPickerText.SelectFolder, tui.Root, _ => { }));
      tui.Press(ConsoleKey.Tab);
      tui.Press(ConsoleKey.RightArrow);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"─ {tui.Text(() => PathPickerText.NewFolderTitle)} ─");
      tui.Snapshot("picker-new-folder-prompt");
    }
  }

  /// A file system in memory one of whose folders cannot be read.
  private sealed class DenyingFileSystem(string denied)
      : MockFileSystem(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot) {
    public override IDirectoryInfoFactory DirectoryInfo =>
        new Factory(this, base.DirectoryInfo, denied);

    private sealed class Factory(IFileSystem fileSystem, IDirectoryInfoFactory inner, string denied)
        : IDirectoryInfoFactory {
      public IFileSystem FileSystem => fileSystem;

      public IDirectoryInfo New(string path) {
        if (!string.Equals(path, denied, StringComparison.OrdinalIgnoreCase)) {
          return inner.New(path);
        }

        Mock<IDirectoryInfo> info = new();
        info.Setup(i => i.EnumerateFileSystemInfos())
            .Throws(new UnauthorizedAccessException("Access to the path is denied."));
        return info.Object;
      }

      public IDirectoryInfo? Wrap(DirectoryInfo? directoryInfo) => inner.Wrap(directoryInfo);
    }
  }
}
