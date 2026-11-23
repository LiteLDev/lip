using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;

namespace Lip.Cli.Tests.Tui;

/// The Server page of the interface, driven by the keys of a fake terminal.
public sealed class ServerPageTests {
  [Theory]
  [InlineData(Language.En, "Server")]
  [InlineData(Language.ZhHans, "服务器")]
  [InlineData(Language.ZhHant, "伺服器")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Server);

    Assert.Equal(title, tui.Text(() => ServerText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36, "No server here yet.", "[ Set up server… ]")]
  [InlineData(Language.ZhHans, 120, 36, "这里还没有服务器。", "[ 安装服务器… ]")]
  [InlineData(Language.ZhHant, 120, 36, "這裡還沒有伺服器。", "[ 安裝伺服器… ]")]
  [InlineData(Language.En, 60, 16, "No server here yet.", "[ Set up server… ]")]
  [InlineData(Language.ZhHans, 60, 16, "这里还没有服务器。", "[ 安装服务器… ]")]
  [InlineData(Language.ZhHant, 60, 16, "這裡還沒有伺服器。", "[ 安裝伺服器… ]")]
  public async Task WorkspaceWithoutAServer_LeadsToItsSetup(
      Language language, int width, int height, string line, string row) {
    using TuiHarness tui = TuiHarness.Start(
        new TuiHarnessOptions { Language = language, Width = width, Height = height });
    await tui.GoTo(PageId.Server);

    await tui.WaitFor(line);
    Assert.Contains(row, tui.Screen);
    Assert.DoesNotContain($"[ {ServerText.Start} ]", tui.Screen);
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", LastRow(tui));
    tui.Snapshot("server-none");

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor($"─ {tui.Text(() => SetupText.GameVersion)} ─");
    Assert.Equal(PageId.Server, await tui.Invoke(() => tui.Context.CurrentPage));
  }

  [Fact]
  public async Task Start_ThatFailsBeforeTheServerStarts_ShowsTheFailure() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = WithGame() });
    await tui.GoTo(PageId.Server);
    await tui.WaitFor("[ Start ]");
    Assert.Contains("○ Stopped", tui.Screen);
    Assert.Contains("No output yet.", tui.Screen);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor("Start server · Failed");
    Assert.Contains("There is no server to run", tui.Screen);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("[ Start ]");
    Assert.Contains("lip run failed", tui.Screen);
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 120, 36)]
  public async Task AutoRestart_IsSwitchedWithSpace_AndWrittenToTheWorkspace(
      Language language, int width, int height) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      FileSystem = WithGame(),
      Language = language,
      Width = width,
      Height = height,
    });
    string config = Path.Join(tui.Root, ".lip", "config.toml");
    await tui.GoTo(PageId.Server);
    await tui.WaitFor($"[ ] {CommonText.AutoRestart}");

    // Act: → chooses the toggle, which then says what it does; Space switches it.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(ServerText.AutoRestartDetail);
    Assert.Contains($"Space {CommonText.HintToggle.Text}", LastRow(tui));
    tui.Press(ConsoleKey.Spacebar);

    await tui.WaitFor($"[x] {CommonText.AutoRestart}");
    await tui.WaitFor(
        _ => tui.Fs.FileExists(config) && tui.Fs.File.ReadAllText(config).Contains("on-failure"),
        "restart = on-failure");
    tui.Snapshot("server-auto-restart");

    // Leaving and coming back reads it again; Space switches it off.
    await tui.GoTo(PageId.Home);
    await tui.GoTo(PageId.Server);
    await tui.WaitFor($"[x] {CommonText.AutoRestart}");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor($"[ ] {CommonText.AutoRestart}");
    await tui.WaitFor(
        _ => !tui.Fs.File.ReadAllText(config).Contains("on-failure"), "restart = never");

    // ← goes back to the button.
    tui.Press(ConsoleKey.LeftArrow);
    await tui.Settle();
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", LastRow(tui));
    Assert.True(tui.Context.ContentFocused);
  }

  [Fact]
  public async Task Output_ScrollsWhileItHasTheFocus_AndFollowsItsEndOtherwise() {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { FileSystem = WithGame() });
    WorkspaceServer server = tui.Servers.For(tui.Root);
    lock (server.Gate) {
      for (int i = 1; i <= 80; i++) {
        server.Log.AddLine(new StyledLine($"line {i}."));
      }
    }

    await tui.GoTo(PageId.Server);
    await tui.WaitFor("line 80.");
    Assert.DoesNotContain("line 1.", tui.Screen);

    // ↓ from the button goes to the output, where ↑ scrolls back one row.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitUntilGone("line 80.");
    Assert.Contains("line 79.", tui.Screen);
    Assert.Contains($"↑↓ {CommonText.HintScroll.Text}", LastRow(tui));
    // The bottom bar names every key that scrolls the output.
    Assert.Contains($"PgUp/PgDn {CommonText.HintPage.Text}", LastRow(tui));
    Assert.Contains($"Home/End {CommonText.HintEnds.Text}", LastRow(tui));

    // What the server writes meanwhile does not move the rows scrolled back to.
    lock (server.Gate) {
      server.Log.AddLine(new StyledLine("line 81."));
      server.Log.AddLine(new StyledLine("line 82."));
    }

    await Task.Delay(600, TestContext.Current.CancellationToken);
    await tui.Settle();
    Assert.Contains("line 79.", tui.Screen);
    Assert.DoesNotContain("line 80.", tui.Screen);
    Assert.DoesNotContain("line 82.", tui.Screen);
    tui.Press(ConsoleKey.PageUp);
    await tui.WaitUntilGone("line 79.");
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor("line 1.");
    tui.Snapshot("server-scrolled");
    tui.Press(ConsoleKey.PageDown);
    await tui.WaitUntilGone("line 1.");
    tui.Press(ConsoleKey.End);
    await tui.WaitFor("line 80.");

    // At the first row ↑ leaves the output, which shows its end again.
    tui.Press(ConsoleKey.Home);
    await tui.WaitFor("line 1.");
    tui.Press(ConsoleKey.UpArrow);
    await tui.WaitFor("line 80.");
    Assert.Contains($"Enter {CommonText.HintSelect.Text}", LastRow(tui));

    // Tab goes to the output too, and from it, the last area, back to the sidebar.
    tui.Press(ConsoleKey.Tab);
    await tui.WaitFor(_ => LastRow(tui).Contains($"↑↓ {CommonText.HintScroll.Text}"), "the output");
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Server_IsStarted_ReadsTheLinesEntered_AndIsStoppedWithCtrlC(
      Language language, int width, int height) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Servers = servers,
        Language = language,
        Width = width,
        Height = height,
      });
      await tui.GoTo(PageId.Server);
      await tui.WaitFor($"[ {ServerText.Start} ]");
      Assert.Contains("○ " + CommonText.ServerStopped, tui.Screen);
      tui.Snapshot("server-stopped");

      // Act: Enter presses Start; the input line then has the focus.
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.WaitFor($"● {CommonText.ServerRunning} · 00:00:0");
      await tui.WaitFor(_ => LastRow(tui).Contains($"Enter {ServerText.HintSend.Text}"), "Send");
      Assert.Contains($"Ctrl+C {CommonText.HintStop.Text}", LastRow(tui));
      if (width >= 80) {
        // Esc leaves the input line for the output, and the output for the sidebar; Enter
        // comes back to the output, and ↓ at its end goes on to the input line.
        tui.Press(ConsoleKey.Escape);
        await tui.WaitUntilGone($"Enter {ServerText.HintSend.Text}");
        Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
        Assert.Contains($"[ {ServerText.Stop} ]", tui.Screen);
        tui.Press(ConsoleKey.Escape);
        await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
        Assert.Matches(Regex.Escape(ServerText.Title) + @"\s+●", tui.Screen);
        tui.Press(ConsoleKey.Enter);
        tui.Press(ConsoleKey.DownArrow);
        await tui.WaitFor(_ => LastRow(tui).Contains($"Enter {ServerText.HintSend.Text}"), "Send");
      }

      // The server reads the line entered, and ↑ brings it back.
      tui.Type("echo marco polo");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(
          screen => Regex.IsMatch(screen, @"(?m)│ marco polo\s*│$"), "the echo");
      if (height >= 36) {
        // On a small terminal the prompt of cmd.exe has pushed the line entered out of sight.
        Assert.Contains("> echo marco polo", tui.Screen);
      }

      Assert.Contains($"↑↓ {ServerText.HintHistory.Text}", LastRow(tui));
      tui.Press(ConsoleKey.UpArrow);
      tui.Type("!");
      await tui.WaitFor("> echo marco polo!");
      tui.Snapshot("server-running");
      tui.Press(ConsoleKey.DownArrow);
      await tui.WaitUntilGone("> echo marco polo!");

      // Ctrl+C asks lip to stop for the server; cmd.exe goes on until it reads "exit".
      tui.Interrupt();
      await tui.WaitFor("● " + CommonText.ServerStopping);
      Assert.DoesNotContain($"Ctrl+C {CommonText.HintStop.Text}", LastRow(tui));
      tui.Snapshot("server-stopping");
      tui.Type("exit 7");
      tui.Press(ConsoleKey.Enter);

      // Assert: the interface goes on, and says how the server ended.
      await tui.WaitFor("○ " + ServerText.ExitedWith(7));
      await tui.WaitFor($"[ {ServerText.Start} ]");
      Assert.Contains($"Enter {CommonText.HintSelect.Text}", LastRow(tui));
      tui.Snapshot("server-exited");
      Assert.False(tui.Run.IsCompleted);
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Fact]
  public async Task Server_ThatAnotherPageStarts_IsShown_AndStoppedWithTheButton() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Servers = servers,
      });
      await tui.Settle();

      // Act: what the Home page does to start the server.
      await tui.Invoke(
          () => tui.Context.Navigate(PageId.Server, PageRequest.StartServer));

      await tui.WaitFor("[ Stop ]");
      await tui.WaitFor("── starting the server ──");
      tui.Type("echo from home");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("> echo from home");

      // Shift+Tab goes from the input line to the output, and on to the button.
      tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
      await tui.WaitFor(_ => LastRow(tui).Contains("↑↓ Move  Tab Next"), "the output");
      tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
      await tui.WaitFor(_ => LastRow(tui).Contains("Enter Select"), "the button");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("● Stopping");
      await tui.WaitFor("── asking the server to stop ──");

      // ↓ twice goes back to the input line.
      tui.Press(ConsoleKey.DownArrow, 2);
      await tui.WaitFor(_ => LastRow(tui).Contains("Enter Send"), "the input line");

      // ↑ shows the line entered before, and ↓ the line being typed again.
      tui.Type("ex");
      tui.Press(ConsoleKey.UpArrow);
      await tui.WaitFor(screen => screen.Contains("> echo from home    "), "the earlier line");
      tui.Press(ConsoleKey.DownArrow);
      tui.Type("it 0");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("○ Exited with code 0");
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 120, 36)]
  [InlineData(Language.ZhHant, 120, 36)]
  [InlineData(Language.En, 60, 16)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task InputLine_IsLeftWithEsc_AndTabGoesRoundTheSidebarAndEveryArea(
      Language language, int width, int height) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Servers = servers,
        Language = language,
        Width = width,
        Height = height,
      });
      string input = Bar(ServerText.HintSend);
      // The output scrolls on a small terminal, and does not on a large one.
      string output = Bar(
          height < 36 ? CommonText.HintScroll : CommonText.HintMove, CommonText.HintNext,
          CommonText.HintStop);
      string button = Bar(CommonText.HintSwitch, CommonText.HintSelect, CommonText.HintNext);
      await tui.GoTo(PageId.Server);
      await tui.WaitFor($"[ {ServerText.Start} ]");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.WaitFor(_ => LastRow(tui).Contains(input), "the input line");
      tui.Type("echo one");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor(screen => Regex.IsMatch(screen, @"(?m)│ one\s*│$"), "the echo");

      // ↑ and ↓ on the input line are its history, and nothing else.
      tui.Press(ConsoleKey.UpArrow, 3);
      tui.Press(ConsoleKey.DownArrow, 3);
      await tui.Settle();
      Assert.Contains(
          " " + Bar(
              ServerText.HintSend, ServerText.HintHistory, CommonText.HintNext,
              CommonText.HintStop, CommonText.HintBack),
          LastRow(tui));
      tui.Snapshot("server-focus-input");

      // Esc leaves the input line for the output, where Enter does nothing; from there Esc
      // goes back to the sidebar.
      tui.Press(ConsoleKey.Escape);
      await tui.WaitFor(_ => LastRow(tui).Contains(output), "the output");
      Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
      tui.Snapshot("server-focus-output");
      tui.Press(ConsoleKey.Enter);
      await tui.Settle();
      Assert.Contains($"[ {ServerText.Stop} ]", tui.Screen);
      Assert.Contains("● " + CommonText.ServerRunning, tui.Screen);
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");

      // Tab goes round: the button and the toggle, the output, the input line, the sidebar.
      tui.Press(ConsoleKey.Tab);
      await tui.WaitFor(_ => LastRow(tui).Contains(button), "the button");
      tui.Snapshot("server-focus-button");
      tui.Press(ConsoleKey.Tab);
      await tui.WaitFor(_ => LastRow(tui).Contains(output), "the output");
      tui.Press(ConsoleKey.Tab);
      await tui.WaitFor(_ => LastRow(tui).Contains(input), "the input line");
      tui.Press(ConsoleKey.Tab);
      await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");

      // Shift+Tab goes round the other way, to the input line first.
      tui.Press(ConsoleKey.Tab, modifiers: ConsoleModifiers.Shift);
      await tui.WaitFor(_ => LastRow(tui).Contains(input), "the input line");
      tui.Type("exit 0");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("○ " + ServerText.ExitedWith(0));
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Theory]
  [InlineData(Language.En, 120, 36)]
  [InlineData(Language.ZhHans, 60, 16)]
  [InlineData(Language.ZhHant, 60, 16)]
  public async Task Server_ThatAnotherLipRuns_IsShownAsRunningElsewhere_AndIsNotStartedHere(
      Language language, int width, int height) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
      FileSystem = WithGame(),
      Language = language,
      Width = width,
      Height = height,
    });
    await tui.GoTo(PageId.Server);
    await tui.WaitFor("○ " + CommonText.ServerStopped);
    await tui.WaitFor($"[ ] {CommonText.AutoRestart}");

    // Another lip run starts the server: it holds the server lock of the workspace.
    await tui.SetServerRunning(true);

    // The page says that it runs, and where it is stopped, and offers neither its output nor
    // an input line; the top bar agrees.
    await tui.WaitFor("● " + ServerText.RunningElsewhere);
    tui.Snapshot("server-elsewhere");
    Assert.Contains(ServerText.RunningElsewhereDetail, tui.Screen);
    Assert.Contains($"[ {ServerText.Start} ]", tui.Screen);
    Assert.DoesNotContain(ServerText.NoOutput, tui.Screen);
    Assert.DoesNotContain($" {ServerText.Output} ", tui.Screen);
    Assert.DoesNotContain("> ", tui.Screen);
    Assert.DoesNotContain(CommonText.ServerStopped, tui.Screen);
    Assert.EndsWith("● " + CommonText.ServerRunning, tui.Screen.Split('\n')[0]);

    // The button is disabled: the bar names no Enter, and Enter starts nothing. It looks
    // disabled though it has the focus: muted, and never as a button that can be pressed.
    Assert.Equal(" " + Bar(CommonText.HintSwitch, CommonText.HintBack), LastRow(tui));
    Style start = await tui.StyleOf($"[ {ServerText.Start} ]");
    Assert.Equal(Theme.Muted with { Underline = true }, start);
    Assert.NotEqual(Theme.Selected.Bg, start.Bg);
    Assert.NotEqual(Theme.SelectedInactive.Bg, start.Bg);
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Spacebar);
    await tui.Settle();
    Assert.DoesNotContain(CommonText.Failed, tui.Screen);
    Assert.DoesNotContain(tui.Text(() => ServerText.StartTitle), tui.Screen);
    Assert.Null(tui.Servers.Find(tui.Root));

    // Asking to start it from another page, as the Home page does, starts nothing either.
    await tui.Invoke(() => tui.Context.Navigate(PageId.Server, PageRequest.StartServer));
    await tui.Settle();
    Assert.DoesNotContain(CommonText.Failed, tui.Screen);
    Assert.Null(tui.Servers.Find(tui.Root));

    // The toggle still works, and says so; ↓ and Tab have no output to go to: Tab leaves.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(_ => LastRow(tui).Contains(
        Bar(CommonText.HintSwitch, CommonText.HintToggleEither, CommonText.HintBack)),
        "the toggle");
    Assert.Equal(Theme.Muted, await tui.StyleOf($"[ {ServerText.Start} ]"));
    tui.Press(ConsoleKey.DownArrow);
    await tui.Settle();
    Assert.True(await tui.Invoke(() => tui.Context.ContentFocused));
    tui.Press(ConsoleKey.Tab);
    await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");

    // The sidebar marks the server as running, once it is drawn with the focus.
    await tui.WaitFor(
        screen => Regex.IsMatch(screen, $@"{Regex.Escape(ServerText.Title)}\s+●"),
        "the mark of the sidebar");

    // Once the other lip has stopped it, it can be started here again.
    await tui.SetServerRunning(false);
    await tui.WaitFor("○ " + CommonText.ServerStopped);
    await tui.WaitFor(ServerText.NoOutput);
    Assert.DoesNotContain(ServerText.RunningElsewhere, tui.Screen);
  }

  /// A system preparer that waits until it is released, or fails.
  private sealed class HeldPreparer : Lip.Core.Infrastructure.ISystemPreparer {
    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Asked { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task Prepare(
        string platform, string workspaceRoot, CancellationToken cancellationToken) {
      Asked.TrySetResult();
      await Release.Task;
    }
  }

  /// From Start until lip run has started the server, while it prepares the system
  /// (protocol/lip-run.md, step 6), the server is starting, not running: the page, its mark in
  /// the sidebar and the top bar say so.
  [Theory]
  [InlineData(Language.En)]
  [InlineData(Language.ZhHans)]
  [InlineData(Language.ZhHant)]
  public async Task Start_SaysStarting_UntilTheServerProcessRuns(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    HeldPreparer preparer = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        Language = language,
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root, preparer),
        Servers = servers,
      });
      await tui.GoTo(PageId.Server);
      await tui.WaitFor($"[ {ServerText.Start} ]");

      // Act: Start, with the system still being prepared.
      tui.Press(ConsoleKey.Enter);
      await preparer.Asked.Task.WaitAsync(
          TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

      // Assert: starting, in the page and in the top bar; nothing says that it runs.
      await tui.WaitFor("● " + CommonText.ServerStarting);
      Assert.EndsWith("● " + CommonText.ServerStarting, tui.Screen.Split('\n')[0]);
      Assert.DoesNotContain("● " + CommonText.ServerRunning, tui.Screen);
      Assert.Equal(Theme.Warning, await tui.StyleOf("● " + CommonText.ServerStarting));
      Assert.Contains($"[ {ServerText.Stop} ]", tui.Screen);
      WorkspaceServer server = servers.Find(tui.Root)!;
      Assert.True(server is { Running: true, Starting: true });
      tui.Snapshot("server-starting");

      // The process runs: the page says Running, with the time since then.
      preparer.Release.SetResult();
      await tui.WaitFor("● " + CommonText.ServerRunning + " · 00:00:0");
      Assert.DoesNotContain(CommonText.ServerStarting, tui.Screen);
      Assert.False(server.Starting);

      tui.Type("exit 0");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("○ " + ServerText.ExitedWith(0));
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  /// A failure before the server starts is shown as a failure, and the server is not left
  /// starting.
  [Fact]
  public async Task Start_ThatFailsWhileStarting_IsShownAsAFailure_AndIsNotStartingAnyMore() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    HeldPreparer preparer = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root, preparer),
        Servers = servers,
      });
      await tui.GoTo(PageId.Server);
      await tui.WaitFor("[ Start ]");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("● Starting");

      preparer.Release.SetException(
          new InvalidOperationException("The Visual C++ runtime cannot be installed."));

      await tui.WaitFor("Start server · Failed");
      Assert.Contains("The Visual C++ runtime cannot be installed.", tui.Screen);
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("○ Stopped");
      Assert.DoesNotContain("Starting", tui.Screen.Split('\n')[0]);
      Assert.True(servers.Find(tui.Root) is { Running: false, Starting: false });
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Fact]
  public async Task Server_OfThisInterface_WhichHoldsTheLockToo_IsNotTakenForOneOfAnotherLip() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
        Servers = servers,
      });
      await tui.GoTo(PageId.Server);
      await tui.WaitFor($"[ {ServerText.Start} ]");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {ServerText.Stop} ]");

      // The server lock of the workspace is held, as lip run holds it for the server of this
      // interface too. The interface looks at it about once a second: more than once here.
      tui.SetServerLockHeld(true, tui.Root);
      await Task.Delay(2500, TestContext.Current.CancellationToken);
      Assert.True(await tui.Invoke(() => tui.Context.ServerRunning));
      Assert.False(await tui.Invoke(() => tui.Context.ServerRunsElsewhere));
      Assert.DoesNotContain(ServerText.RunningElsewhere, tui.Screen);
      Assert.Contains($"[ {ServerText.Stop} ]", tui.Screen);

      // It exits, and lets go of the lock: the page says how it ended, with its output.
      tui.SetServerLockHeld(false, tui.Root);
      tui.Type("exit 3");
      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor("○ " + ServerText.ExitedWith(3));
      Assert.DoesNotContain(ServerText.RunningElsewhere, tui.Screen);
      Assert.Contains("> exit 3", tui.Screen);
      Assert.False(await tui.Invoke(() => tui.Context.ServerRunsElsewhere));
    }
    finally {
      TestClients.Remove(root, servers);
    }
  }

  [Fact]
  public async Task Harness_DisposedWhileItsServerRuns_EndsIt_AndItsWorkspaceIsRemoved() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      using (TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions {
        CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root),
      })) {
        await tui.GoTo(PageId.Server);
        await tui.WaitFor($"[ {ServerText.Start} ]");
        tui.Press(ConsoleKey.Enter);
        await tui.WaitFor($"[ {ServerText.Stop} ]");

        // The test ends here, as one that failed does: its server still runs.
      }
    }
    finally {
      TestClients.Remove(root);
    }

    Assert.False(Directory.Exists(root));
  }

  [Fact]
  public void Remove_OfADirectoryThatCmdWorkspaceDidNotMake_DeletesNothing() {
    string other = Directory.CreateTempSubdirectory("lip-tui-other-").FullName;
    try {
      Assert.Throws<ArgumentException>(() => TestClients.Remove(other));
      Assert.True(Directory.Exists(other));
    }
    finally {
      Directory.Delete(other, recursive: true);
    }
  }

  /// The hints as the bottom bar shows them, one after the other.
  private static string Bar(params Hint[] hints) =>
      string.Join("  ", hints.Select(hint => $"{hint.Key} {hint.Text}"));

  /// A file system whose default workspace has a game, and so a server, which is no file.
  private static MockFileSystem WithGame() {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(
        Path.Join(TuiHarness.DefaultRoot, ".lip", "state.json"),
        new MockFileData(TuiHarness.StateWithGame));
    return fs;
  }

  /// The bottom bar.
  private static string LastRow(TuiHarness tui) => tui.Screen.Split('\n')[^1];
}
