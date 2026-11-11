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
  // A platform whose server runs without Wine wherever the test runs
  // (protocol/lip-workspace.md, "Running programs").
  private static readonly string _hostPlatform =
      OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

  private readonly string _root =
      Path.Combine(Path.GetTempPath(), "lip-run-test-" + Guid.NewGuid().ToString("N"));

  private readonly FileSystem _fs = new();
  private readonly LockStore _lockStore;
  private readonly McmodService _service;
  private readonly Mock<IUserInteraction> _ui = new();
  private readonly ServerSessionTests.FakeServer _server = new();
  private readonly List<ProgramStartInfo> _started = [];
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
        workspaceRoot: () => _root);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }

    GC.SuppressFinalize(this);
  }

  private string ServerPath => Path.Join(_root, "bedrock_server_mod.exe");

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

  private async Task SaveState(string side = "server") {
    await _lockStore.Save(
        _root, new LockFile { Side = side, Platform = _hostPlatform, Packages = [] });
  }

  [Fact]
  public async Task Run_Server_RunsItInTheWorkspaceAndReturnsItsExitCode() {
    // Arrange
    await SaveState();
    File.WriteAllText(ServerPath, "");

    // Act
    int code = await Run();

    // Assert
    Assert.Equal(3, code);
    ProgramStartInfo start = Assert.Single(_started);
    Assert.Equal(ServerPath, start.FileName);
    Assert.Empty(start.Arguments);
    Assert.Equal(_root, start.WorkingDirectory);
  }

  [Fact]
  public async Task Run_NoStateFile_RunsForWinX64() {
    // Arrange
    File.WriteAllText(ServerPath, "");
    ProgramStartInfo expected;
    try {
      expected = WorkspaceProgram.Server(ProgramHost.Current("win-x64", _root));
    }
    catch (InvalidOperationException) {
      // Wine runs the server here, and is not found through PATH.
      InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(Run);
      Assert.Contains("for the platform win-x64", ex.Message);
      Assert.Contains("Install Wine", ex.Message);
      Assert.Empty(_started);
      return;
    }

    // Act
    await Run();

    // Assert
    Assert.Equal(expected, Assert.Single(_started), new StartComparer());
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
    if (directory) {
      Directory.CreateDirectory(ServerPath);
    }

    // Act & Assert
    FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(Run);
    Assert.Contains("bedrock_server_mod.exe", ex.Message);
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

  private sealed class StartComparer : IEqualityComparer<ProgramStartInfo> {
    public bool Equals(ProgramStartInfo? x, ProgramStartInfo? y) =>
        x is not null && y is not null
        && x.FileName == y.FileName
        && x.Arguments.SequenceEqual(y.Arguments)
        && x.VerbatimArguments == y.VerbatimArguments
        && x.WorkingDirectory == y.WorkingDirectory
        && x.EnvironmentOverrides.OrderBy(v => v.Key).SequenceEqual(
            y.EnvironmentOverrides.OrderBy(v => v.Key));

    public int GetHashCode(ProgramStartInfo obj) => obj.FileName.GetHashCode();
  }
}
