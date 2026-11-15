using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using System.Text;
using Lip.Cli.Commands;
using Lip.Cli.Daemon;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tests.Daemon;

/// The daemon and its clients over a real named pipe or Unix domain socket, in this process
/// (protocol/lip-daemon.md, "Carrying out a command").
public sealed class DaemonTests : IAsyncDisposable {
  private const string Version = "1.0.0";
  private const string Executable = "/opt/lip/lip";

  private static readonly ConsoleProfile _redirected =
      new(false, false, 0, false, false, false, 80, 24);

  private readonly string _directory = Directory.CreateTempSubdirectory("lip-daemon-tests-").FullName;
  private readonly DaemonEndpoint _endpoint;
  private readonly List<Task> _hosts = [];

  public DaemonTests() {
    _endpoint = new DaemonEndpoint(OperatingSystem.IsWindows()
        ? $"lip-test-{Guid.NewGuid():N}"
        : Path.Join(Path.GetTempPath(), $"lip-{Guid.NewGuid():N}"[..12], "daemon.sock"));
  }

  public async ValueTask DisposeAsync() {
    // Every daemon of the test exits.
    for (int i = 0; i < 3 && _hosts.Any(h => !h.IsCompleted); i++) {
      if (await _endpoint.TryConnect(CancellationToken.None) is { } connection) {
        await using (connection) {
          await Client().Greet(connection);
          await DaemonClient.AskToExit(connection);
        }
      }

      await Task.WhenAny(Task.WhenAll(_hosts), Task.Delay(TimeSpan.FromSeconds(10)));
    }

    Directory.Delete(_directory, recursive: true);
    // A test that starts no daemon leaves the directory of the socket uncreated.
    string socketDirectory = Path.GetDirectoryName(_endpoint.Name)!;
    if (!OperatingSystem.IsWindows() && Directory.Exists(socketDirectory)) {
      Directory.Delete(socketDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Command_WritesToTheClientAndExitsWithItsCode() {
    // Arrange
    _ = Host(async (args, _, invocation, _) => {
      await invocation.Out.WriteAsync($"out {string.Join(',', args)}\n");
      await invocation.Error.WriteAsync("err\n");
      invocation.OpenStandardOutput().Write("raw"u8);
      return 7;
    });
    Streams streams = new("");

    // Act
    ClientOutcome outcome = await Client().Run(Start(["a", "b"]), streams.Client);

    // Assert
    Assert.Equal(new ClientOutcome(7, null, null), outcome);
    Assert.Equal("out a,b\n", streams.OutputText.ToString());
    Assert.Equal("err\n", streams.ErrorText.ToString());
    Assert.Equal("raw", Encoding.UTF8.GetString(streams.Output.ToArray()));
  }

  [Fact]
  public async Task Command_ReadsTheStandardInputOfTheClientOnlyAsItNeedsIt() {
    // Arrange
    _ = Host(async (_, _, invocation, _) => {
      using StreamReader reader = new(invocation.OpenStandardInput());
      string first = (await reader.ReadLineAsync())!;
      string rest = await reader.ReadToEndAsync();
      await invocation.Out.WriteAsync($"{first}|{rest}");
      return 0;
    });
    Streams streams = new("line 1\nline 2\nline 3\n");

    // Act
    ClientOutcome outcome = await Client().Run(Start([]), streams.Client);

    // Assert
    Assert.Equal(0, outcome.ExitCode);
    Assert.Equal("line 1|line 2\nline 3\n", streams.OutputText.ToString());
  }

  [Fact]
  public async Task Commands_OfTwoClients_HaveTheirOwnDirectoryAndEnvironment() {
    // Arrange
    TaskCompletionSource bothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int started = 0;
    _ = Host(async (_, _, invocation, _) => {
      if (Interlocked.Increment(ref started) == 2) {
        bothStarted.SetResult();
      }

      // Both run at once.
      await bothStarted.Task;
      Invocation current = Invocation.Current;
      RunTrace.Write("test", current.WorkingDirectory);
      await current.Out.WriteAsync(string.Join('|',
          current.WorkingDirectory,
          current.GetEnvironmentVariable("LIP_TEST"),
          current.GetEnvironmentVariable("XDG_STATE_HOME"),
          RunTrace.Dump().Contains(current.WorkingDirectory) ? "own trace" : "other trace",
          ReferenceEquals(current, invocation) ? "same" : "other"));
      return 0;
    });
    string a = Path.Join(_directory, "a");
    string b = Path.Join(_directory, "b");
    Streams first = new("");
    Streams second = new("");

    // Act
    ClientOutcome[] outcomes = await Task.WhenAll(
        Client().Run(Start([], a, ("LIP_TEST", "1")), first.Client),
        Client().Run(Start([], b, ("LIP_TEST", "2")), second.Client));

    // Assert
    Assert.All(outcomes, o => Assert.Equal(0, o.ExitCode));
    Assert.Equal($"{a}|1||own trace|same", first.OutputText.ToString());
    Assert.Equal($"{b}|2||own trace|same", second.OutputText.ToString());
  }

  [Fact]
  public async Task Interruption_WhileAnOperationChangesTheWorkspace_CancelsIt() {
    // Arrange
    TaskCompletionSource changing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Host(async (_, _, invocation, _) => {
      MockFileSystem fs = new();
      using WorkspaceTransaction transaction = new(fs, "/w/.lip/journal");
      changing.SetResult();
      try {
        await Task.Delay(Timeout.Infinite, invocation.Cancellation);
        return 0;
      }
      catch (OperationCanceledException) {
        return 130;
      }
    });
    Streams streams = new("");
    DaemonClient client = Client();

    // Act
    Task<ClientOutcome> run = client.Run(Start([]), streams.Client);
    await changing.Task;
    await Signal(client, PosixSignal.SIGINT);
    ClientOutcome outcome = await run;

    // Assert
    Assert.Equal(130, outcome.ExitCode);
    Assert.Contains("Cancelling", streams.ErrorText.ToString());
  }

  [Fact]
  public async Task Interruption_BeforeTheWorkspaceChanges_EndsTheClientAndAbandonsTheCommand() {
    // Arrange
    TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<bool> abandoned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Host(async (_, _, invocation, _) => {
      running.SetResult();
      try {
        await Task.Delay(Timeout.Infinite, invocation.Cancellation);
      }
      catch (OperationCanceledException) {
        abandoned.SetResult(true);
      }

      await invocation.Out.WriteAsync("never seen");
      return 0;
    });
    Streams streams = new("");
    DaemonClient client = Client();

    // Act
    Task<ClientOutcome> run = client.Run(Start([]), streams.Client);
    await running.Task;
    await Signal(client, PosixSignal.SIGINT);
    ClientOutcome outcome = await run;

    // Assert
    Assert.Equal(new ClientOutcome(null, PosixSignal.SIGINT, null), outcome);
    Assert.True(await abandoned.Task);
    Assert.Equal("", streams.OutputText.ToString());
  }

  [Fact]
  public async Task StopRequest_WhileTheServerRuns_IsPassedOnAndTheCommandGoesOn() {
    // Arrange
    TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Host(async (_, _, invocation, _) => {
      TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
      using (invocation.ListenForStop(() => stopped.TrySetResult())) {
        listening.SetResult();
        await stopped.Task;
      }

      return 3;
    });
    Streams streams = new("");
    DaemonClient client = Client();

    // Act
    Task<ClientOutcome> run = client.Run(Start([]), streams.Client);
    await listening.Task;
    await Signal(client, PosixSignal.SIGTERM);

    // Assert
    Assert.Equal(new ClientOutcome(3, null, null), await run);
  }

  [Fact]
  public async Task ClientThatGoesAway_AsksTheServerToStop() {
    // Arrange
    TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Host(async (_, _, invocation, _) => {
      using (invocation.ListenForStop(() => stopped.TrySetResult())) {
        listening.SetResult();
        await stopped.Task;
      }

      return 0;
    });

    // Act: a client that sends its command and goes away.
    Stream connection = (await _endpoint.TryConnect(TestContext.Current.CancellationToken))!;
    await Client().Greet(connection);
    Frames.Write(connection, FrameType.Start, Start([]).Encode());
    await listening.Task;
    await connection.DisposeAsync();

    // Assert
    await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
  }

  [Fact]
  public async Task NoDaemon_IsStartedAndConnectedTo() {
    // Arrange
    int starts = 0;
    DaemonClient client = new(_endpoint, Version, Executable, () => {
      starts++;
      _ = Host((_, _, _, _) => Task.FromResult(5));
      return null;
    });

    // Act
    ClientOutcome outcome = await client.Run(Start([]), new Streams("").Client);

    // Assert
    Assert.Equal(5, outcome.ExitCode);
    Assert.Equal(1, starts);
  }

  [Fact]
  public async Task DaemonOfAnotherVersion_IsReplaced() {
    // Arrange
    Task old = Host((_, _, _, _) => Task.FromResult(1), version: "0.9.0");
    DaemonClient client = new(_endpoint, Version, Executable, () => {
      _ = Host((_, _, _, _) => Task.FromResult(2));
      return null;
    });

    // Act
    ClientOutcome outcome = await client.Run(Start([]), new Streams("").Client);

    // Assert
    Assert.Equal(2, outcome.ExitCode);
    Assert.True(old.IsCompleted);
  }

  [Fact]
  public async Task DaemonOfAnotherVersion_ThatCarriesOutACommand_IsKeptAndTheClientFallsBack() {
    // Arrange
    TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Host(async (_, _, _, _) => {
      running.SetResult();
      await release.Task;
      return 0;
    }, version: "0.9.0");
    Task<ClientOutcome> busy = new DaemonClient(_endpoint, "0.9.0", Executable, () => null)
        .Run(Start([]), new Streams("").Client);
    await running.Task;

    // Act
    ClientOutcome outcome = await Client().Run(Start([]), new Streams("").Client);
    release.SetResult();

    // Assert
    Assert.Null(outcome.ExitCode);
    Assert.Contains("0.9.0", outcome.Fallback);
    Assert.Equal(0, (await busy).ExitCode);
  }

  [Fact]
  public async Task DaemonThatCannotBeStarted_MakesTheClientFallBack() {
    // Arrange
    DaemonClient client = new(_endpoint, Version, Executable, () => "no daemon here");

    // Act
    ClientOutcome outcome = await client.Run(Start([]), new Streams("").Client);

    // Assert
    Assert.Equal(new ClientOutcome(null, null, "no daemon here"), outcome);
  }

  [Fact]
  public async Task SecondDaemon_CannotListenOnTheEndpoint() {
    // Arrange
    _ = Host((_, _, _, _) => Task.FromResult(0));

    // Act & Assert
    await Assert.ThrowsAsync<EndpointInUseException>(() => new DaemonHost(
        _endpoint, Version, Executable, TimeSpan.FromMinutes(1),
        (_, _, _, _) => Task.FromResult(0)).Run());
  }

  [Fact]
  public async Task Shutdown_WhileACommandRuns_IsRefused() {
    // Arrange
    TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task host = Host(async (_, _, _, _) => {
      running.SetResult();
      await release.Task;
      return 0;
    });
    Task<ClientOutcome> command = Client().Run(Start([]), new Streams("").Client);
    await running.Task;

    // Act
    bool refused;
    Welcome welcome;
    await using (Stream connection =
        (await _endpoint.TryConnect(TestContext.Current.CancellationToken))!) {
      welcome = await Client().Greet(connection);
      refused = !await DaemonClient.AskToExit(connection);
    }

    release.SetResult();
    await command;

    // Assert
    Assert.True(refused);
    Assert.Equal(new Welcome(Version, Executable, Environment.ProcessId, 1), welcome);
    Assert.False(host.IsCompleted);
  }

  [Fact]
  public async Task Idle_DaemonExits() {
    // Arrange & Act
    Task host = Host((_, _, _, _) => Task.FromResult(0), idle: TimeSpan.FromMilliseconds(100));

    // Assert
    await host.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    Assert.Null(await _endpoint.TryConnect(TestContext.Current.CancellationToken));
  }

  /// The commands of lip itself (DaemonEntry.CarryOut), in two workspaces at once.
  [Fact]
  public async Task LipCommands_InTwoWorkspaces_ReadTheirOwnWorkspace() {
    // Arrange
    _ = Host(DaemonEntry.CarryOut);
    string a = Path.Join(_directory, "a");
    string b = Path.Join(_directory, "b");
    Directory.CreateDirectory(Path.Join(a, ".lip"));
    Directory.CreateDirectory(b);
    await File.WriteAllTextAsync(
        Path.Join(a, ".lip", "config.toml"), "format = 4\nrestart = \"on-failure\"\n",
        TestContext.Current.CancellationToken);
    Streams first = new("");
    Streams second = new("");
    Streams usage = new("");

    // Act
    ClientOutcome[] outcomes = await Task.WhenAll(
        Client().Run(Start(["config", "get", "restart", "--workspace", "--json"], a), first.Client),
        Client().Run(Start(["config", "get", "restart", "--workspace", "--json"], b), second.Client),
        Client().Run(Start(["list", "--bogus"], b), usage.Client));

    // Assert
    Assert.Equal(
        [new ClientOutcome(0, null, null), new ClientOutcome(0, null, null),
          new ClientOutcome(1, null, null)],
        outcomes);
    Assert.Equal("\"on-failure\"", first.OutputText.ToString().Trim());
    Assert.Equal("\"never\"", second.OutputText.ToString().Trim());
    Assert.Contains("Unknown option '--bogus'", usage.ErrorText.ToString());
    Assert.Equal("", usage.OutputText.ToString());
  }

  /// The interface, which the daemon carries out on the terminal of each client that attaches,
  /// with a workspace list that they share and that outlives them (protocol/lip-tui.md,
  /// "Workspaces").
  [Fact]
  public async Task Interface_OfClientsAttachedAtOnceAndLater_SharesTheWorkspaceList() {
    // Arrange
    MockFileSystem fs = new();
    string list = Path.Join(_directory, "state", WorkspaceList.FileName);
    ConcurrentDictionary<string, App> apps = new();
    _ = Host(InterfaceHost(fs, list, apps, []));
    string a = Path.Join(_directory, "a");
    string b = Path.Join(_directory, "b");
    string c = Path.Join(_directory, "c");

    // Act: two clients attach at once.
    Attached first = Attach(a);
    Attached second = Attach(b);
    App appA = await AppOf(apps, a);
    App appB = await AppOf(apps, b);
    await WaitFor(appA, s => s.Contains("lip 1.2.3", StringComparison.Ordinal));
    await WaitFor(appB, s => s.Contains("lip 1.2.3", StringComparison.Ordinal));

    // The terminal of a client is resized, and both see the workspace of the other.
    (await first.Writer.Task).Send(FrameType.Resize, ResizeMessage.Encode(210, 33));
    await WaitFor(appA, s => s.Split('\n').Length == 33);
    IReadOnlyList<string> seen = [];
    appA.Post(() => seen = appA.Context.Workspaces.Read());
    await WaitFor(appA, _ => seen.Contains(a) && seen.Contains(b));
    await Quit(first, appA);
    await Quit(second, appB);
    ClientOutcome[] outcomes = await Task.WhenAll(first.Outcome, second.Outcome);

    // A client attaches later, in another directory.
    Attached third = Attach(c);
    App appC = await AppOf(apps, c);
    await WaitFor(appC, s => s.Contains(c, StringComparison.Ordinal));
    await Quit(third, appC);
    ClientOutcome last = await third.Outcome;

    // Assert
    Assert.All([.. outcomes, last], o => Assert.Equal(0, o.ExitCode));
    Assert.Contains("lip 1.2.3", first.Streams.OutputText.ToString());
    Assert.Contains("\x1b[", first.Streams.OutputText.ToString());
    IReadOnlyList<string> workspaces = new WorkspaceList(fs, list).Read();
    Assert.Equal(3, workspaces.Count);
    Assert.Equal(new HashSet<string>([a, b]), new HashSet<string>(workspaces.Take(2)));
    Assert.Equal(c, workspaces[2]);
  }

  /// A client that is gone ends the interface that the daemon carries out for it.
  [Fact]
  public async Task Interface_ClientGone_EndsIt() {
    // Arrange
    ConcurrentDictionary<string, App> apps = new();
    ConcurrentDictionary<string, Task<int>> runs = new();
    _ = Host(InterfaceHost(new MockFileSystem(), null, apps, runs));
    string directory = Path.Join(_directory, "gone");
    Directory.CreateDirectory(directory);
    Stream connection = (await _endpoint.TryConnect(TestContext.Current.CancellationToken))!;
    await Client().Greet(connection);
    Frames.Write(connection, FrameType.Start, InterfaceStart(directory).Encode());
    App app = await AppOf(apps, directory);
    await WaitFor(app, s => s.Contains("lip 1.2.3", StringComparison.Ordinal));

    // Act
    await connection.DisposeAsync();

    // Assert
    Assert.Equal(0, await runs[directory].WaitAsync(
        TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
  }

  /// Carries out the interface for each client, with the workspace list at list (in memory when
  /// null) of fs, as DaemonEntry.CarryOut does with that of the state directory.
  private static CarryOutCommand InterfaceHost(
      MockFileSystem fs,
      string? list,
      ConcurrentDictionary<string, App> apps,
      ConcurrentDictionary<string, Task<int>> runs) {
    ServerSessions servers = new();
    return (_, start, invocation, terminal) => {
      Task<int> run = Task.Factory.StartNew(() => {
        App app = new(new AppOptions {
          Terminal = terminal,
          CreateClient = (interaction, hookOutput, workspace) =>
              LipClient.Create(fs, interaction, hookOutput, workspace),
          Catalog = new ModCatalogClient((_, _) => Task.FromResult("{}")),
          Version = "1.2.3",
          WorkingDirectory = invocation.WorkingDirectory,
          Workspaces = new WorkspaceList(fs, list),
          Servers = servers,
          ClientLanguage = Languages.Parse(start.Language),
        });
        apps[invocation.WorkingDirectory] = app;
        return app.Run();
      }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
      runs[invocation.WorkingDirectory] = run;
      return run;
    };
  }

  private static Start InterfaceStart(string directory) {
    ConsoleProfile terminal = new(true, true, 3, true, false, true, 200, 30);
    return new Start(
        [], directory, [], Environment.ProcessId, false, false, false, terminal, terminal);
  }

  private Attached Attach(string directory) {
    Directory.CreateDirectory(directory);
    Start start = InterfaceStart(directory);
    Streams streams = new("");
    TaskCompletionSource<FrameWriter> writer = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    Task<ClientOutcome> outcome = Client().Run(start, streams.Client, w => {
      writer.TrySetResult(w);
      return new MemoryStream();
    });
    return new Attached(streams, writer, outcome);
  }

  /// Quits the interface of a client as the user does: Ctrl+C, then Enter in the dialog that
  /// asks.
  private static async Task Quit(Attached client, App app) {
    client.Press((char)3, ConsoleKey.C, control: true);
    await WaitFor(app, s => s.Contains("Quit lip?", StringComparison.Ordinal));
    client.Press((char)13, ConsoleKey.Enter);
  }

  private static async Task<App> AppOf(ConcurrentDictionary<string, App> apps, string key) {
    for (int i = 0; i < 400; i++) {
      if (apps.TryGetValue(key, out App? app)) {
        return app;
      }

      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    throw new TimeoutException($"The daemon never ran the interface in {key}.");
  }

  private static async Task WaitFor(App app, Func<string, bool> shown) {
    for (int i = 0; i < 400; i++) {
      if (shown(app.ScreenText)) {
        return;
      }

      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Fail("The screen never showed what was waited for:\n" + app.ScreenText);
  }

  [Fact]
  public async Task ManagedRun_KeepsRunningWhenTheClientDetaches_AndIsAttachedToAgain() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    // Arrange (protocol/lip-run.md, "Managed servers" and "Attaching")
    ServerSessions servers = new();
    string root = Path.Join(_directory, "ws");
    Directory.CreateDirectory(root);
    File.Copy(
        Path.Join(Environment.SystemDirectory, "cmd.exe"),
        Path.Join(root, WorkspaceProgram.WindowsServerFileName));
    Directory.CreateDirectory(Path.Join(root, ".lip"));
    File.WriteAllText(Path.Join(root, ".lip", "state.json"), """
        { "format": 4, "side": "server", "platform": "win-x64", "packages": [ {
          "manifest": { "format": 4, "id": "github.com/LiteLDev/bds", "version": "1.21.0",
            "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "", "explicit": true, "ref": "github.com/LiteLDev/bds@1.21.0",
          "files": [] } ] }
        """);
    System.IO.Abstractions.FileSystem fs = new();
    _ = Host(
        (args, _, invocation, _) => RunCommand.RunManaged(
            invocation, args.Contains("--detach"), servers,
            (interaction, workspace) => Tui.TestClients.CmdClient(
                fs, interaction, () => Stream.Null, workspace)),
        runsServers: () => servers.AnyRunning);
    try {
      // Act: the first client starts the server, gives it a line, and detaches.
      SyncStream firstOutput = new();
      DaemonClient first = Client();
      Task<ClientOutcome> firstRun = first.Run(
          Start(["run", "--managed"], root), Streams.Of("echo first-line\n", firstOutput));
      // cmd.exe writes the line it reads, then what it runs writes.
      await WaitUntil(() => firstOutput.Text.Split("first-line").Length > 2);
      await Signal(first, PosixSignal.SIGHUP);
      ClientOutcome detached = await firstRun;
      WorkspaceServer server = servers.Find(root)!;
      bool runningAfterDetach = server.Running;

      // The daemon does not exit while the server runs.
      bool refused;
      await using (Stream connection =
          (await _endpoint.TryConnect(TestContext.Current.CancellationToken))!) {
        await Client().Greet(connection);
        refused = !await DaemonClient.AskToExit(connection);
      }

      // A second client attaches, gets what the server wrote before, and makes it exit.
      SyncStream secondOutput = new();
      ClientOutcome exited = await Client().Run(
          Start(["run", "--managed"], root), Streams.Of("exit 5\n", secondOutput));

      // Assert
      Assert.Equal(new ClientOutcome(0, null, null), detached);
      Assert.True(runningAfterDetach);
      Assert.True(refused);
      Assert.Equal(new ClientOutcome(5, null, null), exited);
      Assert.Contains("first-line", secondOutput.Text);
      Assert.False(server.Running);
    }
    finally {
      servers.Find(root)?.Send("exit");
    }
  }

  [Fact]
  public async Task ManagedRun_Detach_StartsTheServerAndEnds_AndAFailureBeforeItStartsFails() {
    // Arrange: a workspace without a game.
    ServerSessions servers = new();
    System.IO.Abstractions.FileSystem fs = new();
    _ = Host((args, _, invocation, _) => RunCommand.RunManaged(
        invocation, args.Contains("--detach"), servers,
        (interaction, workspace) => Tui.TestClients.CmdClient(
            fs, interaction, () => Stream.Null, workspace)));
    Streams streams = new("");

    // Act
    ClientOutcome outcome = await Client().Run(
        Start(["run", "--managed", "--detach"]), streams.Client);

    // Assert: the command failed as lip run did.
    Assert.Equal(1, outcome.ExitCode);
    Assert.Contains("There is no server to run", streams.ErrorText.ToString());
    Assert.False(servers.AnyRunning);
  }

  private static async Task WaitUntil(Func<bool> condition) {
    for (int i = 0; i < 400 && !condition(); i++) {
      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.True(condition());
  }

  /// A stream that is written and read at once.
  private sealed class SyncStream : MemoryStream {
    public string Text {
      get {
        lock (this) {
          return Encoding.UTF8.GetString(ToArray());
        }
      }
    }

    public override void Write(byte[] buffer, int offset, int count) {
      lock (this) {
        base.Write(buffer, offset, count);
      }
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
      lock (this) {
        base.Write(buffer);
      }
    }
  }

  /// A client of the daemon of the test, which runs already. Where every instance of the named
  /// pipe is taken for a moment, as when several clients connect at once, the client finds no
  /// daemon; it then waits for the one of the test as it would for one that it started.
  private DaemonClient Client() => new(_endpoint, Version, Executable, () => null);

  /// Has the client pass a signal on to its command. The daemon may run the command a moment
  /// before the client is ready to pass signals on, until when the client would end by the
  /// signal instead; the signal is given once, when the client passes it on.
  private static async Task Signal(DaemonClient client, PosixSignal signal) {
    for (int i = 0; i < 400; i++) {
      if (client.Signal(signal)) {
        return;
      }

      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Fail("The client did not pass the signal on.");
  }

  private Task Host(
      CarryOutCommand run,
      string version = Version,
      TimeSpan? idle = null,
      Func<bool>? runsServers = null) {
    Task host = new DaemonHost(
        _endpoint, version, Executable, idle ?? TimeSpan.FromMinutes(1), run, runsServers).Run();
    lock (_hosts) {
      _hosts.Add(host);
    }

    return host;
  }

  private Start Start(
      string[] args, string? directory = null, params (string Name, string Value)[] variables) {
    directory ??= _directory;
    Directory.CreateDirectory(directory);
    return new Start(
        args, directory, [.. variables.Select(v => new KeyValuePair<string, string>(v.Name, v.Value))],
        Environment.ProcessId, true, true, true, _redirected, _redirected);
  }

  /// A client attached to the interface: Press sends a key once the daemon has the command.
  private sealed record Attached(
      Streams Streams, TaskCompletionSource<FrameWriter> Writer, Task<ClientOutcome> Outcome) {
    public void Press(char character, ConsoleKey key, bool control = false) =>
        Writer.Task.GetAwaiter().GetResult().Send(
            FrameType.Key,
            KeyMessage.Encode(new ConsoleKeyInfo(character, key, false, false, control)));
  }

  /// The standard streams of a client, with input as its standard input.
  private sealed class Streams(string input) {
    public MemoryStream Output { get; } = new();

    public StringWriter OutputText { get; } = new() { NewLine = "\n" };

    public StringWriter ErrorText { get; } = new() { NewLine = "\n" };

    /// Streams whose standard input is input and standard output is output.
    public static ClientStreams Of(string input, Stream output) => new(
        new MemoryStream(Encoding.UTF8.GetBytes(input)), false, output, output,
        new StringWriter(), new StringWriter());

    public ClientStreams Client => new(
        new MemoryStream(Encoding.UTF8.GetBytes(input)), false, Output, new MemoryStream(),
        OutputText, ErrorText);
  }
}
