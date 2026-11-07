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

/// Installing and uninstalling packages of format 4 (protocol/workspace.md §4).
public class McmodInstallerTests {
  private const string Pack = "example.com/pack";
  private const string LegacyMoneyUrl = "https://example.com/LegacyMoney.mcmod";

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";

  private static readonly LipEnvironment _server = new(McmodPlatform.Server, "win-x64");
  private static readonly LipEnvironment _client = new(McmodPlatform.Client, "win-x64");

  private readonly MockFileSystem _fs = new();
  private readonly Mock<ICommandRunner> _commandRunner = new();
  private readonly Mock<IUserInteraction> _ui = new();
  private readonly Mock<IArchiveProvider> _provider = new();

  /// The assets the provider serves, by URL.
  private readonly Dictionary<string, ISource> _assets = [];
  private readonly SoloInstaller _installer;

  public McmodInstallerTests() {
    _fs.AddDirectory(_root);
    _ui.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    _ui.Setup(u => u.PrintWarning(It.IsAny<string>())).Returns(Task.CompletedTask);
    _provider
        .Setup(p => p.OpenAsset(It.IsAny<IReadOnlyList<string>>()))
        .Returns((IReadOnlyList<string> urls) =>
            urls.FirstOrDefault(_assets.ContainsKey) is string url
                ? Task.FromResult(_assets[url])
                : Task.FromException<ISource>(
                    new ArchiveUnavailableException($"none of {string.Join(", ", urls)}")));
    _installer = new SoloInstaller(
        _fs, new McmodInstaller(_fs, _commandRunner.Object, _ui.Object));
  }

  private static readonly Dictionary<string, string> _legacyMoneyFiles = new() {
    ["manifest.json"] = """{"name":"LegacyMoney"}""",
    ["LegacyMoney.dll"] = "dll",
    ["lang/en_US.json"] = "{}",
  };

  private string Full(string relative) =>
      _fs.Path.Combine(_root, relative.Replace('/', _fs.Path.DirectorySeparatorChar));

  private string Read(string relative) => _fs.File.ReadAllText(Full(relative));

  private bool Exists(string relative) => _fs.File.Exists(Full(relative));

  private void AddFile(string relative, string content) =>
      _fs.AddFile(Full(relative), new MockFileData(content));

  /// An entry of format 4 for (server, win-x64) with the given members.
  private static PackageEntry Entry(string members) {
    using JsonDocument json = JsonDocument.Parse(
        $$"""{ "platform": "server", "runtime": "win-x64"{{(members == "" ? "" : ", " + members)}} }""");
    return PackageFileParser.ParseEntry(json.RootElement);
  }

  /// A candidate of the package id with the given entry members, whose archive holds the
  /// files self.
  private Candidate Candidate(
      string entry,
      Dictionary<string, string>? self = null,
      string id = Pack,
      string version = "1.0.0") =>
      Candidate(Entry(entry), new InMemorySource(self ?? []), id, version);

  private Candidate Candidate(
      PackageEntry entry, ISource self, string id = Pack, string version = "1.0.0") {
    Package package = new(id, "", SemVersion.Parse(version), entry) {
      Manifest = Resolution.TestCandidates.Manifest(id, version, "", entry),
    };
    ArchiveHandle archive = new($"{id}@{version}", self, new string('0', 40));
    return Lip.Core.Mcmod.Candidate.Create(
        package, archive, Ref.Parse(id, allowFile: false), _provider.Object);
  }

  /// The mod LegacyMoney (an asset of type "mcmod") with the given extra entry members.
  private Candidate LegacyMoney(
      string extra = "",
      InMemorySource? files = null,
      string version = "0.22.0") {
    _assets[LegacyMoneyUrl] = files ?? new InMemorySource(_legacyMoneyFiles);
    return Candidate(
        $$""" "assets": [ { "type": "mcmod", "urls": [ "{{LegacyMoneyUrl}}" ] } ]"""
            + (extra == "" ? "" : ", " + extra),
        id: "example.com/legacymoney",
        version: version);
  }

  /// The entry members of one asset of type "self" with the given placements (JSON objects).
  private static string SelfPlacements(params string[] placements) =>
      $$""" "assets": [ { "type": "self", "place": [ {{string.Join(", ", placements)}} ] } ]""";

  private static string Placement(string src, string dest) =>
      $$"""{ "src": "{{src}}", "dest": "{{dest}}" }""";

  /// The lock entry of an installed candidate.
  private static LockedPackage Locked(Candidate candidate, IEnumerable<string> files) => new() {
    Manifest = candidate.Package.Manifest,
    Variant = candidate.Package.Variant,
    Explicit = true,
    Source = candidate.Source.ToString(),
    Commit = candidate.Archive.Commit,
    Files = [.. files],
  };

  /// A workspace file of the environment (server, win-x64) with the given packages.
  private static LockFile Lock(params LockedPackage[] packages) => new() {
    Platform = "server",
    Runtime = "win-x64",
    Packages = [.. packages],
  };

  private static InstallContext Context(
      LipEnvironment? environment = null,
      LockFile? lockFile = null,
      bool dryRun = false,
      bool ignoreScripts = false) =>
      new(_root, environment ?? _server, lockFile ?? Lock(), dryRun, ignoreScripts);

  private SortedDictionary<string, string> Snapshot() {
    SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
    foreach (string path in _fs.AllPaths) {
      snapshot[path] = _fs.File.Exists(path) ? _fs.File.ReadAllText(path) : "<dir>";
    }
    return snapshot;
  }

  private static string Compact(string json) =>
      JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

  // Step 2: "self" and "zip" placements.

  [Fact]
  public async Task Install_FileForm_WritesOnlyThePlacedFile() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("PeEditor.exe", "PeEditor.exe")),
        new() { ["lip.yaml"] = "{}", ["PeEditor.exe"] = "exe", ["README.md"] = "readme" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["PeEditor.exe"], owned);
    Assert.Equal("exe", Read("PeEditor.exe"));
    Assert.False(Exists("lip.yaml"));
    Assert.False(Exists("README.md"));
  }

  [Fact]
  public async Task Install_FileFormNested_WritesAtDest() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("bin/sub/a.dll", "tools/x.dll")),
        new() { ["bin/sub/a.dll"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["tools/x.dll"], owned);
    Assert.Equal("a", Read("tools/x.dll"));
  }

  [Fact]
  public async Task Install_DirectoryForm_WritesFilesAtTheirPathRelativeToSrc() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(
            Placement("assets/", "resource_packs/pack/"),
            Placement("", "copy/")),
        new() {
          ["lip.yaml"] = "{}",
          ["assets/a.txt"] = "a",
          ["assets/sub/b.txt"] = "b",
          ["assets/lip.yaml"] = "nested",
        });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("a", Read("resource_packs/pack/a.txt"));
    Assert.Equal("b", Read("resource_packs/pack/sub/b.txt"));
    Assert.Equal("nested", Read("resource_packs/pack/lip.yaml"));
    Assert.Equal("a", Read("copy/assets/a.txt"));
    Assert.Equal("nested", Read("copy/assets/lip.yaml"));
    // The package file of the package is never written (protocol/workspace.md §4 step 2).
    Assert.False(Exists("copy/lip.yaml"));
    Assert.Equal(6, owned.Count);
  }

  [Fact]
  public async Task Install_DirectoryFormDestRoot_WritesIntoWorkspaceRoot() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("defaults/", "")),
        new() { ["defaults/server.properties"] = "props", ["defaults/a/b.txt"] = "b" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["a/b.txt", "server.properties"], owned.Order(StringComparer.Ordinal));
    Assert.Equal("props", Read("server.properties"));
  }

  [Fact]
  public async Task Install_GlobForm_KeepsPathsBelowTheFixedPrefix() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("bin/**/*.dll", "tools/pack/")),
        new() {
          ["bin/a.dll"] = "a",
          ["bin/x/b.dll"] = "b",
          ["bin/c.pdb"] = "c",
          ["other/d.dll"] = "d",
        });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["tools/pack/a.dll", "tools/pack/x/b.dll"], owned.Order(StringComparer.Ordinal));
    Assert.Equal("b", Read("tools/pack/x/b.dll"));
  }

  [Fact]
  public async Task Install_GlobFormFixedPrefixPartOfSegment_IsNotStripped() {
    // Arrange: the fixed prefix is only whole segments without metacharacters.
    Candidate candidate = Candidate(
        SelfPlacements(Placement("lib/a*/x.txt", "out/")),
        new() { ["lib/ab/x.txt"] = "x" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["out/ab/x.txt"], owned);
  }

  [Fact]
  public async Task Install_GlobFormMatchingNothing_WritesNothing() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("*.dll", "")), new() { ["a.txt"] = "a" });
    SortedDictionary<string, string> before = Snapshot();

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Empty(owned);
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_GlobFormMatchingPackageFile_NeverWritesIt() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("*.yaml", "cfg/")),
        new() { ["lip.yaml"] = "{}", ["a.yaml"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["cfg/a.yaml"], owned);
    Assert.False(Exists("cfg/lip.yaml"));
  }

  [Fact]
  public async Task Install_ToothJsonBesidePackageFile_IsWritten() {
    // Arrange: only the package file is never written; with one, a tooth.json is a file like any
    // other.
    Candidate candidate = Candidate(
        SelfPlacements(Placement("*.json", "cfg/")),
        new() { ["lip.yaml"] = "{}", ["tooth.json"] = "{}", ["a.json"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["cfg/a.json", "cfg/tooth.json"], owned.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task Install_ToothJsonOfArchiveWithoutPackageFile_IsNeverWritten() {
    // Arrange: a package read from a tooth.json (protocol/compatibility.md §2) has it as its
    // package file.
    Candidate candidate = Candidate(
        SelfPlacements(Placement("*.json", "cfg/")),
        new() { ["tooth.json"] = "{}", ["a.json"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["cfg/a.json"], owned);
    Assert.False(Exists("cfg/tooth.json"));
  }

  [Theory]
  [InlineData("missing.txt", "out.txt")]
  [InlineData("missing/", "out/")]
  [InlineData("a.txt/", "out/")]
  public async Task Install_SrcNotInArchive_ThrowsWithoutChanges(string src, string dest) {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", "a.txt"), Placement(src, dest)),
        new() { ["a.txt"] = "a" });
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_LaterPlacementSameDest_Wins() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("one.txt", "out.txt"), Placement("two.txt", "out.txt")),
        new() { ["one.txt"] = "1", ["two.txt"] = "2" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["out.txt"], owned);
    Assert.Equal("2", Read("out.txt"));
  }

  [Fact]
  public async Task Install_LaterAssetSameDest_Wins() {
    // Arrange
    _assets["https://example.com/a.zip"] = new InMemorySource(new() { ["one.txt"] = "zip" });
    Candidate candidate = Candidate(
        """
        "assets": [
          { "type": "zip", "urls": [ "https://example.com/a.zip" ],
            "place": [ { "src": "one.txt", "dest": "out.txt" } ] },
          { "type": "self", "place": [ { "src": "one.txt", "dest": "out.txt" } ] }
        ]
        """,
        new() { ["one.txt"] = "self" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["out.txt"], owned);
    Assert.Equal("self", Read("out.txt"));
  }

  [Fact]
  public async Task Install_ZipAsset_PlacesFilesOfTheDownloadedArchive() {
    // Arrange
    _assets["https://example.com/bds.zip"] = new InMemorySource(new() {
      ["bedrock_server.exe"] = "bds",
      ["worlds/a.mcworld"] = "w",
      ["lip.yaml"] = "zip's",
    });
    Candidate candidate = Candidate(
        """
        "assets": [
          { "type": "zip", "urls": [ "https://mirror.invalid/bds.zip", "https://example.com/bds.zip" ],
            "place": [ { "src": "", "dest": "" } ] },
          { "type": "self", "place": [ { "src": "defaults/", "dest": "" } ] }
        ]
        """,
        new() { ["lip.yaml"] = "{}", ["defaults/server.properties"] = "props" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(
        ["bedrock_server.exe", "lip.yaml", "server.properties", "worlds/a.mcworld"],
        owned.Order(StringComparer.Ordinal));
    Assert.Equal("bds", Read("bedrock_server.exe"));
    Assert.Equal("props", Read("server.properties"));
    // Only the package file of the package itself is never written.
    Assert.Equal("zip's", Read("lip.yaml"));
    _provider.Verify(p => p.OpenAsset(
        It.Is<IReadOnlyList<string>>(u => u.SequenceEqual(
            new[] { "https://mirror.invalid/bds.zip", "https://example.com/bds.zip" }))));
  }

  [Fact]
  public async Task Install_AssetUnavailable_ThrowsWithoutChangesOrScripts() {
    // Arrange
    Candidate candidate = Candidate(
        """
        "assets": [ { "type": "zip", "urls": [ "https://example.com/gone.zip" ],
                      "place": [ { "src": "", "dest": "" } ] } ],
        "hooks": { "pre_install": [ "pre" ] }
        """);
    SortedDictionary<string, string> before = Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));

    // Assert
    Assert.Contains("asset 0", ex.Message);
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
  }

  // Step 2: "mcmod" assets.

  [Fact]
  public async Task Install_Mcmod_WritesWholeArchiveIntoModDirectory() {
    // Arrange
    InMemorySource source = new(_legacyMoneyFiles);

    // Act
    IReadOnlyList<string> owned =
        await _installer.Install(LegacyMoney(files: source), Context());

    // Assert
    Assert.Equal(
        ["mods/LegacyMoney/manifest.json", "mods/LegacyMoney/LegacyMoney.dll",
          "mods/LegacyMoney/lang/en_US.json"],
        owned);
    Assert.Equal("dll", Read("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.Equal("{}", Read("mods/LegacyMoney/lang/en_US.json"));
    Assert.Equal(
        ["LegacyMoney.dll", "lang/en_US.json", "manifest.json"],
        source.Prefetched.Order(StringComparer.Ordinal));
    Assert.False(_fs.Directory.Exists(Full("plugins")));
  }

  [Fact]
  public async Task Install_McmodOnClient_WritesUnderMods() {
    // Act
    IReadOnlyList<string> owned = await _installer.Install(LegacyMoney(), Context(_client));

    // Assert
    Assert.All(owned, f => Assert.StartsWith("mods/LegacyMoney/", f));
    Assert.Equal("dll", Read("mods/LegacyMoney/LegacyMoney.dll"));
  }

  [Theory]
  [InlineData("""{"name":"LegacyMoney"}""")]
  [InlineData("""{"name":"LegacyMoney","dependencies":[{"name":"Other"}],"extra":1}""")]
  public async Task Install_McmodManifest_IsWrittenAsItIs(string manifest) {
    // Arrange: lip reads only "name"; it neither checks nor fills in "dependencies".
    Candidate candidate = LegacyMoney(
        """ "deps": { "example.com/a": "^1.0.0" } """,
        new InMemorySource(new() { ["manifest.json"] = manifest, ["LegacyMoney.dll"] = "dll" }));

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(manifest, Read("mods/LegacyMoney/manifest.json"));
    Assert.Equal(["mods/LegacyMoney/manifest.json", "mods/LegacyMoney/LegacyMoney.dll"], owned);
  }

  [Fact]
  public async Task Install_McmodWithoutManifest_ThrowsWithoutChanges() {
    // Arrange
    Candidate candidate = LegacyMoney(
        files: new InMemorySource(new() { ["LegacyMoney.dll"] = "dll", ["sub/manifest.json"] = "{}" }));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("""{"entry":"x.dll"}""")]
  [InlineData("""{"name":1}""")]
  [InlineData("""{"name":".."}""")]
  [InlineData("""{"name":"a/b"}""")]
  [InlineData("""{"name":"x","name":"y"}""")]
  public async Task Install_InvalidModManifest_ThrowsWithoutChanges(string manifest) {
    // Arrange
    Candidate candidate = LegacyMoney(
        files: new InMemorySource(new() { ["manifest.json"] = manifest, ["x.dll"] = "dll" }));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_SelfPlacementIntoModDirectoryAfterMcmod_Wins() {
    // Arrange
    _assets[LegacyMoneyUrl] = new InMemorySource(_legacyMoneyFiles);
    Candidate candidate = Candidate(
        $$"""
        "assets": [
          { "type": "mcmod", "urls": [ "{{LegacyMoneyUrl}}" ] },
          { "type": "self", "place": [ { "src": "patch.dll", "dest": "mods/LegacyMoney/LegacyMoney.dll" } ] }
        ]
        """,
        new() { ["patch.dll"] = "patched" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("patched", Read("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.Equal(3, owned.Count);
  }

  // Write rules.

  [Theory]
  [InlineData(".lip/workspace.json")]
  [InlineData(".lip")]
  [InlineData(".LIP/x.txt")]
  public async Task Install_DestInLipDirectory_Throws(string dest) {
    // Arrange
    if (!OperatingSystem.IsWindows() && dest.StartsWith(".LIP")) {
      return;
    }
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", dest)), new() { ["a.txt"] = "a" });
    SortedDictionary<string, string> before = Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));

    // Assert
    Assert.Contains("reserved", ex.Message);
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_DestThroughNameWithTildeWhoseLongNameCannotBeRead_Throws() {
    // Arrange: "dir~1" exists, so it may be a short name, but the file system cannot give its
    // long name (the workspace exists only in memory); the write rules cannot be checked, so
    // they fail (protocol/workspace.md §4).
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has 8.3 short names.");
    _fs.AddDirectory(Full("dir~1"));
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", "dir~1/a.txt")), new() { ["a.txt"] = "a" });
    SortedDictionary<string, string> before = Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));

    // Assert
    Assert.Contains("long name", ex.Message);
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData("trailing./a.txt")]
  [InlineData("a.txt ")]
  [InlineData("config/a:b.txt")]
  public async Task Install_AliasingDestOnWindows_Throws(string dest) {
    // Arrange: Windows drops trailing dots and spaces, and takes ':' for an alternate data
    // stream, so the path would alias another one.
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Such names can be written elsewhere.");
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", dest)), new() { ["a.txt"] = "a" });
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData("lip_lock.json")]
  [InlineData("tooth_lock.json")]
  [InlineData("tooth.json")]
  [InlineData("lip.yaml")]
  [InlineData(".git/hooks/pre-commit")]
  public async Task Install_DestNamedLikeLipOrGitFile_Writes(string dest) {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", dest)), new() { ["a.txt"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal([dest], owned);
    Assert.Equal("a", Read(dest));
  }

  [Fact]
  public async Task Install_DestWithDotSegment_WritesAndOwnsThePathWithoutIt() {
    // Arrange: lip.schema.yaml #/definitions/path allows "." segments.
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", "x/./a.txt")), new() { ["a.txt"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(["x/a.txt"], owned);
    Assert.Equal("a", Read("x/a.txt"));
  }

  [Theory]
  [InlineData("config/a:b.txt")]
  [InlineData("trailing./a.txt")]
  [InlineData("a.txt ")]
  public async Task Install_SchemaValidDestOutsideWindows_Writes(string dest) {
    // Arrange: lip.schema.yaml #/definitions/path allows ':' after the first segment and
    // segments ending in '.' or ' '.
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows cannot write such names as they are.");
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", dest)), new() { ["a.txt"] = "a" });

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal([dest], owned);
    Assert.Equal("a", Read(dest));
  }

  [Fact]
  public async Task Install_OverwritesExistingFile_BacksItUpAsHiddenSibling() {
    // Arrange
    AddFile("dir/existing.txt", "original");
    AddFile("dir/.existing.txt.bak", "someone else's");
    InMemorySource source = new(new() { ["a.txt"] = "a", ["b.txt"] = "b" });
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
    Candidate candidate = Candidate(
        Entry(SelfPlacements(
            Placement("a.txt", "dir/existing.txt"), Placement("b.txt", "dir/b.txt"))),
        source);

    // Act
    await _installer.Install(candidate, Context());

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
    AddFile("foo", "original");
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", "foo"), Placement("b.txt", ".foo.bak")),
        new() { ["a.txt"] = "a", ["b.txt"] = "b" });

    // Act
    await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("a", Read("foo"));
    Assert.Equal("b", Read(".foo.bak"));
    Assert.False(Exists(".foo.bak1"));
  }

  [Fact]
  public async Task Install_PrefetchFails_ThrowsBeforeScripts() {
    // Arrange
    InMemorySource source = new(_legacyMoneyFiles) { FailPrefetch = true };
    Candidate candidate = LegacyMoney(""" "hooks": { "pre_install": [ "pre" ] } """, source);

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    _commandRunner.VerifyNoOtherCalls();
    Assert.False(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Install_DamagedZipEntry_ThrowsBeforeScripts() {
    // Arrange: the data of a.txt is damaged, which only extracting it reveals.
    byte[] zip = Archives.ZipSourceTests.Zip(("a.txt", "content"));
    Archives.ZipSourceTests.CorruptData(zip, "a.txt");
    Candidate candidate = Candidate(
        Entry(SelfPlacements(Placement("a.txt", "a.txt"))
            + """, "hooks": { "pre_install": [ "pre" ] }"""),
        new ZipSource(zip));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    _commandRunner.VerifyNoOtherCalls();
    Assert.False(Exists("a.txt"));
  }

  [Fact]
  public async Task Install_FileOwnedByAnotherPackage_ThrowsAndLeavesWorkspaceIdentical() {
    // Arrange
    AddFile("PeEditor.exe", "other's");
    AddFile("unrelated.txt", "keep");
    Candidate other = Candidate("", id: "example.com/other");
    LockFile lockFile = Lock() with { Packages = [Locked(other, ["PeEditor.exe"])] };
    Candidate candidate = Candidate(
        SelfPlacements(Placement("new.txt", "new.txt"), Placement("PeEditor.exe", "PeEditor.exe"))
            + """, "hooks": { "pre_install": [ "echo pre" ] }""",
        new() { ["PeEditor.exe"] = "exe", ["new.txt"] = "n" });
    SortedDictionary<string, string> before = Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context(lockFile: lockFile)));

    // Assert
    Assert.Contains("PeEditor.exe (owned by example.com/other)", ex.Message);
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Install_PreservedFileOwnedByAnotherPackage_Throws() {
    // Arrange
    AddFile("cfg.ini", "other's");
    Candidate other = Candidate("", id: "example.com/other");
    LockFile lockFile = Lock() with { Packages = [Locked(other, ["cfg.ini"])] };
    Candidate candidate = Candidate(
        SelfPlacements(Placement("cfg.ini", "cfg.ini")) + """, "preserve": [ "cfg.ini" ]""",
        new() { ["cfg.ini"] = "default" });

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context(lockFile: lockFile)));
    Assert.Equal("other's", Read("cfg.ini"));
  }

  [Fact]
  public async Task Install_OwnershipComparedOnNormalizedPaths() {
    // Arrange: the lock spells the path with backslashes and, on Windows, in another case.
    string lockedPath = OperatingSystem.IsWindows() ? @"Mods\LegacyMoney\LegacyMoney.DLL"
        : @"mods\LegacyMoney\LegacyMoney.dll";
    Candidate other = Candidate("", id: "example.com/other");
    LockFile lockFile = Lock() with { Packages = [Locked(other, [lockedPath])] };

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(LegacyMoney(), Context(lockFile: lockFile)));
    Assert.False(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Install_FileOwnedBySamePackageInLock_IsNotAConflict() {
    // Arrange: a reinstall while the old entry is still in the lock.
    Candidate old = LegacyMoney(version: "0.21.0");
    LockFile lockFile = Lock() with {
      Packages = [Locked(old, ["mods/LegacyMoney/LegacyMoney.dll"])],
    };

    // Act
    IReadOnlyList<string> owned = await _installer.Install(
        LegacyMoney(), Context(lockFile: lockFile));

    // Assert
    Assert.Contains("mods/LegacyMoney/LegacyMoney.dll", owned);
  }

  [Fact]
  public async Task Install_PreservedExistingFile_KeepsItAndDoesNotOwnIt() {
    // Arrange: preserve is relative to the workspace.
    AddFile("mods/LegacyMoney/config/config.json", "user");
    InMemorySource source = new(new() {
      ["manifest.json"] = """{"name":"LegacyMoney","dependencies":[]}""",
      ["LegacyMoney.dll"] = "dll",
      ["config/config.json"] = "default",
      ["config/other.json"] = "default",
    });
    Candidate candidate = LegacyMoney(
        """ "preserve": [ "mods/LegacyMoney/config/" ] """, source);

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("user", Read("mods/LegacyMoney/config/config.json"));
    Assert.Equal("default", Read("mods/LegacyMoney/config/other.json"));
    Assert.Equal(
        ["mods/LegacyMoney/manifest.json", "mods/LegacyMoney/LegacyMoney.dll",
          "mods/LegacyMoney/config/other.json"],
        owned);
    // A file expected to be kept is fetched too: a hook command may delete it before it would
    // be written.
    Assert.Contains("config/config.json", source.Prefetched);
  }

  [Fact]
  public async Task Install_PreserveRelativeToModDirectory_DoNotMatch() {
    // Arrange: preserve is relative to the workspace, not to the mod directory.
    AddFile("mods/LegacyMoney/config/config.json", "user");
    InMemorySource source = new(new() {
      ["manifest.json"] = """{"name":"LegacyMoney","dependencies":[]}""",
      ["config/config.json"] = "default",
    });
    Candidate candidate = LegacyMoney(""" "preserve": [ "config/*.json" ] """, source);

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("default", Read("mods/LegacyMoney/config/config.json"));
    Assert.Contains("mods/LegacyMoney/config/config.json", owned);
  }

  [Fact]
  public async Task Install_PreservedFileOwnedBySamePackage_IsKeptAndNotOwned() {
    // Arrange
    AddFile("mods/LegacyMoney/config/config.json", "user");
    LockFile lockFile = Lock() with {
      Packages = [Locked(
          LegacyMoney(version: "0.21.0"), ["mods/LegacyMoney/config/config.json"])],
    };
    Candidate candidate = LegacyMoney(
        """ "preserve": [ "mods/LegacyMoney/config/*.json" ] """,
        new InMemorySource(new() {
          ["manifest.json"] = """{"name":"LegacyMoney","dependencies":[]}""",
          ["config/config.json"] = "default",
        }));

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context(lockFile: lockFile));

    // Assert: a kept file is not written, so the package does not own it
    // (protocol/workspace.md §4).
    Assert.Equal(["mods/LegacyMoney/manifest.json"], owned);
    Assert.Equal("user", Read("mods/LegacyMoney/config/config.json"));
  }

  [Fact]
  public async Task Install_PreservedNewFile_IsWritten() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("cfg/", "cfg/")) + """, "preserve": [ "cfg/*.ini" ]""",
        new() { ["cfg/a.ini"] = "default", ["cfg/b.ini"] = "b" });
    AddFile("cfg/a.ini", "user");

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("user", Read("cfg/a.ini"));
    Assert.Equal("b", Read("cfg/b.ini"));
    Assert.Equal(["cfg/b.ini"], owned);
  }

  [Fact]
  public async Task Install_PreservedFileDeletedByPreInstall_IsWrittenAndOwned() {
    // Arrange: whether the file exists is decided when it is written, after pre_install
    // (protocol/workspace.md §4).
    AddFile("cfg.ini", "user");
    Candidate candidate = Candidate(
        SelfPlacements(Placement("cfg.ini", "cfg.ini"))
            + """, "preserve": [ "cfg.ini" ], "hooks": { "pre_install": [ "reset" ] }""",
        new() { ["cfg.ini"] = "default" });
    _commandRunner.Setup(r => r.Run("reset", _root))
        .Callback(() => _fs.File.Delete(Full("cfg.ini")))
        .Returns(Task.CompletedTask);

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("default", Read("cfg.ini"));
    Assert.Equal(["cfg.ini"], owned);
  }

  [Fact]
  public async Task Install_PreservedFileCreatedByPreInstall_IsKeptAndNotOwned() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("cfg.ini", "cfg.ini"), Placement("a.txt", "a.txt"))
            + """, "preserve": [ "cfg.ini" ], "hooks": { "pre_install": [ "init" ] }""",
        new() { ["cfg.ini"] = "default", ["a.txt"] = "a" });
    _commandRunner.Setup(r => r.Run("init", _root))
        .Callback(() => AddFile("cfg.ini", "hook"))
        .Returns(Task.CompletedTask);

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal("hook", Read("cfg.ini"));
    Assert.Equal(["a.txt"], owned);
  }

  [Fact]
  public async Task Install_PreservedFileDeletedByPreInstall_FailureUndoesTheWrite() {
    // Arrange: the file written in place of the deleted one is undone; the deletion by the hook
    // command is not (protocol/workspace.md §6 rule 3).
    AddFile("cfg.ini", "user");
    Candidate candidate = Candidate(
        SelfPlacements(Placement("cfg.ini", "cfg.ini"))
            + """, "preserve": [ "cfg.ini" ],"""
            + """ "hooks": { "pre_install": [ "reset" ], "post_install": [ "fail" ] }""",
        new() { ["cfg.ini"] = "default" });
    _commandRunner.Setup(r => r.Run("reset", _root))
        .Callback(() => _fs.File.Delete(Full("cfg.ini")))
        .Returns(Task.CompletedTask);
    _commandRunner.Setup(r => r.Run("fail", _root))
        .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.False(Exists("cfg.ini"));
  }

  [Fact]
  public async Task Install_TargetIsExistingDirectory_ThrowsWithoutChanges() {
    // Arrange
    _fs.AddDirectory(Full("mods/LegacyMoney/LegacyMoney.dll"));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(LegacyMoney(), Context()));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_ParentIsExistingFile_ThrowsWithoutChanges() {
    // Arrange
    AddFile("mods/LegacyMoney/lang", "file");
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(LegacyMoney(), Context()));
    Assert.Equal(before, Snapshot());
  }

  // Hooks.

  [Fact]
  public async Task Install_Hooks_RunInWorkspaceRootAroundWrites() {
    // Arrange
    Candidate candidate = LegacyMoney(
        """ "hooks": { "pre_install": [ "pre1", "pre2" ], "post_install": [ "post" ], "pre_uninstall": [ "x" ] } """);
    string dll = Full("mods/LegacyMoney/LegacyMoney.dll");
    List<(string Command, string Cwd, bool DllExists)> calls = [];
    _commandRunner.Setup(r => r.Run(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string c, string cwd) => calls.Add((c, cwd, _fs.File.Exists(dll))))
        .Returns(Task.CompletedTask);

    // Act
    await _installer.Install(candidate, Context());

    // Assert
    Assert.Equal(
        [("pre1", _root, false), ("pre2", _root, false), ("post", _root, true)], calls);
  }

  [Fact]
  public async Task Install_IgnoreScripts_RunsNoHooks() {
    // Arrange
    Candidate candidate = LegacyMoney(
        """ "hooks": { "pre_install": [ "pre" ], "post_install": [ "post" ] } """);

    // Act
    await _installer.Install(candidate, Context(ignoreScripts: true));

    // Assert
    _commandRunner.VerifyNoOtherCalls();
    Assert.True(Exists("mods/LegacyMoney/LegacyMoney.dll"));
  }

  [Fact]
  public async Task Install_PreInstallFails_ThrowsWithoutWriting() {
    // Arrange
    Candidate candidate = LegacyMoney(""" "hooks": { "pre_install": [ "fail" ] } """);
    _commandRunner.Setup(r => r.Run("fail", It.IsAny<string>()))
        .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.False(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Install_WriteFailsMidway_RollsBackWrittenFiles() {
    // Arrange
    AddFile("existing.txt", "original");
    InMemorySource source = new(new() { ["a.txt"] = "a", ["b.txt"] = "b", ["c.txt"] = "c" });
    source.Failing.Add("c.txt");
    Candidate candidate = Candidate(
        Entry(SelfPlacements(
            Placement("a.txt", "existing.txt"),
            Placement("b.txt", "new/dir/b.txt"),
            Placement("c.txt", "c.txt"))),
        source);
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_PostInstallFails_RollsBackWrittenFiles() {
    // Arrange
    Candidate candidate = LegacyMoney(""" "hooks": { "post_install": [ "fail" ] } """);
    _commandRunner.Setup(r => r.Run("fail", It.IsAny<string>()))
        .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Install(candidate, Context()));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_DryRun_ReportsPlanWithoutChanges() {
    // Arrange
    Candidate candidate = LegacyMoney(
        """ "hooks": { "pre_install": [ "pre" ], "post_install": [ "post" ] } """);
    SortedDictionary<string, string> before = Snapshot();

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context(dryRun: true));

    // Assert
    Assert.Equal(3, owned.Count);
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
    _ui.Verify(u => u.PrintInfo(It.Is<string>(s => s.Contains("LegacyMoney.dll"))), Times.Once);
  }

  // Uninstalling.

  [Fact]
  public async Task Uninstall_Mod_DeletesOwnedFilesAndPrunesEmptyDirectories() {
    // Arrange
    Candidate candidate = LegacyMoney();
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());
    AddFile("mods/Other/x.dll", "x");

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context());

    // Assert
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney")));
    Assert.True(_fs.Directory.Exists(Full("mods")));
    Assert.True(Exists("mods/Other/x.dll"));
  }

  [Fact]
  public async Task Uninstall_UsesTheRecordedEntryOnly() {
    // Arrange: nothing but the lock entry; no archive or asset is opened.
    AddFile("a.txt", "a");
    AddFile("keep.txt", "k");
    AddFile("logs/x.log", "x");
    using JsonDocument manifest = JsonDocument.Parse($$"""
        {
          "format": 4,
          "id": "{{Pack}}",
          "version": "1.0.0",
          "variants": {
            "": [
              {
                "platform": "server", "runtime": "win-x64",
                "assets": [ { "type": "zip", "urls": [ "https://example.com/gone.zip" ] } ],
                "preserve": [ "keep.txt" ],
                "purge": [ "logs/" ],
                "hooks": { "pre_uninstall": [ "pre" ] }
              }
            ]
          }
        }
        """);
    LockedPackage locked = new() {
      Manifest = manifest.RootElement,
      Variant = "",
      Explicit = true,
      Source = Pack,
      Commit = new string('0', 40),
      Files = ["a.txt", "keep.txt"],
    };

    // Act
    await _installer.Uninstall(locked, Context());

    // Assert
    Assert.False(Exists("a.txt"));
    Assert.True(Exists("keep.txt"));
    Assert.False(_fs.Directory.Exists(Full("logs")));
    _commandRunner.Verify(r => r.Run("pre", _root), Times.Once);
    _provider.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_InvalidRecordedEntry_Throws() {
    // Arrange
    LockedPackage locked = Locked(Candidate(""), []) with {
      Manifest = JsonDocument.Parse("""{ "format": 4, "variants": {} }""").RootElement,
    };

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _installer.Uninstall(locked, Context()));
  }

  [Fact]
  public async Task Uninstall_PreexistingEmptyDirectory_IsKept() {
    // Arrange
    _fs.AddDirectory(Full("mods/LegacyMoney/empty"));
    _fs.AddDirectory(Full("unrelated"));
    Candidate candidate = LegacyMoney();
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context());

    // Assert
    Assert.True(_fs.Directory.Exists(Full("mods/LegacyMoney/empty")));
    Assert.True(_fs.Directory.Exists(Full("unrelated")));
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney/lang")));
  }

  [Fact]
  public async Task Uninstall_Preserve_KeepsMatchingOwnedFiles() {
    // Arrange
    Candidate candidate = LegacyMoney(
        """ "preserve": [ "mods/LegacyMoney/config/" ] """,
        new InMemorySource(new() {
          ["manifest.json"] = """{"name":"LegacyMoney","dependencies":[]}""",
          ["LegacyMoney.dll"] = "dll",
          ["config/config.json"] = "default",
        }));
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context());

    // Assert
    Assert.False(Exists("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.False(Exists("mods/LegacyMoney/manifest.json"));
    Assert.Equal("default", Read("mods/LegacyMoney/config/config.json"));
  }

  [Fact]
  public async Task Uninstall_FileOwnedByAnotherPackage_IsKept() {
    // Arrange
    AddFile("shared.txt", "s");
    Candidate candidate = Candidate("");
    Candidate other = Candidate("", id: "example.com/other");
    LockFile lockFile = Lock() with {
      Packages = [Locked(candidate, ["shared.txt"]), Locked(other, ["shared.txt"])],
    };

    // Act
    await _installer.Uninstall(Locked(candidate, ["shared.txt"]), Context(lockFile: lockFile));

    // Assert
    Assert.True(Exists("shared.txt"));
  }

  [Fact]
  public async Task Uninstall_RecordedPathsWithDotSegments_KeepLipAndOthersFiles() {
    // Arrange
    AddFile(".lip/lock", "l");
    AddFile("shared.txt", "s");
    AddFile("own.txt", "o");
    Candidate candidate = Candidate("");
    Candidate other = Candidate("", id: "example.com/other");
    string[] recorded = ["./.lip/lock", "./shared.txt", "./own.txt"];
    LockFile lockFile = Lock() with {
      Packages = [Locked(candidate, recorded), Locked(other, ["shared.txt"])],
    };

    // Act
    await _installer.Uninstall(Locked(candidate, recorded), Context(lockFile: lockFile));

    // Assert
    Assert.True(Exists(".lip/lock"));
    Assert.True(Exists("shared.txt"));
    Assert.False(Exists("own.txt"));
  }

  [Fact]
  public async Task Uninstall_PurgeDirectoryPattern_DeletesRecursivelyExceptOthers() {
    // Arrange
    Candidate candidate = LegacyMoney(
        """ "purge": [ "mods/LegacyMoney/data/", "mods/LegacyMoney/*.log" ] """);
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());
    AddFile("mods/LegacyMoney/data/db/money.db", "db");
    AddFile("mods/LegacyMoney/data/top.txt", "t");
    _fs.AddDirectory(Full("mods/LegacyMoney/data/emptysub"));
    AddFile("mods/LegacyMoney/data/foreign.txt", "f");
    AddFile("mods/LegacyMoney/run.log", "log");
    AddFile("mods/LegacyMoney/keep.txt", "k");
    Candidate other = Candidate("", id: "example.com/other");
    LockFile lockFile = Lock() with {
      Packages = [
        Locked(candidate, owned),
        Locked(other, ["mods/LegacyMoney/data/foreign.txt"]),
      ],
    };

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context(lockFile: lockFile));

    // Assert
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney/data/db")));
    Assert.False(_fs.Directory.Exists(Full("mods/LegacyMoney/data/emptysub")));
    Assert.False(Exists("mods/LegacyMoney/data/top.txt"));
    Assert.False(Exists("mods/LegacyMoney/run.log"));
    Assert.False(Exists("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.Equal("f", Read("mods/LegacyMoney/data/foreign.txt"));
    Assert.Equal("k", Read("mods/LegacyMoney/keep.txt"));
  }

  [Fact]
  public async Task Uninstall_Purge_RelativeToWorkspaceRoot() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("PeEditor.exe", "PeEditor.exe"))
            + """, "purge": [ "PeEditor.log", "cache/" ]""",
        new() { ["PeEditor.exe"] = "exe" });
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());
    AddFile("PeEditor.log", "log");
    AddFile("sub/PeEditor.log", "log");
    AddFile("cache/a/b.bin", "b");
    AddFile("bedrock_server.exe", "bds");

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context());

    // Assert
    Assert.False(Exists("PeEditor.exe"));
    Assert.False(Exists("PeEditor.log"));
    Assert.True(Exists("sub/PeEditor.log"));
    Assert.False(_fs.Directory.Exists(Full("cache")));
    Assert.True(Exists("bedrock_server.exe"));
    Assert.True(_fs.Directory.Exists(_root));
  }

  [Theory]
  [InlineData("mods/")]
  [InlineData("plugins/")]
  [InlineData("*/")]
  [InlineData("plugins")]
  public async Task Uninstall_PurgeNamingLayoutDirectories_RemovesNothingThroughThem(
      string pattern) {
    // Arrange
    Candidate candidate = Candidate($$""" "purge": [ "{{pattern}}" ] """);
    AddFile("mods/Other/other.dll", "dll");

    // Act
    await _installer.Uninstall(Locked(candidate, []), Context());

    // Assert
    Assert.True(Exists("mods/Other/other.dll"));
    Assert.True(_fs.Directory.Exists(Full("mods")));
  }

  [Fact]
  public async Task Uninstall_PurgeMatchingLipDirectory_LeavesIt() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", "a.txt"))
            + """, "purge": [ ".lip/", ".lip/workspace.json", "*/", "**/*.json" ]""",
        new() { ["a.txt"] = "a" });
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());
    AddFile(".lip/workspace.json", "lock");
    AddFile("logs/a.json", "x");

    // Act
    await _installer.Uninstall(
        Locked(candidate, [.. owned, ".lip/workspace.json"]), Context());

    // Assert
    Assert.Equal("lock", Read(".lip/workspace.json"));
    Assert.False(Exists("a.txt"));
    Assert.False(Exists("logs/a.json"));
  }

  [Fact]
  public async Task Uninstall_PurgeGlobs_MatchOnlyAsDeepAsThePattern() {
    // Arrange
    Candidate candidate = Candidate(
        SelfPlacements(Placement("a.txt", "a.txt"))
            + """, "purge": [ "*.log", "logs/**/*.txt" ]""",
        new() { ["a.txt"] = "a" });
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());
    AddFile("top.log", "x");
    AddFile("worlds/deep.log", "keep");
    AddFile("logs/a.txt", "x");
    AddFile("logs/2026/b.txt", "x");
    AddFile("logs/keep.bin", "keep");

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context());

    // Assert
    Assert.False(Exists("top.log"));
    Assert.True(Exists("worlds/deep.log"));
    Assert.False(Exists("logs/a.txt"));
    Assert.False(_fs.Directory.Exists(Full("logs/2026")));
    Assert.True(Exists("logs/keep.bin"));
  }

  [Fact]
  public async Task Uninstall_MissingOwnedFile_IsSkipped() {
    // Arrange
    AddFile("a.txt", "a");
    Candidate candidate = Candidate("");

    // Act
    await _installer.Uninstall(Locked(candidate, ["gone.txt", "a.txt"]), Context());

    // Assert
    Assert.False(Exists("a.txt"));
  }

  [Fact]
  public async Task Uninstall_Hooks_RunInWorkspaceRootAroundDeletion() {
    // Arrange
    Candidate candidate = LegacyMoney(
        """ "hooks": { "pre_uninstall": [ "pre" ], "post_uninstall": [ "post" ], "post_install": [ "x" ] } """);
    IReadOnlyList<string> owned =
        await _installer.Install(candidate, Context(ignoreScripts: true));
    string dll = Full("mods/LegacyMoney/LegacyMoney.dll");
    List<(string Command, string Cwd, bool DllExists)> calls = [];
    _commandRunner.Setup(r => r.Run(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string c, string cwd) => calls.Add((c, cwd, _fs.File.Exists(dll))))
        .Returns(Task.CompletedTask);

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context());

    // Assert
    Assert.Equal([("pre", _root, true), ("post", _root, false)], calls);
  }

  [Fact]
  public async Task Uninstall_IgnoreScripts_RunsNoHooks() {
    // Arrange
    Candidate candidate = Candidate(
        """ "hooks": { "pre_uninstall": [ "pre" ], "post_uninstall": [ "post" ] } """);

    // Act
    await _installer.Uninstall(Locked(candidate, []), Context(ignoreScripts: true));

    // Assert
    _commandRunner.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Uninstall_DryRun_ReportsWithoutChanges() {
    // Arrange
    Candidate candidate = LegacyMoney(""" "hooks": { "pre_uninstall": [ "pre" ] } """);
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context());
    SortedDictionary<string, string> before = Snapshot();

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context(dryRun: true));

    // Assert
    Assert.Equal(before, Snapshot());
    _commandRunner.VerifyNoOtherCalls();
    _ui.Verify(u => u.PrintInfo(It.Is<string>(s => s.Contains("Would delete"))), Times.Exactly(3));
  }

  [Fact]
  public async Task Update_UninstallOldThenInstallNew_PreservesConfigAndReplacesFiles() {
    // Arrange
    const string Preserve = """ "preserve": [ "mods/LegacyMoney/config/config.json" ] """;
    Candidate v1 = LegacyMoney(Preserve, new InMemorySource(new() {
      ["manifest.json"] = """{"name":"LegacyMoney","dependencies":[]}""",
      ["LegacyMoney.dll"] = "v1",
      ["old.dll"] = "old",
      ["config/config.json"] = "default1",
    }), "0.21.0");
    IReadOnlyList<string> ownedV1 = await _installer.Install(v1, Context());
    _fs.File.WriteAllText(Full("mods/LegacyMoney/config/config.json"), "user");
    LockedPackage lockedV1 = Locked(v1, ownedV1);
    Candidate v2 = LegacyMoney(Preserve, new InMemorySource(new() {
      ["manifest.json"] = """{"name":"LegacyMoney","dependencies":[]}""",
      ["LegacyMoney.dll"] = "v2",
      ["config/config.json"] = "default2",
    }));

    // Act
    await _installer.Uninstall(lockedV1, Context(lockFile: Lock(lockedV1)));
    IReadOnlyList<string> ownedV2 = await _installer.Install(v2, Context());

    // Assert
    Assert.Equal("v2", Read("mods/LegacyMoney/LegacyMoney.dll"));
    Assert.False(Exists("mods/LegacyMoney/old.dll"));
    Assert.Equal("user", Read("mods/LegacyMoney/config/config.json"));
    Assert.Equal(
        ["mods/LegacyMoney/manifest.json", "mods/LegacyMoney/LegacyMoney.dll"], ownedV2);
  }

  [Fact]
  public async Task Update_OwnedFileOverwrittenByNewVersion_IsOwnedAgain() {
    // Arrange
    Candidate v1 = Candidate(SelfPlacements(Placement("a.txt", "a.txt")), new() { ["a.txt"] = "1" });
    IReadOnlyList<string> ownedV1 = await _installer.Install(v1, Context());
    LockedPackage lockedV1 = Locked(v1, ownedV1);
    Candidate v2 = Candidate(
        SelfPlacements(Placement("a.txt", "a.txt")), new() { ["a.txt"] = "2" }, version: "2.0.0");

    // Act
    await _installer.Uninstall(lockedV1, Context(lockFile: Lock(lockedV1)));
    IReadOnlyList<string> ownedV2 = await _installer.Install(v2, Context());

    // Assert
    Assert.Equal(["a.txt"], ownedV2);
    Assert.Equal("2", Read("a.txt"));
  }

  /// The files of an archive, held in memory.
  private sealed class InMemorySource(Dictionary<string, string> files) : ISource {
    public List<string> Prefetched { get; } = [];

    public HashSet<string> Failing { get; } = [];

    /// Called with the key before it is read.
    public Action<string>? OnOpen { get; set; }

    public bool FailPrefetch { get; init; }

    public IEnumerable<string> Keys => files.Keys;

    public Task<Stream> OpenRead(string key) {
      OnOpen?.Invoke(key);
      return Failing.Contains(key)
          ? throw new IOException($"cannot read {key}")
          : Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(files[key])));
    }

    public Task Prefetch(IEnumerable<string> keys) {
      if (FailPrefetch) {
        throw new IOException("network down");
      }
      Prefetched.AddRange(keys);
      return Task.CompletedTask;
    }
  }
}

/// Assets downloaded through the archive provider (protocol/workspace.md §4 step 2).
public class McmodInstallerAssetDownloadTests {
  private static readonly OperationOptions _options = new(Rid: WorkspaceHarness.Runtime);

  private static string ZipEntry(params string[] urls) => $$"""
      "assets": [ { "type": "zip", "urls": [ {{string.Join(", ", urls.Select(u => $"\"{u}\""))}} ],
                    "place": [ { "src": "", "dest": "out/" } ] } ]
      """;

  /// The workspace part of the snapshot (lip's cache, outside it, may change).
  private static SortedDictionary<string, string> Workspace(WorkspaceHarness h) => new(
      h.Snapshot().Where(e => e.Key.StartsWith(h.Root, StringComparison.Ordinal))
          .ToDictionary(e => e.Key, e => e.Value),
      StringComparer.Ordinal);

  [Fact]
  public async Task Install_ZipAsset_DownloadedFromFirstUrlThatCanBeObtained() {
    // Arrange
    WorkspaceHarness h = new();
    h.AddDownload("https://two.example.com/a.zip", WorkspaceHarness.Zip(new() { ["a.txt"] = "two" }));
    h.AddDownload("https://three.example.com/a.zip", WorkspaceHarness.Zip(new() { ["a.txt"] = "three" }));
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml("example.com/a", "1.0.0", ZipEntry(
          "https://one.example.com/a.zip",
          "https://two.example.com/a.zip",
          "https://three.example.com/a.zip")),
    });

    // Act
    await h.Service.Install(["example.com/a"], _options);

    // Assert
    Assert.Equal("two", h.Fs.File.ReadAllText(h.Path("out/a.txt")));
    Assert.Equal(["out/a.txt"], Assert.Single((await h.Lock()).Packages).Files);
  }

  [Fact]
  public async Task Install_ZipAssetNoUrlCanBeObtained_FailsWithoutChanges() {
    // Arrange
    WorkspaceHarness h = new();
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml("example.com/a", "1.0.0", ZipEntry(
          "https://one.example.com/a.zip", "https://two.example.com/a.zip")),
    });
    SortedDictionary<string, string> before = Workspace(h);

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => h.Service.Install(["example.com/a"], _options));
    Assert.Equal(before, Workspace(h));
  }

  [Fact]
  public async Task Install_ZipAssetNotAZipFile_FailsWithoutChanges() {
    // Arrange
    WorkspaceHarness h = new();
    h.AddDownload("https://one.example.com/a.zip", Encoding.UTF8.GetBytes("not a zip"));
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/a", "1.0.0", ZipEntry("https://one.example.com/a.zip")),
    });
    SortedDictionary<string, string> before = Workspace(h);

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => h.Service.Install(["example.com/a"], _options));
    Assert.Equal(before, Workspace(h));
  }
}
