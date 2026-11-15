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
  [InlineData(Language.En, 120, 36, "No server here yet.", "[ New server… ]")]
  [InlineData(Language.ZhHans, 120, 36, "这里还没有服务器。", "[ 新建服务器… ]")]
  [InlineData(Language.ZhHant, 120, 36, "這裡還沒有伺服器。", "[ 建立伺服器… ]")]
  [InlineData(Language.En, 60, 16, "No server here yet.", "[ New server… ]")]
  [InlineData(Language.ZhHans, 60, 16, "这里还没有服务器。", "[ 新建服务器… ]")]
  [InlineData(Language.ZhHant, 60, 16, "這裡還沒有伺服器。", "[ 建立伺服器… ]")]
  public async Task WorkspaceWithoutAServer_LeadsToCreatingOne(
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

    await tui.WaitFor(_ => tui.Context.CurrentPage == PageId.Workspaces, "the Workspaces page");
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
    await tui.WaitFor($"[ ] {ServerText.AutoRestart}");

    // Act: → chooses the toggle, which then says what it does; Space switches it.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(ServerText.AutoRestartDetail);
    Assert.Contains($"Space {CommonText.HintToggle.Text}", LastRow(tui));
    tui.Press(ConsoleKey.Spacebar);

    await tui.WaitFor($"[x] {ServerText.AutoRestart}");
    await tui.WaitFor(
        _ => tui.Fs.FileExists(config) && tui.Fs.File.ReadAllText(config).Contains("on-failure"),
        "restart = on-failure");
    tui.Snapshot("server-auto-restart");

    // Leaving and coming back reads it again; Space switches it off.
    await tui.GoTo(PageId.Home);
    await tui.GoTo(PageId.Server);
    await tui.WaitFor($"[x] {ServerText.AutoRestart}");
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor($"[ ] {ServerText.AutoRestart}");
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
    await tui.WaitFor(_ => !tui.Context.ContentFocused, "the sidebar");
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
        tui.Press(ConsoleKey.Escape);
        await tui.WaitFor(
            screen => Regex.IsMatch(screen, Regex.Escape(ServerText.Title) + @"\s+●"), "the badge");
        tui.Press(ConsoleKey.Enter);
        await tui.Settle();
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
      await End(servers, root);
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
      await tui.WaitFor(_ => LastRow(tui).Contains("↑↓ Scroll"), "the output");
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
      await End(servers, root);
    }
  }

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

  /// Ends the server of a test that failed before it did, and deletes its workspace.
  private static async Task End(ServerSessions servers, string root) {
    foreach (WorkspaceServer server in servers.Running()) {
      server.Send("exit");
      for (int i = 0; i < 200 && server.Running; i++) {
        await Task.Delay(25, TestContext.Current.CancellationToken);
      }
    }

    try {
      Directory.Delete(root, recursive: true);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
      // The server still runs.
    }
  }
}
