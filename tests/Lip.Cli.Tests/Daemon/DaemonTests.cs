using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using System.Text;
using Lip.Cli.Daemon;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;

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
    if (!OperatingSystem.IsWindows()) {
      Directory.Delete(Path.GetDirectoryName(_endpoint.Name)!, recursive: true);
    }
  }

  [Fact]
  public async Task Command_WritesToTheClientAndExitsWithItsCode() {
    // Arrange
    _ = Host(async (args, _, invocation) => {
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
    _ = Host(async (_, _, invocation) => {
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
    _ = Host(async (_, _, invocation) => {
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
    _ = Host(async (_, _, invocation) => {
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
    Assert.True(client.Signal(PosixSignal.SIGINT));
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
    _ = Host(async (_, _, invocation) => {
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
    client.Signal(PosixSignal.SIGINT);
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
    _ = Host(async (_, _, invocation) => {
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
    client.Signal(PosixSignal.SIGTERM);

    // Assert
    Assert.Equal(new ClientOutcome(3, null, null), await run);
  }

  [Fact]
  public async Task ClientThatGoesAway_AsksTheServerToStop() {
    // Arrange
    TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Host(async (_, _, invocation) => {
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
      _ = Host((_, _, _) => Task.FromResult(5));
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
    Task old = Host((_, _, _) => Task.FromResult(1), version: "0.9.0");
    DaemonClient client = new(_endpoint, Version, Executable, () => {
      _ = Host((_, _, _) => Task.FromResult(2));
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
    _ = Host(async (_, _, _) => {
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
    _ = Host((_, _, _) => Task.FromResult(0));

    // Act & Assert
    await Assert.ThrowsAsync<EndpointInUseException>(() => new DaemonHost(
        _endpoint, Version, Executable, TimeSpan.FromMinutes(1),
        (_, _, _) => Task.FromResult(0)).Run());
  }

  [Fact]
  public async Task Shutdown_WhileACommandRuns_IsRefused() {
    // Arrange
    TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task host = Host(async (_, _, _) => {
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
    Task host = Host((_, _, _) => Task.FromResult(0), idle: TimeSpan.FromMilliseconds(100));

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
    Assert.Equal([0, 0, 1], outcomes.Select(o => o.ExitCode));
    Assert.Equal("\"on-failure\"", first.OutputText.ToString().Trim());
    Assert.Equal("\"never\"", second.OutputText.ToString().Trim());
    Assert.Contains("Unknown option '--bogus'", usage.ErrorText.ToString());
    Assert.Equal("", usage.OutputText.ToString());
  }

  private DaemonClient Client() => new(_endpoint, Version, Executable, () => "not started");

  private Task Host(CarryOutCommand run, string version = Version, TimeSpan? idle = null) {
    Task host = new DaemonHost(
        _endpoint, version, Executable, idle ?? TimeSpan.FromMinutes(1), run).Run();
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

  /// The standard streams of a client, with input as its standard input.
  private sealed class Streams(string input) {
    public MemoryStream Output { get; } = new();

    public StringWriter OutputText { get; } = new() { NewLine = "\n" };

    public StringWriter ErrorText { get; } = new() { NewLine = "\n" };

    public ClientStreams Client => new(
        new MemoryStream(Encoding.UTF8.GetBytes(input)), false, Output, new MemoryStream(),
        OutputText, ErrorText);
  }
}
