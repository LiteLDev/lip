using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

/// The tooth_lock.json of lip v0.x (protocol/compatibility.md §3), through the whole pipeline of
/// WorkspaceHarness.
public class ToothLockMigrationTests {
  private const string Lib = "example.com/lib";
  private const string App = "example.com/app";
  private const string Other = "example.com/other";

  private static readonly OperationOptions _options = new(Rid: WorkspaceHarness.Runtime);

  private readonly WorkspaceHarness _ws = new();

  private string ToothLockPath => _ws.Path("tooth_lock.json");

  /// A tooth.json of format 3 of a package with a "lua" variant too.
  private static JsonNode Manifest(string id, string dest, string dependencies = "{}") =>
      JsonNode.Parse(WorkspaceHarness.Tooth(id, "1.0.0", dest, dependencies))!;

  /// A member of "packages" of a tooth_lock.json.
  private static JsonObject Member(JsonNode manifest, bool locked, params string[] files) => new() {
    ["files"] = new JsonArray([.. files.Select(f => (JsonNode)f)]),
    ["locked"] = locked,
    ["manifest"] = manifest,
    ["variant"] = "",
  };

  private void AddToothLock(params JsonObject[] members) =>
      _ws.Fs.AddFile(ToothLockPath, new(new JsonObject {
        ["format_version"] = 3,
        ["format_uuid"] = "289f771f-2c9a-4d73-9f3f-8492495a924d",
        ["packages"] = new JsonArray(members),
      }.ToJsonString()));

  /// app (explicitly installed, depending on lib) before lib, as lip v0.x may have listed them;
  /// their tags, and their files in the workspace.
  private (string LibCommit, string AppCommit) AddInstalledLibAndApp() {
    string lib = _ws.Git.AddTag(Lib, "v1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.0.0", "lib/lib.dll"),
      ["file.dll"] = "lib",
    });
    string app = _ws.Git.AddTag(App, "v1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(
          App, "1.0.0", "app/app.dll", $$"""{ "{{Lib}}": ">=1.0.0" }"""),
      ["file.dll"] = "app",
    });
    _ws.AddFile("lib/lib.dll", "lib");
    _ws.AddFile("app/app.dll", "app");
    string outside = OperatingSystem.IsWindows() ? @"C:\elsewhere\x.dll" : "/elsewhere/x.dll";
    AddToothLock(
        Member(
            Manifest(App, "app/app.dll", $$"""{ "{{Lib}}": ">=1.0.0" }"""),
            locked: true,
            _ws.Path("app/app.dll")),
        Member(
            Manifest(Lib, "lib/lib.dll"), locked: false, "lib/lib.dll", "../x.dll", outside, ""));
    _ws.AddTag(Other, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Other, "1.0.0", WorkspaceHarness.SelfAsset("o.txt", "o.txt")),
      ["o.txt"] = "o",
    });
    return (lib, app);
  }

  [Fact]
  public async Task List_ToothLock_IsReadInMemoryInInstallationOrder() {
    // Arrange
    AddInstalledLibAndApp();
    string before = _ws.Fs.File.ReadAllText(ToothLockPath);

    // Act
    IReadOnlyList<InstalledPackage> packages = await _ws.Service.List();

    // Assert
    Assert.Equal(
        [new InstalledPackage(Lib, "1.0.0", false), new InstalledPackage(App, "1.0.0", true)],
        packages);
    Assert.Equal(before, _ws.Fs.File.ReadAllText(ToothLockPath));
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
    Assert.Empty(_ws.Git.ListedRefs);
  }

  [Fact]
  public async Task Install_ToothLock_WritesDerivedWorkspaceFileAndDeletesToothLock() {
    // Arrange
    (string libCommit, string appCommit) = AddInstalledLibAndApp();

    // Act
    await _ws.Service.Install([Other], _options);

    // Assert
    Assert.False(_ws.Fs.File.Exists(ToothLockPath));
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(("server", WorkspaceHarness.Runtime), (lockFile.Platform, lockFile.Runtime));
    Assert.Equal(
        [
          (Lib, false, Lib, (string?)libCommit),
          (App, true, App, appCommit),
        ],
        lockFile.Packages.Take(2).Select(p => (p.Identity, p.Explicit, p.Source, p.Commit)));
    Assert.Equal(["lib/lib.dll"], lockFile.Packages[0].Files);
    Assert.Equal(["app/app.dll"], lockFile.Packages[1].Files);
    Assert.Equal(Other, lockFile.Packages[2].Identity);
    JsonElement manifest = lockFile.Packages[0].Manifest;
    Assert.Equal(4, manifest.GetProperty("format").GetInt32());
    Assert.Equal(Lib, manifest.GetProperty("id").GetString());
    Assert.Equal("1.0.0", manifest.GetProperty("version").GetString());
    JsonElement entry = Assert.Single(manifest.GetProperty("variants").GetProperty("").EnumerateArray());
    Assert.Equal(
        ("server", WorkspaceHarness.Runtime),
        (entry.GetProperty("platform").GetString(), entry.GetProperty("runtime").GetString()));
    Assert.Equal("lib", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Uninstall_MigratedPackage_DeletesItsFiles() {
    // Arrange
    AddInstalledLibAndApp();

    // Act
    await _ws.Service.Uninstall([App], _options);

    // Assert
    Assert.False(_ws.Exists("app/app.dll"));
    Assert.False(_ws.Exists("lib/lib.dll"));
    Assert.False(_ws.Fs.File.Exists(ToothLockPath));
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Install_CommitOfTagCannotBeObtained_FailsAndChangesNothing() {
    // Arrange: lip v0.x installed a version whose tag is gone.
    AddInstalledLibAndApp();
    AddToothLock(Member(Manifest("example.com/gone", "gone.dll"), locked: true, "gone.dll"));
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Other], _options));

    // Assert
    Assert.Contains("tooth_lock.json", ex.Message);
    Assert.Equal(before, _ws.Snapshot());
  }

  [Fact]
  public async Task Install_MemberWithoutVariantForEnvironment_IsLeftOut() {
    // Arrange: a variant that the manifest does not have, and a manifest for linux-x64 only.
    AddInstalledLibAndApp();
    JsonObject lua = Member(Manifest(Lib, "lib/lua.dll"), locked: true, "lib/lua.dll");
    lua["variant"] = "lua";
    JsonNode linux = Manifest("example.com/linux", "linux.so");
    linux["variants"]![0]!["platform"] = "linux-x64";
    AddToothLock(
        Member(Manifest(Lib, "lib/lib.dll"), locked: true, "lib/lib.dll"),
        lua,
        Member(linux, locked: true, "linux.so"));

    // Act
    await _ws.Service.Install([Other], _options);

    // Assert
    Assert.Equal([Lib, Other], (await _ws.Lock()).Packages.Select(p => p.Identity));
  }

  [Fact]
  public async Task Install_DryRun_WritesNothing() {
    // Arrange
    AddInstalledLibAndApp();
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act
    await _ws.Service.Install([Other], _options with { DryRun = true });

    // Assert
    Assert.Equal(before, _ws.Snapshot());
  }

  [Fact]
  public async Task Install_WorkspaceFileExists_IgnoresToothLock() {
    // Arrange
    AddInstalledLibAndApp();
    string toothLock = _ws.Fs.File.ReadAllText(ToothLockPath);
    _ws.Fs.File.Move(ToothLockPath, ToothLockPath + ".bak");
    await _ws.Service.Install([Other], _options);
    _ws.Fs.File.Move(ToothLockPath + ".bak", ToothLockPath);
    await _ws.Service.Uninstall([Other], _options);

    // Act
    IReadOnlyList<InstalledPackage> packages = await _ws.Service.List();

    // Assert
    Assert.Empty(packages);
    Assert.Equal(toothLock, _ws.Fs.File.ReadAllText(ToothLockPath));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("""{ "format_version": 2, "packages": [] }""")]
  [InlineData("""{ "format_version": 3 }""")]
  [InlineData("""{ "format_version": 3, "packages": [ { "variant": "", "locked": true, "files": [] } ] }""")]
  [InlineData("""{ "format_version": 3, "packages": [ { "manifest": { "format_version": 4 }, "variant": "", "locked": true, "files": [] } ] }""")]
  [InlineData("""{ "format_version": 3, "packages": [ { "manifest": { "format_version": 3 }, "variant": "", "locked": "yes", "files": [] } ] }""")]
  [InlineData("""{ "format_version": 3, "packages": [ { "manifest": { "format_version": 3 }, "variant": "", "locked": true, "files": [1] } ] }""")]
  public async Task Install_ToothLockNotReadBySection3_IsIgnoredAndKept(string json) {
    // Arrange
    _ws.Fs.AddFile(ToothLockPath, new(json));
    _ws.AddTag(Other, "1.0.0", new() { ["lip.yaml"] = WorkspaceHarness.LipYaml(Other, "1.0.0") });

    // Act
    Assert.Empty(await _ws.Service.List());
    await _ws.Service.Install([Other], _options);

    // Assert
    Assert.Equal([Other], (await _ws.Lock()).Packages.Select(p => p.Identity));
    Assert.Equal(json, _ws.Fs.File.ReadAllText(ToothLockPath));
  }

  [Fact]
  public async Task Derive_CycleOfDependencies_IsInAscendingOrderOfIdentities() {
    // Arrange: b and a depend on each other; c depends on b.
    string Dep(string id) => $$"""{ "{{id}}": "*" }""";
    List<ToothLockMigration.Member> members = [
      .. new[] { ("example.com/c", "example.com/b"), ("example.com/b", "example.com/a"),
                 ("example.com/a", "example.com/b") }
          .Select(p => new ToothLockMigration.Member(
              JsonSerializer.SerializeToElement(Manifest(p.Item1, "x.dll", Dep(p.Item2))),
              "",
              true,
              [])),
    ];

    // Act
    LockFile lockFile = await ToothLockMigration.Derive(
        members, _ws.Fs, _ws.Root, new(McmodPlatform.Server, WorkspaceHarness.Runtime), null);

    // Assert
    Assert.Equal(
        ["example.com/a", "example.com/b", "example.com/c"],
        lockFile.Packages.Select(p => p.Identity));
    Assert.All(lockFile.Packages, p => Assert.Null(p.Commit));
  }

  [Fact]
  public async Task Derive_Variant_IsInRefAndIdentity() {
    // Arrange
    JsonNode manifest = JsonNode.Parse(WorkspaceHarness.Tooth(Lib, "1.0.0", "x.dll", label: "lua"))!;
    ToothLockMigration.Member member = new(
        JsonSerializer.SerializeToElement(manifest), "lua", false, [_ws.Path("x.dll")]);

    // Act
    LockFile lockFile = await ToothLockMigration.Derive(
        [member], _ws.Fs, _ws.Root, new(McmodPlatform.Server, WorkspaceHarness.Runtime),
        (_, _) => Task.FromResult<string?>(new string('A', 40)));

    // Assert
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal(($"{Lib}#lua", $"{Lib}#lua", "lua"), (package.Identity, package.Source, package.Variant));
    Assert.Equal(new string('a', 40), package.Commit);
    Assert.Equal(["x.dll"], package.Files);
  }
}
