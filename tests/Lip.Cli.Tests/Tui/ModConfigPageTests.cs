using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.ModConfig;

namespace Lip.Cli.Tests.Tui;

/// The Mod config page of the interface, driven by the keys of a fake terminal.
public sealed class ModConfigPageTests {
  private const string AlphaConfig = """
      {
          "version": 2, // kept by the mod
          "enabled": false,
          "limits": {"max": 5, "name": "x"},
          "tags": ["a", "b"],
      }
      """;

  private static readonly string Root = TuiHarness.DefaultRoot;

  private static string Package(string name) => $$"""
      { "manifest": { "format": 4, "id": "github.com/acme/{{name}}", "version": "1.0.0",
          "variants": { "": [ { "side": "server", "platform": "win-x64",
            "assets": [ { "type": "mcmod", "urls": ["https://example.com/{{name}}.zip"] } ] } ] } },
        "variant": "", "explicit": true, "ref": "github.com/acme/{{name}}@1.0.0",
        "files": ["mods/{{name}}/manifest.json"] }
      """;

  /// A file system whose workspace has the mods named, without any file of theirs.
  private static MockFileSystem Mods(string root, params string[] names) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), Root);
    fs.AddFile(
        Path.Join(root, ".lip", "state.json"),
        new MockFileData(
            """{ "format": 4, "side": "server", "platform": "win-x64", "packages": ["""
                + string.Join(",", names.Select(Package)) + "] }"));
    return fs;
  }

  /// The mods Alpha, with a JSON file, Beta, with a TOML file and a JSON file that is not
  /// JSON, and Gamma, without a file.
  private static MockFileSystem Workspace() {
    MockFileSystem fs = Mods(Root, "Alpha", "Beta", "Gamma");
    fs.AddFile(AlphaPath, new MockFileData(AlphaConfig));
    fs.AddFile(Path.Join(Root, "mods", "Beta", "broken.json"), new MockFileData("{ \"a\": }"));
    fs.AddFile(
        Path.Join(Root, "mods", "Beta", "settings.toml"),
        new MockFileData("[general]\nmotd = \"hello\"\n"));
    return fs;
  }

  private static string AlphaPath => Path.Join(Root, "mods", "Alpha", "config", "config.json");

  private static TuiHarness Start(
      MockFileSystem? fs = null, Language language = Language.En, int width = 120,
      int height = 36) =>
      TuiHarness.Start(new TuiHarnessOptions {
        FileSystem = fs ?? Workspace(),
        Language = language,
        Width = width,
        Height = height,
      });

  /// Goes to the page and into the tree of the first file, that of Alpha.
  private static async Task OpenAlpha(TuiHarness tui) {
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("limits");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
  }

  [Theory]
  [InlineData(Language.En, "Mod Config")]
  [InlineData(Language.ZhHans, "模组配置")]
  [InlineData(Language.ZhHant, "模組設定")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.ModConfig);

    Assert.Equal(title, tui.Text(() => ModConfigText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Fact]
  public async Task NoMods_OffersGetMods_WhichEnterOpens() {
    using TuiHarness tui = TuiHarness.Start();
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("No configurable mods.");
    Assert.Contains("[ Marketplace ]", tui.Screen);
    Assert.Contains("Enter Select", tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.Settle();

    Assert.Equal(PageId.GetMods, await tui.Invoke(() => tui.Context.CurrentPage));
  }

  [Fact]
  public async Task Files_AreListedByMod_AndTheFirstIsShownAsATree() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.ModConfig);

    await tui.WaitFor("{…} 2 items");
    tui.Snapshot("modconfig-files");
    string screen = tui.Screen;
    Assert.Contains("Alpha · config/config.json", screen);
    Assert.Contains("Beta · broken.json", screen);
    Assert.Contains("Beta · settings.toml", screen);
    Assert.Contains("Gamma", screen);
    Assert.Contains("[ ] false", screen);
    Assert.Contains("[…] 2 items", screen);
    Assert.Contains("↑↓ Move  Enter Open  Tab Next  Esc Back", screen);

    // A mod without a file says so, and has nothing to open.
    tui.Press(ConsoleKey.End);
    await tui.WaitFor("No config file. Starting the server may create one.");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.Contains("↑↓ Move  Esc Back", tui.Screen);
  }

  [Fact]
  public async Task Tab_GoesRoundTheFilesTheFileAndTheSidebar() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("{…} 2 items");
    Assert.Contains(" ↑↓ Move  Enter Open  Tab Next  Esc Back", tui.Screen);

    // The tree of the file is the last area while nothing is changed. Its first row is the
    // version, which nothing changes.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" ↑↓ Move  Tab Next  Esc Back");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");

    // From the sidebar Tab starts at the files, and Shift+Tab at the tree.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("Enter Open  Tab Next");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor(" ↑↓ Move  Tab Next  Esc Back");

    // With a change, the buttons are the last area.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(" ↑↓ Move  Enter/Space Toggle  Tab Next  Esc Back");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModConfigText.Unsaved);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(" ←→ Switch  Enter Select  Tab Next  ↑↓ Move  Esc Back");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor(" ←→ Switch  Enter Select  Tab Next  ↑↓ Move  Esc Back");

    // ↑ goes back from the buttons to the tree.
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("Enter/Space Toggle  Tab Next");
  }

  [Fact]
  public async Task Json_IsEditedAsATree_AndWrittenOnlyOnSave() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await OpenAlpha(tui);

    // The version of the file is not edited.
    await tui.WaitFor("The mod keeps this version itself.");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Spacebar);
    await tui.Settle();
    Assert.DoesNotContain("Unsaved", tui.Screen);
    Assert.DoesNotContain("Enter Edit", tui.Screen);

    // Space switches a boolean.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter/Space Toggle");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[x] true");
    await tui.WaitFor("Unsaved");

    // Enter opens an object, and asks for a number, which must be one.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter Open");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("config/config.json › limits");
    tui.Snapshot("modconfig-tree");
    await tui.WaitFor("Enter Edit");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ max ─");
    tui.Press(ConsoleKey.Backspace);
    tui.Type("nine");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Enter a number.");
    tui.Snapshot("modconfig-prompt");
    tui.Press(ConsoleKey.Backspace, 4);
    tui.Type("9");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("─ max ─");
    await tui.WaitFor(screen => screen.Contains("max ") && screen.Contains(" 9"));

    // A string takes the text as it is, the spaces around it included.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ name ─");
    tui.Type("y ");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("\"xy \"");

    // Esc goes up a level; nothing is written yet.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("{…} 2 items");
    Assert.Equal(AlphaConfig, fs.File.ReadAllText(AlphaPath));

    // Tab reaches the buttons, and Save writes the file.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch  Enter Select");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Saved.");
    await tui.WaitUntilGone("Unsaved");
    Assert.Equal(
        "{\n    \"version\": 2,\n    \"enabled\": true,\n    \"limits\": {\n        \"max\": 9,\n"
            + "        \"name\": \"xy \"\n    },\n    \"tags\": [\n        \"a\",\n        \"b\"\n"
            + "    ]\n}\n",
        fs.File.ReadAllText(AlphaPath));
    Assert.False(fs.File.Exists(AlphaPath + ".tmp"));
  }

  [Fact]
  public async Task Array_AddsEditsAndRemovesValues_InSimplifiedChinese() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs, Language.ZhHans);
    await OpenAlpha(tui);
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("+ 添加值…");
    Assert.Contains("config/config.json › tags", tui.Screen);

    // The row after the elements adds one: JSON, or text for a string.
    tui.Press(ConsoleKey.End);
    await tui.WaitFor("填 JSON；其他内容视为字符串。");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ 添加值 ─");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("必填");
    tui.Type("42");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[2]");
    await tui.WaitFor("未保存");

    // Enter on an element offers to edit it or to remove it.
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("移除");
    tui.Snapshot("modconfig-menu");
    Assert.Contains("编辑", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("[2]");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("─ [0] ─");
    tui.Type("c");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("\"bc\"");

    // ← goes up a level, ↓ below the last row reaches the buttons.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("limits");
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("已保存。");
    Assert.Contains(
        "\"tags\": [\n        \"bc\",\n        42\n    ]", fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task OtherFormats_AndBrokenJson_AreOnlyShown_InTraditionalChinese() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs, Language.ZhHant);
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("Beta · settings.toml");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("唯讀：不是有效的 JSON。");
    Assert.Contains("{ \"a\": }", tui.Screen);
    tui.Snapshot("modconfig-broken");

    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("唯讀，請用文字編輯器修改。");
    tui.Snapshot("modconfig-readonly");
    Assert.Contains("motd = \"hello\"", tui.Screen);
    Assert.DoesNotContain($"[ {CommonText.Save} ]", tui.Screen);

    // Nothing in it takes Enter or Space.
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Spacebar);
    await tui.Settle();
    Assert.Equal(PageId.ModConfig, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Equal(
        "[general]\nmotd = \"hello\"\n",
        fs.File.ReadAllText(Path.Join(Root, "mods", "Beta", "settings.toml")));
    Assert.Equal(
        "{ \"a\": }", fs.File.ReadAllText(Path.Join(Root, "mods", "Beta", "broken.json")));
  }

  /// A row of the list of files that is longer than its column is cut with an ellipsis, in
  /// its middle, and the mark of unsaved changes stays visible after it.
  [Theory]
  [InlineData(Language.En, 100, 28)]
  [InlineData(Language.ZhHans, 100, 28)]
  [InlineData(Language.ZhHant, 100, 28)]
  [InlineData(Language.En, 120, 36)]
  public async Task Files_ALongRow_IsCutWithAnEllipsis_AndKeepsTheMarkOfUnsavedChanges(
      Language language, int width, int height) {
    const string Mod = "LegacyScriptEngineQuickJsBridge";
    const string Name = "a-configuration-file-with-a-rather-long-name.json";
    MockFileSystem fs = Mods(Root, Mod);
    fs.AddFile(
        Path.Join(Root, "mods", Mod, "config", Name), new MockFileData("{ \"enabled\": false }"));
    using TuiHarness tui = Start(fs, language, width, height);
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("enabled");

    // The row is cut in its middle: the mod and the end of the name of the file stay.
    string Row() => tui.Screen.Split('\n').First(
        line => line.Contains("│ Legacy", StringComparison.Ordinal)
            && line.Contains('…', StringComparison.Ordinal));
    Assert.Matches(@"│ Legacy\S*…\S*\.json\s+│", Row());
    Assert.DoesNotContain("●", Row());

    // Act: a change that is not saved.
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(tui.Text(() => ModConfigText.Unsaved));

    // Assert: the mark is after the label, inside the box, and the label still has its
    // ellipsis and its end.
    Assert.Matches(@"│ Legacy\S*…\S*\.json ●\s*│", Row());
    tui.Snapshot("modconfig-long-row");
  }

  /// Opens the tree of Alpha and switches "enabled".
  private static async Task Change(TuiHarness tui) {
    await OpenAlpha(tui);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(tui.Text(() => ModConfigText.Unsaved));
  }

  [Fact]
  public async Task LeavingThePage_WithChanges_AsksToSaveDiscardOrCancel() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);

    // Cancel keeps the page and the changes.
    tui.Press(ConsoleKey.Escape, 2);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Save the changes?");
    tui.Snapshot("modconfig-unsaved");
    Assert.Contains("[ Save ] [ Discard ] [ Cancel ]", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Save the changes?");
    Assert.Equal(PageId.ModConfig, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Contains("[x] true", tui.Screen);

    // Save writes the file, and the page is left.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Save the changes?");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage != PageId.ModConfig);
    Assert.Contains("\"enabled\": true", fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task Quitting_WithChanges_Asks_AndDiscardWritesNothing() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);

    tui.Interrupt();
    await tui.WaitFor("Save the changes?");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(CommonText.QuitQuestion);
    tui.Press(ConsoleKey.Escape);

    await tui.WaitFor("[ ] false");
    Assert.DoesNotContain("Unsaved", tui.Screen);
    Assert.Equal(AlphaConfig, fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task AnotherFile_WithChanges_Asks_AndDiscardShowsIt() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);

    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Save the changes?");
    Assert.Contains("Alpha · config/config.json", tui.Screen);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Read-only: not valid JSON.");
    Assert.Equal(AlphaConfig, fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task AnotherWorkspace_WithChanges_Asks_AndSaveWritesTheFile() {
    MockFileSystem fs = Workspace();
    string other = Path.Join(Path.GetDirectoryName(Root)!, "other");
    fs.AddDirectory(other);
    using TuiHarness tui = Start(fs);
    await Change(tui);

    await tui.Invoke(() => tui.Context.SwitchWorkspace(other));
    await tui.WaitFor("Save the changes?");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("No configurable mods.");
    Assert.Contains("\"enabled\": true", fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task Save_DoesNotCreateAFileThatIsGone() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);
    fs.File.Delete(AlphaPath);

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("The file is gone.");
    Assert.False(fs.File.Exists(AlphaPath));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Unsaved");
  }

  [Fact]
  public async Task DiscardChanges_ReadsTheFileAgain() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);

    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Discard the changes?");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("[ ] false");
    await tui.WaitUntilGone("Unsaved");
    Assert.Equal(AlphaConfig, fs.File.ReadAllText(AlphaPath));
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task SmallTerminal_ShowsTheFilesOrTheFile_WhicheverHasTheFocus(Language language) {
    using TuiHarness tui = Start(language: language, width: 60, height: 16);
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("Beta · settings.toml");
    tui.Snapshot("modconfig-files");
    Assert.DoesNotContain("limits", tui.Screen);

    // A mod without a file says so below the list.
    tui.Press(ConsoleKey.End);
    await tui.WaitFor(tui.Text(() => ModConfigText.NoFile));
    tui.Snapshot("modconfig-nofile");
    tui.Press(ConsoleKey.Home);
    await tui.WaitUntilGone(tui.Text(() => ModConfigText.NoFile));
    await tui.Settle();

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("limits");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(tui.Text(() => ModConfigText.Unsaved));
    tui.Snapshot("modconfig-tree");
    Assert.Contains("Alpha · config/config.json", tui.Screen);
    Assert.Contains(
        $"[ {CommonText.Save} ] [ {tui.Text(() => CommonText.Discard)} ]", tui.Screen);

    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("+ " + tui.Text(() => ModConfigText.AddValue));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(tui.Text(() => ModConfigText.Remove));
    tui.Snapshot("modconfig-menu");
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {tui.Text(() => ModConfigText.AddTitle)} ─");
    tui.Snapshot("modconfig-add");
    tui.Press(ConsoleKey.Escape);

    // Esc goes up a level, then to the files, then to the sidebar.
    tui.Press(ConsoleKey.Escape, 2);
    await tui.WaitFor("Beta · settings.toml");
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.UnsavedQuestion);
    tui.Snapshot("modconfig-unsaved");
  }

  [Theory]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task LargeTerminal_Frames(Language language) {
    using TuiHarness tui = Start(language: language);
    await OpenAlpha(tui);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(tui.Text(() => ModConfigText.Unsaved));
    tui.Press(ConsoleKey.DownArrow);
    await tui.Settle();
    tui.Snapshot("modconfig-tree");
    Assert.Contains(tui.Text(() => ModConfigText.Files), tui.Screen);
    Assert.Contains($"{{…}} {tui.Text(() => ModConfigText.Items(2))}", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Save_OfAFileThatChangedOnDisk_AsksToOverwriteReloadOrCancel(
      Language language, int width, int height) {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs, language, width, height);
    // On a narrow terminal the page shows the files, or the file that has the focus.
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("Alpha · config/config.json");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("limits");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModConfigText.Unsaved);
    string buttons =
        $"[ {CommonText.Overwrite} ] [ {ModConfigText.Reload} ] [ {CommonText.Cancel} ]";

    // The mod writes its file while the user edits it.
    string rewritten = AlphaConfig.Replace("\"max\": 5", "\"max\": 9");
    fs.File.WriteAllText(AlphaPath, rewritten);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    tui.Press(ConsoleKey.Enter);

    // The question, with the button that changes nothing chosen: Enter cancels.
    await tui.WaitFor(buttons);
    tui.Snapshot("modconfig-changed-on-disk");
    Assert.Contains(Flat(CommonText.ChangedOnDisk), Flat(tui.Screen));
    Assert.Contains(Flat(ModConfigText.ReloadDetail), Flat(tui.Screen));
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(buttons);
    Assert.Equal(rewritten, fs.File.ReadAllText(AlphaPath));
    Assert.Contains(ModConfigText.Unsaved, tui.Screen);
    Assert.Contains("[x] true", tui.Screen);

    // Esc cancels too.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(buttons);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(buttons);
    Assert.Equal(rewritten, fs.File.ReadAllText(AlphaPath));
    Assert.Contains(ModConfigText.Unsaved, tui.Screen);

    // Reload reads the file as the mod wrote it, and drops the change.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(buttons);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ModConfigText.Unsaved);
    await tui.WaitFor("[ ] false");
    Assert.Equal(rewritten, fs.File.ReadAllText(AlphaPath));

    // Changed again, and written again by the mod: Overwrite saves what the user has, which
    // is what was read at the reload with their change.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModConfigText.Unsaved);
    fs.File.WriteAllText(AlphaPath, AlphaConfig.Replace("\"max\": 5", "\"max\": 7"));
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(buttons);
    tui.Press(ConsoleKey.LeftArrow, 2);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ModConfigText.Unsaved);
    await tui.WaitFor(CommonText.Saved);
    string saved = fs.File.ReadAllText(AlphaPath);
    Assert.Contains("\"enabled\": true", saved);
    Assert.Contains("\"max\": 9", saved);

    // A file that holds what was read of it is saved without a question.
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModConfigText.Unsaved);
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(ModConfigText.Unsaved);
    Assert.DoesNotContain(buttons, tui.Screen);
    Assert.Contains("\"enabled\": false", fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task LeavingThePage_WithAFileThatChangedOnDisk_AsksBeforeItOverwrites() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);
    string rewritten = AlphaConfig.Replace("\"max\": 5", "\"max\": 9");
    fs.File.WriteAllText(AlphaPath, rewritten);

    // Leaving, and saving: the question comes before anything is written, and cancelling it
    // keeps the user on the page with the changes.
    tui.Press(ConsoleKey.Escape, 2);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Save the changes?");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Overwrite ] [ Reload ] [ Cancel ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("[ Overwrite ] [ Reload ] [ Cancel ]");
    await tui.Settle();

    Assert.Equal(PageId.ModConfig, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Equal(rewritten, fs.File.ReadAllText(AlphaPath));
    Assert.Contains("Unsaved", tui.Screen);

    // Reload drops the changes, as Discard does, and the user goes where they were going.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Save the changes?");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Overwrite ] [ Reload ] [ Cancel ]");
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage != PageId.ModConfig, "another page");
    Assert.Equal(rewritten, fs.File.ReadAllText(AlphaPath));
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("limits");
    Assert.DoesNotContain("Unsaved", tui.Screen);
    Assert.Contains("[ ] false", tui.Screen);
  }

  /// The text without the line breaks, the borders and the spaces that wrapping it in a
  /// dialog puts into it.
  private static string Flat(string text) =>
      System.Text.RegularExpressions.Regex.Replace(text, @"[\s│]+", "");

  #region The empty page, and while the server runs

  [Theory]
  [InlineData(Language.En, "No configurable mods.", 120, 36)]
  [InlineData(Language.ZhHans, "没有可配置的模组。", 120, 36)]
  [InlineData(Language.ZhHant, "沒有可設定的模組。", 120, 36)]
  [InlineData(Language.En, "No configurable mods.", 60, 16)]
  [InlineData(Language.ZhHans, "没有可配置的模组。", 60, 16)]
  [InlineData(Language.ZhHant, "沒有可設定的模組。", 60, 16)]
  public async Task NoMods_SaysThatNoneIsConfigurable_InEveryLanguage(
      Language language, string text, int width, int height) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      Language = language,
      Width = width,
      Height = height,
    });
    await tui.GoTo(PageId.ModConfig);

    await tui.WaitFor(text);
    tui.Snapshot("modconfig-empty");
    Assert.DoesNotContain(CommonText.NoMods, tui.Screen);
    Assert.Contains($"[ {CommonText.GetMods} ]", tui.Screen);
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", tui.Screen);

    // The server changes nothing about it.
    await tui.SetServerRunning(true);
    await tui.Settle();
    Assert.Contains(text, tui.Screen);
    Assert.DoesNotContain(ModConfigText.MayNeedRestart, tui.Screen);
  }

  [Fact]
  public async Task AModWithoutAFile_DoesNotPromiseOne() {
    using TuiHarness tui = Start(Mods(Root, "Gamma"));
    await tui.GoTo(PageId.ModConfig);

    await tui.WaitFor("No config file. Starting the server may create one.");
    Assert.Contains("Gamma", tui.Screen);
    Assert.DoesNotContain("No configurable mods.", tui.Screen);
  }

  [Fact]
  public async Task WhileTheServerRuns_FilesAreEditedAndSaved_AndThePageSaysARestartMayBeNeeded() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("limits");
    Assert.DoesNotContain("Changes may need a server restart.", tui.Screen);

    // The line is there as soon as the server runs, whatever has the focus.
    await tui.SetServerRunning(true);
    await tui.WaitFor("Changes may need a server restart.");
    tui.Snapshot("modconfig-running-files");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Enter/Space Toggle");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("[x] true");
    await tui.WaitFor("Unsaved");
    Assert.Contains("Changes may need a server restart.", tui.Screen);
    Assert.Equal(AlphaConfig, fs.File.ReadAllText(AlphaPath));

    // Saving writes the file, and says the same.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch  Enter Select");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Saved. May need a restart.");
    await tui.WaitUntilGone("Unsaved");
    Assert.Contains("\"enabled\": true", fs.File.ReadAllText(AlphaPath));
    Assert.Contains("Changes may need a server restart.", tui.Screen);

    // Once the server has stopped the line is gone, and a save is just a save.
    await tui.SetServerRunning(false);
    await tui.WaitUntilGone("Changes may need a server restart.");
    await tui.WaitFor("Enter/Space Toggle");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor("Unsaved");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("←→ Switch  Enter Select");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Unsaved");
    await tui.WaitFor(screen => screen.Contains("Saved.")
        && !screen.Contains("Saved. May need a restart."));
    Assert.Contains("\"enabled\": false", fs.File.ReadAllText(AlphaPath));
  }

  [Fact]
  public async Task AServerThatStarts_KeepsTheChangesOfAFile_WhichAreSavedAllTheSame() {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs);
    await Change(tui);
    Assert.DoesNotContain(ModConfigText.MayNeedRestart, tui.Screen);

    await tui.SetServerRunning(true);
    await tui.WaitFor(ModConfigText.MayNeedRestart);
    Assert.Contains("[x] true", tui.Screen);
    Assert.Contains(ModConfigText.Unsaved, tui.Screen);

    // Leaving offers to save, as ever.
    tui.Press(ConsoleKey.Escape, 2);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Save the changes?");
    Assert.Contains("[ Save ] [ Discard ] [ Cancel ]", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntil(() => tui.Context.CurrentPage != PageId.ModConfig);
    Assert.Contains("\"enabled\": true", fs.File.ReadAllText(AlphaPath));
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task WhileTheServerRuns_ThePageFitsTheTerminal_InEveryLanguage(
      Language language, int width, int height) {
    MockFileSystem fs = Workspace();
    using TuiHarness tui = Start(fs, language, width, height);
    await tui.GoTo(PageId.ModConfig);
    await tui.WaitFor("Beta · settings.toml");
    tui.Snapshot("modconfig-stopped-files");
    await tui.SetServerRunning(true);
    await tui.WaitFor(ModConfigText.MayNeedRestart);
    tui.Snapshot("modconfig-running-files");

    // A mod without a file, and the line about the server below what says so.
    tui.Press(ConsoleKey.End);
    await tui.WaitFor(ModConfigText.NoFile);
    tui.Snapshot("modconfig-running-nofile");
    Assert.Contains(ModConfigText.MayNeedRestart, tui.Screen);
    tui.Press(ConsoleKey.Home);
    await tui.WaitUntilGone(ModConfigText.NoFile);
    await tui.Settle();

    // The tree, with a change, its buttons, and the line.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("limits");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor(ModConfigText.Unsaved);
    tui.Snapshot("modconfig-running-tree");
    Assert.Contains($"[ {CommonText.Save} ] [ {CommonText.Discard} ]", tui.Screen);
    Assert.Contains(ModConfigText.MayNeedRestart, tui.Screen);

    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor($"Enter {CommonText.HintSelect.Text}");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModConfigText.SavedWhileRunning);
    tui.Snapshot("modconfig-running-saved");
    Assert.Contains("\"enabled\": true", fs.File.ReadAllText(AlphaPath));
  }

  #endregion
}
