using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Server page, for content/lip/pages/interface/server.md of liteldev.github.io: every state of
/// it that ServerShots does not show. As there, a server that runs is cmd.exe in a folder of its
/// own on the disk, and what it wrote is replaced by what the game writes.
public sealed class ServerDocShots(ShotRun run) {
  private const string Starting = "\x1b[90m── starting the server ──\x1b[0m";
  private const string AskedToStop = "\x1b[90m── asking the server to stop ──\x1b[0m";

  /// What the game writes when it is asked to stop.
  private static readonly string[] _stopped = [
    "[2026-11-14 21:40:07:512 INFO] Server stop requested.",
    "[2026-11-14 21:40:07:540 INFO] Stopping server...",
    "Quit correctly",
  ];

  /// What the game writes when it crashes.
  private static readonly string[] _crashed = [
    "[2026-11-14 20:31:48:006 INFO] Player disconnected: Steve, xuid: 2535416409681153",
    "[2026-11-14 20:32:10:377 ERROR] Unhandled exception: access violation reading 0x18",
    "[2026-11-14 20:32:10:381 ERROR] Crash dump: logs\\crash\\2026-11-14_20-32-10.dmp",
  ];

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task WithoutAServer(Language language) {
    using Shot shot = run.Start(Worlds.Empty(language, height: 16));
    await shot.Tui.GoTo(PageId.Server);
    await shot.Tui.WaitFor(ServerText.NoServer);
    await shot.Capture(
        "server-03-no-server", Note.Box(At.Button(CommonText.SetUpServer)), Note.Keys("Enter"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task AutoRestart(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 16));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Server);
    await tui.WaitFor($"[ ] {CommonText.AutoRestart}");

    // → goes from the button to the option, which says what it does; Space switches it.
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor(ServerText.AutoRestartDetail);
    tui.Press(ConsoleKey.Spacebar);
    await tui.WaitFor($"[x] {CommonText.AutoRestart}");
    await shot.Capture(
        "server-04-auto-restart",
        Note.Box(At.Text($"[x] {CommonText.AutoRestart}")),
        Note.Callout(
            1, At.Text(ServerText.AutoRestartDetail),
            shot.T("What the option does", "这个选项的作用", "這個選項的作用"), Side.Right),
        Note.Keys("→", "Space"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task RunningElsewhere(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 16));
    TuiHarness tui = shot.Tui;
    await tui.GoTo(PageId.Server);
    await tui.WaitFor($"[ ] {CommonText.AutoRestart}");
    await tui.SetServerRunning(true);
    await tui.WaitFor("● " + ServerText.RunningElsewhere);
    await shot.Capture(
        "server-10-elsewhere",
        Note.Callout(
            1, At.Text("● " + ServerText.RunningElsewhere),
            shot.T(
                "The server runs, but this interface did not start it.",
                "服务器在运行，但不是由这个界面启动的。", "伺服器在執行，但不是由這個介面啟動的。")),
        Note.Callout(
            2, At.Button(ServerText.Start),
            shot.T(
                "Start does nothing here, and there is no Stop.",
                "“启动”在这里不起作用，也没有“停止”。", "「啟動」在這裡沒有作用，也沒有「停止」。")),
        Note.Callout(
            3, At.Text(ServerText.RunningElsewhereDetail),
            shot.T("Where to stop it.", "该去哪里停止它。", "該去哪裡停止它。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task JustStarted(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    await using Cmd cmd = Cmd.Create(run, language, height: 18);
    await cmd.Begin();
    Shot shot = cmd.Shot;
    await cmd.Write(Starting);
    // The server started a moment ago.
    shot.Scrub("· 00:12:34", "· 00:00:01");
    await shot.Capture(
        "server-05-just-started",
        Note.Callout(
            1, At.Text("● " + CommonText.ServerRunning, 2),
            shot.T(
                "The server runs, and the time starts to count.",
                "服务器已在运行，并开始计时。", "伺服器已在執行，並開始計時。")),
        Note.Callout(
            2, At.Button(ServerText.Stop),
            shot.T("The button now stops the server.", "按钮变成了“停止”。", "按鈕變成了「停止」。")),
        Note.Callout(
            3, At.Text("── starting the server ──"),
            shot.T(
                "A line of lip itself, in gray. The server's own lines follow.",
                "lip 自己写的一行（灰色），后面才是服务器的输出。",
                "lip 自己寫的一行（灰色），後面才是伺服器的輸出。")),
        Note.Callout(
            4, At.Text("> " + ServerText.InputExample),
            shot.T(
                "The input line appears, with the focus in it.", "输入行出现，焦点就在这里。",
                "輸入列出現，焦點就在這裡。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task OutputAndInput(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    await using Cmd cmd = Cmd.Create(run, language, height: 22);
    await cmd.Begin();
    Shot shot = cmd.Shot;
    TuiHarness tui = shot.Tui;
    await cmd.Write([Starting, .. Worlds.ServerOutput]);

    // Esc leaves the input line for the output; ↑ scrolls it back.
    tui.Press(ConsoleKey.Escape);
    tui.Press(ConsoleKey.UpArrow, 4);
    await tui.WaitFor(_ => LastRow(tui).Contains(CommonText.HintPage.Key), "the output scrolls");
    await shot.Capture(
        "server-06-output-scrolled",
        Note.Callout(
            1, At.Text($" {ServerText.Output} "),
            shot.T(
                "The output has the focus: its title is highlighted.", "焦点在输出区：标题高亮。",
                "焦點在輸出區：標題反白。")),
        Note.Callout(
            2, At.Text("▲").To(At.Text("▼")),
            shot.T(
                "More lines above and below.", "上方和下方还有内容。", "上方和下方還有內容。"),
            Side.Right),
        Note.Callout(
            3, At.Text($"{CommonText.HintScroll.Key} {CommonText.HintScroll.Text}")
                .To(At.Text($"{CommonText.HintEnds.Key} {CommonText.HintEnds.Text}")),
            shot.T("The keys that scroll.", "用来滚动的按键。", "用來捲動的按鍵。"), Side.Top),
        Note.KeysAfter(shot.T("From the input line", "从输入行开始", "從輸入列開始"), "Esc", "↑"));

    // End goes back to the last line, and ↓ from there to the input line.
    tui.Press(ConsoleKey.End);
    tui.Press(ConsoleKey.DownArrow);
    await tui.WaitFor(_ => LastRow(tui).Contains(ServerText.HintSend.Text), "the input line");
    await cmd.Enter("list");
    await cmd.Enter("say Welcome to Survival");
    await cmd.Write([
      .. Worlds.ServerOutput[^4..],
      "[2026-11-14 20:18:40:117 INFO] There are 1/10 players online:",
      "[2026-11-14 20:18:40:117 INFO] Steve",
      "[2026-11-14 20:19:02:731 INFO] [Server] Welcome to Survival",
    ]);
    tui.Press(ConsoleKey.UpArrow, 2);
    await tui.WaitFor("> list");
    await shot.Capture(
        "server-07-history",
        Note.Box(At.Text("> list")),
        Note.Arrow(
            At.Text($"{ServerText.HintHistory.Key} {ServerText.HintHistory.Text}"),
            shot.T("↑ and ↓ bring back what you sent", "↑ 和 ↓ 调出发送过的命令", "↑ 和 ↓ 叫出傳送過的命令"),
            Side.Bottom),
        Note.Keys("↑", "↑"));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task StoppingAndExited(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    await using Cmd cmd = Cmd.Create(run, language, height: 18);
    await cmd.Begin();
    Shot shot = cmd.Shot;
    TuiHarness tui = shot.Tui;
    string[] before = [.. Worlds.ServerOutput[^4..]];
    await cmd.Write(before);

    // Ctrl+C asks the server to stop; cmd.exe goes on until it reads "exit".
    tui.Interrupt();
    await tui.WaitFor("● " + CommonText.ServerStopping);
    await cmd.Idle();
    await cmd.Write([.. before, AskedToStop, _stopped[0]]);
    await shot.Capture(
        "server-08-stopping",
        Note.Callout(
            1, At.Text("● " + CommonText.ServerStopping, 2),
            shot.T(
                "lip asked the server to stop, and waits for it.", "lip 已请服务器停止，正在等它退出。",
                "lip 已請伺服器停止，正在等它結束。")),
        Note.Callout(
            2, At.Text("── asking the server to stop ──"),
            shot.T(
                "lip sent the server the command stop.", "lip 向服务器发送了 stop 命令。",
                "lip 向伺服器傳送了 stop 命令。")),
        Note.KeysAfter(shot.T("After", "按下之后", "按下之後"), "Ctrl+C"));

    cmd.Server.Send("exit 0");
    await tui.WaitFor("○ " + ServerText.ExitedWith(0));
    await cmd.Write([
      .. before, AskedToStop, .. _stopped, "\x1b[90m── the server exited with code 0 ──\x1b[0m",
    ]);
    await shot.Capture(
        "server-09-exited",
        Note.Callout(
            1, At.Text("○ " + ServerText.ExitedWith(0)),
            shot.T(
                "The server is not running; 0 means that it ended normally.",
                "服务器已不在运行；0 表示正常结束。", "伺服器已不在執行；0 表示正常結束。")),
        Note.Callout(
            2, At.Button(ServerText.Start),
            shot.T("Starts it again.", "再次启动。", "再次啟動。")),
        Note.Callout(
            3, At.Text("── the server exited with code 0 ──"),
            shot.T(
                "The output stays, to be read and scrolled.", "输出会保留，可以继续查看和滚动。",
                "輸出會保留，可以繼續檢視和捲動。")));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Restarting(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    // lip waits before it restarts the server: here for as long as the scenario takes.
    await using Cmd cmd = Cmd.Create(
        run, language, height: 18,
        restart: new RestartPolicy(
            5, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeProvider.System));
    await cmd.Begin(autoRestart: true);
    Shot shot = cmd.Shot;
    TuiHarness tui = shot.Tui;

    // The server crashes: it exits with a code that is not 0.
    cmd.Server.Send("exit -1073741819");
    await tui.WaitFor("● " + CommonText.ServerRestarting);
    string[] lip;
    lock (cmd.Server.Gate) {
      lip = [
        .. cmd.Server.Log.Lines.Select(line => line.PlainText)
            .Where(text => text.StartsWith("The server exited", StringComparison.Ordinal))
            .Select(text => text.Replace("3600 seconds", "5 seconds", StringComparison.Ordinal)),
      ];
    }

    await cmd.Write([.. _crashed, .. lip.Select(line => $"\x1b[33m{line}\x1b[0m")]);
    await shot.Capture(
        "server-11-restarting",
        Note.Callout(
            1, At.Text("● " + CommonText.ServerRestarting, 2),
            shot.T(
                "lip waits a few seconds, then starts the server again.",
                "lip 等待几秒后会重新启动服务器。", "lip 等待幾秒後會重新啟動伺服器。")),
        Note.Callout(
            2, At.Text(ServerText.ExitedWith(-1073741819)),
            shot.T("How the server ended.", "服务器是怎样退出的。", "伺服器是怎樣結束的。"),
            Side.Right),
        Note.Callout(
            3, At.Text($"[x] {CommonText.AutoRestart}"),
            shot.T("Auto restart is on.", "已开启自动重启。", "已開啟自動重新啟動。"),
            Side.Right));
  }

  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Quitting(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    foreach (bool managed in new[] { true, false }) {
      await using Cmd cmd = Cmd.Create(run, language, height: 18, managed: managed);
      await cmd.Begin();
      Shot shot = cmd.Shot;
      TuiHarness tui = shot.Tui;
      await cmd.Write([Starting, .. Worlds.ServerOutput]);

      // Esc: from the input line to the output, to the sidebar, and then the question.
      tui.Press(ConsoleKey.Escape);
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntil(() => !tui.Context.ContentFocused, "the sidebar");
      tui.Press(ConsoleKey.Escape);
      await tui.WaitFor(CommonText.QuitQuestion);
      string note = managed ? CommonText.QuitServersKeepRunning : CommonText.QuitStopsServers;
      await shot.Capture(
          managed ? "server-12-quit-keeps-running" : "server-13-quit-stops",
          Note.Focus(At.Panel(CommonText.QuitTitle)),
          Note.Box(At.Text(note)),
          Note.KeysAfter(shot.T("Opened with", "打开方式", "開啟方式"), "Esc", "Esc", "Esc"));
      tui.Press(ConsoleKey.Escape);
      await tui.WaitUntilGone(CommonText.QuitQuestion);
    }
  }

  private static string LastRow(TuiHarness tui) => tui.Screen.Split('\n')[^1];

  /// A scenario whose server runs: cmd.exe in a workspace on the disk, started with the Start
  /// button of the Server page. Disposing it ends the server and deletes the workspace.
  private sealed class Cmd : IAsyncDisposable {
    private readonly string _root;
    private readonly ServerSessions _servers;

    private Cmd(string root, ServerSessions servers, Shot shot) {
      _root = root;
      _servers = servers;
      Shot = shot;
    }

    public Shot Shot { get; }

    public WorkspaceServer Server => _servers.Find(Shot.World.Workspace)!;

    /// Starts the interface in a workspace whose server is cmd.exe. Not async on purpose, as
    /// TuiHarness.Start is not: afterwards the texts of the interface are in its language
    /// for the scenario that called.
    public static Cmd Create(
        ShotRun run, Language language, int height, bool managed = true,
        RestartPolicy? restart = null) {
      string root = TestClients.CmdWorkspace();
      ServerSessions servers = new(restart);
      try {
        System.IO.Abstractions.FileSystem fs = new();
        World world = Worlds.Survival(language, width: 110, height: height);
        world.Servers = servers;
        world.ManagedServers = managed;
        world.CreateClient = (interaction, hookOutput, _) =>
            TestClients.CmdClient(fs, interaction, hookOutput, root);
        return new Cmd(root, servers, run.Start(world));
      }
      catch {
        TestClients.Remove(root, servers);
        throw;
      }
    }

    /// Goes to the Server page, switches Auto restart on when asked, and presses Start; then
    /// waits until cmd.exe reads.
    public async Task Begin(bool autoRestart = false) {
      TuiHarness tui = Shot.Tui;
      await tui.GoTo(PageId.Server);
      await tui.WaitFor($"[ {ServerText.Start} ]");
      await tui.WaitFor($"[ ] {CommonText.AutoRestart}");
      if (autoRestart) {
        tui.Press(ConsoleKey.RightArrow);
        tui.Press(ConsoleKey.Spacebar);
        await tui.WaitFor($"[x] {CommonText.AutoRestart}");
        string config = Path.Join(_root, ".lip", "config.toml");
        await tui.WaitFor(
            _ => File.Exists(config) && File.ReadAllText(config).Contains("on-failure"),
            "restart = on-failure");
        tui.Press(ConsoleKey.LeftArrow);
      }

      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.WaitFor(
          _ => LastRow(tui).Contains($"Enter {ServerText.HintSend.Text}"), "the input line");
      await Idle();
    }

    /// Waits until cmd.exe has written its prompt, which is the last that it writes before
    /// it reads.
    public Task Idle() =>
        Shot.Tui.WaitFor(
            _ => {
              lock (Server.Gate) {
                return Server.Log.Lines[^1].PlainText.EndsWith('>');
              }
            },
            "the prompt of cmd.exe");

    /// Types a line in the input line and sends it, and waits until cmd.exe has read it.
    public async Task Enter(string line) {
      int before;
      lock (Server.Gate) {
        before = Server.Log.Lines.Count;
      }

      Shot.Tui.Type(line);
      Shot.Tui.Press(ConsoleKey.Enter);
      await Shot.Tui.WaitFor(
          _ => {
            lock (Server.Gate) {
              return Server.Log.Lines.Count > before
                  && Server.Log.Lines[^1].PlainText.EndsWith('>');
            }
          },
          "the answer of cmd.exe");
    }

    /// Replaces what cmd.exe wrote by the lines given, each with its escape sequences.
    public async Task Write(params string[] lines) {
      lock (Server.Gate) {
        Server.Log.Append("\n");
        Server.Log.Lines.Clear();
        Server.Log.Lines.Add(new StyledLine());
        foreach (string line in lines) {
          Server.Log.Append(line + "\n");
        }
      }

      // The interface draws the screen again after whatever it is asked to do.
      await Shot.Tui.Invoke(() => { });
    }

    public ValueTask DisposeAsync() {
      // A server that waits to be restarted is not: lip is asked to stop.
      _servers.Find(Shot.World.Workspace)?.Stop();
      Shot.Dispose();
      TestClients.Remove(_root, _servers);
      return ValueTask.CompletedTask;
    }
  }
}
