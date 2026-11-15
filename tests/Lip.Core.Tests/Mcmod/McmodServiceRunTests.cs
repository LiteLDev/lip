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
}
