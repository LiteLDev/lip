using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Services;

public class PackageInstallerTests {
  private readonly Mock<ICommandRunner> _mockCommandRunner;
  private readonly MockFileSystem _mockFileSystem;
  private readonly Mock<IUserInteraction> _userInteraction;
  private readonly Mock<ISourceService> _mockSourceService;
  private readonly Mock<IWorkspaceService> _mockWorkspaceService;
  private readonly PackageInstaller _installer;

  public PackageInstallerTests() {
    _mockCommandRunner = new Mock<ICommandRunner>();
    _mockFileSystem = new MockFileSystem();
    _userInteraction = new Mock<IUserInteraction>();
    _mockSourceService = new Mock<ISourceService>();
    _mockWorkspaceService = new Mock<IWorkspaceService>();

    _installer = new PackageInstaller(
        _mockCommandRunner.Object,
        _mockFileSystem,
        _userInteraction.Object,
        _mockSourceService.Object,
        _mockWorkspaceService.Object);
  }

  [Fact]
  public async Task InstallPackage_AlreadyInstalled_ThrowsException() {
    // Arrange
    PackageSpec pkgSpec = new(new PackageId("github.com/test/pkg", string.Empty), new SemVersion(1, 0, 0));
    PackageArtifact artifact = new(pkgSpec, new Mock<ISource>().Object);

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.InstallPackage(
        artifact,
        dryRun: false,
        explicitInstall: false,
        ignoreScripts: false));
  }

  [Fact]
  public async Task UninstallPackage_NotInstalled_ThrowsException() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", string.Empty);

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.UninstallPackage(
        pkgId,
        dryRun: false,
        ignoreScripts: false));
  }
  [Fact]
  public async Task InstallPackage_ValidArtifact_InstallsFiles() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    SemVersion pkgVer = new(1, 0, 0);
    PackageSpec pkgSpec = new(pkgId, pkgVer);

    // Setup WorkspaceService to allow install
    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    // Setup Manifest
    string manifestJson = """
            {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/test/pkg",
                "version": "1.0.0",
                "variants": [
                    {
                        "assets": [
                            {
                                "type": "self",
                                "placements": [
                                    { "type": "file", "src": "file.txt", "dest": "plugins/file.txt" }
                                ] 
                            }
                        ]
                    }
                ]
            }
            """;
    Mock<ISource> mockSource = new();
    mockSource.Setup(p => p.OpenRead("tooth.json"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));

    mockSource.Setup(p => p.Keys).Returns(["file.txt"]);
    mockSource.Setup(p => p.OpenRead("file.txt"))
        .ReturnsAsync(new MemoryStream("content"u8.ToArray()));

    PackageArtifact artifact = new(pkgSpec, mockSource.Object);

    // Act
    await _installer.InstallPackage(artifact, false, false, false);

    // Assert
    Assert.True(_mockFileSystem.File.Exists(Path.Combine("plugins", "file.txt")));
    _mockWorkspaceService.Verify(w => w.AddInstalledPackage(pkgSpec, It.IsAny<PackageManifest>(), It.IsAny<IEnumerable<IFileInfo>>(), false), Times.Once);
  }

  [Fact]
  public async Task InstallPackage_ManifestDeclaresOtherPathAndVersion_UsesRequestedIdentity() {
    // Arrange
    PackageSpec pkgSpec = new(new PackageId("github.com/Old/Name", ""), new SemVersion(1, 2, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    string manifestJson = """
            {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/New/Name",
                "version": "1.1.0",
                "variants": [ { "assets": [] } ]
            }
            """;
    Mock<ISource> mockSource = new();
    mockSource.Setup(p => p.OpenRead("tooth.json"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
    mockSource.Setup(p => p.Keys).Returns(["tooth.json"]);

    // Act
    await _installer.InstallPackage(new(pkgSpec, mockSource.Object), false, true, false);

    // Assert
    _mockWorkspaceService.Verify(w => w.AddInstalledPackage(
        pkgSpec,
        It.Is<PackageManifest>(m => m.Path == "github.com/Old/Name" && m.Version == new SemVersion(1, 2, 0)),
        It.IsAny<IEnumerable<IFileInfo>>(),
        true), Times.Once);
    _userInteraction.Verify(u => u.PrintWarning(It.Is<string>(s => s.Contains("github.com/New/Name"))), Times.Once);
  }

  [Fact]
  public async Task InstallPackage_RunsScripts() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    string manifestJson = """
            {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/test/pkg",
                "version": "1.0.0",
                "variants": [
                    {
                        "scripts": {
                            "pre_install": ["echo pre"],
                            "post_install": ["echo post"]
                        }
                    }
                ]
            }
            """;

    Mock<ISource> mockSource = new();
    mockSource.Setup(p => p.OpenRead("tooth.json"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
    mockSource.Setup(p => p.Keys).Returns([]);

    PackageArtifact artifact = new(pkgSpec, mockSource.Object);

    // Act
    await _installer.InstallPackage(artifact, false, false, false);

    // Assert
    _mockCommandRunner.Verify(c => c.Run("echo pre"), Times.Once);
    _mockCommandRunner.Verify(c => c.Run("echo post"), Times.Once);
  }

  [Fact]
  public async Task InstallPackage_InstallAliasRunsAfterFilesArePlaced() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    string filePath = Path.Combine("plugins", "file.txt");
    string manifestJson = """
            {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/test/pkg",
                "version": "1.0.0",
                "variants": [
                    {
                        "assets": [
                            {
                                "type": "self",
                                "placements": [
                                    { "type": "file", "src": "file.txt", "dest": "plugins/file.txt" }
                                ]
                            }
                        ],
                        "scripts": {
                            "pre_install": ["echo pre"],
                            "install": ["echo install"],
                            "post_install": ["echo post"]
                        }
                    }
                ]
            }
            """;

    Mock<ISource> mockSource = new();
    mockSource.Setup(p => p.OpenRead("tooth.json"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
    mockSource.Setup(p => p.Keys).Returns(["file.txt"]);
    mockSource.Setup(p => p.OpenRead("file.txt"))
        .ReturnsAsync(new MemoryStream("content"u8.ToArray()));

    List<string> executionOrder = [];
    _mockCommandRunner.Setup(c => c.Run(It.IsAny<string>()))
        .Returns<string>(command => {
          if (command == "echo pre") {
            Assert.False(_mockFileSystem.File.Exists(filePath));
          }

          if (command is "echo install" or "echo post") {
            Assert.True(_mockFileSystem.File.Exists(filePath));
          }

          executionOrder.Add(command);
          return Task.CompletedTask;
        });
    _mockWorkspaceService.Setup(w => w.AddInstalledPackage(
            pkgSpec,
            It.IsAny<PackageManifest>(),
            It.IsAny<IEnumerable<IFileInfo>>(),
            false))
        .Callback(() => executionOrder.Add("state"))
        .Returns(Task.CompletedTask);

    PackageArtifact artifact = new(pkgSpec, mockSource.Object);

    // Act
    await _installer.InstallPackage(artifact, false, false, false);

    // Assert
    Assert.Equal(["echo pre", "echo install", "echo post", "state"], executionOrder);
  }

  [Fact]
  public async Task InstallPackage_PrefetchesAndCopiesOnlyMatchedKeys() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    string manifestJson = """
            {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/test/pkg",
                "version": "1.0.0",
                "variants": [
                    {
                        "assets": [
                            {
                                "type": "self",
                                "placements": [
                                    { "type": "file", "src": "bin/*.dll", "dest": "plugins/p" },
                                    { "type": "file", "src": "bin/a.dll", "dest": "plugins/copy/a.dll" },
                                    { "type": "dir", "src": "res", "dest": "plugins/p/res" }
                                ]
                            }
                        ]
                    }
                ]
            }
            """;

    Mock<ISource> mockSource = new();
    mockSource.Setup(p => p.OpenRead("tooth.json"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
    mockSource.Setup(p => p.Keys).Returns(
        ["tooth.json", "README.md", "bin/a.dll", "bin/b.pdb", "res/cfg/x.json", "src/main.cpp"]);
    mockSource.Setup(p => p.OpenRead("bin/a.dll"))
        .ReturnsAsync(() => new MemoryStream("dll"u8.ToArray()));
    mockSource.Setup(p => p.OpenRead("res/cfg/x.json"))
        .ReturnsAsync(() => new MemoryStream("json"u8.ToArray()));

    List<List<string>> prefetchCalls = [];
    mockSource.Setup(p => p.Prefetch(It.IsAny<IEnumerable<string>>()))
        .Callback<IEnumerable<string>>(keys => prefetchCalls.Add([.. keys]))
        .Returns(Task.CompletedTask);

    List<IFileInfo>? placedFiles = null;
    _mockWorkspaceService.Setup(w => w.AddInstalledPackage(
            pkgSpec,
            It.IsAny<PackageManifest>(),
            It.IsAny<IEnumerable<IFileInfo>>(),
            false))
        .Callback<PackageSpec, PackageManifest, IEnumerable<IFileInfo>, bool>(
            (_, _, files, _) => placedFiles = [.. files])
        .Returns(Task.CompletedTask);

    PackageArtifact artifact = new(pkgSpec, mockSource.Object);

    // Act
    await _installer.InstallPackage(artifact, false, false, false);

    // Assert
    List<string> prefetched = Assert.Single(prefetchCalls);
    Assert.Equal(["bin/a.dll", "res/cfg/x.json"], prefetched);

    mockSource.VerifyGet(p => p.Keys, Times.Once);
    mockSource.Verify(p => p.OpenRead("bin/a.dll"), Times.Exactly(2));
    mockSource.Verify(p => p.OpenRead("res/cfg/x.json"), Times.Once);
    mockSource.Verify(p => p.OpenRead("README.md"), Times.Never);
    mockSource.Verify(p => p.OpenRead("src/main.cpp"), Times.Never);

    string[] expectedFiles = [
      Path.Combine("plugins", "p", "a.dll"),
      Path.Combine("plugins", "copy", "a.dll"),
      Path.Combine("plugins", "p", "res", "cfg", "x.json"),
    ];
    Assert.Equal(
        expectedFiles.Select(f => _mockFileSystem.Path.GetFullPath(f)),
        placedFiles!.Select(f => f.FullName));
    Assert.Equal("dll", _mockFileSystem.File.ReadAllText(expectedFiles[0]));
    Assert.Equal("dll", _mockFileSystem.File.ReadAllText(expectedFiles[1]));
    Assert.Equal("json", _mockFileSystem.File.ReadAllText(expectedFiles[2]));
  }

  [Fact]
  public async Task InstallPackage_NoMatchedKeys_DoesNotPrefetch() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([]);

    string manifestJson = """
            {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/test/pkg",
                "version": "1.0.0",
                "variants": [
                    {
                        "assets": [
                            {
                                "type": "self",
                                "placements": [
                                    { "type": "file", "src": "missing.dll", "dest": "plugins/missing.dll" }
                                ]
                            }
                        ]
                    }
                ]
            }
            """;

    Mock<ISource> mockSource = new();
    mockSource.Setup(p => p.OpenRead("tooth.json"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
    mockSource.Setup(p => p.Keys).Returns(["tooth.json"]);

    PackageArtifact artifact = new(pkgSpec, mockSource.Object);

    // Act
    await _installer.InstallPackage(artifact, false, false, false);

    // Assert
    mockSource.Verify(p => p.Prefetch(It.IsAny<IEnumerable<string>>()), Times.Never);
    _mockWorkspaceService.Verify(w => w.AddInstalledPackage(
        pkgSpec,
        It.IsAny<PackageManifest>(),
        It.Is<IEnumerable<IFileInfo>>(files => !files.Any()),
        false), Times.Once);
  }

  [Fact]
  public async Task UninstallPackage_RemovesFiles() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [new()]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);

    string filePath = Path.Combine("plugins", "file.txt");
    _mockFileSystem.AddFile(filePath, new MockFileData("content"));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([_mockFileSystem.FileInfo.New(filePath)]);

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.False(_mockFileSystem.File.Exists(filePath));
    _mockWorkspaceService.Verify(w => w.RemoveInstalledPackage(pkgSpec), Times.Once);
  }

  [Fact]
  public async Task UninstallPackage_PreserveFiles_KeepsFiles() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    PreserveFiles = [DotNet.Globbing.Glob.Parse("*.txt")]
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);

    string filePath = Path.Combine("plugins", "file.txt");
    _mockFileSystem.AddFile(filePath, new MockFileData("content"));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([_mockFileSystem.FileInfo.New(filePath)]);

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    // The pattern does not match the relative path `plugins/file.txt`, so this relies on
    // the file-name fallback.
    Assert.True(_mockFileSystem.File.Exists(filePath)); // Should exist
  }

  [Fact]
  public async Task UninstallPackage_PreserveFiles_MatchesRelativePath() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    PreserveFiles = [DotNet.Globbing.Glob.Parse("config/*.json")]
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);

    string preservedFile = Path.Combine("config", "lip.json");
    string removedFile = Path.Combine("config", "notes.txt");
    string otherRemovedFile = Path.Combine("plugins", "file.txt");
    _mockFileSystem.AddFile(preservedFile, new MockFileData("{}"));
    _mockFileSystem.AddFile(removedFile, new MockFileData("notes"));
    _mockFileSystem.AddFile(otherRemovedFile, new MockFileData("content"));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([
            _mockFileSystem.FileInfo.New(preservedFile),
            _mockFileSystem.FileInfo.New(removedFile),
            _mockFileSystem.FileInfo.New(otherRemovedFile)
        ]);

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.True(_mockFileSystem.File.Exists(preservedFile));
    Assert.False(_mockFileSystem.File.Exists(removedFile));
    Assert.False(_mockFileSystem.File.Exists(otherRemovedFile));
  }

  [Fact]
  public async Task UninstallPackage_UninstallAliasRunsBeforeFilesAreRemoved() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    Scripts = new PackageManifestScripts {
                      PreUninstall = ["echo pre"],
                      Uninstall = ["echo uninstall"],
                      PostUninstall = ["echo post"]
                    }
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);

    string filePath = Path.Combine("plugins", "file.txt");
    _mockFileSystem.AddFile(filePath, new MockFileData("content"));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([_mockFileSystem.FileInfo.New(filePath)]);

    List<string> executionOrder = [];
    _mockCommandRunner.Setup(c => c.Run(It.IsAny<string>()))
        .Returns<string>(command => {
          if (command is "echo pre" or "echo uninstall") {
            Assert.True(_mockFileSystem.File.Exists(filePath));
          }

          if (command == "echo post") {
            Assert.False(_mockFileSystem.File.Exists(filePath));
          }

          executionOrder.Add(command);
          return Task.CompletedTask;
        });
    _mockWorkspaceService.Setup(w => w.RemoveInstalledPackage(pkgSpec))
        .Callback(() => executionOrder.Add("state"))
        .Returns(Task.CompletedTask);

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.Equal(["echo pre", "echo uninstall", "echo post", "state"], executionOrder);
    Assert.False(_mockFileSystem.File.Exists(filePath));
  }

  [Fact]
  public async Task UninstallPackage_RemoveFiles_RemovesExtraFiles() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    RemoveFiles = [DotNet.Globbing.Glob.Parse("extra.log")]
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([]);

    string extraFile = "extra.log";
    _mockFileSystem.AddFile(extraFile, new MockFileData("log"));

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.False(_mockFileSystem.File.Exists(extraFile));
  }

  [Fact]
  public async Task UninstallPackage_RemoveFiles_MatchesRelativePathOnly() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    RemoveFiles = [DotNet.Globbing.Glob.Parse("extra.log")]
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([]);

    string matchingFile = "extra.log";
    string nonMatchingFile = Path.Combine("sub", "extra.log");
    _mockFileSystem.AddFile(matchingFile, new MockFileData("log"));
    _mockFileSystem.AddFile(nonMatchingFile, new MockFileData("log"));

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.False(_mockFileSystem.File.Exists(matchingFile));
    Assert.True(_mockFileSystem.File.Exists(nonMatchingFile));
  }

  [Fact]
  public async Task UninstallPackage_RemoveFiles_MatchesGlobstarAcrossDirectories() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    RemoveFiles = [DotNet.Globbing.Glob.Parse("**/*.log")]
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([]);

    string rootFile = "extra.log";
    string nestedFile = Path.Combine("sub", "extra.log");
    string untouchedFile = Path.Combine("sub", "extra.txt");
    _mockFileSystem.AddFile(rootFile, new MockFileData("log"));
    _mockFileSystem.AddFile(nestedFile, new MockFileData("log"));
    _mockFileSystem.AddFile(untouchedFile, new MockFileData("txt"));

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.False(_mockFileSystem.File.Exists(rootFile));
    Assert.False(_mockFileSystem.File.Exists(nestedFile));
    Assert.True(_mockFileSystem.File.Exists(untouchedFile));
  }

  [Fact]
  public async Task UninstallPackage_RemoveFiles_MatchesDirectoryRelativePathOnly() {
    // Arrange
    PackageId pkgId = new("github.com/test/pkg", "");
    PackageSpec pkgSpec = new(pkgId, new SemVersion(1, 0, 0));

    _mockWorkspaceService.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
        .ReturnsAsync([pkgSpec]);

    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0),
      Variants = [
            new() {
                    RemoveFiles = [DotNet.Globbing.Glob.Parse("temp/")]
                }
        ]
    };
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageManifest(pkgSpec))
        .ReturnsAsync(manifest);
    _mockWorkspaceService.Setup(w => w.GetInstalledPackageFiles(pkgSpec))
        .ReturnsAsync([]);

    string removedFile = Path.Combine("temp", "file.txt");
    string keptFile = Path.Combine("sub", "temp", "file.txt");
    _mockFileSystem.AddFile(removedFile, new MockFileData("temp"));
    _mockFileSystem.AddFile(keptFile, new MockFileData("temp"));

    // Act
    await _installer.UninstallPackage(pkgId, false, false);

    // Assert
    Assert.False(_mockFileSystem.Directory.Exists("temp"));
    Assert.True(_mockFileSystem.File.Exists(keptFile));
  }
}
