using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Tests.Infrastructure;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// lip run before the server starts (protocol/lip-run.md, steps 1 to 4), on the real file system,
/// where the workspace lock is enforced.
public class McmodServiceRunTests : IDisposable {
  // The platform of every environment (protocol/lip.md, "Environment").
  private const string Platform = "win-x64";

  // The game whose server runs without Wine where the test runs: the Linux server on Linux on
  // x64, otherwise the Windows server with a loader (protocol/lip-run.md).
  private static readonly bool _linux = WorkspaceProgram.RunsLinuxServer;

  // The wine program that runs the server of the test, the Windows server where lip does not
  // run on Windows: null where the server of the test is the Linux server, on Windows, and on a
  // system that has no Wine of lip (protocol/lip-workspace.md, "Running programs" and "System
  // dependencies").
  private static readonly string? _wine =
      _linux ? null : ManagedWine.Executable(LipDirectories.Data, SystemInfo.Current);

  // Whether lip can start the server of the test here: its own server, or the Windows server
  // on Windows or through Wine.
  private static readonly bool _serverStarts =
      _linux || OperatingSystem.IsWindows() || _wine is not null;

  private readonly string _root =
      Path.Combine(Path.GetTempPath(), "lip-run-test-" + Guid.NewGuid().ToString("N"));

  private readonly FileSystem _fs = new();
  private readonly LockStore _lockStore;
  private readonly McmodService _service;
  private readonly Mock<IUserInteraction> _ui = new();
  private readonly ServerSessionTests.FakeServer _server = new();
  private readonly List<ProgramStartInfo> _started = [];
  private readonly Mock<ISystemPreparer> _preparer = new();
  private Action? _onStart;

  public McmodServiceRunTests() {
    Directory.CreateDirectory(_root);
    _lockStore = new LockStore(_fs);
    _ui.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    _ui.Setup(u => u.PrintWarning(It.IsAny<string>())).Returns(Task.CompletedTask);
    _service = new McmodService(
        _fs,
        _ui.Object,
        _lockStore,
        Mock.Of<IArchiveProvider>(),
        new ArchiveReader(),
        new McmodInstaller(_fs, Mock.Of<ICommandRunner>(), _ui.Object),
        workspaceRoot: () => _root,
        systemPreparer: _preparer.Object);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }

    GC.SuppressFinalize(this);
  }

  private string ServerPath =>
      Path.Join(_root, _linux ? "bedrock_server" : "bedrock_server_mod.exe");

  private static void SkipWhereTheServerCannotStart() => Assert.SkipUnless(
      _serverStarts, "This system has no Wine of lip, which runs the Windows server here.");

  private async Task<int> Run() {
    Mock<IProgramStarter> starter = new();
    starter.Setup(s => s.Start(It.IsAny<ProgramStartInfo>()))
        .Callback((ProgramStartInfo start) => {
          _started.Add(start);
          _onStart?.Invoke();
          _server.Exit(3);
        })
        .Returns(_server);
    return await _service.Run(
        Stream.Null, inputIsConsole: false, _ => new MemoryStream(), starter.Object);
  }

  /// Saves a state file with a game (of the build of the test, unless linux says otherwise) and,
  /// unless loader is false, LeviLamina, installed the way lip before lip setup did.
  private async Task SaveState(
      string side = "server", bool game = true, bool? loader = null, bool? linux = null) {
    bool linuxGame = linux ?? _linux;
    List<LockedPackage> packages = [];
    if (game) {
      packages.Add(Package(BedrockServer.Repository, linuxGame ? "linux" : ""));
    }

    if (loader ?? !linuxGame) {
      packages.Add(Package(GameSetup.LoaderRepository, ""));
    }

    await _lockStore.Save(
        _root, new LockFile { Side = side, Platform = Platform, Packages = packages });
  }

  private static LockedPackage Package(string id, string variant) => new() {
    Manifest = System.Text.Json.JsonDocument.Parse($$"""
        { "format": 4, "id": "{{id}}", "version": "1.0.0",
          "variants": { "{{variant}}": [ { "side": "server", "platform": "win-x64" } ] } }
        """).RootElement.Clone(),
    Variant = variant,
    Explicit = true,
    Source = id + (variant == "" ? "" : $"#{variant}"),
    Files = [],
  };

  [Fact]
  public async Task Run_Server_RunsItInTheWorkspaceAndReturnsItsExitCode() {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");

    // Act
    int code = await Run();

    // Assert
    Assert.Equal(3, code);
    ProgramStartInfo start = Assert.Single(_started);
    if (_wine is null) {
      Assert.Equal(ServerPath, start.FileName);
      Assert.Empty(start.Arguments);
    } else {
      // Wine runs the Windows server, with the absolute path of its file as the only argument.
      Assert.Equal(_wine, start.FileName);
      Assert.Equal([ServerPath], start.Arguments);
    }

    Assert.Equal(_root, start.WorkingDirectory);
  }

  [Fact]
  public async Task Run_WindowsServerWhereTheSystemHasNoWine_FailsWithoutStarting() {
    Assert.SkipWhen(_serverStarts, "lip can start the server of the test here.");
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(Run);
    Assert.Contains("lip has no Wine for this system", ex.Message);
    Assert.Empty(_started);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Run_NoGame_FailsPointingToSetup(bool stateFile) {
    // Arrange: a server file, but no game (protocol/lip-run.md, step 4).
    if (stateFile) {
      await SaveState(game: false, loader: true);
    }

    File.WriteAllText(Path.Join(_root, "bedrock_server_mod.exe"), "");

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(Run);
    Assert.Contains("lip setup", ex.Message);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_WindowsGameWithoutLoader_RunsBedrockServerExe() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Wine runs the Windows server elsewhere.");
    // Arrange
    await SaveState(loader: false, linux: false);
    string server = Path.Join(_root, "bedrock_server.exe");
    File.WriteAllText(server, "");
    File.WriteAllText(Path.Join(_root, "bedrock_server_mod.exe"), "");

    // Act
    await Run();

    // Assert
    Assert.Equal(server, Assert.Single(_started).FileName);
  }

  [Fact]
  public async Task Run_LinuxGameNotOnLinuxX64_FailsWithoutStarting() {
    Assert.SkipWhen(_linux, "lip runs the Linux server here.");
    // Arrange
    await SaveState(linux: true);
    File.WriteAllText(Path.Join(_root, "bedrock_server"), "");

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(Run);
    Assert.Contains("Linux on x64", ex.Message);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_LinuxGame_RunsItWithoutPreparingTheSystem() {
    Assert.SkipUnless(_linux, "lip runs the Linux server on Linux on x64 only.");
    // Arrange
    await SaveState(linux: true);
    File.WriteAllText(ServerPath, "");

    // Act
    await Run();

    // Assert
    ProgramStartInfo start = Assert.Single(_started);
    Assert.Equal(ServerPath, start.FileName);
    _preparer.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Run_WindowsServer_PreparesTheSystemFirst() {
    SkipWhereTheServerCannotStart();
    Assert.SkipWhen(_linux, "The server of the test is the Linux server here.");
    // Arrange (protocol/lip-run.md, step 5).
    await SaveState();
    File.WriteAllText(ServerPath, "");
    _preparer.Setup(p => p.Prepare(Platform, _root, It.IsAny<CancellationToken>()))
        .Callback(() => Assert.Empty(_started))
        .Returns(Task.CompletedTask);

    // Act
    await Run();

    // Assert
    _preparer.Verify(
        p => p.Prepare(Platform, _root, It.IsAny<CancellationToken>()), Times.Once);
    Assert.Single(_started);
  }

  [Fact]
  public async Task Run_PreparingFails_FailsWithoutStarting() {
    Assert.SkipWhen(_linux, "The server of the test is the Linux server here.");
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    _preparer.Setup(p => p.Prepare(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new SystemDependencyException("no Wine"));

    // Act & Assert
    await Assert.ThrowsAsync<SystemDependencyException>(Run);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_LockHeld_FailsWithoutStarting() {
    // Arrange
    File.WriteAllText(ServerPath, "");
    _lockStore.CreateLipDirectory(_root);
    using WorkspaceMutex held =
        WorkspaceMutex.Acquire(_fs, _lockStore.GetMutexPath(_root), _root);

    // Act & Assert
    await Assert.ThrowsAsync<WorkspaceBusyException>(Run);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_LockNotHeldWhileTheServerRuns() {
    SkipWhereTheServerCannotStart();
    // Arrange: the platform lip runs on, so that the server is started without Wine.
    await SaveState();
    File.WriteAllText(ServerPath, "");
    bool locked = true;
    _onStart = () => {
      using WorkspaceMutex mutex =
          WorkspaceMutex.Acquire(_fs, _lockStore.GetMutexPath(_root), _root);
      locked = false;
    };

    // Act
    await Run();

    // Assert
    Assert.False(locked);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Run_NoServerFile_FailsWithoutStarting(bool directory) {
    // Arrange
    await SaveState();
    if (directory) {
      Directory.CreateDirectory(ServerPath);
    }

    // Act & Assert
    FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(Run);
    Assert.Contains(Path.GetFileName(ServerPath), ex.Message);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_ClientStateFile_FailsWithoutStarting() {
    // Arrange
    await SaveState(side: "client");
    File.WriteAllText(ServerPath, "");

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(Run);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_InterruptedOperation_UndoesItBeforeStarting() {
    SkipWhereTheServerCannotStart();
    // Arrange: an operation killed after writing a file.
    await SaveState();
    File.WriteAllText(ServerPath, "");
    _lockStore.CreateLipDirectory(_root);
    string written = Path.Join(_root, "written.txt");
    using (WorkspaceTransaction killed = new(_fs, _lockStore.GetJournalPath(_root))) {
      using MemoryStream content = new("x"u8.ToArray());
      await killed.WriteFile(written, content, TestContext.Current.CancellationToken);
    }

    bool recovered = false;
    _onStart = () => recovered =
        !File.Exists(written) && !File.Exists(_lockStore.GetJournalPath(_root));

    // Act
    await Run();

    // Assert
    Assert.True(recovered);
  }

  [Fact]
  public async Task Run_ServerLockHeld_FailsWithoutStarting() {
    // Arrange: another lip run runs the server (protocol/lip-run.md, step 1).
    await SaveState();
    File.WriteAllText(ServerPath, "");
    _lockStore.CreateLipDirectory(_root);
    using ServerLock held =
        ServerLock.Acquire(_fs, _lockStore.GetServerLockPath(_root), _root);

    // Act & Assert
    ServerRunningException ex = await Assert.ThrowsAsync<ServerRunningException>(Run);
    Assert.Contains("already running", ex.Message);
    Assert.Empty(_started);
  }

  [Fact]
  public async Task Run_ServerLockHeldWhileTheServerRuns_AndReleasedAfter() {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    bool held = false;
    _onStart = () => held = Assert.Throws<ServerRunningException>(() =>
        ServerLock.Acquire(_fs, _lockStore.GetServerLockPath(_root), _root)) is not null;

    // Act
    await Run();

    // Assert
    Assert.True(held);
    using ServerLock released =
        ServerLock.Acquire(_fs, _lockStore.GetServerLockPath(_root), _root);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("never")]
  public async Task Run_RestartNotOnFailure_DoesNotRestart(string? restart) {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    if (restart is not null) {
      WriteRestart(restart);
    }

    // Act
    int code = await RunExiting([1]);

    // Assert
    Assert.Equal(1, code);
    Assert.Single(_started);
  }

  [Fact]
  public async Task Run_OnFailure_RestartsAFailedServerUntilItExitsWithZero() {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    WriteRestart("on-failure");

    // Act
    int code = await RunExiting([1, 2, 0]);

    // Assert
    Assert.Equal(0, code);
    Assert.Equal(3, _started.Count);
    _ui.Verify(
        u => u.PrintWarning(It.Is<string>(m => m.Contains("restarts it"))), Times.Exactly(2));
  }

  [Fact]
  public async Task Run_OnFailure_GivesUpAfterFiveRestartsWithinTenMinutes() {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    WriteRestart("on-failure");

    // Act
    int code = await RunExiting([1, 1, 1, 1, 1, 7, 1]);

    // Assert: the first run and 5 restarts.
    Assert.Equal(7, code);
    Assert.Equal(6, _started.Count);
    _ui.Verify(u => u.PrintWarning(It.Is<string>(m => m.Contains("does not restart it again"))));
  }

  [Fact]
  public async Task Run_OnFailure_RestartsOlderThanTheWindowDoNotCount() {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    WriteRestart("on-failure");
    ManualClock clock = new();

    // Act: 11 minutes pass while each server runs.
    int code = await RunExiting(
        [1, 1, 1, 1, 1, 1, 1, 0], clock, () => clock.Now += TimeSpan.FromMinutes(11));

    // Assert
    Assert.Equal(0, code);
    Assert.Equal(8, _started.Count);
  }

  [Fact]
  public async Task Run_OnFailure_AskedToStop_DoesNotRestart() {
    SkipWhereTheServerCannotStart();
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");
    WriteRestart("on-failure");
    Action? stop = null;

    // Act: lip is asked to stop while the server runs, which then fails.
    int code = await RunExiting([3, 0], listen: s => {
      stop = s;
      return new MemoryStream();
    }, onStart: () => stop!());

    // Assert
    Assert.Equal(3, code);
    Assert.Single(_started);
  }

  private void WriteRestart(string restart) {
    _lockStore.CreateLipDirectory(_root);
    File.WriteAllText(
        Path.Join(_root, ".lip", "config.toml"), $"format = 4\nrestart = \"{restart}\"\n");
  }

  /// Runs lip run, whose servers exit with the codes in order, restarting them at once.
  private async Task<int> RunExiting(
      int[] codes, ManualClock? clock = null, Action? onStart = null, StopListener? listen = null) {
    int next = 0;
    Mock<IProgramStarter> starter = new();
    starter.Setup(s => s.Start(It.IsAny<ProgramStartInfo>()))
        .Returns((ProgramStartInfo start) => {
          _started.Add(start);
          onStart?.Invoke();
          ServerSessionTests.FakeServer server = new();
          server.Exit(codes[next++]);
          return server;
        });
    return await _service.Run(
        Stream.Null, inputIsConsole: false, listen ?? (_ => new MemoryStream()), starter.Object,
        new ServerRunOptions {
          Restart = new RestartPolicy(
              5, TimeSpan.FromMinutes(10), TimeSpan.Zero,
              (TimeProvider?)clock ?? TimeProvider.System),
        });
  }

  private sealed class ManualClock : TimeProvider {
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => Now;
  }
}
