using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod.Lock;

/// The workspace file (protocol/workspace.md §7.1, workspace.schema.json) and earlier formats
/// (protocol/workspace.md §7.6).
public class LockStoreTests {
  private static readonly string _root = OperatingSystem.IsWindows()
      ? @"C:\lip-workspace"
      : "/lip-workspace";

  private static readonly string _sha = new('a', 64);
  private static readonly string _commit = new('c', 40);

  private readonly MockFileSystem _fs = new();
  private readonly LockStore _store;

  public LockStoreTests() {
    _fs.AddDirectory(_root);
    _store = new LockStore(_fs);
  }

  private string LipLockPath => _store.GetLockPath(_root);
  private string V036LockPath => _fs.Path.Join(_root, ".lip", "lock.json");
  private string ToothLockPath => _fs.Path.Join(_root, "tooth_lock.json");

  private static LockedPackage SamplePackage(
      string tooth,
      ArchiveIdentity archive,
      List<AssetIdentity?>? assets = null,
      string entryJson = """{"platform":"server","runtime":"win-x64"}""",
      string variant = "",
      int? toothFormatVersion = null) => new() {
        Tooth = tooth,
        Variant = variant,
        Version = "1.0.0",
        Explicit = true,
        Source = tooth,
        Archive = archive,
        Assets = assets ?? [],
        Entry = JsonDocument.Parse(entryJson).RootElement.Clone(),
        Files = ["mods/" + tooth.Split('/')[^1] + "/a.dll"],
        ToothFormatVersion = toothFormatVersion,
      };

  private static LockedPackage GitPackage(string tooth = "github.com/LiteLDev/A") =>
      SamplePackage(tooth, new GitArchiveIdentity($"https://{tooth}.git", _commit));

  private static string[] PropertyNames(JsonElement element) =>
      [.. element.EnumerateObject().Select(p => p.Name)];

  [Fact]
  public async Task Load_NoWorkspaceFile_ReturnsEmptyLock() {
    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    Assert.Equal(4, lockFile.FormatVersion);
    Assert.Null(lockFile.Platform);
    Assert.Null(lockFile.Runtime);
    Assert.Empty(lockFile.Packages);
    Assert.False(_fs.Directory.Exists(_fs.Path.Join(_root, ".lip")));
  }

  [Fact]
  public async Task Save_ThenLoad_RoundTrips() {
    // Arrange
    string directory = OperatingSystem.IsWindows() ? @"C:\src\pkg" : "/src/pkg";
    LockFile original = new() {
      Platform = "server",
      Runtime = "win-x64",
      Packages = [
        SamplePackage(
            "github.com/a/zip",
            new ZipArchiveIdentity(_sha, "https://example.com/z.zip"),
            [null, new AssetIdentity("https://example.com/m.mcmod", new string('b', 64))]),
        SamplePackage("github.com/a/git", new GitArchiveIdentity("https://github.com/a/git.git", _commit),
            variant: "lua/client"),
        SamplePackage(
            "github.com/a/file",
            new ZipArchiveIdentity(new string('d', 64), "file:///C:/abs/x.zip"),
            [null]),
        SamplePackage("github.com/a/dir", new DirectoryArchiveIdentity(directory)),
        SamplePackage(
            "github.com/a/legacy",
            new GitArchiveIdentity("https://github.com/a/legacy.git", _commit),
            entryJson: """{"platform":"server","runtime":"win-x64","nested":{"list":[1,"two",null,true]}}""",
            toothFormatVersion: 3),
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
      Assert.Equal(expected.Tooth, actual.Tooth);
      Assert.Equal(expected.Variant, actual.Variant);
      Assert.Equal(expected.Version, actual.Version);
      Assert.Equal(expected.Explicit, actual.Explicit);
      Assert.Equal(expected.Source, actual.Source);
      Assert.Equal(expected.Archive, actual.Archive);
      Assert.Equal(expected.Assets, actual.Assets);
      Assert.True(JsonElement.DeepEquals(expected.Entry, actual.Entry));
      Assert.Equal(expected.Files, actual.Files);
      Assert.Equal(expected.ToothFormatVersion, actual.ToothFormatVersion);
      Assert.Equal(expected.IsLegacy, actual.IsLegacy);
    }

    Assert.True(loaded.Packages[4].IsLegacy);
    Assert.Equal("github.com/a/git#lua/client", loaded.Packages[1].Identity);
  }

  [Fact]
  public async Task Save_Always_WritesMembersInSchemaOrder() {
    // Arrange
    LockFile lockFile = new() {
      Platform = "server",
      Runtime = "win-x64",
      Packages = [
        SamplePackage(
            "github.com/a/zip",
            new ZipArchiveIdentity(_sha, "https://example.com/z.zip"),
            [null, new AssetIdentity("https://example.com/m.mcmod", _sha)]),
        GitPackage(),
        SamplePackage("github.com/a/dir", new DirectoryArchiveIdentity(_root)),
      ],
    };

    // Act
    await _store.Save(_root, lockFile);

    // Assert
    string text = _fs.File.ReadAllText(LipLockPath);
    Assert.Contains('\n', text);
    JsonElement root = JsonDocument.Parse(text).RootElement;
    Assert.Equal(["format_version", "platform", "runtime", "packages"], PropertyNames(root));
    Assert.Equal(4, root.GetProperty("format_version").GetInt32());
    JsonElement[] packages = [.. root.GetProperty("packages").EnumerateArray()];
    Assert.All(packages, p => Assert.Equal(
        ["tooth", "variant", "version", "explicit", "source", "archive", "assets", "entry", "files"],
        PropertyNames(p)));
    Assert.Equal(["kind", "sha256", "url"], PropertyNames(packages[0].GetProperty("archive"))[..3]);
    Assert.Equal("zip", packages[0].GetProperty("archive").GetProperty("kind").GetString());
    Assert.Equal(["kind", "repo", "commit"], PropertyNames(packages[1].GetProperty("archive"))[..3]);
    Assert.Equal("git", packages[1].GetProperty("archive").GetProperty("kind").GetString());
    Assert.Equal(["kind", "path"], PropertyNames(packages[2].GetProperty("archive"))[..2]);
    Assert.Equal("directory", packages[2].GetProperty("archive").GetProperty("kind").GetString());
    JsonElement assets = packages[0].GetProperty("assets");
    Assert.Equal(JsonValueKind.Null, assets[0].ValueKind);
    Assert.Equal(["url", "sha256"], PropertyNames(assets[1]));
    Assert.Equal(JsonValueKind.Object, packages[0].GetProperty("entry").ValueKind);
  }

  [Fact]
  public async Task Save_ArchiveIdentifiers_HaveExactlyTheMembersOfTheSchema() {
    // Arrange
    LockFile lockFile = new() {
      Packages = [
        SamplePackage("github.com/a/zip", new ZipArchiveIdentity(_sha, "https://example.com/z.zip")),
        GitPackage(),
        SamplePackage("github.com/a/dir", new DirectoryArchiveIdentity(_root)),
      ],
    };

    // Act
    await _store.Save(_root, lockFile);

    // Assert
    JsonElement packages = JsonDocument.Parse(_fs.File.ReadAllText(LipLockPath)).RootElement
        .GetProperty("packages");
    Assert.Equal(["kind", "sha256", "url"], PropertyNames(packages[0].GetProperty("archive")));
    Assert.Equal(["kind", "repo", "commit"], PropertyNames(packages[1].GetProperty("archive")));
    Assert.Equal(["kind", "path"], PropertyNames(packages[2].GetProperty("archive")));
  }

  [Fact]
  public async Task Save_EntryOfFormat3_WritesToothFormatVersionAfterFiles() {
    // Arrange
    LockFile lockFile = new() {
      Packages = [
        GitPackage() with { ToothFormatVersion = 3 },
        GitPackage("github.com/LiteLDev/B") with { ToothFormatVersion = null },
      ],
    };

    // Act
    await _store.Save(_root, lockFile);

    // Assert
    JsonElement packages = JsonDocument.Parse(_fs.File.ReadAllText(LipLockPath)).RootElement
        .GetProperty("packages");
    Assert.Equal("tooth_format_version", PropertyNames(packages[0])[^1]);
    Assert.Equal(3, packages[0].GetProperty("tooth_format_version").GetInt32());
    Assert.False(packages[1].TryGetProperty("tooth_format_version", out _));
  }

  [Fact]
  public async Task Save_UnknownEnvironment_WritesNullPlatformAndRuntime() {
    // Act
    await _store.Save(_root, new LockFile());

    // Assert
    JsonElement root = JsonDocument.Parse(_fs.File.ReadAllText(LipLockPath)).RootElement;
    Assert.Equal(JsonValueKind.Null, root.GetProperty("platform").ValueKind);
    Assert.Equal(JsonValueKind.Null, root.GetProperty("runtime").ValueKind);
    Assert.Equal(0, root.GetProperty("packages").GetArrayLength());
  }

  [Fact]
  public async Task Save_ExistingLock_ReplacesItAsAWhole() {
    // Arrange
    await _store.Save(_root, new LockFile {
      Packages = [GitPackage("github.com/LiteLDev/A"), GitPackage("github.com/LiteLDev/B")],
    });

    // Act
    await _store.Save(_root, new LockFile { Packages = [GitPackage("github.com/LiteLDev/C")] });

    // Assert
    LockFile loaded = await _store.Load(_root);
    Assert.Equal(["github.com/LiteLDev/C"], loaded.Packages.Select(p => p.Tooth));
    Assert.Equal([LipLockPath], _fs.Directory.GetFiles(_fs.Path.GetDirectoryName(LipLockPath)!));
    Assert.Empty(_fs.Directory.GetFiles(_root));
  }

  [Fact]
  public async Task Stage_ThenCommitStaged_RenamesWorkspaceJsonNewOverWorkspaceJson() {
    // Arrange
    await _store.Save(_root, new LockFile { Runtime = "linux-x64" });
    string staged = _fs.Path.Join(_root, ".lip", "workspace.json.new");

    // Act
    await _store.Stage(_root, new LockFile { Runtime = "win-x64" });

    // Assert
    Assert.True(_fs.File.Exists(staged));
    Assert.Equal("linux-x64", (await _store.Load(_root)).Runtime);
    _store.CommitStaged(_root);
    Assert.False(_fs.File.Exists(staged));
    Assert.Equal("win-x64", (await _store.Load(_root)).Runtime);
  }

  [Fact]
  public async Task DiscardStaged_StagedLock_DeletesItAndKeepsWorkspaceJson() {
    // Arrange
    await _store.Save(_root, new LockFile { Runtime = "linux-x64" });
    await _store.Stage(_root, new LockFile { Runtime = "win-x64" });

    // Act
    _store.DiscardStaged(_root);
    _store.CommitStaged(_root);

    // Assert
    Assert.False(_fs.File.Exists(_fs.Path.Join(_root, ".lip", "workspace.json.new")));
    Assert.Equal("linux-x64", (await _store.Load(_root)).Runtime);
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
    await _store.Save(_root, new LockFile { Packages = [GitPackage()] });
    _fs.AddDirectory(_fs.Path.GetDirectoryName(moved)!);
    _fs.Directory.Move(_root, moved);

    // Act
    LockFile loaded = await _store.Load(moved);

    // Assert
    Assert.Equal(["github.com/LiteLDev/A"], loaded.Packages.Select(p => p.Tooth));
  }

  [Fact]
  public async Task Load_ArchiveKindNotFirst_ReadsArchive() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData($$"""
        {
          "format_version": 4, "platform": null, "runtime": null,
          "packages": [{
            "tooth": "github.com/a/b", "variant": "", "version": "1.0.0", "explicit": false,
            "source": "https://example.com/a.zip", "assets": [], "entry": {}, "files": [],
            "archive": { "sha256": "{{_sha}}", "url": "https://example.com/a.zip", "kind": "zip" }
          }]
        }
        """));

    // Act
    LockFile lockFile = await _store.Load(_root);

    // Assert
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal(new ZipArchiveIdentity(_sha, "https://example.com/a.zip"), package.Archive);
    Assert.False(package.Explicit);
    Assert.False(package.IsLegacy);
  }

  [Fact]
  public async Task Load_WorkspaceFileAndToothLockAtTheRoot_ReadsTheWorkspaceFile() {
    // Arrange: once a workspace file exists, a tooth_lock.json at the root is a package's file.
    await _store.Save(_root, new LockFile { Runtime = "win-x64" });
    _fs.AddFile(ToothLockPath, new MockFileData("a package's file"));

    // Act
    LockFile lockFile = await _store.Load(_root);
    await _store.Save(_root, lockFile with { Platform = "server" });

    // Assert
    Assert.Equal("win-x64", lockFile.Runtime);
    Assert.Equal("a package's file", _fs.File.ReadAllText(ToothLockPath));
  }

  public static TheoryData<string> EarlierWorkspaceFiles() => [
    """{ "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d", "packages": [] }""",
    """{ "format": "bf45b1", "packages": [] }""",
    """{ "packages": [] }""",
    """{ "format_version": 5, "platform": null, "runtime": null, "packages": [] }""",
    """{ "format_version": "4", "platform": null, "runtime": null, "packages": [] }""",
  ];

  [Theory]
  [MemberData(nameof(EarlierWorkspaceFiles))]
  public async Task Load_WorkspaceJsonWithoutFormatVersion4_ThrowsAndKeepsIt(string json) {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData(json));

    // Act
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));

    // Assert
    Assert.Contains(LipLockPath, ex.Message);
    Assert.Contains("format_version 4", ex.Message);
    Assert.Equal(json, _fs.File.ReadAllText(LipLockPath));
  }

  [Theory]
  [InlineData(".lip/lock.json")]
  [InlineData("tooth_lock.json")]
  public async Task Load_EarlierWorkspaceFile_ThrowsAndKeepsIt(string relative) {
    // Arrange
    string path = _fs.Path.Join(_root, relative);
    const string json = """
        { "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [] }
        """;
    _fs.AddFile(path, new MockFileData(json));

    // Act
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));

    // Assert
    Assert.Contains(_fs.Path.GetFullPath(path), ex.Message);
    Assert.Equal(json, _fs.File.ReadAllText(path));
    Assert.False(_fs.File.Exists(LipLockPath));
  }

  [Fact]
  public async Task Load_V036LockJsonOfAnyContent_Throws() {
    // Arrange
    _fs.AddFile(V036LockPath, new MockFileData("not json"));

    // Act & Assert
    await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));
  }

  public static TheoryData<LockFile> InvalidLocks() => [
    new LockFile { FormatVersion = 3 },
    new LockFile { Platform = "Server" },
    new LockFile { Runtime = "" },
    new LockFile { Packages = [GitPackage() with { Entry = default }] },
    new LockFile { Packages = [GitPackage() with { Version = "v1.0.0" }] },
    new LockFile { Packages = [GitPackage() with { Variant = "Client" }] },
    new LockFile { Packages = [GitPackage() with {
      Archive = new GitArchiveIdentity("https://github.com/a/b.git", "ABC"),
    }] },
    new LockFile { Packages = [GitPackage() with {
      Archive = new ZipArchiveIdentity(_sha.ToUpperInvariant(), "https://example.com/a.zip"),
    }] },
  ];

  [Theory]
  [MemberData(nameof(InvalidLocks))]
  public async Task Save_InvalidLock_ThrowsAndWritesNothing(LockFile lockFile) {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => _store.Save(_root, lockFile));
    Assert.False(_fs.File.Exists(LipLockPath));
    Assert.False(_fs.Directory.Exists(_fs.Path.Join(_root, ".lip")));
  }

  public static TheoryData<string> CorruptWorkspaceFiles() {
    const string head = """
        "format_version": 4, "platform": "server", "runtime": "win-x64"
        """;
    string zip = $$"""{ "kind": "zip", "sha256": "{{_sha}}", "url": "https://example.com/a.zip" }""";
    string git = $$"""{ "kind": "git", "repo": "https://github.com/a/b.git", "commit": "{{_commit}}" }""";
    string Package(string archive = "", string members = "", string entry = "{}") =>
        $$"""
        "tooth": "github.com/a/b", "variant": "", "version": "1.0.0", "explicit": true,
        "source": "github.com/a/b", "archive": {{(archive == "" ? git : archive)}}, "assets": [],
        "entry": {{entry}}, "files": []{{members}}
        """;
    return [
      "",
      "not json",
      "[]",
      "null",
      """{ "format_version": 4 """,
      $$"""{ {{head}}, "packages": null }""",
      $$"""{ {{head}}, "packages": [null] }""",
      $$"""{ "format_version": 4, "platform": "universal", "runtime": null, "packages": [] }""",
      $$"""{ "format_version": 4, "platform": null, "runtime": "", "packages": [] }""",
      $$"""{ {{head}}, "packages": [], }""",
      // The package members are required.
      $$"""{ {{head}}, "packages": [{ "tooth": "github.com/a/b" }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"explicit\": true,", "")}} }] }""",
      // Bad entry, variant and version.
      $$"""{ {{head}}, "packages": [{ {{Package(entry: "[]")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package(entry: "null")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"variant\": \"\"", "\"variant\": \"A\"")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"1.0.0\"", "\"1.0\"")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package(members: ", \"files\": [null]")}} }] }""",
      // Bad archive identifiers.
      $$"""{ {{head}}, "packages": [{ {{Package("""{ "kind": "tar" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package("""{ "kind": "zip", "sha256": "ab", "url": "https://example.com/a.zip" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package($$"""{ "kind": "zip", "sha256": "{{_sha.ToUpperInvariant()}}", "url": "x" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package($$"""{ "kind": "zip", "sha256": "{{_sha}}", "url": null }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package($$"""{ "kind": "zip", "sha256": "{{_sha}}" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package("""{ "kind": "git", "repo": "https://github.com/a/b.git", "commit": "abc" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package($$"""{ "kind": "git", "repo": "https://github.com/a/b.git", "commit": "{{_commit.ToUpperInvariant()}}" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package($$"""{ "kind": "git", "repo": "http://github.com/a/b.git", "commit": "{{_commit}}" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package($$"""{ "kind": "git", "repo": "https://github.com/a/b", "commit": "{{_commit}}" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package("""{ "kind": "directory", "path": "" }""")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package("""{ "kind": "directory" }""")}} }] }""",
      // Bad asset identifiers.
      $$"""{ {{head}}, "packages": [{ {{Package(members: ", \"assets\": [{ \"url\": \"https://example.com/m.mcmod\", \"sha256\": \"ab\" }]")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package(members: ", \"assets\": [{ \"url\": \"https://example.com/m.mcmod\" }]")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package(members: ", \"assets\": null")}} }] }""",
    ];
  }

  [Theory]
  [MemberData(nameof(CorruptWorkspaceFiles))]
  public async Task Load_InvalidWorkspaceFile_ThrowsLockFormatException(string json) {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData(json));

    // Act & Assert
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));
    Assert.Contains(LipLockPath, ex.Message);
  }
}
