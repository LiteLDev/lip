using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod.Lock;

/// The workspace file (protocol/workspace.md §7.1, workspace.schema.yaml).
public class LockStoreTests {
  private static readonly string _root = OperatingSystem.IsWindows()
      ? @"C:\lip-workspace"
      : "/lip-workspace";

  private static readonly string _commit = new('c', 40);

  private readonly MockFileSystem _fs = new();
  private readonly LockStore _store;

  public LockStoreTests() {
    _fs.AddDirectory(_root);
    _store = new LockStore(_fs);
  }

  private string LipLockPath => _fs.Path.Join(_root, ".lip", "workspace.yaml");

  private static string ManifestJson(
      string id,
      string version = "1.0.0",
      string variant = "",
      string entry = """{"platform":"server","runtime":"win-x64"}""") => $$"""
      {"format":4,
       "id":"{{id}}","version":"{{version}}","variants":{"{{variant}}":[{{entry}}]} }
      """;

  private static JsonElement Manifest(
      string id,
      string version = "1.0.0",
      string variant = "",
      string entry = """{"platform":"server","runtime":"win-x64"}""") =>
      JsonDocument.Parse(ManifestJson(id, version, variant, entry)).RootElement.Clone();

  private static LockedPackage SamplePackage(
      string id,
      string source,
      string? commit = null,
      string variant = "",
      string entry = """{"platform":"server","runtime":"win-x64"}""") => new() {
        Manifest = Manifest(id, variant: variant, entry: entry),
        Variant = variant,
        Explicit = true,
        Source = source,
        Commit = commit,
        Files = ["mods/" + id.Split('/')[^1] + "/a.dll"],
      };

  private static LockedPackage GitPackage(string id = "github.com/LiteLDev/A") =>
      SamplePackage(id, id, _commit);

  /// The workspace file as written, read as the JSON value it represents.
  private JsonElement ReadWorkspaceFile() {
    using JsonDocument document = YamlJson.Parse(_fs.File.ReadAllBytes(LipLockPath));
    return document.RootElement.Clone();
  }

  private const string _toothLock = """
      { "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "packages": [] }
      """;

  private static LockFile Lock(params LockedPackage[] packages) => new() {
    Platform = "server",
    Runtime = "win-x64",
    Packages = [.. packages],
  };

  private static string[] PropertyNames(JsonElement element) =>
      [.. element.EnumerateObject().Select(p => p.Name)];

  [Fact]
  public async Task Load_NoWorkspaceFile_ReturnsNull() {
    // Act
    LockFile? lockFile = await _store.Load(_root);

    // Assert
    Assert.Null(lockFile);
    Assert.False(_fs.Directory.Exists(_fs.Path.Join(_root, ".lip")));
  }

  [Fact]
  public async Task Save_ThenLoad_RoundTrips() {
    // Arrange
    LockFile original = Lock(
        SamplePackage(
            "github.com/a/zip",
            "https://example.com/z.zip",
            entry: """
                {"platform":"server","runtime":"win-x64","assets":[{"type":"self"},
                {"type":"mcmod","urls":["https://example.com/m.zip"]}]}
                """),
        SamplePackage("github.com/a/git", "github.com/a/git#lua/client@1.0.0", _commit,
            variant: "lua/client"),
        SamplePackage("github.com/a/file", "file:///C:/abs/x.zip"),
        SamplePackage(
            "github.com/a/derived",
            "github.com/a/derived",
            _commit,
            entry: """
                {"platform":"server","runtime":"win-x64","preserve":["**/config.json"],
                "purge":["data","data/"],"hooks":{"post_install":["echo"]}}
                """));

    // Act
    await _store.Save(_root, original);
    LockFile loaded = (await _store.Load(_root))!;

    // Assert
    Assert.Equal(original.Platform, loaded.Platform);
    Assert.Equal(original.Runtime, loaded.Runtime);
    Assert.Equal(original.Packages.Count, loaded.Packages.Count);
    for (int i = 0; i < original.Packages.Count; i++) {
      LockedPackage expected = original.Packages[i];
      LockedPackage actual = loaded.Packages[i];
      Assert.True(JsonElement.DeepEquals(expected.Manifest, actual.Manifest));
      Assert.Equal(expected.Id, actual.Id);
      Assert.Equal(expected.Version, actual.Version);
      Assert.Equal(expected.Variant, actual.Variant);
      Assert.Equal(expected.Explicit, actual.Explicit);
      Assert.Equal(expected.Source, actual.Source);
      Assert.Equal(expected.Commit, actual.Commit);
      Assert.Equal(expected.Files, actual.Files);
    }

    Assert.Equal("github.com/a/git#lua/client", loaded.Packages[1].Identity);
    Assert.Equal("1.0.0", loaded.Packages[1].Version);
    Assert.Null(loaded.Packages[0].Commit);
  }

  [Fact]
  public async Task Save_Always_WritesExactlyTheMembersOfTheSchemaInOrder() {
    // Arrange
    LockFile lockFile = Lock(
        SamplePackage("github.com/a/zip", "https://example.com/z.zip"),
        GitPackage(),
        SamplePackage("github.com/a/dir", "file:///C:/src/pkg"));

    // Act
    await _store.Save(_root, lockFile);

    // Assert
    string text = _fs.File.ReadAllText(LipLockPath);
    Assert.StartsWith("format: 4\nplatform: server\nruntime: win-x64\npackages:\n", text);
    JsonElement root = ReadWorkspaceFile();
    Assert.Equal(["format", "platform", "runtime", "packages"], PropertyNames(root));
    Assert.Equal(4, root.GetProperty("format").GetInt32());
    Assert.Equal("server", root.GetProperty("platform").GetString());
    Assert.Equal("win-x64", root.GetProperty("runtime").GetString());
    JsonElement[] packages = [.. root.GetProperty("packages").EnumerateArray()];
    Assert.Equal(
        ["manifest", "variant", "explicit", "ref", "files"], PropertyNames(packages[0]));
    Assert.Equal(
        ["manifest", "variant", "explicit", "ref", "commit", "files"], PropertyNames(packages[1]));
    Assert.Equal(_commit, packages[1].GetProperty("commit").GetString());
    Assert.Equal(["manifest", "variant", "explicit", "ref", "files"], PropertyNames(packages[2]));
    Assert.Equal(JsonValueKind.Object, packages[0].GetProperty("manifest").ValueKind);
    Assert.Equal(4, packages[0].GetProperty("manifest").GetProperty("format").GetInt32());
  }

  [Fact]
  public async Task Save_ExistingLock_ReplacesItAsAWhole() {
    // Arrange
    await _store.Save(
        _root, Lock(GitPackage("github.com/LiteLDev/A"), GitPackage("github.com/LiteLDev/B")));

    // Act
    await _store.Save(_root, Lock(GitPackage("github.com/LiteLDev/C")));

    // Assert
    LockFile loaded = (await _store.Load(_root))!;
    Assert.Equal(["github.com/LiteLDev/C"], loaded.Packages.Select(p => p.Id));
    Assert.Equal([LipLockPath], _fs.Directory.GetFiles(_fs.Path.GetDirectoryName(LipLockPath)!));
    Assert.Empty(_fs.Directory.GetFiles(_root));
  }

  [Fact]
  public async Task Stage_ThenCommitStaged_RenamesWorkspaceYamlTmpOverWorkspaceYaml() {
    // Arrange
    await _store.Save(_root, Lock() with { Runtime = "linux-x64" });
    string staged = _fs.Path.Join(_root, ".lip", "workspace.yaml.tmp");

    // Act
    await _store.Stage(_root, Lock());

    // Assert
    Assert.True(_fs.File.Exists(staged));
    Assert.Equal("linux-x64", (await _store.Load(_root))!.Runtime);
    _store.CommitStaged(_root);
    Assert.False(_fs.File.Exists(staged));
    Assert.Equal("win-x64", (await _store.Load(_root))!.Runtime);
  }

  [Fact]
  public async Task DiscardStaged_StagedLock_DeletesItAndKeepsWorkspaceYaml() {
    // Arrange
    await _store.Save(_root, Lock() with { Runtime = "linux-x64" });
    await _store.Stage(_root, Lock());

    // Act
    _store.DiscardStaged(_root);
    _store.CommitStaged(_root);

    // Assert
    Assert.False(_fs.File.Exists(_fs.Path.Join(_root, ".lip", "workspace.yaml.tmp")));
    Assert.Equal("linux-x64", (await _store.Load(_root))!.Runtime);
  }

  [Fact]
  public async Task CommitStaged_FirstWorkspaceFileBesideToothLock_DeletesToothLock() {
    // Arrange: the workspace file was derived from tooth_lock.json (protocol/compatibility.md §3).
    string toothLock = _fs.Path.Join(_root, "tooth_lock.json");
    _fs.AddFile(toothLock, new MockFileData(_toothLock));
    await _store.Stage(_root, Lock());

    // Act
    _store.CommitStaged(_root);

    // Assert
    Assert.False(_fs.File.Exists(toothLock));
    Assert.NotNull(await _store.Load(_root));
  }

  [Fact]
  public async Task CommitStaged_ExistingWorkspaceFile_KeepsToothLock() {
    // Arrange: a tooth_lock.json is not read once there is a workspace file.
    await _store.Save(_root, Lock());
    string toothLock = _fs.Path.Join(_root, "tooth_lock.json");
    _fs.AddFile(toothLock, new MockFileData(_toothLock));
    await _store.Stage(_root, Lock());

    // Act
    _store.CommitStaged(_root);

    // Assert
    Assert.Equal(_toothLock, _fs.File.ReadAllText(toothLock));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("""{ "format_version": 2, "packages": [] }""")]
  public async Task CommitStaged_ToothLockNotReadByCompatibility_KeepsIt(string json) {
    // Arrange
    string toothLock = _fs.Path.Join(_root, "tooth_lock.json");
    _fs.AddFile(toothLock, new MockFileData(json));
    await _store.Stage(_root, Lock());

    // Act
    _store.CommitStaged(_root);

    // Assert
    Assert.Equal(json, _fs.File.ReadAllText(toothLock));
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
    await _store.Save(_root, Lock());

    // Assert
    JsonElement root = ReadWorkspaceFile();
    Assert.False(root.TryGetProperty("workspace", out _));
    Assert.Equal([_fs.Path.Join(_root, ".lip")], _fs.Directory.GetFileSystemEntries(_root));
  }

  [Fact]
  public async Task Load_MovedWorkspace_KeepsItsLock() {
    // Arrange
    string moved = OperatingSystem.IsWindows() ? @"C:\moved\server" : "/moved/server";
    await _store.Save(_root, Lock(GitPackage()));
    _fs.AddDirectory(_fs.Path.GetDirectoryName(moved)!);
    _fs.Directory.Move(_root, moved);

    // Act
    LockFile loaded = (await _store.Load(moved))!;

    // Assert
    Assert.Equal(["github.com/LiteLDev/A"], loaded.Packages.Select(p => p.Id));
  }

  [Fact]
  public async Task Load_HandWrittenWorkspaceFile_ReadsIt() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData($$"""
        {
          "format": 4, "platform": "server", "runtime": "win-x64",
          "packages": [{
            "files": ["a.txt"], "ref": "github.com/a/b@branch:main", "commit": "{{_commit}}",
            "explicit": false, "variant": "",
            "manifest": {{ManifestJson("github.com/a/b", "1.2.3")}}
          }]
        }
        """));

    // Act
    LockFile lockFile = (await _store.Load(_root))!;

    // Assert
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal("github.com/a/b", package.Identity);
    Assert.Equal("1.2.3", package.Version);
    Assert.Equal("github.com/a/b@branch:main", package.Source);
    Assert.Equal(_commit, package.Commit);
    Assert.False(package.Explicit);
  }

  [Fact]
  public async Task Load_HandWrittenBlockYaml_ReadsIt() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData($$"""
        format: 4
        platform: server
        runtime: win-x64
        packages:
          - manifest:
              format: 4
              id: github.com/a/b
              version: 1.2.3
              variants:
                "":
                  - platform: server
                    runtime: win-x64
            variant: ""
            explicit: true
            ref: github.com/a/b
            commit: {{_commit}}
            files: [a.txt, "1.0"]
        """));

    // Act
    LockFile lockFile = (await _store.Load(_root))!;

    // Assert
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal("1.2.3", package.Version);
    Assert.Equal(["a.txt", "1.0"], package.Files);
    Assert.True(package.Explicit);
  }

  /// protocol/workspace.md §7.1: a workspace without a workspace file has no installed packages,
  /// whatever other files it has; a tooth_lock.json is read by McmodService
  /// (protocol/compatibility.md §3).
  [Theory]
  [InlineData(".lip/lock.json")]
  [InlineData(".lip/workspace.json")]
  [InlineData("tooth_lock.json")]
  public async Task Load_NoWorkspaceFile_ReturnsNullWhateverOtherFiles(string relative) {
    // Arrange
    string path = _fs.Path.Join(_root, relative);
    _fs.AddFile(path, new MockFileData(_toothLock));

    // Act
    LockFile? lockFile = await _store.Load(_root);

    // Assert
    Assert.Null(lockFile);
    Assert.Equal(_toothLock, _fs.File.ReadAllText(path));
  }

  public static TheoryData<string> WorkspaceFilesWithoutFormat4() => [
    """{ "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d", "packages": [] }""",
    """{ "format_version": 4, "platform": "server", "runtime": "win-x64", "packages": [] }""",
    """{ "format": "bf45b1", "packages": [] }""",
    """{ "packages": [] }""",
    """{ "format": 5, "platform": "server", "runtime": "win-x64", "packages": [] }""",
    """{ "format": "4", "platform": "server", "runtime": "win-x64", "packages": [] }""",
    "format: '4'\nplatform: server\nruntime: win-x64\npackages: []\n",
  ];

  /// A workspace file not of format 4 is not valid against workspace.schema.yaml.
  [Theory]
  [MemberData(nameof(WorkspaceFilesWithoutFormat4))]
  public async Task Load_WorkspaceFileWithoutFormat4_ThrowsAndKeepsIt(string json) {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData(json));

    // Act
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));

    // Assert
    Assert.Contains(LipLockPath, ex.Message);
    Assert.Equal(json, _fs.File.ReadAllText(LipLockPath));
  }

  public static TheoryData<LockFile> InvalidLocks() => [
    Lock() with { Format = 3 },
    Lock() with { Platform = "Server" },
    Lock() with { Platform = null! },
    Lock() with { Runtime = "" },
    Lock() with { Runtime = null! },
    Lock(GitPackage() with { Manifest = default }),
    Lock(GitPackage() with { Manifest = Manifest("github.com/a/b", "v1.0.0") }),
    Lock(GitPackage() with { Variant = "Client" }),
    Lock(GitPackage() with { Commit = "ABC" }),
    Lock(GitPackage() with { Commit = _commit.ToUpperInvariant() }),
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
        "format": 4, "platform": "server", "runtime": "win-x64"
        """;
    string manifest = ManifestJson("github.com/a/b");
    string Package(string members = "") =>
        $$"""
        "manifest": {{manifest}}, "variant": "", "explicit": true, "ref": "github.com/a/b",
        "commit": "{{_commit}}", "files": []{{members}}
        """;
    return [
      "",
      "not json",
      "[]",
      "null",
      """{ "format": 4 """,
      "format: 4\n---\nformat: 4\n",
      $$"""{ {{head}}, "packages": null }""",
      $$"""{ {{head}}, "packages": [null] }""",
      $$"""{ {{head}}, "packages": [], "packages": [] }""",
      // The environment is required and never null.
      """{ "format": 4, "platform": null, "runtime": "win-x64", "packages": [] }""",
      """{ "format": 4, "platform": "server", "runtime": null, "packages": [] }""",
      """{ "format": 4, "platform": "server", "runtime": ~, "packages": [] }""",
      """{ "format": 4, "runtime": "win-x64", "packages": [] }""",
      """{ "format": 4, "platform": "server", "packages": [] }""",
      """{ "format": 4, "platform": "universal", "runtime": "win-x64", "packages": [] }""",
      """{ "format": 4, "platform": "server", "runtime": "", "packages": [] }""",
      // The package members are required.
      $$"""{ {{head}}, "packages": [{ "manifest": {{manifest}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"explicit\": true,", "")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"ref\"", "\"source\"")}} }] }""",
      // Bad manifest, variant, files and commit.
      $$"""{ {{head}}, "packages": [{ {{Package().Replace(manifest, "[]")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace(manifest, "null")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"1.0.0\"", "\"1.0\"")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"format\":4", "\"format\":3")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"variant\": \"\"", "\"variant\": \"A\"")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"files\": []", "\"files\": [null]")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace(_commit, "abc")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace(_commit, _commit.ToUpperInvariant())}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace($"\"{_commit}\"", "null")}} }] }""",
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
    // What is wrong is kept as the cause.
    Assert.NotNull(ex.InnerException);
  }

  [Fact]
  public async Task Load_InvalidPlatform_CauseNamesTheFieldAndExpectedValues() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData(
        """{ "format": 4, "platform": "universal", "runtime": "win-x64", "packages": [] }"""));

    // Act
    LockFormatException ex =
        await Assert.ThrowsAsync<LockFormatException>(() => _store.Load(_root));

    // Assert
    Assert.Contains("\"platform\"", ex.InnerException!.Message);
    Assert.Contains("\"server\"", ex.InnerException.Message);
  }
}
