using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Mcmod.Compat;

/// Installing and uninstalling tooth.json packages converted to format 4 (spec §9) with
/// McmodInstaller: the v3 behaviour that the conversion keeps.
public class ToothInstallTests {
  private const string _uuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private readonly Mock<ICommandRunner> _commandRunner = new();
  private readonly MockFileSystem _fileSystem = new();
  private readonly Mock<IArchiveProvider> _archiveProvider = new();
  private readonly Mock<IUserInteraction> _userInteraction = new();
  private readonly McmodInstaller _installer;
  private readonly string _root;

  public ToothInstallTests() {
    _installer = new McmodInstaller(
        _fileSystem, _commandRunner.Object, new DocumentParser(), _userInteraction.Object);
    _root = _fileSystem.Path.GetFullPath("ws");
    _fileSystem.Directory.CreateDirectory(_root);
    _archiveProvider.Setup(p => p.OpenAsset(
            It.Is<IReadOnlyList<string>>(u => u.Contains("https://example.com/a.zip")), true))
        .ReturnsAsync(CreateSource(new() { ["tool.exe"] = "exe", ["other.txt"] = "x" }));
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
                "urls": ["https://example.com/a.zip"],
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

  private async Task<Candidate> CreateCandidateFrom(
      string json,
      Dictionary<string, string> selfFiles,
      string variant = "",
      LipEnvironment? environment = null) {
    PackageManifest manifest = await ParseManifest(json);
    ToothConversion conversion = ToothCompat.Convert(manifest, variant, environment ?? _serverWin)!;
    ArchiveHandle self = new(
        $"{manifest.Path}@{manifest.Version}",
        CreateSource(new(selfFiles) { ["tooth.json"] = json }),
        new GitArchiveIdentity($"https://{manifest.Path}.git", "abc", ""));
    return new Candidate(
        conversion.Package,
        conversion.OpenArchive(self, _archiveProvider.Object),
        [new ChainHop(Ref.Parse(manifest.Path).WithVariant(variant), null)]);
  }

  private Task<Candidate> CreateCandidate(
      string variant = "", LipEnvironment? environment = null) => CreateCandidateFrom(
          _manifestJson,
          new() {
            ["bin/a.dll"] = "dll",
            ["data/config.json"] = "{}",
            ["data/sub/b.txt"] = "b",
            ["README.md"] = "readme",
          },
          variant,
          environment);

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
    Assert.True(_installer.CanHandle("mcmod"));
  }

  [Fact]
  public async Task Install_SelfAndZipAssets_PlacesFilesInV3Order() {
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
        files);
    Assert.Equal("dll", _fileSystem.File.ReadAllText(Workspace("mods/repo/a.dll")));
    Assert.Equal("b", _fileSystem.File.ReadAllText(Workspace("mods/repo/data/sub/b.txt")));
    Assert.Equal("exe", _fileSystem.File.ReadAllText(Workspace("tool.exe")));
    Assert.False(_fileSystem.File.Exists(Workspace("README.md")));
    Assert.False(_fileSystem.File.Exists(Workspace("other.txt")));
  }

  [Fact]
  public async Task Install_GlobAndOverlappingPlacements_LaterPlacementWins() {
    // Arrange
    Candidate candidate = await CreateCandidateFrom(
        $$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/glob", "version": "1.0.0",
          "variants": [{ "assets": [
            { "type": "self", "placements": [
              { "type": "file", "src": "bin/*.dll", "dest": "plugins/g" },
              { "type": "file", "src": "bin/a.dll", "dest": "plugins/g/b.dll" },
              { "type": "dir", "src": "bin/*", "dest": "plugins/never" },
              { "type": "dir", "src": "", "dest": "./all/." }
            ] }
          ] }]
        }
        """,
        new() { ["bin/a.dll"] = "a", ["bin/b.dll"] = "b", ["bin/c.txt"] = "c" });

    // Act
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));

    // Assert
    Assert.Equal(
        [
          "mods/g/a.dll",
          "mods/g/b.dll",
          "all/bin/a.dll",
          "all/bin/b.dll",
          "all/bin/c.txt",
          "all/tooth.json",
        ],
        files);
    // bin/b.dll is placed after bin/a.dll's second placement, so it wins.
    Assert.Equal("b", _fileSystem.File.ReadAllText(Workspace("mods/g/b.dll")));
    Assert.False(_fileSystem.Directory.Exists(Workspace("plugins/never")));
  }

  [Fact]
  public async Task Install_UncompressedAsset_PlacesTheFile() {
    // Arrange
    _archiveProvider.Setup(p => p.OpenAsset(
            It.Is<IReadOnlyList<string>>(u => u.Contains("https://example.com/node.zip")), false))
        .ReturnsAsync(CreateSource(new() { [""] = "node" }));
    Candidate candidate = await CreateCandidateFrom(
        $$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/single", "version": "1.0.0",
          "variants": [{ "assets": [
            { "type": "uncompressed", "urls": ["https://example.com/node.zip"], "placements": [
              { "type": "file", "src": "", "dest": "plugins/x/node.zip" }
            ] }
          ] }]
        }
        """,
        []);

    // Act
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));

    // Assert
    Assert.Equal(["mods/x/node.zip"], files);
    Assert.Equal("node", _fileSystem.File.ReadAllText(Workspace("mods/x/node.zip")));
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
    Assert.Contains("tool.exe", files);
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods")));
    Assert.False(_fileSystem.File.Exists(Workspace("tool.exe")));
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Install_AssetUnavailable_ThrowsAndWritesNothing() {
    // Arrange
    _archiveProvider.Setup(p => p.OpenAsset(It.IsAny<IReadOnlyList<string>>(), true))
        .ThrowsAsync(new ArchiveUnavailableException("down"));
    Candidate candidate = await CreateCandidate();

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => _installer.Install(candidate, CreateContext()));
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods")));
    _commandRunner.VerifyNoOtherCalls();
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
  public async Task Uninstall_PreserveFileName_KeepsFileInAnyDirectory() {
    // Arrange
    Candidate candidate = await CreateCandidateFrom(
        $$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/named", "version": "1.0.0",
          "variants": [{
            "assets": [{ "type": "self", "placements": [
              { "type": "dir", "src": "p", "dest": "plugins/p" }
            ] }],
            "preserve_files": ["config.json"]
          }]
        }
        """,
        new() { ["p/config.json"] = "{}", ["p/p.js"] = "js" });
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));

    // Act
    await _installer.Uninstall(Lock(candidate, files), CreateContext());

    // Assert
    Assert.True(_fileSystem.File.Exists(Workspace("mods/p/config.json")));
    Assert.False(_fileSystem.File.Exists(Workspace("mods/p/p.js")));
  }

  [Fact]
  public async Task Uninstall_RemoveFilesWithoutSlash_RemovesDirectory() {
    // Arrange
    Candidate candidate = await CreateCandidateFrom(
        $$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/remover", "version": "1.0.0",
          "variants": [{ "remove_files": ["plugins/Remover", "stray.log"] }]
        }
        """,
        []);
    _fileSystem.AddFile(Workspace("mods/Remover/data/db.json"), new MockFileData("{}"));
    _fileSystem.AddFile(Workspace("stray.log"), new MockFileData("log"));
    _fileSystem.AddFile(Workspace("sub/stray.log"), new MockFileData("log"));

    // Act
    await _installer.Uninstall(Lock(candidate, []), CreateContext());

    // Assert
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods/Remover")));
    Assert.False(_fileSystem.File.Exists(Workspace("stray.log")));
    Assert.True(_fileSystem.File.Exists(Workspace("sub/stray.log")));
  }

  [Fact]
  public async Task Uninstall_RemoveFilesNamingLayoutDirectories_RemovesNothingBelowThem() {
    // Arrange
    Candidate candidate = await CreateCandidateFrom(
        $$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/remover", "version": "1.0.0",
          "variants": [{ "remove_files": ["plugins", "mods/"] }]
        }
        """,
        []);
    _fileSystem.AddFile(Workspace("mods/Other/other.dll"), new MockFileData("dll"));

    // Act
    await _installer.Uninstall(Lock(candidate, []), CreateContext());

    // Assert
    Assert.True(_fileSystem.File.Exists(Workspace("mods/Other/other.dll")));
  }

  [Fact]
  public async Task Uninstall_ClientEnvironment_UsesClientLabel() {
    // Arrange
    LipEnvironment client = new(McmodPlatform.Client, "win-x64");
    Candidate candidate = await CreateCandidate(environment: client);

    // Act
    await _installer.Uninstall(Lock(candidate, []), CreateContext(environment: client));

    // Assert
    _commandRunner.Verify(c => c.Run("echo client", _root), Times.Once);
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_MissingFile_Skips() {
    // Arrange
    Candidate candidate = await CreateCandidate();
    _fileSystem.AddFile(Workspace("tool.exe"), new MockFileData("exe"));

    // Act
    await _installer.Uninstall(
        Lock(candidate, ["gone.txt", "tool.exe"]),
        CreateContext(ignoreScripts: true));

    // Assert
    Assert.False(_fileSystem.File.Exists(Workspace("tool.exe")));
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
  public async Task Uninstall_NoVariantForEnvironment_Throws() {
    // Arrange
    Candidate candidate = await CreateCandidate();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Uninstall(
        Lock(candidate, []),
        CreateContext(environment: new LipEnvironment(McmodPlatform.Server, "linux-x64"))));
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
    _fileSystem.AddFile(Workspace("a.txt"), new MockFileData("a"));
    _fileSystem.AddFile(Workspace("keep.txt"), new MockFileData("keep"));
    LockedPackage locked = new() {
      Identity = "github.com/user/old",
      Version = "1.0.0",
      Kind = "tooth",
      Explicit = true,
      Chain = [new LockedHop("github.com/user/old", null)],
      ArchiveKey = "github.com/user/old@1.0.0",
      Files = ["a.txt"],
      Manifest = JsonSerializer.SerializeToElement(manifest),
    };

    // Act
    await _installer.Uninstall(locked, CreateContext());

    // Assert
    Assert.False(_fileSystem.File.Exists(Workspace("a.txt")));
    Assert.True(_fileSystem.File.Exists(Workspace("keep.txt")));
  }
}
