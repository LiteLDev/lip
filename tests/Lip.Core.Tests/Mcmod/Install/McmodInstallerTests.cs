using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Install;

public class McmodInstallerTests {
  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";

  private static readonly LipEnvironment _server = new(McmodPlatform.Server, "win-x64");
  private static readonly LipEnvironment _client = new(McmodPlatform.Client, "win-x64");

  private readonly MockFileSystem _fs = new();
  private readonly Mock<ICommandRunner> _commandRunner = new();
  private readonly Mock<IUserInteraction> _ui = new();
  private readonly Dictionary<string, PackageDocument> _documents = [];
  private readonly McmodInstaller _installer;

  public McmodInstallerTests() {
    _fs.AddDirectory(_root);
    Mock<IDocumentParser> parser = new();
    parser.Setup(p => p.Parse(It.IsAny<byte[]>()))
        .Returns((byte[] bytes) => _documents[Encoding.UTF8.GetString(bytes)]);
    _installer = new McmodInstaller(_fs, _commandRunner.Object, parser.Object, _ui.Object);
  }

  // LegacyMoney from the spec appendix.
  private static PackageDocument LegacyMoney(string version = "0.22.0") => new() {
    Id = "LegacyMoney",
    Version = SemVersion.Parse(version),
    IsMod = true,
    Platform = McmodPlatform.Server,
    Runtime = "win-x64",
  };

  private static readonly Dictionary<string, string> _legacyMoneyFiles = new() {
    ["manifest.json"] = "{\"name\":\"LegacyMoney\"}",
    ["LegacyMoney.dll"] = "dll",
    ["lang/en_US.json"] = "{}",
  };

  private static PackageDocument FilePackage(string name, params PackageFile[] files) => new() {
    Id = name,
    Version = SemVersion.Parse("1.0.0"),
    IsMod = false,
    Files = files,
  };

  private string Full(string relative) =>
      _fs.Path.Combine(_root, relative.Replace('/', _fs.Path.DirectorySeparatorChar));

  private string Read(string relative) => _fs.File.ReadAllText(Full(relative));

  private void AddFile(string relative, string content) =>
      _fs.AddFile(Full(relative), new MockFileData(content));

  private static InMemorySource Source(Dictionary<string, string> files) => new(files);

  private Candidate Candidate(PackageDocument document, ISource source) {
    string manifest = ManifestJson(document);
    return new Candidate(
        new McmodPackage(document, JsonDocument.Parse(manifest).RootElement),
        new ArchiveHandle("test", source, new ZipArchiveIdentity(TestIdentity.Sha256, "/archive")),
        []);
  }

  private string ManifestJson(PackageDocument document) {
    string json = $"{{\"name\":\"{document.Id}\",\"version\":\"{document.Version}\"}}";
    _documents[json] = document;
    return json;
  }

  private LockedPackage Locked(PackageDocument document, IEnumerable<string> files) => new() {
    Identity = document.Id,
    Version = document.Version.ToString(),
    Kind = McmodPackage.KindName,
    Explicit = true,
    Chain = [],
    ArchiveKey = "test",
    Files = [.. files],
    Manifest = JsonDocument.Parse(ManifestJson(document)).RootElement,
  };

  private static InstallContext Context(
      LipEnvironment environment,
      LockFile? lockFile = null,
      bool dryRun = false,
      bool ignoreScripts = false) =>
      new(_root, environment, lockFile ?? new LockFile(), dryRun, ignoreScripts);

  private SortedDictionary<string, string> Snapshot() {
    SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
    foreach (string path in _fs.AllPaths) {
      snapshot[path] = _fs.File.Exists(path) ? _fs.File.ReadAllText(path) : "<dir>";
    }
    return snapshot;
  }

  [Fact]
  public void CanHandle_Kinds_McmodAndTooth() {
    Assert.True(_installer.CanHandle("mcmod"));
    Assert.True(_installer.CanHandle("tooth"));
    Assert.False(_installer.CanHandle("other"));
  }

  [Fact]
  public async Task Install_ModOnServer_WritesWholeArchiveUnderMods() {
    // Arrange
    InMemorySource source = Source(_legacyMoneyFiles);

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(LegacyMoney(), source), Context(_server));

    // Assert
    Assert.Equal(
        ["mods/LegacyMoney/manifest.json", "mods/LegacyMoney/LegacyMoney.dll",
          "mods/LegacyMoney/lang/en_US.json"],
        owned);
    Assert.Equal("dll", Read("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.Equal("{\"name\":\"LegacyMoney\"}", Read("mods/LegacyMoney/manifest.json"));
    Assert.Equal("{}", Read("mods/LegacyMoney/lang/en_US.json"));
    Assert.Equal(3, source.Prefetched.Count);
  }

  [Fact]
  public async Task Install_ModOnClient_WritesUnderMods() {
    // Act
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(LegacyMoney(), Source(_legacyMoneyFiles)), Context(_client));

    // Assert
    Assert.All(owned, f => Assert.StartsWith("mods/LegacyMoney/", f));
    Assert.Equal("dll", Read("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.False(_fs.Directory.Exists(Full("plugins")));
  }

  [Fact]
  public async Task Install_FilePackage_WritesPlacementOnly() {
    // Arrange: PeEditor from the spec appendix.
    PackageDocument document =
        FilePackage("PeEditor", new PackageFile(null, "PeEditor.exe", "PeEditor.exe"));
    InMemorySource source = Source(new() {
      ["manifest.json"] = "{}",
      ["PeEditor.exe"] = "exe",
      ["README.md"] = "readme",
    });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal(["PeEditor.exe"], owned);
    Assert.Equal("exe", Read("PeEditor.exe"));
    Assert.False(_fs.File.Exists(Full("manifest.json")));
    Assert.False(_fs.File.Exists(Full("README.md")));
  }

  [Fact]
  public async Task Install_GlobFile_KeepsPathsBelowTheFixedPrefix() {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack", new PackageFile(null, "bin/**/*.dll", "tools/pack/"));
    InMemorySource source = Source(new() {
      ["manifest.json"] = "{}",
      ["bin/a.dll"] = "a",
      ["bin/x/b.dll"] = "b",
      ["bin/c.pdb"] = "c",
    });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal(["tools/pack/a.dll", "tools/pack/x/b.dll"], owned.Order());
    Assert.Equal("b", Read("tools/pack/x/b.dll"));
  }

  [Fact]
  public async Task Install_GlobFileMatchingNothing_WritesNothing() {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "*.dll", ""));
    InMemorySource source = Source(new() { ["manifest.json"] = "{}", ["a.txt"] = "a" });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Empty(owned);
  }

  [Fact]
  public async Task Install_DirectoryFileWithoutFiles_Throws() {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "missing/", "x/"));
    InMemorySource source = Source(new() { ["manifest.json"] = "{}", ["a.txt"] = "a" });

    // Act & Assert
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(Candidate(document, source), Context(_server)));
    Assert.Contains("'missing/' has no file", ex.Message);
  }

  [Fact]
  public async Task Install_FilesFromArchives_DownloadsAndPlacesThem() {
    // Arrange
    Mock<IArchiveProvider> provider = new();
    provider
        .Setup(p => p.OpenAsset(
            It.Is<IReadOnlyList<string>>(u => u.Single() == "https://example.com/bds.zip"),
            PackageArchiveFormat.Detect, "ab"))
        .ReturnsAsync(Source(new() { ["bedrock_server.exe"] = "bds", ["worlds/a.mcworld"] = "w" }));
    provider
        .Setup(p => p.OpenAsset(
            It.IsAny<IReadOnlyList<string>>(), PackageArchiveFormat.File, null))
        .ReturnsAsync(Source(new() { [""] = "tool" }));
    Mock<IDocumentParser> parser = new();
    parser.Setup(p => p.Parse(It.IsAny<byte[]>()))
        .Returns((byte[] bytes) => _documents[Encoding.UTF8.GetString(bytes)]);
    McmodInstaller installer = new(_fs, _commandRunner.Object, parser.Object, _ui.Object, provider.Object);
    PackageDocument document = FilePackage(
        "bedrock-server",
        new PackageFile("bds", "", ""),
        new PackageFile("tool", "tool", "tools/tool.exe"),
        new PackageFile(null, "defaults/", "")) with {
      Archives = [
        new PackageArchive("bds", ["https://example.com/bds.zip"], "ab", PackageArchiveFormat.Detect),
        new PackageArchive("tool", ["https://example.com/tool.exe"], null, PackageArchiveFormat.File),
      ],
    };
    InMemorySource source = Source(new() {
      ["manifest.json"] = "{}",
      ["defaults/server.properties"] = "props",
    });

    // Act
    IReadOnlyList<string> owned =
        await installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal(
        ["bedrock_server.exe", "server.properties", "tools/tool.exe", "worlds/a.mcworld"],
        owned.Order(StringComparer.Ordinal));
    Assert.Equal("bds", Read("bedrock_server.exe"));
    Assert.Equal("tool", Read("tools/tool.exe"));
    Assert.Equal("props", Read("server.properties"));
  }

  [Fact]
  public async Task Install_DirectoryPlacements_CopyRelativePathsAndNeverRootManifest() {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack",
        new PackageFile(null, "assets/", "resource_packs/pack/"),
        new PackageFile(null, "", "copy/"));
    InMemorySource source = Source(new() {
      ["manifest.json"] = "{}",
      ["assets/a.txt"] = "a",
      ["assets/sub/b.txt"] = "b",
      ["assets/manifest.json"] = "nested",
    });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal("a", Read("resource_packs/pack/a.txt"));
    Assert.Equal("b", Read("resource_packs/pack/sub/b.txt"));
    Assert.Equal("nested", Read("resource_packs/pack/manifest.json"));
    Assert.Equal("a", Read("copy/assets/a.txt"));
    Assert.False(_fs.File.Exists(Full("copy/manifest.json")));
    Assert.Equal(6, owned.Count);
  }

  [Fact]
  public async Task Install_LaterPlacementSameDest_Overwrites() {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack", new PackageFile(null, "one.txt", "out.txt"), new PackageFile(null, "two.txt", "out.txt"));
    InMemorySource source = Source(new() { ["one.txt"] = "1", ["two.txt"] = "2" });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal(["out.txt"], owned);
    Assert.Equal("2", Read("out.txt"));
  }

  [Theory]
  [InlineData("missing.txt", "out.txt")]
  [InlineData("missing/", "out/")]
  public async Task Install_SrcNotInArchive_ThrowsWithoutChanges(string src, string dest) {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack", new PackageFile(null, "a.txt", "a.txt"), new PackageFile(null, src, dest));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData(".lip/lock.json")]
  [InlineData(".lip")]
  [InlineData(".LIP/x.txt")]
  public async Task Install_DestInLipDirectory_Throws(string dest) {
    // Arrange
    if (!OperatingSystem.IsWindows() && dest.StartsWith(".LIP")) {
      return;
    }
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "a.txt", dest));
    SortedDictionary<string, string> before = Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(
            Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server)));

    // Assert
    Assert.Contains("reserved", ex.Message);
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData("../escape.txt")]
  [InlineData("a/../../escape.txt")]
  [InlineData("C:/escape.txt")]
  public async Task Install_DestOutsideWorkspace_Throws(string dest) {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "a.txt", dest));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData("trailing./a.txt")]
  [InlineData("a.txt ")]
  public async Task Install_AliasingDest_Throws(string dest) {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "a.txt", dest));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData("lip_lock.json")]
  [InlineData("lock.json")]
  [InlineData("tooth_lock.json")]
  [InlineData(".git/hooks/pre-commit")]
  public async Task Install_DestNamedLikeLipOrGitFile_Writes(string dest) {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "a.txt", dest));

    // Act
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server));

    // Assert
    Assert.Equal([dest], owned);
    Assert.Equal("a", Read(dest));
  }

  [Fact]
  public async Task Install_OverwritesExistingFile_BacksItUpAsHiddenSibling() {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack", new PackageFile(null, "a.txt", "dir/existing.txt"), new PackageFile(null, "b.txt", "dir/b.txt"));
    AddFile("dir/existing.txt", "original");
    AddFile("dir/.existing.txt.bak", "someone else's");
    InMemorySource source = Source(new() { ["a.txt"] = "a", ["b.txt"] = "b" });
    string[] duringInstall = [];
    string? backup = null;
    bool hidden = false;
    source.OnOpen = key => {
      if (key == "b.txt") {
        duringInstall = [.. _fs.Directory.GetFiles(Full("dir")).Select(f => Path.GetFileName(f))];
        backup = Read("dir/.existing.txt.bak1");
        hidden = _fs.File.GetAttributes(Full("dir/.existing.txt.bak1")).HasFlag(FileAttributes.Hidden);
      }
    };

    // Act
    await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Contains(".existing.txt.bak1", duringInstall);
    Assert.Equal("original", backup);
    Assert.Equal(OperatingSystem.IsWindows(), hidden);
    Assert.Equal(
        [".existing.txt.bak", "b.txt", "existing.txt"],
        _fs.Directory.GetFiles(Full("dir")).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
    Assert.Equal("someone else's", Read("dir/.existing.txt.bak"));
    Assert.Equal("a", Read("dir/existing.txt"));
  }

  [Fact]
  public async Task Install_PackageWritesBackupName_BackupAvoidsIt() {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack", new PackageFile(null, "a.txt", "foo"), new PackageFile(null, "b.txt", ".foo.bak"));
    AddFile("foo", "original");
    InMemorySource source = Source(new() { ["a.txt"] = "a", ["b.txt"] = "b" });

    // Act
    await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal("a", Read("foo"));
    Assert.Equal("b", Read(".foo.bak"));
    Assert.False(_fs.File.Exists(Full(".foo.bak1")));
  }

  [Fact]
  public async Task Install_InvalidModName_ThrowsWithoutChanges() {
    // Arrange
    PackageDocument document = LegacyMoney() with { Id = ".." };
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_PrefetchFails_ThrowsBeforeScripts() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreInstall = ["pre"] },
    };
    InMemorySource source = Source(_legacyMoneyFiles);
    source.FailPrefetch = true;

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(Candidate(document, source), Context(_server)));
    _commandRunner.VerifyNoOtherCalls();
    Assert.False(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Install_FileOwnedByAnotherPackage_ThrowsAndLeavesWorkspaceIdentical() {
    // Arrange
    PackageDocument other = FilePackage("Other", new PackageFile(null, "x", "x"));
    AddFile("PeEditor.exe", "other's");
    AddFile("unrelated.txt", "keep");
    LockFile lockFile = new() { Packages = [Locked(other, ["PeEditor.exe"])] };
    PackageDocument document = FilePackage(
        "PeEditor",
        new PackageFile(null, "new.txt", "new.txt"),
        new PackageFile(null, "PeEditor.exe", "PeEditor.exe")) with {
      Hooks = new Hooks { PreInstall = ["echo pre"] },
    };
    InMemorySource source = Source(new() { ["PeEditor.exe"] = "exe", ["new.txt"] = "n" });
    SortedDictionary<string, string> before = Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(Candidate(document, source), Context(_server, lockFile)));

    // Assert
    Assert.Contains("Other", ex.Message);
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Install_OwnershipComparedOnNormalizedPaths() {
    // Arrange: the lock spells the path with backslashes and, on Windows, another case.
    PackageDocument other = FilePackage("Other", new PackageFile(null, "x", "x"));
    string lockedPath = OperatingSystem.IsWindows() ? @"Plugins\LegacyMoney\LegacyMoney.DLL"
        : @"plugins\LegacyMoney\LegacyMoney.dll";
    LockFile lockFile = new() { Packages = [Locked(other, [lockedPath])] };

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(LegacyMoney(), Source(_legacyMoneyFiles)), Context(_server, lockFile)));
    Assert.False(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Install_PreservedExistingFile_KeepsItAndDoesNotOwnIt() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Preserve = [new PathGlob("config/")],
    };
    AddFile("mods/LegacyMoney/config/config.json", "user");
    InMemorySource source = Source(new() {
      ["LegacyMoney.dll"] = "dll",
      ["config/config.json"] = "default",
      ["config/other.json"] = "default",
    });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal("user", Read("mods/LegacyMoney/config/config.json"));
    Assert.Equal("default", Read("mods/LegacyMoney/config/other.json"));
    Assert.Equal(
        ["mods/LegacyMoney/LegacyMoney.dll", "mods/LegacyMoney/config/other.json"], owned);
    Assert.DoesNotContain("config/config.json", source.Prefetched);
  }

  [Fact]
  public async Task Install_PreservedFileAlreadyOwnedBySamePackage_StaysOwned() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Preserve = [new PathGlob("config/*.json")],
    };
    AddFile("mods/LegacyMoney/config/config.json", "user");
    LockFile lockFile = new() {
      Packages = [Locked(LegacyMoney("0.21.0"), ["mods/LegacyMoney/config/config.json"])],
    };
    InMemorySource source = Source(new() { ["config/config.json"] = "default" });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server, lockFile));

    // Assert
    Assert.Equal(["mods/LegacyMoney/config/config.json"], owned);
    Assert.Equal("user", Read("mods/LegacyMoney/config/config.json"));
  }

  [Fact]
  public async Task Install_PreserveFilesForFilePackage_RelativeToWorkspaceRoot() {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "cfg/", "cfg/")) with {
      Preserve = [new PathGlob("cfg/*.ini")],
    };
    AddFile("cfg/a.ini", "user");
    InMemorySource source = Source(new() { ["cfg/a.ini"] = "default", ["cfg/b.ini"] = "b" });

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Assert
    Assert.Equal("user", Read("cfg/a.ini"));
    Assert.Equal(["cfg/b.ini"], owned);
  }

  [Fact]
  public async Task Install_Scripts_RunInWorkspaceRootAroundWrites() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreInstall = ["pre1", "pre2"], PostInstall = ["post"] },
    };
    string dll = Full("mods/LegacyMoney/LegacyMoney.dll");
    List<(string Command, string Cwd, bool DllExists)> calls = [];
    _commandRunner.Setup(r => r.Run(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string c, string cwd) => calls.Add((c, cwd, _fs.File.Exists(dll))))
        .Returns(Task.CompletedTask);

    // Act
    await _installer.Install(Candidate(document, Source(_legacyMoneyFiles)), Context(_server));

    // Assert
    Assert.Equal(
        [("pre1", _root, false), ("pre2", _root, false), ("post", _root, true)], calls);
  }

  [Fact]
  public async Task Install_IgnoreScripts_RunsNoScripts() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreInstall = ["pre"], PostInstall = ["post"] },
    };

    // Act
    await _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server, ignoreScripts: true));

    // Assert
    _commandRunner.VerifyNoOtherCalls();
    Assert.True(_fs.File.Exists(Full("mods/LegacyMoney/LegacyMoney.dll")));
  }

  [Fact]
  public async Task Install_PreInstallFails_ThrowsWithoutWriting() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreInstall = ["fail"] },
    };
    _commandRunner.Setup(r => r.Run("fail", It.IsAny<string>()))
        .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server)));
    Assert.False(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Install_WriteFailsMidway_RollsBackWrittenFiles() {
    // Arrange
    PackageDocument document = FilePackage(
        "Pack",
        new PackageFile(null, "a.txt", "existing.txt"),
        new PackageFile(null, "b.txt", "new/dir/b.txt"),
        new PackageFile(null, "c.txt", "c.txt"));
    AddFile("existing.txt", "original");
    InMemorySource source = Source(new() { ["a.txt"] = "a", ["b.txt"] = "b", ["c.txt"] = "c" });
    source.Failing.Add("c.txt");
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(Candidate(document, source), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_PostInstallFails_RollsBackWrittenFiles() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PostInstall = ["fail"] },
    };
    _commandRunner.Setup(r => r.Run("fail", It.IsAny<string>()))
        .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_TargetIsExistingDirectory_ThrowsWithoutChanges() {
    // Arrange
    _fs.AddDirectory(Full("mods/LegacyMoney/LegacyMoney.dll"));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _installer.Install(
        Candidate(LegacyMoney(), Source(_legacyMoneyFiles)), Context(_server)));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_DryRun_ReportsPlanWithoutChanges() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreInstall = ["pre"], PostInstall = ["post"] },
    };
    SortedDictionary<string, string> before = Snapshot();

    // Act
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server, dryRun: true));

    // Assert
    Assert.Equal(3, owned.Count);
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
    _ui.Verify(u => u.PrintInfo(It.Is<string>(s => s.Contains("LegacyMoney.dll"))), Times.Once);
  }

  [Fact]
  public async Task Uninstall_Mod_DeletesOwnedFilesAndPrunesEmptyDirectories() {
    // Arrange
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(LegacyMoney(), Source(_legacyMoneyFiles)), Context(_server));
    AddFile("mods/Other/x.dll", "x");

    // Act
    await _installer.Uninstall(Locked(LegacyMoney(), owned), Context(_server));

    // Assert
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney")));
    Assert.True(_fs.File.Exists(Full("mods/Other/x.dll")));
  }

  [Fact]
  public async Task Install_FilePackagePlacingUnderPlugins_WritesAndOwnsUnderMods() {
    // Arrange
    PackageDocument document = FilePackage(
        "Patch",
        new PackageFile(null, "a.dll", "plugins/X/a.dll"),
        new PackageFile(null, "b.dll", "mods/X/a.dll"));
    InMemorySource source = Source(new() { ["a.dll"] = "a", ["b.dll"] = "b" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, source), Context(_server));

    // Assert: both placements are one file; the later one wins.
    Assert.Equal(["mods/X/a.dll"], owned);
    Assert.Equal("b", Read("mods/X/a.dll"));
    Assert.False(_fs.Directory.Exists(Full("plugins")));
  }

  [Fact]
  public async Task Uninstall_FilePackageGlobsThroughPlugins_MatchFilesUnderMods() {
    // Arrange
    PackageDocument document = FilePackage(
        "Patch",
        new PackageFile(null, "a.dll", "plugins/X/a.dll"),
        new PackageFile(null, "c.json", "plugins/X/c.json")) with {
      Preserve = [new PathGlob("plugins/X/*.json")],
      Remove = [new PathGlob("plugins/X/logs/")],
    };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["a.dll"] = "a", ["c.json"] = "c" })),
        Context(_server));
    AddFile("mods/X/logs/today.log", "log");

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server));

    // Assert
    Assert.False(_fs.File.Exists(Full("mods/X/a.dll")));
    Assert.Equal("c", Read("mods/X/c.json"));
    Assert.False(_fs.Directory.Exists(Full("mods/X/logs")));
  }

  [Fact]
  public async Task Uninstall_FilePackageEmptyingMods_KeepsModsDirectory() {
    // Arrange
    PackageDocument document = FilePackage("Patch", new PackageFile(null, "a.dll", "mods/X/a.dll"));
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["a.dll"] = "a" })), Context(_server));

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server));

    // Assert
    Assert.False(_fs.Directory.Exists(Full("mods/X")));
    Assert.True(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Uninstall_LastMod_KeepsModsDirectoryAndWorkspaceRoot() {
    // Arrange
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(LegacyMoney(), Source(_legacyMoneyFiles)), Context(_server));

    // Act
    await _installer.Uninstall(Locked(LegacyMoney(), owned), Context(_server));

    // Assert
    Assert.True(_fs.Directory.Exists(Full("mods")));
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney")));
  }

  [Fact]
  public async Task Uninstall_PreexistingEmptyDirectory_IsKept() {
    // Arrange
    _fs.AddDirectory(Full("mods/LegacyMoney/empty"));
    _fs.AddDirectory(Full("unrelated"));
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(LegacyMoney(), Source(_legacyMoneyFiles)), Context(_server));

    // Act
    await _installer.Uninstall(Locked(LegacyMoney(), owned), Context(_server));

    // Assert
    Assert.True(_fs.Directory.Exists(Full("mods/LegacyMoney/empty")));
    Assert.True(_fs.Directory.Exists(Full("unrelated")));
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney/lang")));
  }

  [Fact]
  public async Task Uninstall_PreserveFiles_KeepsMatchingOwnedFiles() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Preserve = [new PathGlob("config/")],
    };
    InMemorySource source = Source(new() {
      ["LegacyMoney.dll"] = "dll",
      ["config/config.json"] = "default",
    });
    IReadOnlyList<string> owned =
        await _installer.Install(Candidate(document, source), Context(_server));

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server));

    // Assert
    Assert.False(_fs.File.Exists(Full("mods/LegacyMoney/LegacyMoney.dll")));
    Assert.Equal("default", Read("mods/LegacyMoney/config/config.json"));
  }

  [Fact]
  public async Task Uninstall_RemoveFilesDirectoryPattern_DeletesRecursivelyExceptOthers() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Remove = [new PathGlob("data/"), new PathGlob("*.log")],
    };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["LegacyMoney.dll"] = "dll" })), Context(_server));
    AddFile("mods/LegacyMoney/data/db/money.db", "db");
    AddFile("mods/LegacyMoney/data/top.txt", "t");
    _fs.AddDirectory(Full("mods/LegacyMoney/data/emptysub"));
    AddFile("mods/LegacyMoney/data/foreign.txt", "f");
    AddFile("mods/LegacyMoney/run.log", "log");
    AddFile("mods/LegacyMoney/keep.txt", "k");
    PackageDocument other = FilePackage("Other", new PackageFile(null, "x", "x"));
    LockFile lockFile = new() {
      Packages = [
        Locked(document, owned),
        Locked(other, ["mods/LegacyMoney/data/foreign.txt"]),
      ],
    };

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server, lockFile));

    // Assert
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney/data/db")));
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney/data/emptysub")));
    Assert.False(_fs.File.Exists(Full("mods/LegacyMoney/data/top.txt")));
    Assert.False(_fs.File.Exists(Full("mods/LegacyMoney/run.log")));
    Assert.False(_fs.File.Exists(Full("mods/LegacyMoney/LegacyMoney.dll")));
    Assert.Equal("f", Read("mods/LegacyMoney/data/foreign.txt"));
    Assert.Equal("k", Read("mods/LegacyMoney/keep.txt"));
  }

  [Fact]
  public async Task Uninstall_FilePackageRemoveFiles_RelativeToWorkspaceRoot() {
    // Arrange
    PackageDocument document =
        FilePackage("PeEditor", new PackageFile(null, "PeEditor.exe", "PeEditor.exe")) with {
          Remove = [new PathGlob("PeEditor.log"), new PathGlob("cache/")],
        };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["PeEditor.exe"] = "exe" })), Context(_server));
    AddFile("PeEditor.log", "log");
    AddFile("cache/a/b.bin", "b");
    AddFile("bedrock_server.exe", "bds");

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server));

    // Assert
    Assert.False(_fs.File.Exists(Full("PeEditor.exe")));
    Assert.False(_fs.File.Exists(Full("PeEditor.log")));
    Assert.False(_fs.Directory.Exists(Full("cache")));
    Assert.True(_fs.File.Exists(Full("bedrock_server.exe")));
    Assert.True(_fs.Directory.Exists(_root));
  }

  [Fact]
  public async Task Uninstall_RemoveFilesMatchingLipDirectory_LeavesIt() {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "a.txt", "a.txt")) with {
      Remove = [
        new PathGlob(".lip/"), new PathGlob(".lip/lock.json"), new PathGlob("*/"),
        new PathGlob("**/*.json"),
      ],
    };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server));
    AddFile(".lip/lock.json", "lock");
    AddFile("logs/a.json", "x");

    // Act
    await _installer.Uninstall(
        Locked(document, [.. owned, ".lip/lock.json"]), Context(_server));

    // Assert
    Assert.Equal("lock", Read(".lip/lock.json"));
    Assert.False(_fs.File.Exists(Full("a.txt")));
    Assert.False(_fs.File.Exists(Full("logs/a.json")));
  }

  [Fact]
  public async Task Uninstall_RemoveFilesGlobs_MatchOnlyAsDeepAsThePattern() {
    // Arrange
    PackageDocument document = FilePackage("Pack", new PackageFile(null, "a.txt", "a.txt")) with {
      Remove = [new PathGlob("*.log"), new PathGlob("logs/**/*.txt")],
    };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(new() { ["a.txt"] = "a" })), Context(_server));
    AddFile("top.log", "x");
    AddFile("worlds/deep.log", "keep");
    AddFile("logs/a.txt", "x");
    AddFile("logs/2026/b.txt", "x");
    AddFile("logs/keep.bin", "keep");

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server));

    // Assert
    Assert.False(_fs.File.Exists(Full("top.log")));
    Assert.True(_fs.File.Exists(Full("worlds/deep.log")));
    Assert.False(_fs.File.Exists(Full("logs/a.txt")));
    Assert.False(_fs.Directory.Exists(Full("logs/2026")));
    Assert.True(_fs.File.Exists(Full("logs/keep.bin")));
  }

  [Fact]
  public async Task Uninstall_Scripts_RunInWorkspaceRootAroundDeletion() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreUninstall = ["pre"], PostUninstall = ["post"] },
    };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server));
    string dll = Full("mods/LegacyMoney/LegacyMoney.dll");
    List<(string Command, string Cwd, bool DllExists)> calls = [];
    _commandRunner.Setup(r => r.Run(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string c, string cwd) => calls.Add((c, cwd, _fs.File.Exists(dll))))
        .Returns(Task.CompletedTask);

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server));

    // Assert
    Assert.Equal([("pre", _root, true), ("post", _root, false)], calls);
  }

  [Fact]
  public async Task Uninstall_DryRun_ReportsWithoutChanges() {
    // Arrange
    PackageDocument document = LegacyMoney() with {
      Hooks = new Hooks { PreUninstall = ["pre"] },
    };
    IReadOnlyList<string> owned = await _installer.Install(
        Candidate(document, Source(_legacyMoneyFiles)), Context(_server));
    SortedDictionary<string, string> before = Snapshot();

    // Act
    await _installer.Uninstall(Locked(document, owned), Context(_server, dryRun: true));

    // Assert
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
    _ui.Verify(u => u.PrintInfo(It.Is<string>(s => s.Contains("Would delete"))), Times.Exactly(3));
  }

  [Fact]
  public async Task Update_UninstallOldThenInstallNew_PreservesConfigAndReplacesFiles() {
    // Arrange
    PackageDocument v1 = LegacyMoney("0.21.0") with {
      Preserve = [new PathGlob("config/config.json")],
    };
    PackageDocument v2 = LegacyMoney("0.22.0") with {
      Preserve = [new PathGlob("config/config.json")],
    };
    IReadOnlyList<string> ownedV1 = await _installer.Install(
        Candidate(v1, Source(new() {
          ["LegacyMoney.dll"] = "v1",
          ["old.dll"] = "old",
          ["config/config.json"] = "default1",
        })),
        Context(_server));
    _fs.File.WriteAllText(Full("mods/LegacyMoney/config/config.json"), "user");
    LockedPackage lockedV1 = Locked(v1, ownedV1);

    // Act
    await _installer.Uninstall(lockedV1, Context(_server, new LockFile { Packages = [lockedV1] }));
    IReadOnlyList<string> ownedV2 = await _installer.Install(
        Candidate(v2, Source(new() {
          ["LegacyMoney.dll"] = "v2",
          ["config/config.json"] = "default2",
        })),
        Context(_server));

    // Assert
    Assert.Equal("v2", Read("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.False(_fs.File.Exists(Full("mods/LegacyMoney/old.dll")));
    Assert.Equal("user", Read("mods/LegacyMoney/config/config.json"));
    Assert.Equal(["mods/LegacyMoney/LegacyMoney.dll"], ownedV2);
  }

  [Fact]
  public async Task Update_OwnedFileOverwrittenByNewVersion_IsOwnedAgain() {
    // Arrange
    PackageDocument v1 = FilePackage("Pack", new PackageFile(null, "a.txt", "a.txt"));
    IReadOnlyList<string> ownedV1 =
        await _installer.Install(
            Candidate(v1, Source(new() { ["a.txt"] = "1" })), Context(_server));
    LockedPackage lockedV1 = Locked(v1, ownedV1);
    PackageDocument v2 = v1 with { Version = SemVersion.Parse("2.0.0") };

    // Act
    await _installer.Uninstall(lockedV1, Context(_server, new LockFile { Packages = [lockedV1] }));
    IReadOnlyList<string> ownedV2 =
        await _installer.Install(
            Candidate(v2, Source(new() { ["a.txt"] = "2" })), Context(_server));

    // Assert
    Assert.Equal(["a.txt"], ownedV2);
    Assert.Equal("2", Read("a.txt"));
  }

  private sealed class InMemorySource(Dictionary<string, string> files) : ISource {
    public List<string> Prefetched { get; } = [];

    public HashSet<string> Failing { get; } = [];

    /// Called with the key before it is read.
    public Action<string>? OnOpen { get; set; }

    public IEnumerable<string> Keys => files.Keys;

    public Task<Stream> OpenRead(string key) {
      OnOpen?.Invoke(key);
      return Failing.Contains(key)
          ? throw new IOException($"cannot read {key}")
          : Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(files[key])));
    }

    public bool FailPrefetch { get; set; }

    public Task Prefetch(IEnumerable<string> keys) {
      if (FailPrefetch) {
        throw new IOException("network down");
      }
      Prefetched.AddRange(keys);
      return Task.CompletedTask;
    }
  }
}
