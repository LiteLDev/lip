using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Mcmod.Compat;

/// Installing and uninstalling packages whose tooth file is of format 3 with McmodInstaller: they
/// are installed with the tooth file of format 4 derived from it (protocol/package.md §5), which
/// the workspace file records (protocol/workspace.md §7.1), with nothing of format 3 left.
public class ToothInstallTests {
  private const string _uuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private readonly Mock<ICommandRunner> _commandRunner = new();
  private readonly MockFileSystem _fileSystem = new();
  private readonly Mock<IArchiveProvider> _archiveProvider = new();
  private readonly Mock<IUserInteraction> _userInteraction = new();
  private readonly SoloInstaller _installer;
  private readonly string _root;

  public ToothInstallTests() {
    _userInteraction.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    _userInteraction.Setup(u => u.PrintWarning(It.IsAny<string>())).Returns(Task.CompletedTask);
    _installer = new SoloInstaller(_fileSystem, new McmodInstaller(_fileSystem, _commandRunner.Object, _userInteraction.Object));
    _root = _fileSystem.Path.GetFullPath("ws");
    _fileSystem.Directory.CreateDirectory(_root);
    _archiveProvider.Setup(p => p.OpenAsset(
            It.Is<IReadOnlyList<string>>(u => u.Contains("https://example.com/a.zip"))))
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

  /// The candidate of the variant of a tooth file of format 3 for the environment, as the
  /// resolver makes it: the entry derived from it (protocol/package.md §5).
  private Candidate CreateCandidateFrom(
      string json,
      Dictionary<string, string> selfFiles,
      string variant = "",
      LipEnvironment? environment = null) {
    LipEnvironment env = environment ?? _serverWin;
    ToothDocument document = ToothDocument.Parse(Encoding.UTF8.GetBytes(json));
    Assert.True(document.IsLegacy);
    ToothFile tooth = document.ReadFor(env);
    ToothEntry entry = tooth.SelectEntry(variant, env)!;
    Assert.True(entry.Derived);
    ArchiveHandle self = new(
        $"{tooth.Tooth}@{tooth.Version}",
        CreateSource(new(selfFiles) { ["tooth.json"] = json }),
        new string('a', 40));
    return Candidate.Create(
        new Package(tooth.Tooth, variant, tooth.Version, entry) { Manifest = tooth.Json },
        self,
        Ref.Parse(tooth.Tooth, allowFile: false).WithVariant(variant),
        _archiveProvider.Object);
  }

  private Candidate CreateCandidate(
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
      new(
          _root,
          environment ?? _serverWin,
          new LockFile {
            Platform = (environment ?? _serverWin).Platform.ToSpecString(),
            Runtime = (environment ?? _serverWin).Runtime,
            Packages = [],
          },
          dryRun,
          ignoreScripts);

  private string Workspace(string relativePath) => _fileSystem.Path.Combine(
      _root, relativePath.Replace('/', Path.DirectorySeparatorChar));

  /// The workspace file entry of an installed candidate (protocol/workspace.md §7.1).
  private static LockedPackage Lock(Candidate candidate, IReadOnlyList<string> files) => new() {
    Manifest = candidate.Package.Manifest,
    Variant = candidate.Package.Variant,
    Explicit = true,
    Source = candidate.Source.ToString(),
    Commit = candidate.Archive.Commit,
    Files = [.. files],
  };

  [Fact]
  public async Task Install_SelfAndZipAssets_PlacesFilesInV3Order() {
    // Arrange
    Candidate candidate = CreateCandidate();

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
    Candidate candidate = CreateCandidateFrom(
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

    // Assert: the placements are carried out as format 4 carries out the members of place they
    // become; the tooth file is never written.
    Assert.Equal(
        [
          "mods/g/a.dll",
          "mods/g/b.dll",
          "all/bin/a.dll",
          "all/bin/b.dll",
          "all/bin/c.txt",
        ],
        files);
    // The file placement of bin/a.dll at plugins/g/b.dll comes after the glob, so it wins.
    Assert.Equal("a", _fileSystem.File.ReadAllText(Workspace("mods/g/b.dll")));
    Assert.False(_fileSystem.Directory.Exists(Workspace("plugins/never")));
    Assert.False(_fileSystem.File.Exists(Workspace("all/tooth.json")));
  }

  [Fact]
  public async Task Install_MissingSrc_FailsAsInFormat4() {
    // Arrange: lip v0.37.0 skipped placements matching nothing; format 4 does not.
    Candidate candidate = CreateCandidateFrom(
        $$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/missing", "version": "1.0.0",
          "variants": [{ "assets": [
            { "type": "self", "placements": [
              { "type": "file", "src": "missing.dll", "dest": "plugins/m/missing.dll" },
              { "type": "dir", "src": "nodir", "dest": "plugins/m/nodir" },
              { "type": "file", "src": "a.dll", "dest": "plugins/m/a.dll" }
            ] }
          ] }]
        }
        """,
        new() { ["a.dll"] = "a" });

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, CreateContext(ignoreScripts: true)));
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods")));
  }

  [Fact]
  public async Task Install_ExistingFile_Overwrites() {
    // Arrange
    Candidate candidate = CreateCandidate();
    _fileSystem.AddFile(Workspace("mods/repo/a.dll"), new MockFileData("old"));

    // Act
    IReadOnlyList<string> files = await _installer.Install(candidate, CreateContext());

    // Assert
    Assert.Contains("mods/repo/a.dll", files);
    Assert.Equal("dll", _fileSystem.File.ReadAllText(Workspace("mods/repo/a.dll")));
  }

  [Fact]
  public async Task Install_PreservedExistingFile_IsKept() {
    // Arrange
    Candidate candidate = CreateCandidate();
    _fileSystem.AddFile(Workspace("mods/repo/data/config.json"), new MockFileData("user"));

    // Act
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));

    // Assert
    Assert.DoesNotContain("mods/repo/data/config.json", files);
    Assert.Equal("user", _fileSystem.File.ReadAllText(Workspace("mods/repo/data/config.json")));
  }

  [Fact]
  public async Task Install_Scripts_RunInOrderInWorkspaceRoot() {
    // Arrange
    Candidate candidate = CreateCandidate();
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
    Candidate candidate = CreateCandidate();

    // Act
    await _installer.Install(candidate, CreateContext(ignoreScripts: true));

    // Assert
    _commandRunner.VerifyNoOtherCalls();
    Assert.True(_fileSystem.File.Exists(Workspace("tool.exe")));
  }

  [Fact]
  public async Task Install_DryRun_TouchesNothing() {
    // Arrange
    Candidate candidate = CreateCandidate();

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
    _archiveProvider.Setup(p => p.OpenAsset(It.IsAny<IReadOnlyList<string>>()))
        .ThrowsAsync(new ArchiveUnavailableException("down"));
    Candidate candidate = CreateCandidate();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, CreateContext()));
    Assert.False(_fileSystem.Directory.Exists(Workspace("mods")));
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_InstalledPackage_RemovesOwnedFilesExceptPreserved() {
    // Arrange
    Candidate candidate = CreateCandidate();
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
    Candidate candidate = CreateCandidateFrom(
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
    Candidate candidate = CreateCandidateFrom(
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
    Candidate candidate = CreateCandidateFrom(
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
  public async Task Uninstall_ClientEnvironment_UsesEntryDerivedFromClientLabel() {
    // Arrange
    LipEnvironment client = new(McmodPlatform.Client, "win-x64");
    Candidate candidate = CreateCandidate(environment: client);

    // Act
    await _installer.Uninstall(Lock(candidate, []), CreateContext(environment: client));

    // Assert
    _commandRunner.Verify(c => c.Run("echo client", _root), Times.Once);
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_RecordedToothFileWithoutEntryForTheEnvironment_Throws() {
    // Arrange: the recorded tooth file was derived for (server, win-x64).
    Candidate candidate = CreateCandidate();
    _fileSystem.AddFile(Workspace("tool.exe"), new MockFileData("exe"));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Uninstall(
        Lock(candidate, ["tool.exe"]),
        CreateContext(
            ignoreScripts: true,
            environment: new LipEnvironment(McmodPlatform.Server, "linux-x64"))));
    Assert.True(_fileSystem.File.Exists(Workspace("tool.exe")));
  }

  [Fact]
  public async Task Uninstall_MissingFile_Skips() {
    // Arrange
    Candidate candidate = CreateCandidate();
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
    Candidate candidate = CreateCandidate();
    IReadOnlyList<string> files = await _installer.Install(
        candidate, CreateContext(ignoreScripts: true));

    // Act
    await _installer.Uninstall(Lock(candidate, files), CreateContext(dryRun: true));

    // Assert
    Assert.True(_fileSystem.File.Exists(Workspace("mods/repo/a.dll")));
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public void LockedEntry_DerivedToothFile_IsReadAsFormat4() {
    // Arrange
    Candidate candidate = CreateCandidate();

    // Act
    ToothEntry entry = McmodInstaller.LockedEntry(
        JsonSerializer.Deserialize<LockedPackage>(JsonSerializer.Serialize(Lock(candidate, [])))!,
        _serverWin);

    // Assert
    Assert.False(entry.Derived);
    Assert.Equal(AssetType.Zip, entry.Assets[1].Type);
    Assert.Equal(
        [new EntryPlacement("data/", "plugins/repo/data/")], entry.Assets[0].Placements.Skip(1));
    Assert.Equal(["plugins/repo/logs/"], entry.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal(["echo preun", "echo un"], entry.Hooks.PreUninstall);
  }
}
