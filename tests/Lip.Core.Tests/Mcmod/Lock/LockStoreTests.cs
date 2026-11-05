using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod.Lock;

public class LockStoreTests {
  private static readonly string _root = OperatingSystem.IsWindows()
      ? @"C:\lip-workspace"
      : "/lip-workspace";

  private static readonly string _fixtureDir =
      Path.Combine(AppContext.BaseDirectory, "Mcmod", "Lock", "Fixtures");

  private readonly MockFileSystem _fs = new();
  private readonly LockStore _store;

  public LockStoreTests() {
    _fs.AddDirectory(_root);
    _store = new LockStore(_fs);
  }

  private string LipLockPath => _store.GetLockPath(_root);
  private string ToothLockPath => _fs.Path.Combine(_root, "tooth_lock.json");

  /// The real v3 lock lip wrote after `lip install github.com/LiteLDev/LeviLamina` in
  /// C:\lip-workspace. On other platforms its Windows paths are rewritten to /lip-workspace.
  private static string LoadRealToothLock() {
    string text = File.ReadAllText(
        Path.Combine(_fixtureDir, "tooth_lock.levilamina.v3.json"));
    if (OperatingSystem.IsWindows()) {
      return text;
    }

    JsonNode root = JsonNode.Parse(text)!;
    foreach (JsonNode? package in root["packages"]!.AsArray()) {
      JsonArray files = package!["files"]!.AsArray();
      for (int i = 0; i < files.Count; i++) {
        files[i] = files[i]!.GetValue<string>()
            .Replace(@"C:\lip-workspace", "/lip-workspace")
            .Replace('\\', '/');
      }
    }

    return root.ToJsonString();
  }

  private static LockedPackage SamplePackage(
      string name,
      ArchiveIdentity? archive,
      string manifestJson = """{"name":"x","version":"1.0.0"}""") => new() {
        Identity = name,
        Version = "1.0.0",
        Kind = "mcmod",
        Explicit = true,
        Chain = [
          new LockedHop("github.com/LiteLDev/" + name + "@branch:main", 0) {
            Commit = new string('e', 40),
          },
          new LockedHop("https://example.com/" + name + ".mcmod", null),
        ],
        ArchiveKey = "https://example.com/" + name + ".mcmod",
        Archive = archive,
        Files = ["mods/" + name + "/" + name + ".dll"],
        Manifest = JsonDocument.Parse(manifestJson).RootElement.Clone(),
      };

  [Fact]
  public async Task Load_NoLockFile_ReturnsEmptyLock() {
    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    Assert.Equal(ManifestFormat.Format, lockFile.Format);
    Assert.Null(lockFile.Platform);
    Assert.Null(lockFile.Runtime);
    Assert.Empty(lockFile.Packages);
  }

  [Fact]
  public async Task Load_RealV3ToothLock_MigratesEveryPackage() {
    // Arrange
    _fs.AddFile(ToothLockPath, new MockFileData(LoadRealToothLock()));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    Assert.Equal(ManifestFormat.Format, lockFile.Format);
    Assert.Null(lockFile.Platform);
    Assert.Null(lockFile.Runtime);
    Assert.Equal(
        [
          ("bdsdown", "1.2.1"),
          ("bds", "1.26.51"),
          ("CrashLogger", "1.3.3"),
          ("levilamina-loc", "1.6.4"),
          ("PeEditor", "3.9.2"),
          ("PreLoader", "1.16.5"),
          ("bedrock-runtime-data", "26.51.1-server.11"),
          ("LeviLamina", "26.51.8"),
        ],
        lockFile.Packages.Select(p => (p.Identity, p.Version)));

    foreach (LockedPackage package in lockFile.Packages) {
      string tooth = package.Manifest.GetProperty("tooth").GetString()!;
      Assert.Equal("tooth", package.Kind);
      Assert.Equal($"github.com/LiteLDev/{package.Identity}", tooth);
      Assert.Equal([new LockedHop(tooth, null)], package.Chain);
      Assert.Equal($"{tooth}@{package.Version}", package.ArchiveKey);
      Assert.Null(package.Archive);
      Assert.Equal(package.Version, package.Manifest.GetProperty("version").GetString());
      Assert.Equal(3, package.Manifest.GetProperty("format_version").GetInt32());
      Assert.All(package.Files, f => {
        Assert.False(Path.IsPathRooted(f));
        Assert.DoesNotContain('\\', f);
      });
    }

    Assert.Equal(
        ["LeviLamina"],
        lockFile.Packages.Where(p => p.Explicit).Select(p => p.Identity));

    LockedPackage leviLamina = lockFile.Packages.Single(p => p.Identity == "LeviLamina");
    Assert.Equal(
        [
          "mods/LeviLamina/manifest.json",
          "mods/LeviLamina/LeviLamina.pdb",
          "mods/LeviLamina/LeviLamina.dll",
        ],
        leviLamina.Files);
    Assert.Equal(
        "LeviLamina",
        leviLamina.Manifest.GetProperty("info").GetProperty("name").GetString());
    Assert.True(leviLamina.Manifest.GetProperty("variants").GetArrayLength() > 0);

    Assert.Equal(["bdsdown.exe"], lockFile.Packages[0].Files);
    Assert.Empty(lockFile.Packages[1].Files);
    Assert.Equal(15, lockFile.Packages[3].Files.Count);
    Assert.Equal("mods/LeviLamina/lang/de.json", lockFile.Packages[3].Files[0]);
    Assert.Equal(["bedrock_runtime_data"], lockFile.Packages[6].Files);
  }

  [Fact]
  public async Task Load_V3LockWithVariant_ChainKeepsVariant() {
    // Arrange
    string file = _fs.Path.Combine(_root, "mods", "a.dll");
    string outside = OperatingSystem.IsWindows() ? @"D:\elsewhere\b.dll" : "/elsewhere/b.dll";
    string json = $$"""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [
            {
              "files": [{{JsonSerializer.Serialize(file)}}, {{JsonSerializer.Serialize(outside)}}],
              "locked": true,
              "manifest": {
                "format_version": 3,
                "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
                "tooth": "github.com/LiteLDev/LeviLamina",
                "version": "1.0.0-rc.1",
                "variants": [ { "label": "client" } ]
              },
              "variant": "client"
            }
          ]
        }
        """;
    _fs.AddFile(ToothLockPath, new MockFileData(json));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal("LeviLamina-client", package.Identity);
    Assert.Equal("1.0.0-rc.1", package.Version);
    Assert.True(package.Explicit);
    Assert.Equal([new LockedHop("github.com/LiteLDev/LeviLamina#client", null)], package.Chain);
    Assert.Equal("github.com/LiteLDev/LeviLamina@1.0.0-rc.1", package.ArchiveKey);
    Assert.Equal(["mods/a.dll", outside], package.Files);
  }

  [Fact]
  public async Task Load_BothLocksExist_PrefersLipLock() {
    // Arrange
    await _store.Save(_root, new LockFile { Runtime = "win-x64" });
    _fs.AddFile(ToothLockPath, new MockFileData("not json"));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    Assert.Equal("win-x64", lockFile.Runtime);
  }

  [Fact]
  public async Task Save_ThenLoad_RoundTrips() {
    // Arrange
    LockFile original = new() {
      Platform = "server",
      Runtime = "win-x64",
      Packages = [
        SamplePackage("Zip", new ZipArchiveIdentity(new string('a', 64), "https://example.com/z")),
        SamplePackage("ZipNoUrl", new ZipArchiveIdentity(new string('b', 64), null)),
        SamplePackage("Git", new GitArchiveIdentity(
            "https://github.com/LiteLDev/Git.git", new string('c', 40), "sub/dir")),
        SamplePackage(
            "File", new ZipArchiveIdentity(new string('d', 64), "file:///abs/x.mcmod")),
        SamplePackage(
            "Legacy",
            null,
            """{"tooth":"github.com/a/b","nested":{"list":[1,"two",null,true]}}"""),
      ],
    };

    // Act
    await _store.Save(_root, original);
    LockFile loaded = await _store.Load(_root);

    // Assert
    Assert.Equal(original.Platform, loaded.Platform);
    Assert.Equal(original.Runtime, loaded.Runtime);
    Assert.Equal(original.Packages.Count, loaded.Packages.Count);
    for (int i = 0; i < original.Packages.Count; i++) {
      LockedPackage expected = original.Packages[i];
      LockedPackage actual = loaded.Packages[i];
      Assert.Equal(expected.Identity, actual.Identity);
      Assert.Equal(expected.Version, actual.Version);
      Assert.Equal(expected.Kind, actual.Kind);
      Assert.Equal(expected.Explicit, actual.Explicit);
      Assert.Equal(expected.Chain, actual.Chain);
      Assert.Equal(expected.ArchiveKey, actual.ArchiveKey);
      Assert.Equal(expected.Archive, actual.Archive);
      Assert.Equal(expected.Files, actual.Files);
      Assert.True(JsonElement.DeepEquals(expected.Manifest, actual.Manifest));
    }
  }

  [Fact]
  public async Task Load_V3LockWithFilesUnderPlugins_RewritesThemUnderMods() {
    // Arrange: lip v0.x installed server mods into plugins.
    string upper = OperatingSystem.IsWindows() ? "Plugins" : "plugins";
    string[] files = [
      _fs.Path.Join(_root, "plugins", "A", "a.dll"),
      _fs.Path.Join(_root, upper, "A", "b.dll"),
      _fs.Path.Join(_root, "mods", "A", "a.dll"),
      _fs.Path.Join(_root, "pluginsx", "c.txt"),
      _fs.Path.Join(_root, "d.txt"),
    ];
    _fs.AddFile(ToothLockPath, new MockFileData($$"""
        {
          "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [{
            "files": {{JsonSerializer.Serialize(files)}}, "locked": true, "variant": "",
            "manifest": { "format_version": 3,
              "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
              "tooth": "github.com/a/b", "version": "1.0.0" }
          }]
        }
        """));

    // Act
    LockFile loaded = await _store.Load(_root);

    // Assert: the path through mods appears once.
    Assert.Equal(
        ["mods/A/a.dll", "mods/A/b.dll", "pluginsx/c.txt", "d.txt"],
        Assert.Single(loaded.Packages).Files);
  }

  [Fact]
  public async Task Save_Always_WritesIndentedV4JsonWithArchiveKind() {
    // Arrange
    LockFile lockFile = new() {
      Packages = [
        SamplePackage("Git", new GitArchiveIdentity("https://x/y.git", "abc", "")),
      ],
    };

    // Act
    await _store.Save(_root, lockFile);

    // Assert
    string text = _fs.File.ReadAllText(LipLockPath);
    Assert.Contains('\n', text);
    JsonElement root = JsonDocument.Parse(text).RootElement;
    Assert.Equal("bf45b1", root.GetProperty("format").GetString());
    JsonElement archive = root.GetProperty("packages")[0].GetProperty("archive");
    Assert.Equal("git", archive.GetProperty("kind").GetString());
    Assert.Equal("abc", archive.GetProperty("commit").GetString());
    Assert.Equal(JsonValueKind.Object, root.GetProperty("packages")[0]
        .GetProperty("manifest").ValueKind);
  }

  [Fact]
  public async Task Save_MigratedLock_WritesLipLockAndDeletesToothLock() {
    // Arrange
    _fs.AddFile(ToothLockPath, new MockFileData(LoadRealToothLock()));
    LockFile migrated = await _store.Load(_root);

    // Act
    await _store.Save(_root, migrated with { Platform = "server" });

    // Assert
    Assert.False(_fs.File.Exists(ToothLockPath));
    Assert.True(_fs.File.Exists(LipLockPath));
    Assert.Empty(_fs.Directory.GetFiles(_root));
    LockFile reloaded = await _store.Load(_root);
    Assert.Equal("server", reloaded.Platform);
    Assert.Equal(8, reloaded.Packages.Count);
    Assert.All(reloaded.Packages, p => Assert.Null(p.Archive));
    Assert.Equal(
        migrated.Packages.SelectMany(p => p.Files),
        reloaded.Packages.SelectMany(p => p.Files));
  }

  [Fact]
  public async Task Save_ExistingLock_Overwrites() {
    // Arrange
    await _store.Save(_root, new LockFile {
      Packages = [SamplePackage("A", null), SamplePackage("B", null)],
    });

    // Act
    await _store.Save(_root, new LockFile { Packages = [SamplePackage("C", null)] });

    // Assert
    LockFile loaded = await _store.Load(_root);
    Assert.Equal(["C"], loaded.Packages.Select(p => p.Identity));
    Assert.Equal([LipLockPath], _fs.Directory.GetFiles(_fs.Path.GetDirectoryName(LipLockPath)!));
    Assert.Empty(_fs.Directory.GetFiles(_root));
  }

  [Fact]
  public void GetLockPath_Always_IsWorkspaceJsonInLipDirectoryOfWorkspace() {
    // Act
    string path = _store.GetLockPath(_root + _fs.Path.DirectorySeparatorChar);

    // Assert
    Assert.Equal(_fs.Path.Join(_root, ".lip", "workspace.json"), path);
  }

  [Fact]
  public void GetMutexPathAndGetJournalPath_Always_AreInLipDirectoryOfWorkspace() {
    // Act & Assert
    Assert.Equal(_fs.Path.Join(_root, ".lip", "lock"), _store.GetMutexPath(_root));
    Assert.Equal(_fs.Path.Join(_root, ".lip", "journal"), _store.GetJournalPath(_root));
  }

  [Fact]
  public async Task Save_V036LockJson_WritesWorkspaceJsonAndDeletesLockJson() {
    // Arrange
    string v036 = _fs.Path.Join(_root, ".lip", "lock.json");
    _fs.AddFile(v036, new MockFileData(JsonSerializer.Serialize(
        new LockFile { Packages = [SamplePackage("A", null)] })));
    _fs.AddFile(ToothLockPath, new MockFileData("{}"));

    // Act
    LockFile loaded = await _store.Load(_root);
    await _store.Save(_root, loaded);

    // Assert
    Assert.Equal(["A"], loaded.Packages.Select(p => p.Identity));
    Assert.True(_fs.File.Exists(LipLockPath));
    Assert.False(_fs.File.Exists(v036));
    Assert.True(_fs.File.Exists(ToothLockPath));
  }

  [Fact]
  public async Task Save_Always_WritesOnlyLipDirectoryAndNoWorkspacePath() {
    // Act
    await _store.Save(_root, new LockFile());

    // Assert
    JsonElement root = JsonDocument.Parse(_fs.File.ReadAllText(LipLockPath)).RootElement;
    Assert.False(root.TryGetProperty("workspace", out _));
    Assert.Equal([_fs.Path.Join(_root, ".lip")], _fs.Directory.GetFileSystemEntries(_root));
  }

  [Fact]
  public async Task Load_MovedWorkspace_KeepsItsLock() {
    // Arrange
    string moved = OperatingSystem.IsWindows() ? @"C:\moved\server" : "/moved/server";
    await _store.Save(_root, new LockFile { Packages = [SamplePackage("A", null)] });
    _fs.AddDirectory(_fs.Path.GetDirectoryName(moved)!);
    _fs.Directory.Move(_root, moved);

    // Act
    LockFile loaded = await _store.Load(moved);

    // Assert
    Assert.Equal(["A"], loaded.Packages.Select(p => p.Identity));
  }

  [Fact]
  public async Task Load_V3LockOfMovedWorkspace_RebasesPathsUnderOldRoot() {
    // Arrange: the v3 lock recorded absolute paths under the root the workspace had then.
    string old = OperatingSystem.IsWindows() ? @"D:\old\server" : "/old/server";
    string outside = OperatingSystem.IsWindows() ? @"D:\elsewhere\b.dll" : "/elsewhere/b.dll";
    string[] files = [
      _fs.Path.Join(old, "mods", "A", "a.dll"),
      _fs.Path.Join(old, "mods", "A", "gone.json"),
      _fs.Path.Join(old, "bedrock_server.exe"),
      outside,
    ];
    _fs.AddFile(_fs.Path.Join(_root, "mods", "A", "a.dll"), new MockFileData(""));
    _fs.AddFile(_fs.Path.Join(_root, "bedrock_server.exe"), new MockFileData(""));
    _fs.AddFile(ToothLockPath, new MockFileData($$"""
        {
          "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [{
            "files": {{JsonSerializer.Serialize(files)}}, "locked": true, "variant": "",
            "manifest": { "format_version": 3,
              "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
              "tooth": "github.com/a/b", "version": "1.0.0" }
          }]
        }
        """));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    Assert.Equal(
        ["mods/A/a.dll", "mods/A/gone.json", "bedrock_server.exe", outside],
        Assert.Single(lockFile.Packages).Files);
  }

  [Fact]
  public async Task Save_LockInLipDirectory_KeepsWorkspaceFileOfLegacyName() {
    // Arrange: once migrated, a file of that name in the workspace is not lip's.
    await _store.Save(_root, new LockFile());
    _fs.AddFile(ToothLockPath, new MockFileData("a package's file"));

    // Act
    await _store.Save(_root, new LockFile { Runtime = "win-x64" });

    // Assert
    Assert.True(_fs.File.Exists(ToothLockPath));
  }

  [Fact]
  public async Task Load_ArchiveKindNotFirst_ReadsArchive() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData("""
        {
          "format": "bf45b1",
          "packages": [{
            "id": "A", "version": "1.0.0", "kind": "mcmod", "explicit": false,
            "chain": [{ "ref": "./a.mcmod", "target": null }], "archiveKey": "k", "manifest": {},
            "archive": { "sha256": "ab", "url": null, "kind": "zip" }
          }]
        }
        """));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal(new ZipArchiveIdentity("ab", null), package.Archive);
    Assert.Equal([new LockedHop("./a.mcmod", null)], package.Chain);
    Assert.Empty(package.Files);
  }

  public static TheoryData<LockFile> InvalidLocks() => [
    new LockFile { Format = "bf45b2" },
    new LockFile { Platform = "Server" },
    new LockFile { Runtime = "" },
    new LockFile { Packages = [SamplePackage("A", null) with { Manifest = default }] },
  ];

  [Theory]
  [MemberData(nameof(InvalidLocks))]
  public async Task Save_InvalidLock_ThrowsAndKeepsToothLock(LockFile lockFile) {
    // Arrange
    _fs.AddFile(ToothLockPath, new MockFileData("{}"));

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => _store.Save(_root, lockFile));
    Assert.False(_fs.File.Exists(LipLockPath));
    Assert.True(_fs.File.Exists(ToothLockPath));
  }

  [Theory]
  [InlineData(@"C:\other\a.dll")]
  [InlineData(@"\\server\share\a.dll")]
  public async Task Load_V3LockWithForeignWindowsPath_KeepsItVerbatim(string file) {
    // Arrange
    string json = $$"""
        {
          "packages": [{
            "files": [{{JsonSerializer.Serialize(file)}}], "locked": false, "variant": "",
            "manifest": { "format_version": 3,
              "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
              "tooth": "github.com/a/b", "version": "1.0.0" }
          }]
        }
        """;
    _fs.AddFile(ToothLockPath, new MockFileData(json));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    Assert.Equal([file], Assert.Single(lockFile.Packages).Files);
  }

  public static TheoryData<string> CorruptLipLocks() {
    const string head = """
        "format": "bf45b1"
        """;
    const string package = """
        "id": "A", "version": "1.0.0", "kind": "mcmod", "explicit": true,
        "chain": [], "archiveKey": "k", "files": []
        """;
    return [
      "",
      "not json",
      "[]",
      "null",
      """{ "packages": [] }""",
      """{ "format": "bf45b2", "packages": [] }""",
      """{ "format": 1, "packages": [] }""",
      """{ "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f" }""",
      $$"""{ {{head}}, "packages": null }""",
      $$"""{ {{head}}, "packages": [null] }""",
      $$"""{ {{head}}, "platform": "universal", "packages": [] }""",
      $$"""{ {{head}}, "runtime": "", "packages": [] }""",
      $$"""{ {{head}}, "packages": [{ {{package}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{package}}, "manifest": [] }] }""",
      $$"""{ {{head}}, "packages": [{ {{package}}, "manifest": {}, "chain": [null] }] }""",
      $$"""{ {{head}}, "packages": [{ {{package}}, "manifest": {}, "files": null }] }""",
      $$"""{ {{head}}, "packages": [{ {{package}}, "manifest": {}, "id": null }] }""",
      $$"""
      { {{head}}, "packages": [{ {{package}}, "manifest": {}, "archive": { "kind": "tar" } }] }
      """,
      $$"""{ {{head}}, "packages": [], }""",
    ];
  }

  [Theory]
  [MemberData(nameof(CorruptLipLocks))]
  public async Task Load_CorruptLipLock_ThrowsLockFormatException(string json) {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData(json));

    // Act & Assert
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));
    Assert.Contains(LipLockPath, ex.Message);
  }

  [Fact]
  public async Task Load_WrongFormat_MessageNamesFormats() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData("""{ "format": "abcdef", "packages": [] }"""));

    // Act
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));

    // Assert
    Assert.Contains("format \"abcdef\"", ex.Message);
    Assert.Contains("expected \"bf45b1\"", ex.Message);
  }

  [Theory]
  [InlineData("")]
  [InlineData("not json")]
  [InlineData("null")]
  [InlineData("{}")]
  [InlineData("""{ "format_version": 2, "packages": [] }""")]
  [InlineData("""{ "format_uuid": "nope", "packages": [] }""")]
  [InlineData("""{ "packages": [null] }""")]
  [InlineData("""
      { "packages": [{ "files": [], "locked": true, "variant": "",
        "manifest": { "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "not a path!", "version": "1.0.0" } }] }
      """)]
  [InlineData("""
      { "packages": [{ "files": [], "locked": true, "variant": "",
        "manifest": { "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "github.com/a/b", "version": "x" } }] }
      """)]
  public async Task Load_CorruptToothLock_ThrowsLockFormatException(string json) {
    // Arrange
    _fs.AddFile(ToothLockPath, new MockFileData(json));

    // Act & Assert
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));
    Assert.Contains(ToothLockPath, ex.Message);
  }
}
