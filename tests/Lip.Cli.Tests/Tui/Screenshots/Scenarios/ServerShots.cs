using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// The Server page.
public sealed class ServerShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Stopped(Language language) {
    using Shot shot = run.Start(Worlds.Survival(language, height: 20));
    await shot.Tui.GoTo(PageId.Server);
    await shot.Tui.WaitFor($"[ {ServerText.Start} ]");

    await shot.Capture(
        "server-01-stopped", Note.Box(At.Button(ServerText.Start)), Note.Keys("Enter"));
  }

  /// The server that runs is a real program, cmd.exe, in a folder of its own on the disk:
  /// the interface starts it as it starts the game. What it wrote is replaced by what the
  /// game writes, so that the screenshot shows a server, and nothing of cmd.exe.
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Running(Language language) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the screenshot is cmd.exe.");
    string root = TestClients.CmdWorkspace();
    ServerSessions servers = new();
    try {
      System.IO.Abstractions.FileSystem fs = new();
      World world = Worlds.Survival(language, width: 110);
      world.Servers = servers;
      world.CreateClient = (interaction, hookOutput, _) =>
          TestClients.CmdClient(fs, interaction, hookOutput, root);
      using Shot shot = run.Start(world);
      TuiHarness tui = shot.Tui;
      await tui.GoTo(PageId.Server);
      await tui.WaitFor($"[ {ServerText.Start} ]");

      tui.Press(ConsoleKey.Enter);
      await tui.WaitFor($"[ {ServerText.Stop} ]");
      await tui.WaitFor(_ => LastRow(tui).Contains($"Enter {ServerText.HintSend.Text}"), "Send");
      // The prompt of cmd.exe is the last that it writes before it reads.
      WorkspaceServer server = servers.Find(world.Workspace)!;
      await tui.WaitFor(
          _ => {
            lock (server.Gate) {
              return server.Log.Lines[^1].PlainText.EndsWith('>');
            }
          },
          "the prompt of cmd.exe");
      lock (server.Gate) {
        StyledLine starting = server.Log.Lines.First(
            line => line.PlainText.StartsWith("──", StringComparison.Ordinal));
        server.Log.Append("\n");
        server.Log.Lines.Clear();
        server.Log.Lines.Add(new StyledLine());
        server.Log.AddLine(starting);
        foreach (string line in Worlds.ServerOutput) {
          server.Log.Append(line + "\n");
        }
      }

      tui.Type("say Welcome to Survival");
      await tui.WaitFor("> say Welcome to Survival");
      await tui.WaitFor("Player Spawned: Steve");
      await shot.Capture(
          "server-02-running",
          Note.Callout(
              1, At.Text("● " + CommonText.ServerRunning),
              shot.T(
                  "The server runs, and for how long.", "服务器正在运行，以及已运行的时间。",
                  "伺服器正在執行，以及已執行的時間。")),
          Note.Callout(
              2, At.Text(ServerText.Output),
              shot.T(
                  "What the server writes. Esc goes here from the input line; ↑ and ↓ scroll.",
                  "服务器的输出。在输入行按 Esc 到这里，↑ 和 ↓ 滚动。",
                  "伺服器的輸出。在輸入列按 Esc 到這裡，↑ 和 ↓ 捲動。")),
          Note.Callout(
              3, At.Text("> say Welcome to Survival"),
              shot.T(
                  "The input line: Enter sends the command to the server.",
                  "输入行：按 Enter 把命令发给服务器。", "輸入列：按 Enter 把命令傳給伺服器。")),
          Note.Keys("Enter"));

      server.Send("exit");
      await tui.WaitFor($"[ {ServerText.Start} ]");
    }
    finally {
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

  private static string LastRow(TuiHarness tui) => tui.Screen.Split('\n')[^1];
}
