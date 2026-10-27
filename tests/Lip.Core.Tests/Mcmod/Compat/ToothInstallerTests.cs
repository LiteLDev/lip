using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Mcmod.Compat;

public class ToothInstallerTests {
  private const string _uuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private readonly Mock<ICommandRunner> _commandRunner = new();
  private readonly MockFileSystem _fileSystem = new();
  private readonly Mock<ISourceService> _sourceService = new();
  private readonly Mock<IUserInteraction> _userInteraction = new();
  private readonly ToothInstaller _installer;
  private readonly string _root;

  public ToothInstallerTests() {
    _installer = new ToothInstaller(
        _commandRunner.Object,
        _fileSystem,
        _sourceService.Object,
        _userInteraction.Object);
    _root = _fileSystem.Path.GetFullPath("ws");
    _fileSystem.Directory.CreateDirectory(_root);
  }

  private static readonly string _manifestJson = $$"""
      {
        "format_version": 3,
        "format_uuid": "{{_uuid}}",
        "tooth": "github.com/user/repo",
        "version": "1.0.0",
        "variants": [
          {
            "platform": "win-x64",
            "assets": [
              { "type": "self", "placements": [
                { "type": "file", "src": "bin/a.dll", "dest": "plugins/repo/a.dll" },
                { "type": "dir", "src": "data", "dest": "plugins/repo/data" }
              ] },
              { "type": "zip",
                "urls": ["https://bad.example.com/a.zip", "https://good.example.com/a.zip"],
                "placements": [
                  { "type": "file", "src": "tool.exe", "dest": "tool.exe" }
                ] }
            ],
            "preserve_files": ["plugins/repo/data/config.json"],
            "remove_files": ["plugins/repo/logs/"],
            "scripts": {
              "pre_install": ["echo pre"],
              "install": ["echo install"],
              "post_install": ["echo post"],
              "pre_uninstall": ["echo preun"],
              "uninstall": ["echo un"],
              "post_uninstall": ["echo postun"]
            }
          },
          {
            "label": "client",
            "platform": "win-x64",
            "scripts": { "pre_uninstall": ["echo client"] }
          }
        ]
      }
      """;

  private static ISource CreateSource(Dictionary<string, string> files) {
    Mock<ISource> source = new();
    source.Setup(s => s.Keys).Returns(files.Keys);
    source.Setup(s => s.OpenRead(It.IsAny<string>()))
        .ReturnsAsync((string key) => new MemoryStream(Encoding.UTF8.GetBytes(files[key])));
    return source.Object;
  }

  private static async Task<PackageManifest> ParseManifest(string json) =>
      await PackageManifest.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));

  private async Task<Candidate> CreateCandidate(
      string variant = "",
      LipEnvironment? environment = null) {
    PackageManifest manifest = await ParseManifest(_manifestJson);
    ToothPackage package = ToothCompat.Select(manifest, variant, environment ?? _serverWin)!;
    ISource self = CreateSource(new() {
      ["tooth.json"] = _manifestJson,
      ["bin/a.dll"] = "dll",
      ["data/config.json"] = "{}",
      ["data/sub/b.txt"] = "b",
      ["README.md"] = "readme",
    });

    _sourceService.Setup(s => s.Get(It.Is<Url>(u => u.Host == "bad.example.com"), true))
        .ThrowsAsync(new HttpRequestException("down"));
    _sourceService.Setup(s => s.Get(It.Is<Url>(u => u.Host == "good.example.com"), true))
        .ReturnsAsync(CreateSource(new() { ["tool.exe"] = "exe", ["other.txt"] = "x" }));

    return new Candidate(
        package,
        new ArchiveHandle("github.com/user/repo@1.0.0", self, new DirectoryArchiveIdentity("/x")),
        [new ChainHop(Ref.Parse("github.com/user/repo").WithVariant(variant), null)]);
  }

  private InstallContext CreateContext(
      bool dryRun = false,
      bool ignoreScripts = false,
      LipEnvironment? environment = null) =>
      new(_root, environment ?? _serverWin, new LockFile(), dryRun, ignoreScripts);

  private string Workspace(string relativePath) => _fileSystem.Path.Combine(
      _root, relativePath.Replace('/', Path.DirectorySeparatorChar));

  private static LockedPackage Lock(Candidate candidate, IReadOnlyList<string> files) => new() {
    Identity = candidate.Package.Identity,
    Version = candidate.Package.Version.ToString(),
    Kind = candidate.Package.Kind,
    Explicit = true,
    Chain = [.. candidate.Chain.Select(h => new LockedHop(h.Ref.ToString(), h.ArtifactIndex))],
    ArchiveKey = candidate.Archive.Key,
    Files = [.. files],
    Manifest = candidate.Package.Manifest,
  };

  [Fact]
  public void CanHandle_Tooth_ReturnsTrue() {
    // Arrange & Act & Assert
    Assert.True(_installer.CanHandle("tooth"));
    Assert.False(_installer.CanHandle("mcmod"));
  }

  [Fact]
  public async Task Install_SelfAndZipAssets_PlacesFilesAndReturnsRelativePaths() {
    // Arrange
    Candidate candidate = await CreateCandidate();

    // Act
    IReadOnlyList<string> files = await _installer.Install(candidate, CreateContext());

    // Assert
    Assert.Equal(
        [
          "mods/repo/a.dll",
          "mods/repo/data/config.json",
          "mods/repo/data/sub/b.txt",
          "tool.exe",
        ],
        files.Order(StringComparer.Ordinal));
    Assert.Equal("dll", _fileSystem.File.ReadAllText(Workspace("mods/repo/a.dll")));
    Assert.Equal("b", _fileSystem.File.ReadAllText(Workspace("mods/repo/data/sub/b.txt")));
    Assert.Equal("exe", _fileSystem.File.ReadAllText(Workspace("tool.exe")));
    Assert.False(_fileSystem.File.Exists(Workspace("README.md")));
    Assert.False(_fileSystem.File.Exists(Workspace("other.txt")));
  }

  [Fact]
  public async Task Install_ExistingFile_Overwrites() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    _fileSystem.AddFile(Workspace("mods/repo/a.dll"), new MockFileData("old"));

    // Act
    IReadOnlyList<string> files = await _installer.Install(candidate, CreateContext());

    // Assert
    Assert.Contains("mods/repo/a.dll", files);
    Assert.Equal("dll", _fileSystem.File.ReadAllText(Workspace("mods/repo/a.dll")));
  }

  [Fact]
  public async Task Install_Scripts_RunInOrderInWorkspaceRoot() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    List<string> calls = [];
    _commandRunner.Setup(c => c.Run(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string command, string workingDirectory) => {
          Assert.Equal(_root, workingDirectory);
          // Pre-install runs before files are placed, the others after.
          calls.Add($"{command}:{_fileSystem.File.Exists(Workspace("mods/repo/a.dll"))}");
        })
        .Returns(Task.CompletedTask);

    // Act
    await _installer.Install(candidate, CreateContext());

    // Assert
    Assert.Equal(["echo pre:False", "echo install:True", "echo post:True"], calls);
  }

  [Fact]
  public async Task Install_IgnoreScripts_RunsNoScripts() {
    // Arrange
    Candidate candidate = await CreateCandidate();

    // Act
    await _installer.Install(candidate, CreateContext(ignoreScripts: true));

    // Assert
    _commandRunner.VerifyNoOtherCalls();
    Assert.True(_fileSystem.File.Exists(Workspace("tool.exe")));
  }

  [Fact]
  public async Task Install_DryRun_TouchesNothing() {
    // Arrange
    Candidate candidate = await CreateCandidate();

    // Act
    IReadOnlyList<string> files = await _installer.Install(candidate, CreateContext(dryRun: true));

    // Assert
    Assert.Empty(files);
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods")));
    _commandRunner.VerifyNoOtherCalls();
    _sourceService.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Install_AllUrlsFail_Throws() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    _sourceService.Setup(s => s.Get(It.IsAny<Url>(), true))
        .ThrowsAsync(new HttpRequestException("down"));

    // Act & Assert
    await Assert.ThrowsAsync<AggregateException>(
        () => _installer.Install(candidate, CreateContext(ignoreScripts: true)));
  }

  [Fact]
  public async Task Install_NotToothPackage_Throws() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    Mock<IPackage> other = new();
    other.Setup(p => p.Identity).Returns("Other");

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
        () => _installer.Install(candidate with { Package = other.Object }, CreateContext()));
  }

  [Fact]
  public async Task Uninstall_InstalledPackage_RemovesOwnedFilesExceptPreserved() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));
    _fileSystem.AddFile(Workspace("mods/repo/logs/today.log"), new MockFileData("log"));
    _fileSystem.AddFile(Workspace("unrelated.txt"), new MockFileData("keep"));
    List<string> calls = [];
    _commandRunner.Setup(c => c.Run(It.IsAny<string>(), _root))
        .Callback((string command, string? _) => calls.Add(command))
        .Returns(Task.CompletedTask);

    // Act
    await _installer.Uninstall(Lock(candidate, files), CreateContext());

    // Assert
    Assert.False(_fileSystem.File.Exists(Workspace("mods/repo/a.dll")));
    Assert.False(_fileSystem.File.Exists(Workspace("mods/repo/data/sub/b.txt")));
    Assert.False(_fileSystem.File.Exists(Workspace("tool.exe")));
    Assert.True(_fileSystem.File.Exists(Workspace("mods/repo/data/config.json")));
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods/repo/logs")));
    Assert.True(_fileSystem.File.Exists(Workspace("unrelated.txt")));
    Assert.Equal(["echo preun", "echo un", "echo postun"], calls);
  }

  [Fact]
  public async Task Uninstall_ClientEnvironment_UsesClientLabel() {
    // Arrange
    LipEnvironment client = new(McmodPlatform.Client, "win-x64");
    Candidate candidate = await CreateCandidate(environment: client);
    Assert.Equal("client", ((ToothPackage)candidate.Package).Label);

    // Act
    await _installer.Uninstall(Lock(candidate, []), CreateContext(environment: client));

    // Assert
    _commandRunner.Verify(c => c.Run("echo client", _root), Times.Once);
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_MissingFile_Warns() {
    // Arrange
    Candidate candidate = await CreateCandidate();

    // Act
    await _installer.Uninstall(
        Lock(candidate, ["gone.txt"]),
        CreateContext(ignoreScripts: true));

    // Assert
    _userInteraction.Verify(u => u.PrintWarning(It.Is<string>(s => s.Contains("gone.txt"))));
  }

  [Fact]
  public async Task Uninstall_DryRun_TouchesNothing() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));

    // Act
    await _installer.Uninstall(Lock(candidate, files), CreateContext(dryRun: true));

    // Assert
    Assert.True(_fileSystem.File.Exists(Workspace("mods/repo/a.dll")));
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_V1ManifestFromLock_UsesMigratedManifest() {
    // Arrange
    PackageManifest manifest = await ParseManifest("""
        {
          "format_version": 1,
          "tooth": "github.com/user/old",
          "version": "1.0.0",
          "placement": [{ "source": "a.txt", "destination": "a.txt" }],
          "possession": ["keep.txt"]
        }
        """);
    ToothPackage package = ToothCompat.Select(manifest, "", _serverWin)!;
    _fileSystem.AddFile(Workspace("a.txt"), new MockFileData("a"));
    LockedPackage locked = new() {
      Identity = package.Identity,
      Version = "1.0.0",
      Kind = "tooth",
      Explicit = true,
      Chain = [new LockedHop("github.com/user/old", null)],
      ArchiveKey = "github.com/user/old@1.0.0",
      Files = ["a.txt"],
      Manifest = JsonDocument.Parse(package.Manifest.GetRawText()).RootElement,
    };

    // Act
    await _installer.Uninstall(locked, CreateContext());

    // Assert
    Assert.False(_fileSystem.File.Exists(Workspace("a.txt")));
  }
}
