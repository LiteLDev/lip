using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using TuiContext = Lip.Cli.Tui.AppContext;

namespace Lip.Cli.Tests.Tui;

/// The shell of the interface: the sidebar, the focus, the dialogs, the operations and the
/// language, with pages of the tests in place of those of lip, driven by the keys of a fake
/// terminal.
public sealed class ShellTests {
  /// A page that says what it is told, and takes the keys that the test gives it.
  private sealed class TestPage(TuiContext context, string title) : Page(context) {
    public List<string> Events { get; } = [];

    public bool Dirty { get; set; }

    public Func<ConsoleKeyInfo, bool>? Keys { get; set; }

    public override string Title => title;

    public override bool CanLeave => !Dirty;

    public IReadOnlyList<Hint> OwnHints { get; set; } = [new Hint("Enter", "Act")];

    public override IReadOnlyList<Hint> Hints => OwnHints;

    public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
        screen.Write(area.X, area.Y, $"body of {title}" + (focused ? " [focused]" : ""));

    public override bool OnKey(ConsoleKeyInfo key) => Keys?.Invoke(key) ?? false;

    public override void OnStart() => Events.Add("start");

    public override void OnShown() => Events.Add("shown");

    public override void OnHidden() => Events.Add("hidden");

    public override void OnWorkspaceChanged() => Events.Add("workspace");

    public override void OnRequest(string request) => Events.Add("request " + request);

    public override void OnTabIn(bool backward) =>
        Events.Add(backward ? "tab in backward" : "tab in");

    public override void ConfirmLeave(Action leave) =>
        Context.Confirm(
            CommonText.UnsavedTitle, "Discard the changes?", CommonText.Discard, () => {
              Dirty = false;
              leave();
            });
  }

  private readonly Dictionary<PageId, TestPage> _pages = [];

  private TuiHarness Start(TuiHarnessOptions? options = null) {
    options ??= new TuiHarnessOptions();
    PageEntry Entry(PageId id, string icon, string title) =>
        new(id, icon, context => _pages[id] = new TestPage(context, title));

    return TuiHarness.Start(new TuiHarnessOptions {
      Language = options.Language,
      Width = options.Width,
      Height = options.Height,
      FileSystem = options.FileSystem,
      CreateClient = options.CreateClient,
      ManagedServers = options.ManagedServers,
      DescribeFailure = options.DescribeFailure,
      Pages = [
        Entry(PageId.Home, "a", "Alpha"),
        Entry(PageId.Server, "b", "Beta"),
        Entry(PageId.Mods, "c", "Gamma"),
      ],
    });
  }

  [Fact]
  public async Task Start_ShowsTheFirstPageAndTheWorkspace_WithTheSidebarFocused() {
    using TuiHarness tui = Start();

    await tui.WaitFor("body of Alpha");

    Assert.Contains("lip 1.2.3 · " + tui.Root, tui.Screen);
    Assert.Contains("─ Alpha ─", tui.Screen);
    Assert.DoesNotContain("[focused]", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Open  Esc Quit", tui.Screen);
    Assert.Equal(["start", "shown"], _pages[PageId.Home].Events);
    Assert.Equal(["start"], _pages[PageId.Server].Events);
    Assert.Equal([tui.Root], tui.Workspaces.Read());
  }

  [Fact]
  public async Task Sidebar_ArrowsShowThePages_EnterGivesThePageTheFocus_EscTakesItBack() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    // ↓ shows the next page, without giving it the focus.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("body of Beta");
    Assert.DoesNotContain("[focused]", tui.Screen);
    Assert.Equal(["start", "shown", "hidden"], _pages[PageId.Home].Events);

    // Enter gives it the focus: the bottom bar shows its keys, and Esc.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("body of Beta [focused]");
    Assert.Contains("Enter Act  Esc Back", tui.Screen);

    // Esc gives the focus back to the sidebar; ↑ twice goes round to the last page.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[focused]");
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor("body of Gamma");

    // → and Tab enter the page too; ← and Tab leave it when the page does not take them.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("body of Gamma [focused]");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitUntilGone("[focused]");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("body of Gamma [focused]");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntilGone("[focused]");
  }

  [Fact]
  public async Task Tab_FromTheSidebar_TellsThePageWhichWayItCame_AndEnterDoesNot() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    TestPage page = _pages[PageId.Home];

    // Enter and → give the page the focus where it was.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[focused]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[focused]");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("[focused]");
    Assert.DoesNotContain(page.Events, e => e.StartsWith("tab in", StringComparison.Ordinal));

    // Tab goes on from the page to the sidebar, and from the sidebar to the first area of
    // the page; Shift+Tab goes the other way, to its last area.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntilGone("[focused]");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor("[focused]");
    Assert.Equal("tab in", page.Events[^1]);
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitUntilGone("[focused]");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    await tui.WaitFor("[focused]");
    Assert.Equal("tab in backward", page.Events[^1]);
  }

  [Fact]
  public async Task BottomBar_ThatIsTooNarrow_LeavesOutHintsFromTheEnd_ButKeepsEsc() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Width = 60, Height = 16 });
    await tui.WaitFor("body of Alpha");
    _pages[PageId.Home].OwnHints = [
      new Hint("↑↓", "Move"), new Hint("Enter", "Actions"), new Hint("Tab", "Next"),
      new Hint("PgUp/PgDn", "Page"), new Hint("Home/End", "Ends"),
    ];
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[focused]");
    await tui.Settle();

    // 60 columns have no room for all six: the hints of paging go, and Esc stays.
    string bar = tui.Screen.Split('\n')[^1];
    Assert.Equal(" ↑↓ Move  Enter Actions  Tab Next  PgUp/PgDn Page  Esc Back", bar.TrimEnd());

    tui.Resize(120, 36);
    await tui.WaitFor("Home/End Ends  Esc Back");
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task BottomBar_WithAToast_KeepsTheKeysThatAct_AndCutsTheToastFirst(
      Language language) {
    using TuiHarness tui = Start(
        new TuiHarnessOptions { Width = 60, Height = 16, Language = language });
    await tui.WaitFor("body of Alpha");
    _pages[PageId.Home].OwnHints = [
      CommonText.HintMove, CommonText.HintActions, CommonText.HintCheck, CommonText.HintNext,
      CommonText.HintPage, CommonText.HintEnds,
    ];
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[focused]");
    string enter = $"Enter {CommonText.HintActions.Text}  Space {CommonText.HintCheck.Text}";
    string esc = $"Esc {CommonText.HintBack.Text}";

    // A toast as long as the bar: Enter, Space and Esc stay, in their order, the keys that
    // only move go, and the toast is cut to what is left.
    string toast = string.Concat(Enumerable.Repeat("已儲存 saved ", 8));
    await tui.Invoke(() => tui.Context.Toast(toast, ToastKind.Good));
    await tui.WaitFor("已儲存");
    string bar = tui.Screen.Split('\n')[^1];
    Assert.StartsWith($" {enter}  {esc}  已儲存", bar);
    Assert.EndsWith("…", bar);
    Assert.Equal(59, TextWidth.Of(bar));
    Assert.DoesNotContain(CommonText.HintNext.Text, bar);

    // A short toast leaves room for the keys that move, from the first.
    await tui.Invoke(() => tui.Context.Toast("OK", ToastKind.Good));
    await tui.WaitFor(screen => screen.Split('\n')[^1].EndsWith("  OK", StringComparison.Ordinal),
        "the short toast");
    bar = tui.Screen.Split('\n')[^1];
    Assert.StartsWith($" ↑↓ {CommonText.HintMove.Text}  {enter}  ", bar);
    Assert.Contains($"  {esc}  ", bar);
  }

  [Fact]
  public async Task BottomBar_WithManyKeysThatAct_StillShowsTheToast_InTenColumnsAtLeast() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Width = 60, Height = 16 });
    await tui.WaitFor("body of Alpha");
    _pages[PageId.Home].OwnHints = [
      new Hint("Enter", "Carry out the action"), new Hint("Space", "Check the row"),
      new Hint("Ctrl+C", "Stop it"),
    ];
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[focused]");

    // Without a toast the three keys that act and Esc fill the bar.
    await tui.WaitFor("Enter Carry out the action  Space Check the row  Esc Back");

    // A toast is never left out for them: the last of them go, and the toast has ten
    // columns or more.
    await tui.Invoke(() => tui.Context.Toast("The file was not saved: it is locked."));
    await tui.WaitFor("The file was");
    string bar = tui.Screen.Split('\n')[^1];
    Assert.Equal(" Enter Carry out the action  Esc Back  The file was not sa…", bar);
  }

  [Fact]
  public async Task BottomBar_ThatIsTooNarrow_KeepsEnterAndEsc_BeforeTheKeysThatMove() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Width = 60, Height = 16 });
    await tui.WaitFor("body of Alpha");
    _pages[PageId.Home].OwnHints = [
      new Hint("↑↓/Tab", "Move between fields"), new Hint("←→", "Change the value"),
      new Hint("PgUp/PgDn", "Page"), new Hint("Enter/Space", "Toggle"),
    ];
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[focused]");
    await tui.Settle();

    string bar = tui.Screen.Split('\n')[^1];
    Assert.Equal(
        " ↑↓/Tab Move between fields  Enter/Space Toggle  Esc Back", bar.TrimEnd());
  }

  [Theory]
  [InlineData(Language.En, 60, 16, 4)]
  [InlineData(Language.ZhHans, 60, 16, 4)]
  [InlineData(Language.ZhHant, 60, 16, 4)]
  [InlineData(Language.En, 120, 36, 3)]
  [InlineData(Language.ZhHans, 120, 36, 3)]
  [InlineData(Language.ZhHant, 120, 36, 3)]
  public async Task Question_OfACommand_IsWrapped_AndShownWhole(
      Language language, int width, int height, int rowsAtLeast) {
    using TuiHarness tui = Start(
        new TuiHarnessOptions { Language = language, Width = width, Height = height });
    await tui.WaitFor("body of Alpha");
    const string Question = "What is the package id of the package? It names the repository"
        + " that the package is published in, such as github.com/owner/repo";
    string? answer = null;

    await tui.Invoke(() => tui.Context.RunOperation(
        "Create",
        _ => tui.InteractionOf(tui.Context.Client).Ask(Question),
        result => answer = result));
    string title = $"─ {CommonText.Question} ─";
    await tui.WaitFor(title);
    tui.Snapshot("shell-question");

    // The question takes the rows it needs: every word of it is in the dialog, in order,
    // and the answer is typed below it.
    string[] screen = tui.Screen.Split('\n');
    int top = Array.FindIndex(screen, row => row.Contains(title));
    int left = screen[top].IndexOf("┌" + title, StringComparison.Ordinal);
    string[] rows = [
      .. screen.Skip(top + 1)
          .TakeWhile(row => row[left] == '│')
          .Select(row => row[(left + 1)..row.IndexOf('│', left + 1)].Trim()),
    ];
    Assert.Equal(Question, string.Join(' ', rows.TakeWhile(row => !row.StartsWith('>'))));
    Assert.True(rows.Length >= rowsAtLeast, tui.Screen);
    Assert.All(screen, row => Assert.True(row.Length <= width, row));
    Assert.Contains(
        $" Enter {CommonText.HintConfirm.Text}  Esc {CommonText.HintCancel.Text}", tui.Screen);
    tui.Type("github.com/acme/pkg");
    await tui.WaitFor("> github.com/acme/pkg");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(_ => answer == "github.com/acme/pkg");
  }

  [Fact]
  public async Task Dialogs_NameEveryKeyThatMovesInThem() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    string[]? entered = null;

    // Several fields: ↑, ↓ and Tab go from one to the next, and the bottom bar says so.
    await tui.Invoke(() => tui.Context.Show(new PromptDialog(
        "Pack",
        [new PromptField("Folder", "."), new PromptField("Output file", "", "package.zip")],
        values => entered = values)));
    await tui.WaitFor("package.zip");
    Assert.Contains(" ↑↓/Tab Move  Enter Confirm  Esc Cancel", tui.Screen);
    tui.Press(ConsoleKey.Tab);
    tui.Type("a");
    tui.Press(ConsoleKey.Tab);
    tui.Type("b");
    tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
    tui.Type("c");
    tui.Press(ConsoleKey.DownArrow);
    tui.Type("d");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(_ => entered is not null);
    Assert.Equal([".bd", "ac"], entered!);

    // One field: nothing moves.
    await tui.Invoke(() => tui.Context.Show(new PromptDialog(
        "Rename", [new PromptField("Name")], _ => {
        })));
    await tui.WaitFor("─ Rename ─");
    Assert.Contains(" Enter Confirm  Esc Cancel", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("─ Rename ─");

    // Buttons: ← → and Tab go from one to the next.
    int? chosen = null;
    await tui.Invoke(() => tui.Context.Show(new ConfirmDialog(
        "Save", [new StyledLine("Save the changes?")], ["Save", "Discard", "Cancel"],
        choice => chosen = choice)));
    await tui.WaitFor("[ Save ] [ Discard ] [ Cancel ]");
    Assert.Contains(" ←→/Tab Switch  Enter Select  Esc Cancel", tui.Screen);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(_ => chosen == 1);

    // A text that scrolls: every key that scrolls it.
    await tui.Invoke(() => tui.Context.Show(new ViewerDialog(
        "README", string.Join('\n', Enumerable.Range(1, 80).Select(i => $"line {i}.")))));
    await tui.WaitFor("line 1.");
    Assert.Contains(" ↑↓ Scroll  PgUp/PgDn Page  Home/End Ends  Esc Close", tui.Screen);
    tui.Press(ConsoleKey.End);
    await tui.WaitFor("line 80.");
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor("line 1.");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("line 1.");

    // A text that fits: none of them.
    await tui.Invoke(() => tui.Context.Show(new ViewerDialog("README", "One line.")));
    await tui.WaitFor("One line.");
    Assert.Contains(" Esc Close", tui.Screen);
    Assert.DoesNotContain("Scroll", tui.Screen);
  }

  [Fact]
  public async Task Page_TakesTheKeysItHandles_AndLettersDoNothingElse() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<char> typed = [];
    _pages[PageId.Home].Keys = key => {
      if (key.Key == ConsoleKey.Escape) {
        return false;
      }

      typed.Add(key.KeyChar);
      return true;
    };

    // In the sidebar no letter does anything.
    tui.Type("qw1");
    await tui.Settle();
    Assert.Contains("body of Alpha", tui.Screen);
    Assert.Empty(typed);

    tui.Press(ConsoleKey.Enter);
    tui.Type("qw");
    tui.Press(ConsoleKey.LeftArrow);
    await tui.Settle();

    Assert.Equal(['q', 'w', '\0'], typed);
    Assert.Contains("body of Alpha [focused]", tui.Screen);
  }

  [Fact]
  public async Task Esc_GoesBackOneLevelAtATime_AndAsksBeforeQuitting() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    await tui.Invoke(() => tui.Context.Message("Note", "A message."));
    await tui.WaitFor("A message.");

    // The dialog, then the page, then the sidebar, where Esc asks to quit.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("A message.");
    Assert.Contains("[focused]", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[focused]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Quit lip?");
    Assert.Contains("[ Quit ] [ Cancel ]", tui.Screen);

    // Esc in the question changes nothing.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Quit lip?");
    Assert.False(tui.Run.IsCompleted);

    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Quit lip?");
    tui.Press(ConsoleKey.Enter);
    Assert.Equal(0, await tui.Run.WaitAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task CtrlC_AsksToQuit_UnlessThePageTakesIt() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    int taken = 0;
    _pages[PageId.Home].Keys = key => {
      if (!Ui.IsInterrupt(key)) {
        return false;
      }

      taken++;
      return taken == 1;
    };
    tui.Press(ConsoleKey.Enter);

    tui.Interrupt();
    await tui.Settle();
    Assert.DoesNotContain("Quit lip?", tui.Screen);

    Assert.Equal(0, await tui.Quit());
    Assert.Equal(2, taken);
  }

  [Fact]
  public async Task TerminalGone_EndsTheInterfaceWithCodeZero() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    tui.Terminal.Dispose();

    Assert.Equal(0, await tui.Run.WaitAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task UnsavedChanges_AreAskedAboutBeforeAnotherPageOrQuitting() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    _pages[PageId.Home].Dirty = true;

    // Cancel keeps the page.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Discard the changes?");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Discard the changes?");
    Assert.Contains("body of Alpha", tui.Screen);

    // So does quitting.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Discard the changes?");
    Assert.DoesNotContain("Quit lip?", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Discard the changes?");

    // Discard lets go.
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Discard the changes?");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("body of Beta");
  }

  [Fact]
  public async Task Navigate_ShowsThePageFocused_AndPassesItTheRequest() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    await tui.Invoke(() => tui.Context.Navigate(PageId.Server, PageRequest.StartServer));

    await tui.WaitFor("body of Beta [focused]");
    Assert.Equal(
        ["start", "shown", "request " + PageRequest.StartServer], _pages[PageId.Server].Events);
    Assert.Equal(PageId.Server, tui.Context.CurrentPage);
  }

  [Fact]
  public async Task Dialogs_Stack_AndTheTopOneTakesTheKeys() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<string> log = [];

    // A menu whose first action cannot be chosen; its second opens a prompt.
    await tui.Invoke(() => tui.Context.ShowMenu(
        "Actions",
        new MenuItem("Remove", () => log.Add("remove"), Enabled: false),
        new MenuItem("Rename…", () => tui.Context.Show(new PromptDialog(
            "Rename",
            [new PromptField("Name", "old", "my-server")],
            values => log.Add("name " + values[0]),
            validate: values => values[0].Length == 0 ? "Required" : null))),
        new MenuItem("Other", () => log.Add("other"))));
    await tui.WaitFor("Rename…");
    Assert.Contains("↑↓ Move  Enter Select  Esc Close", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("> old");

    // The prompt checks what is entered, and stays open until it is right.
    tui.Press(ConsoleKey.Backspace, 3);
    await tui.WaitFor("my-server");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Required");
    tui.Type("new name ");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Rename");
    Assert.Equal(["name new name"], log);

    // An action that cannot be chosen and says why: ↑ and ↓ stop at it, the menu shows why,
    // and Enter does nothing. One that does not say why is skipped.
    await tui.Invoke(() => tui.Context.ShowMenu(
        "Actions",
        new MenuItem("Details", () => log.Add("details")),
        new MenuItem("Update", () => log.Add("update"), Enabled: false, "Stop the server first."),
        new MenuItem("Pin", () => log.Add("pin"), Enabled: false),
        new MenuItem("Uninstall", () => log.Add("uninstall"), Enabled: false,
            "Stop the server first.")));
    await tui.WaitFor("Uninstall");
    Assert.DoesNotContain("Stop the server first.", tui.Screen);
    Assert.Contains("↑↓ Move  Enter Select  Esc Close", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("Stop the server first.");
    Assert.Contains("↑↓ Move  Esc Close", tui.Screen);
    tui.Snapshot("shell-menu-reason");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitUntilGone("Stop the server first.");
    Assert.Contains("Uninstall", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Uninstall");
    Assert.Equal(["name new name", "details"], log);
    log.Remove("details");

    // A choice, with the detail of the selected option.
    await tui.Invoke(() => tui.Context.Show(new ChoiceDialog(
        "Build",
        [new ChoiceItem("windows", "With a loader."), new ChoiceItem("linux", "No loader.")],
        chosen => log.Add("choice " + chosen))));
    await tui.WaitFor("With a loader.");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor("No loader.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("No loader.");
    Assert.Equal(["name new name", "choice 1"], log);

    // A question: → goes to the second button, which Esc also is.
    await tui.Invoke(() => tui.Context.Confirm(
        "Remove", "Remove it?", "Remove", () => log.Add("removed")));
    await tui.WaitFor("[ Remove ] [ Cancel ]");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Remove it?");
    Assert.DoesNotContain("removed", log);
  }

  [Fact]
  public async Task RunOperation_ShowsItsProgress_AndEscCancelsIt() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    await tui.Invoke(() => tui.Context.RunOperation(
        "Download",
        async token => {
          await tui.InteractionOf(tui.Context.Client).PrintInfo("Fetching the list");
          started.SetResult();
          await Task.Delay(Timeout.Infinite, token);
        },
        cancellable: true));
    await started.Task;
    await tui.WaitFor("Fetching the list");
    Assert.Contains("─ Download ─", tui.Screen);
    Assert.Contains("Esc Cancel", tui.Screen);

    // Another operation does not start meanwhile.
    Assert.False(await tui.Invoke(() => tui.Context.RunOperation("Other", _ => Task.CompletedTask)));

    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor("Download · Failed");
    Assert.Contains("Cancelled. Changes were undone.", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("Download · Failed");
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task RunOperation_WithAResult_PassesItOn_AndAQuestionIsAskedInADialog() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    string? answer = null;

    await tui.Invoke(() => tui.Context.RunOperation(
        "Create",
        _ => tui.InteractionOf(tui.Context.Client).Ask("the name of the package"),
        result => answer = result));
    await tui.WaitFor("the name of the package");
    Assert.Contains("─ lip asks ─", tui.Screen);
    tui.Type("pkg");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitUntilGone("lip asks");
    await tui.WaitFor(_ => answer == "pkg");
  }

  /// A workspace operation of the tests: it records its calls and their sessions, and says
  /// what lip says.
  private static Func<OperationRun, Task<OperationResult>> Operation(
      TuiHarness tui, List<string> calls, bool setup, List<OperationSession>? sessions = null) =>
      async run => {
        lock (calls) {
          calls.Add($"dryRun={run.DryRun} acceptEula={run.AcceptEula}");
          sessions?.Add(run.Session);
        }

        IUserInteraction interaction = tui.InteractionOf(tui.Context.Client);
        if (run.DryRun) {
          await interaction.PrintInfo("Resolved 1 package");
          await interaction.PrintInfo("Dry run: would install github.com/acme/Alpha 1.2.0");
          if (setup) {
            await interaction.PrintInfo(GameSetup.EulaDryRunMessage + ".");
          }
        } else {
          await interaction.PrintSuccess("Installed github.com/acme/Alpha 1.2.0");
        }

        return OperationResult.Changed;
      };

  /// A workspace operation of the tests that says nothing, and returns the result.
  private static Func<OperationRun, Task<OperationResult>> Operation(
      List<string> calls, OperationResult result) =>
      run => {
        lock (calls) {
          calls.Add($"dryRun={run.DryRun}");
        }

        return Task.FromResult(result);
      };

  [Fact]
  public async Task PlanAndRun_ShowsThePlan_ThenTheLicense_ThenRunsAndShowsTheResult() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];
    List<OperationSession> sessions = [];
    bool done = false;

    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: true, sessions), () => done = true));

    // The plan of the dry run, with [Apply] chosen.
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Contains("Dry run: would install github.com/acme/Alpha 1.2.0", tui.Screen);
    Assert.DoesNotContain("Nothing would change.", tui.Screen);
    tui.Snapshot("shell-plan");
    tui.Press(ConsoleKey.Enter);

    // The license of the game, since the dry run says that the setup would ask for it.
    await tui.WaitFor(GameSetup.EulaUrl);
    Assert.Contains("[ Accept ] [ Decline ]", tui.Screen);
    tui.Snapshot("shell-eula");
    tui.Press(ConsoleKey.Enter);

    // The result.
    await tui.WaitFor("✓ Done.");
    Assert.Contains("✓ Installed github.com/acme/Alpha 1.2.0", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ Done.");
    Assert.True(done);
    Assert.Equal(
        ["dryRun=True acceptEula=False", "dryRun=False acceptEula=True"], calls);
    // The dry run and the operation after it are of one session.
    Assert.Equal(2, sessions.Count);
    Assert.Same(sessions[0], sessions[1]);
    Assert.Contains("workspace", _pages[PageId.Server].Events);
  }

  [Fact]
  public async Task PlanAndRun_WithASession_CarriesBothOutInIt_AndWithoutOne_InANewOneEachTime() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];
    List<OperationSession> sessions = [];
    OperationSession held = new();

    // A flow that read something before gives its session.
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: false, sessions), session: held));
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("✓ Done.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("✓ Done.");

    // Another operation, without one.
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: false, sessions)));
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[ Apply ]");

    Assert.Equal(3, sessions.Count);
    Assert.Same(held, sessions[0]);
    Assert.Same(held, sessions[1]);
    Assert.NotSame(held, sessions[2]);
  }

  [Fact]
  public async Task PlanAndRun_Cancelled_IsThePreviewOnly_AndADeclinedLicenseRunsNothing() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];

    // Cancel, the second button.
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: true)));
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    tui.Press(ConsoleKey.RightArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("[ Apply ]");

    // Apply, then decline the license with Esc.
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: true)));
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Accept ] [ Decline ]");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("[ Accept ]");

    Assert.Equal(["dryRun=True acceptEula=False", "dryRun=True acceptEula=False"], calls);
    Assert.DoesNotContain("workspace", _pages[PageId.Home].Events);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
  }

  [Fact]
  public async Task PlanAndRun_WithoutASetup_AsksNoLicense() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];

    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Update", Operation(calls, OperationResult.Changed)));
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.DoesNotContain("Nothing would change.", tui.Screen);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("✓ Done.");
    Assert.Equal(["dryRun=True", "dryRun=False"], calls);
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHant)]
  public async Task PlanAndRun_APlanThatWouldChangeNothing_CannotBeApplied(Language language) {
    using TuiHarness tui = Start(new TuiHarnessOptions { Language = language });
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];
    bool done = false;

    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Update", async run => {
          lock (calls) {
            calls.Add($"dryRun={run.DryRun}");
          }

          // A line that a plan with changes has too does not make it one.
          await tui.InteractionOf(tui.Context.Client).PrintInfo("Resolving Alpha...");
          return OperationResult.Unchanged;
        },
        () => done = true));

    // The plan says so, and offers nothing but to close it.
    await tui.WaitFor(CommonText.NothingWouldChange);
    await tui.WaitFor($"[ {CommonText.Close} ]");
    Assert.DoesNotContain($"[ {CommonText.Apply} ]", tui.Screen);
    Assert.DoesNotContain($"[ {CommonText.Cancel} ]", tui.Screen);
    Assert.Contains(
        $"Enter {CommonText.HintSelect.Text}  Esc {CommonText.HintClose.Text}", tui.Screen);
    tui.Snapshot("shell-plan-unchanged");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(CommonText.NothingWouldChange);

    // Nothing was carried out.
    await tui.Settle();
    Assert.Equal(["dryRun=True"], calls);
    Assert.False(done);
    Assert.True(await tui.Invoke(() => tui.Context.Idle));
    Assert.DoesNotContain("workspace", _pages[PageId.Home].Events);
  }

  [Fact]
  public async Task PlanAndRun_APlanThatChangesOnlyTheStateFile_SaysSo_AndCanBeApplied() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Language = Language.ZhHans });
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];

    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Update", Operation(calls, new OperationResult(Changes: true, OnlyStateFile: true))));
    await tui.WaitFor(CommonText.OnlyRecordChanges);
    Assert.Contains($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]", tui.Screen);
    Assert.DoesNotContain(CommonText.NothingWouldChange, tui.Screen);
    tui.Snapshot("shell-plan-record");
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("✓ " + CommonText.Done);
    Assert.Equal(["dryRun=True", "dryRun=False"], calls);
  }

  [Fact]
  public async Task PlanAndRun_AWarningOfThePlan_IsShownWithIt() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Set up", async run => {
          await tui.InteractionOf(tui.Context.Client).PrintWarning(
              "Dry run: these packages have no version for the game 1.21.0, so the setup would"
              + " uninstall them, after asking you to confirm it: Alpha 1.0.0");
          return OperationResult.Changed;
        }));

    await tui.WaitFor("[ Apply ] [ Cancel ]");
    Assert.Contains("⚠ Dry run: these packages have no version", tui.Screen);
    Assert.DoesNotContain("Nothing would change.", tui.Screen);
    tui.Snapshot("shell-plan-warning");
  }

  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  public async Task PlanAndRun_WhileTheServerRuns_CarriesNothingOut_AndSaysToStopIt(
      Language language) {
    using TuiHarness tui = Start(new TuiHarnessOptions { Language = language });
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];
    Assert.False(await tui.Invoke(() => tui.Context.ServerRunning));

    // Another lip runs the server of the selected workspace.
    await tui.SetServerRunning(true);
    Assert.True(await tui.Invoke(() => tui.Context.ServerRunning));
    await tui.WaitFor("● " + CommonText.ServerRunning);

    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: false)));
    await tui.WaitFor(CommonText.StopServerFirst);
    Assert.Contains("─ Install Alpha ─", tui.Screen);
    Assert.Contains($"[ {CommonText.Close} ]", tui.Screen);
    Assert.DoesNotContain($"[ {CommonText.Apply} ]", tui.Screen);
    tui.Snapshot("shell-server-runs");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(CommonText.StopServerFirst);

    // The helper of the pages says the same.
    Assert.True(await tui.Invoke(() => tui.Context.RefuseWhileServerRuns("Save")));
    await tui.WaitFor(CommonText.StopServerFirst);
    Assert.Contains("─ Save ─", tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.StopServerFirst);
    Assert.Empty(calls);

    // Once the server has stopped, the operation is carried out.
    await tui.SetServerRunning(false);
    await tui.WaitUntilGone("● " + CommonText.ServerRunning);
    Assert.False(await tui.Invoke(() => tui.Context.RefuseWhileServerRuns("Save")));
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: false)));
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Assert.Equal(["dryRun=True acceptEula=False"], calls);
  }

  [Fact]
  public async Task PlanAndRun_AServerStartedSinceTheInterfaceLooked_IsSeenBeforeEachRun() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    List<string> calls = [];

    // The plan is shown; then the server starts, and the plan is applied at once.
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "Install Alpha", Operation(tui, calls, setup: false)));
    await tui.WaitFor("[ Apply ] [ Cancel ]");
    tui.SetServerLockHeld(true, tui.Root);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Stop the server first.");
    Assert.DoesNotContain("Failed", tui.Screen);
    Assert.Equal(["dryRun=True acceptEula=False"], calls);
    Assert.True(await tui.Invoke(() => tui.Context.ServerRunning));
    Assert.DoesNotContain("workspace", _pages[PageId.Home].Events);
  }

  [Fact]
  public async Task ServerRunning_IsOfEachWorkspace_AndOfTheServersOfThisProcessToo() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddDirectory(other);
    tui.Workspaces.Add(other);

    // Another lip runs the server of a workspace of the list that is not selected.
    await tui.SetServerRunning(true, other);
    Assert.True(await tui.Invoke(() => tui.Context.IsServerRunning(other)));
    Assert.False(await tui.Invoke(() => tui.Context.IsServerRunning(tui.Root)));
    Assert.False(await tui.Invoke(() => tui.Context.ServerRunning));
    Assert.DoesNotContain("● Running", tui.Screen);

    // It is the selected one.
    await tui.Invoke(() => tui.Context.SwitchWorkspace(other));
    await tui.WaitUntil(() => tui.Context.ServerRunning, "a running server");
    await tui.WaitFor("● Running");
    Assert.Null(await tui.Invoke(() => tui.Context.Server));
  }

  [Fact]
  public async Task Failure_IsShownWithItsHintsAndTheReportOfTheSelectedWorkspace() {
    // Arrange: another workspace than the working directory is selected.
    string? reportedFor = null;
    using TuiHarness tui = Start(new TuiHarnessOptions {
      DescribeFailure = (exception, _, cancelled, workspace) => {
        reportedFor = workspace;
        return new Failure(
            exception.Message, ["Check the name."], Path.Join(".lip", "logs", "r.log"), null, "",
            cancelled);
      },
    });
    await tui.WaitFor("body of Alpha");
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddDirectory(other);
    Assert.True(await tui.Invoke(() => tui.Context.AddWorkspace("other", out _)));

    // Act
    await tui.Invoke(() => tui.Context.RunOperation(
        "Install", _ => throw new InvalidOperationException("No such package.")));

    // Assert
    await tui.WaitFor("Install · Failed");
    Assert.Contains("error: No such package.", tui.Screen);
    Assert.Contains("hint: Check the name.", tui.Screen);
    Assert.Contains("Full report: " + Path.Join(other, ".lip", "logs", "r.log"), tui.Screen);
    Assert.Equal(other, reportedFor);
    tui.Snapshot("shell-failure");

    // The failure does not end the interface.
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Install · Failed");
    Assert.False(tui.Run.IsCompleted);
  }

  [Fact]
  public async Task PageThatFails_ShowsTheFailure_AndTheInterfaceGoesOn() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    _pages[PageId.Home].Keys = _ => throw new InvalidOperationException("The page broke.");

    tui.Press(ConsoleKey.Enter);
    tui.Type("x");

    await tui.WaitFor("error: The page broke.");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone("The page broke.");
    Assert.False(tui.Run.IsCompleted);
  }

  [Fact]
  public async Task Workspace_AddedAndSelected_IsWhatTheClientAndThePagesWorkOn() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddDirectory(other);
    LipClient first = tui.Context.Client;

    // What is not a directory is refused.
    string? error = null;
    Assert.False(await tui.Invoke(() => tui.Context.AddWorkspace("missing", out error)));
    Assert.Equal("Not a folder: " + Path.Join(tui.Root, "missing"), error);

    // A relative path is of the selected workspace.
    bool switched = false;
    Assert.True(await tui.Invoke(
        () => tui.Context.AddWorkspace("other", out _, () => switched = true)));

    await tui.WaitFor("lip 1.2.3 · " + other);
    Assert.True(switched);
    Assert.Equal(other, tui.Context.Workspace);
    Assert.NotSame(first, tui.Context.Client);
    Assert.Equal([tui.Root, other], tui.Workspaces.Read());
    Assert.Equal([tui.Root, other], tui.ClientWorkspaces);
    Assert.Contains("workspace", _pages[PageId.Mods].Events);
    Assert.Contains("Workspace: " + other, tui.Screen);

    // What was read for the workspace selected before is dropped.
    TaskCompletionSource<int> read = new();
    bool stale = false;
    await tui.Invoke(() => tui.Context.Background(() => read.Task, _ => stale = true));
    await tui.Invoke(() => tui.Context.SwitchWorkspace(tui.Root));
    read.SetResult(1);
    await tui.WaitFor(screen => screen.Split((char)10)[0].EndsWith("· " + tui.Root));
    await tui.Settle();
    Assert.False(stale);
  }

  [Fact]
  public async Task Background_ForAnyWorkspace_CallsBack_AlsoAfterAnotherWorkspaceWasSelected() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");
    string other = Path.Join(tui.Root, "other");
    tui.Fs.AddDirectory(other);
    TaskCompletionSource<int> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<int> failing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int? result = null;
    string? failure = null;
    bool onInterfaceThread = false;
    int interfaceThread = await tui.Invoke(() => Environment.CurrentManagedThreadId);

    // Work that is of no workspace, such as reading the workspace list or the catalogue.
    await tui.Invoke(() => {
      tui.Context.Background(
          () => read.Task,
          value => {
            result = value;
            onInterfaceThread = Environment.CurrentManagedThreadId == interfaceThread;
          },
          anyWorkspace: true);
      tui.Context.Background(
          () => failing.Task, _ => {
          },
          exception => failure = exception.Message, anyWorkspace: true);
    });
    Assert.True(await tui.Invoke(() => tui.Context.AddWorkspace(other, out _)));
    await tui.WaitFor("lip 1.2.3 · " + other);
    read.SetResult(7);
    failing.SetException(new InvalidOperationException("no list"));

    await tui.WaitFor(_ => result == 7 && failure == "no list");
    Assert.True(onInterfaceThread);
  }

  [Theory]
  [InlineData(Language.En, "↑↓ Move  Enter Open  Esc Quit", "Quit lip?", "[ Quit ] [ Cancel ]")]
  [InlineData(Language.ZhHans, "↑↓ 移动  Enter 打开  Esc 退出", "要退出 lip 吗？", "[ 退出 ] [ 取消 ]")]
  [InlineData(Language.ZhHant, "↑↓ 移動  Enter 開啟  Esc 結束", "要結束 lip 嗎？", "[ 結束 ] [ 取消 ]")]
  public async Task Language_OfTheClient_IsThatOfTheShell(
      Language language, string hints, string question, string buttons) {
    using TuiHarness tui = Start(new TuiHarnessOptions { Language = language });
    await tui.WaitFor("body of Alpha");

    Assert.Contains(hints, tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(question);
    Assert.Contains(buttons, tui.Screen);
    Assert.Equal(question, CommonText.QuitQuestion);
    tui.Snapshot("shell-quit");

    tui.Press(ConsoleKey.Enter);
    Assert.Equal(0, await tui.Run.WaitAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Language_Configured_WinsOverThatOfTheClient_AndWithoutEitherIsEnglish() {
    // Arrange
    System.IO.Abstractions.TestingHelpers.MockFileSystem fs = new(
        new Dictionary<string, System.IO.Abstractions.TestingHelpers.MockFileData>(),
        TuiHarness.DefaultRoot);
    await new ConfigService(fs, Moq.Mock.Of<IUserInteraction>())
        .Set(ConfigService.LanguageKey, "zh-Hant");

    // Act
    using TuiHarness configured = Start(
        new TuiHarnessOptions { Language = Language.ZhHans, FileSystem = fs });
    await configured.WaitFor("↑↓ 移動");
    Assert.Equal(Language.ZhHant, configured.Context.Language);
    Assert.Equal(Language.ZhHant, configured.Context.LanguageSetting);
    Assert.Equal(Language.ZhHans, configured.Context.ClientLanguage);
    configured.Dispose();

    using TuiHarness neither = Start(new TuiHarnessOptions { Language = null });
    await neither.WaitFor("↑↓ Move");

    // Assert
    Assert.Equal(Language.En, neither.Context.Language);
    Assert.Null(neither.Context.LanguageSetting);
  }

  [Fact]
  public async Task Language_ChangedWhileTheInterfaceRuns_IsShownAtOnceAndConfigured() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Language = Language.ZhHans });
    await tui.WaitFor("↑↓ 移动  Enter 打开  Esc 退出");

    // Traditional Chinese, which is written to the configuration.
    await tui.Invoke(() => tui.Context.SetLanguage(Language.ZhHant));
    await tui.WaitFor("↑↓ 移動  Enter 開啟  Esc 結束");
    for (int i = 0; i < 200 && await tui.Config.Get(ConfigService.LanguageKey) != "zh-Hant"; i++) {
      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Equal("zh-Hant", await tui.Config.Get(ConfigService.LanguageKey));

    // English.
    await tui.Invoke(() => tui.Context.SetLanguage(Language.En));
    await tui.WaitFor("↑↓ Move  Enter Open  Esc Quit");

    // Following the client again removes the key.
    await tui.Invoke(() => tui.Context.SetLanguage(null));
    await tui.WaitFor("↑↓ 移动  Enter 打开  Esc 退出");
    for (int i = 0; i < 200 && await tui.Config.Get(ConfigService.LanguageKey) is not null; i++) {
      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Null(await tui.Config.Get(ConfigService.LanguageKey));
    Assert.Null(tui.Context.LanguageSetting);
  }

  [Fact]
  public async Task TerminalTooSmall_SaysSo_AndTakesNoKeyUntilItIsLargeEnough() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Width = 59, Height = 16 });

    await tui.WaitFor("Enlarge the terminal to 60×16. Ctrl+C quits.");
    Assert.DoesNotContain("Alpha", tui.Screen);
    tui.Press(ConsoleKey.DownArrow);
    await tui.Settle();

    tui.Resize(60, 16);
    await tui.WaitFor("─ Alpha ─");
    Assert.Equal(16, tui.Screen.Split('\n').Length);
  }

  [Fact]
  public async Task TerminalTooSmall_CtrlCQuitsWithoutAsking() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Width = 40, Height = 10 });
    await tui.WaitFor("Enlarge the terminal");

    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.Enter);
    await tui.Settle();
    Assert.False(tui.Run.IsCompleted);
    tui.Interrupt();

    Assert.Equal(0, await tui.Run.WaitAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task DialogThatCannotBeDrawn_IsClosed_AndOnlyWebUrlsAreOpened() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    // A dialog that cannot be drawn is closed, and the interface goes on.
    await tui.Invoke(() => tui.Context.Show(new BrokenDialog()));
    await tui.WaitFor("The dialog broke.");
    Assert.True(await tui.Invoke(() => tui.Context.Idle));

    // A URL that is not one of the web is not opened.
    await tui.Invoke(() => tui.Context.OpenUrl("file:///C:/x.bat"));
    await tui.Invoke(() => tui.Context.OpenUrl("https://levimc.org/mods/"));
    Assert.Equal(["https://levimc.org/mods/"], tui.OpenedUrls);
    Assert.False(tui.Run.IsCompleted);
  }

  private sealed class BrokenDialog : Dialog {
    public override IReadOnlyList<Hint> Hints => [];

    public override void Draw(ScreenBuffer screen) =>
        throw new InvalidOperationException("The dialog broke.");

    public override void OnKey(ConsoleKeyInfo key) {
    }
  }

  [Fact]
  public async Task NarrowTerminal_ShowsIconsInTheSidebar_AndTheNamesWhileItHasTheFocus() {
    using TuiHarness tui = Start(new TuiHarnessOptions { Width = 60, Height = 16 });
    await tui.WaitFor("─ Alpha ─");

    // The sidebar has the focus: the names, beside a page with less room.
    Assert.Contains("│ Alpha", tui.Screen);
    Assert.Contains("│ Gamma", tui.Screen);
    tui.Snapshot("shell-narrow-sidebar");

    // The page has the focus: icons only.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("body of Alpha [focused]");
    Assert.Contains("┌───┐┌─ Alpha ─", tui.Screen);
    Assert.Contains("│ a ││ body of Alpha [focused]", tui.Screen);
    Assert.Contains("│ c ││", tui.Screen);
    Assert.DoesNotContain("Gamma", tui.Screen);
    tui.Snapshot("shell-narrow-page");
    Assert.All(tui.Screen.Split('\n'), row => Assert.True(TextWidth.Of(row) <= 60));
  }

  /// The frames of the shell with the pages of lip, in every language, on a large and on the
  /// smallest terminal: no row is wider than the terminal, and the boxes line up.
  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Frames_WithThePagesOfLip_FitTheTerminalInEveryLanguage(
      Language language, int width, int height) {
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, Width = width, Height = height });
    void Check(string name) {
      tui.Snapshot(name);
      string[] rows = tui.Screen.Split('\n');
      Assert.Equal(height, rows.Length);
      Assert.All(rows, row => Assert.True(TextWidth.Of(row) <= width, row));
      // The boxes of the sidebar and of the content area end in the same columns on every row.
      Assert.Single(rows[1..^1].Select(row => TextWidth.Of(row)).Distinct());
    }

    await tui.WaitFor(CommonText.HintMove.Text);
    await tui.Settle();
    Check("frame-start");

    await tui.GoTo(PageId.Settings);
    Check("frame-page");

    await tui.Invoke(() => tui.Context.PlanAndRun("lip install github.com/acme/Alpha", async _ => {
      await tui.InteractionOf(tui.Context.Client).PrintInfo(
          "Dry run: would install github.com/acme/Alpha 1.2.0 (12 files)");
      return OperationResult.Changed;
    }));
    await tui.WaitFor($"[ {CommonText.Apply} ] [ {CommonText.Cancel} ]");
    Check("frame-plan");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"[ {CommonText.Apply} ]");

    // A plan that would change nothing, and one of the state file alone.
    await tui.Invoke(() => tui.Context.PlanAndRun("lip update", async _ => {
      await tui.InteractionOf(tui.Context.Client).PrintInfo("Resolving github.com/acme/Alpha...");
      return OperationResult.Unchanged;
    }));
    await tui.WaitFor(CommonText.NothingWouldChange);
    Assert.DoesNotContain($"[ {CommonText.Apply} ]", tui.Screen);
    Check("frame-plan-unchanged");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.NothingWouldChange);

    await tui.Invoke(() => tui.Context.PlanAndRun("lip update", async _ => {
      await tui.InteractionOf(tui.Context.Client).PrintWarning(
          "Dry run: these packages have no version for the game 1.21.0, so the setup would"
          + " uninstall them, after asking you to confirm it: Alpha 1.0.0");
      return new OperationResult(Changes: true, OnlyStateFile: true);
    }));
    await tui.WaitFor(CommonText.OnlyRecordChanges);
    Check("frame-plan-record");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.OnlyRecordChanges);

    // A menu whose actions say why they cannot be chosen.
    await tui.Invoke(() => tui.Context.ShowMenu(
        "github.com/acme/Alpha",
        new MenuItem("README", () => { }),
        new MenuItem("Update", () => { }, Enabled: false, CommonText.StopServerFirst),
        new MenuItem("Uninstall", () => { }, Enabled: false, CommonText.StopServerFirst)));
    await tui.WaitFor("Uninstall");
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(CommonText.StopServerFirst);
    Check("frame-menu-reason");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("Uninstall");

    // The server of the workspace runs: nothing is carried out.
    await tui.SetServerRunning(true);
    await tui.Invoke(() => tui.Context.PlanAndRun(
        "lip install github.com/acme/Alpha", _ => Task.FromResult(OperationResult.Changed)));
    await tui.WaitFor(CommonText.StopServerFirst);
    Check("frame-server-runs");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.StopServerFirst);
    await tui.SetServerRunning(false);

    await tui.Invoke(() => tui.Context.Show(new PromptDialog(
        "GitHub", [new PromptField("URL", "", "https://example.com/")], _ => { })));
    await tui.WaitFor("https://example.com/");
    Check("frame-prompt");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone("https://example.com/");

    await tui.Invoke(() => tui.Context.RunOperation(
        "lip install", _ => throw new InvalidOperationException(
            "Cannot find versions of the packages that work together.")));
    await tui.WaitFor(CommonText.FullReport);
    Check("frame-failure");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.FullReport);

    tui.Interrupt();
    await tui.WaitFor(CommonText.QuitQuestion);
    Check("frame-quit");
  }

  [Fact]
  public async Task Toast_IsShownInTheBottomBar_AndAMessageOfACommandOutsideAnOperationToo() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    await tui.Invoke(() => tui.Context.Toast("Saved.", ToastKind.Good));
    await tui.WaitFor("Saved.");
    Assert.EndsWith("Saved.", tui.Screen.Split('\n')[^1]);

    await tui.InteractionOf(tui.Context.Client).PrintWarning("Careful.");
    await tui.WaitFor("⚠ Careful.");
  }

  [Fact]
  public async Task Server_NotManaged_IsStoppedBeforeTheInterfaceQuits() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
      });
      await tui.WaitFor("body of Alpha");
      await tui.Invoke(() => tui.Context.StartServer("Start server"));
      await tui.WaitFor("● Running");
      WorkspaceServer server = tui.Servers.Find(tui.Root)!;

      // Quitting says that the server stops first, and asks it to: it reads the line "stop",
      // which cmd.exe does not know, and then the line that makes it exit.
      tui.Press(ConsoleKey.Escape);
      await tui.WaitFor("Running servers stop first.");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("● Stopping");
      Assert.Contains("body of Beta [focused]", tui.Screen);
      Assert.False(tui.Run.IsCompleted);
      server.Send("exit 7");

      Assert.Equal(0, await tui.Run.WaitAsync(TestContext.Current.CancellationToken));
      Assert.False(server.Running);
    }
    finally {
      TestClients.Remove(root);
    }
  }

  [Fact]
  public async Task Server_Managed_KeepsRunningWhenTheInterfaceQuits() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        ManagedServers = true,
        Servers = servers,
        Pages = [new PageEntry(PageId.Home, "a", context => new TestPage(context, "Alpha"))],
      });
      await tui.WaitFor("body of Alpha");
      await tui.Invoke(() => tui.Context.StartServer("Start server"));
      await tui.WaitFor("● Running");

      // Act: quitting says that the server keeps running, and stops nothing.
      tui.Press(ConsoleKey.Escape);
      await tui.WaitFor("Running servers keep running.");
      tui.Press(ConsoleKey.Enter);
      Assert.Equal(0, await tui.Run.WaitAsync(TestContext.Current.CancellationToken));

      // Assert
      WorkspaceServer server = servers.Find(tui.Root)!;
      Assert.True(server.Running);
      server.Send("exit 4");
      for (int i = 0; i < 400 && server.Running; i++) {
        await Task.Delay(25, TestContext.Current.CancellationToken);
      }

      Assert.False(server.Running);
      Assert.Equal(4, server.ExitCode);
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Fact]
  public async Task Server_ThatFailsBeforeItStarts_ShowsTheFailure() {
    using TuiHarness tui = Start();
    await tui.WaitFor("body of Alpha");

    await tui.Invoke(() => tui.Context.StartServer("Start server"));

    await tui.WaitFor("Start server · Failed");
    Assert.Contains("There is no server to run", tui.Screen);
  }
}
