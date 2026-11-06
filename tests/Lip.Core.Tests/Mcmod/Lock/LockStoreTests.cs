using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod.Lock;

/// The workspace file (protocol/workspace.md §7.1, workspace.schema.json).
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

  private string LipLockPath => _fs.Path.Join(_root, ".lip", "workspace.json");

  private static string ManifestJson(
      string tooth,
      string version = "1.0.0",
      string variant = "",
      string entry = """{"platform":"server","runtime":"win-x64"}""") => $$"""
      {"format_version":4,"format_uuid":"f74a5fda-cb2a-4de8-959f-a1a76bad3102",
       "tooth":"{{tooth}}","version":"{{version}}","variants":{"{{variant}}":[{{entry}}]} }
      """;

  private static JsonElement Manifest(
      string tooth,
      string version = "1.0.0",
      string variant = "",
      string entry = """{"platform":"server","runtime":"win-x64"}""") =>
      JsonDocument.Parse(ManifestJson(tooth, version, variant, entry)).RootElement.Clone();

  private static LockedPackage SamplePackage(
      string tooth,
      string source,
      string? commit = null,
      string variant = "",
      string entry = """{"platform":"server","runtime":"win-x64"}""") => new() {
        Manifest = Manifest(tooth, variant: variant, entry: entry),
        Variant = variant,
        Explicit = true,
        Source = source,
        Commit = commit,
        Files = ["mods/" + tooth.Split('/')[^1] + "/a.dll"],
      };

  private static LockedPackage GitPackage(string tooth = "github.com/LiteLDev/A") =>
      SamplePackage(tooth, tooth, _commit);

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
      Assert.Equal(expected.Tooth, actual.Tooth);
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
    Assert.Contains('\n', text);
    JsonElement root = JsonDocument.Parse(text).RootElement;
    Assert.Equal(["format_version", "platform", "runtime", "packages"], PropertyNames(root));
    Assert.Equal(4, root.GetProperty("format_version").GetInt32());
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
    Assert.Equal(4, packages[0].GetProperty("manifest").GetProperty("format_version").GetInt32());
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
    Assert.Equal(["github.com/LiteLDev/C"], loaded.Packages.Select(p => p.Tooth));
    Assert.Equal([LipLockPath], _fs.Directory.GetFiles(_fs.Path.GetDirectoryName(LipLockPath)!));
    Assert.Empty(_fs.Directory.GetFiles(_root));
  }

  [Fact]
  public async Task Stage_ThenCommitStaged_RenamesWorkspaceJsonNewOverWorkspaceJson() {
    // Arrange
    await _store.Save(_root, Lock() with { Runtime = "linux-x64" });
    string staged = _fs.Path.Join(_root, ".lip", "workspace.json.new");

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
  public async Task DiscardStaged_StagedLock_DeletesItAndKeepsWorkspaceJson() {
    // Arrange
    await _store.Save(_root, Lock() with { Runtime = "linux-x64" });
    await _store.Stage(_root, Lock());

    // Act
    _store.DiscardStaged(_root);
    _store.CommitStaged(_root);

    // Assert
    Assert.False(_fs.File.Exists(_fs.Path.Join(_root, ".lip", "workspace.json.new")));
    Assert.Equal("linux-x64", (await _store.Load(_root))!.Runtime);
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
    JsonElement root = JsonDocument.Parse(_fs.File.ReadAllText(LipLockPath)).RootElement;
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
    Assert.Equal(["github.com/LiteLDev/A"], loaded.Packages.Select(p => p.Tooth));
  }

  [Fact]
  public async Task Load_HandWrittenWorkspaceFile_ReadsIt() {
    // Arrange
    _fs.AddFile(LipLockPath, new MockFileData($$"""
        {
          "format_version": 4, "platform": "server", "runtime": "win-x64",
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

  /// protocol/workspace.md §7.1: a workspace without a workspace file has no installed packages,
  /// whatever other files it has.
  [Theory]
  [InlineData(".lip/lock.json")]
  [InlineData("tooth_lock.json")]
  public async Task Load_NoWorkspaceFile_ReturnsNullWhateverOtherFiles(string relative) {
    // Arrange
    string path = _fs.Path.Join(_root, relative);
    const string json = """
        { "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [] }
        """;
    _fs.AddFile(path, new MockFileData(json));

    // Act
    LockFile? lockFile = await _store.Load(_root);

    // Assert
    Assert.Null(lockFile);
    Assert.Equal(json, _fs.File.ReadAllText(path));
  }

  public static TheoryData<string> WorkspaceFilesWithoutFormatVersion4() => [
    """{ "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d", "packages": [] }""",
    """{ "format": "bf45b1", "packages": [] }""",
    """{ "packages": [] }""",
    """{ "format_version": 5, "platform": "server", "runtime": "win-x64", "packages": [] }""",
    """{ "format_version": "4", "platform": "server", "runtime": "win-x64", "packages": [] }""",
  ];

  /// A workspace file not of format_version 4 is not valid against workspace.schema.json.
  [Theory]
  [MemberData(nameof(WorkspaceFilesWithoutFormatVersion4))]
  public async Task Load_WorkspaceJsonWithoutFormatVersion4_ThrowsAndKeepsIt(string json) {
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
    Lock() with { FormatVersion = 3 },
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
        "format_version": 4, "platform": "server", "runtime": "win-x64"
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
      """{ "format_version": 4 """,
      $$"""{ {{head}}, "packages": null }""",
      $$"""{ {{head}}, "packages": [null] }""",
      $$"""{ {{head}}, "packages": [], }""",
      // The environment is required and never null.
      """{ "format_version": 4, "platform": null, "runtime": "win-x64", "packages": [] }""",
      """{ "format_version": 4, "platform": "server", "runtime": null, "packages": [] }""",
      """{ "format_version": 4, "runtime": "win-x64", "packages": [] }""",
      """{ "format_version": 4, "platform": "server", "packages": [] }""",
      """{ "format_version": 4, "platform": "universal", "runtime": "win-x64", "packages": [] }""",
      """{ "format_version": 4, "platform": "server", "runtime": "", "packages": [] }""",
      // The package members are required.
      $$"""{ {{head}}, "packages": [{ "manifest": {{manifest}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"explicit\": true,", "")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"ref\"", "\"source\"")}} }] }""",
      // Bad manifest, variant, files and commit.
      $$"""{ {{head}}, "packages": [{ {{Package().Replace(manifest, "[]")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace(manifest, "null")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"1.0.0\"", "\"1.0\"")}} }] }""",
      $$"""{ {{head}}, "packages": [{ {{Package().Replace("\"format_version\":4", "\"format_version\":3")}} }] }""",
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
  }
}
