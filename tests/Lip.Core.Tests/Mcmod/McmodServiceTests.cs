using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod;

public class McmodServiceTests {
  private const string Lib = "example.com/lib";
  private const string App = "example.com/app";

  private static readonly OperationOptions s_options = new();

  private static readonly OperationOptions s_server = new(Platform: McmodPlatform.Server);

  private readonly WorkspaceHarness _ws = new();

  [Fact]
  public async Task Install_ToothSpecWithoutPlatform_InstallsAndLocksWithoutPlatform() {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.0.0", "lib/lib.dll"),
      ["file.dll"] = "lib 1",
    });

    // Act
    await _ws.Service.Install([Lib], s_options);

    // Assert
    Assert.Equal("lib 1", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    LockFile lockFile = await _ws.Lock();
    Assert.Null(lockFile.Platform);
    Assert.NotNull(lockFile.Rid);
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal(Lib, package.Identity);
    Assert.Equal("1.0.0", package.Version);
    Assert.Equal("tooth", package.Kind);
    Assert.True(package.Explicit);
    Assert.Equal(["lib/lib.dll"], package.Files);
    Assert.Equal($"{Lib}@1.0.0", package.ArchiveKey);
    Assert.IsType<GitArchiveIdentity>(package.Archive);
  }

  [Fact]
  public async Task Install_NewestVersion_OpensOnlyTheNewestTag() {
    // Arrange
    for (int i = 0; i < 20; i++) {
      _ws.AddTag(Lib, $"1.{i}.0", new() {
        ["tooth.json"] = WorkspaceHarness.Tooth(Lib, $"1.{i}.0", "lib/lib.dll"),
        ["file.dll"] = $"lib {i}",
      });
    }

    // Act
    await _ws.Service.Install([Lib], s_options);

    // Assert
    Assert.Equal("lib 19", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    Assert.Single(_ws.Git.ListedTrees.Distinct());
  }

  [Fact]
  public async Task Install_ToothWithDependency_InstallsDependencyAsImplicit() {
    // Arrange
    AddLibAndApp();

    // Act
    await _ws.Service.Install([$"{App}@1.0.0"], s_options);

    // Assert
    IReadOnlyList<InstalledPackage> packages = await _ws.Service.List();
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "1.0.0", true)],
        packages.Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.True(_ws.Exists("lib/lib.dll"));
    Assert.True(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Install_AlreadyExplicit_Throws() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([Lib], s_options);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Lib], s_options));
    Assert.Contains("already explicitly installed", ex.Message);
  }

  [Fact]
  public async Task Install_ImplicitPackage_MarksItExplicit() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], s_options);

    // Act
    await _ws.Service.Install([$"{Lib}@1.1.0"], s_options);

    // Assert
    Assert.All(await _ws.Service.List(), p => Assert.True(p.Explicit));
  }

  [Fact]
  public async Task Install_InstalledDependencyTooOld_ReplacesIt() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.0.0"], s_options);
    await _ws.Service.Install([$"{App}@1.0.0"], s_options);
    await _ws.Service.Uninstall([Lib], s_options);
    Assert.Equal(
        [(Lib, "1.0.0", false), (App, "1.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));

    // Act: app 2.0.0 needs lib >=1.1.0, so the installed lib cannot stay.
    await _ws.Service.Update([$"{App}@2.0.0"], s_options);

    // Assert
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "2.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.Equal("lib 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Uninstall_ExplicitPackage_RemovesItsUnneededDependencies() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], s_options);

    // Act
    await _ws.Service.Uninstall([App], s_options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("lib/lib.dll"));
    Assert.False(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Uninstall_DependencyOfAnotherPackage_KeepsItAsDependency() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.1.0", App], s_options);

    // Act
    await _ws.Service.Uninstall([Lib], s_options);

    // Assert
    LockFile lockFile = await _ws.Lock();
    Assert.False(lockFile.Packages.Single(p => p.Identity == Lib).Explicit);
    Assert.True(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Uninstall_ImplicitPackage_ThrowsUnlessNoDependencies() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], s_options);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall([Lib], s_options));
    await _ws.Service.Uninstall([Lib], s_options with { NoDependencies = true });
    Assert.Equal([App], (await _ws.Lock()).Packages.Select(p => p.Identity));
    Assert.False(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Uninstall_NotInstalled_Throws() {
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall(["Nothing"], s_options));
  }

  [Fact]
  public async Task Update_NoArguments_UpdatesExplicitPackagesToNewest() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([$"{App}@1.0.0"], s_options);

    // Act
    await _ws.Service.Update([], s_options);

    // Assert
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "2.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.Equal("app 2", _ws.Fs.File.ReadAllText(_ws.Path("app/app.dll")));
  }

  [Fact]
  public async Task Update_NotInstalled_Throws() {
    AddLibAndApp();

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Update([App], s_options));
  }

  [Fact]
  public async Task Install_DryRun_ChangesNothing() {
    // Arrange
    AddLibAndApp();

    // Act
    await _ws.Service.Install([App], s_options with { DryRun = true });

    // Assert
    Assert.False(_ws.Exists("app/app.dll"));
    Assert.False(_ws.Exists(LockStore.FileName));
  }

  [Fact]
  public async Task Install_NoDependencies_InstallsOnlyTheRequest() {
    // Arrange
    AddLibAndApp();

    // Act
    await _ws.Service.Install([App], s_options with { NoDependencies = true });

    // Assert
    Assert.Equal([App], (await _ws.Lock()).Packages.Select(p => p.Identity));
    Assert.False(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Install_RoutingDirectoryByPlatform_InstallsServerModUnderPlugins() {
    // Arrange
    AddRoutingFixture();

    // Act
    await _ws.Service.Install(["./fixture/"], s_server);

    // Assert
    Assert.Equal("server", _ws.Fs.File.ReadAllText(_ws.Path("plugins/Demo/Demo.dll")));
    Assert.True(_ws.Exists("plugins/Demo/manifest.json"));
    Assert.False(_ws.Exists("plugins/Demo/client/Demo.dll"));
    LockFile lockFile = await _ws.Lock();
    Assert.Equal("server", lockFile.Platform);
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal("Demo", package.Identity);
    Assert.Equal("mcmod", package.Kind);
    Assert.Equal(
        [("./fixture/", (int?)0), ("./server/", null)],
        package.Chain.Select(h => (h.Ref, h.Artifact)));
  }

  [Fact]
  public async Task Install_RoutingWithoutPlatform_FailsWithPlatformUnknown() {
    AddRoutingFixture();

    await Assert.ThrowsAsync<PlatformUnknownException>(
        () => _ws.Service.Install(["./fixture/"], s_options));
  }

  [Fact]
  public async Task Install_ModdedServerExecutable_DetectsServer() {
    // Arrange
    AddRoutingFixture();
    _ws.AddFile("bedrock_server_mod.exe", "");

    // Act
    await _ws.Service.Install(["./fixture/"], s_options);

    // Assert
    Assert.True(_ws.Exists("plugins/Demo/Demo.dll"));
    Assert.Equal("server", (await _ws.Lock()).Platform);
  }

  [Fact]
  public async Task Uninstall_ByCaseInsensitiveName_RemovesModFiles() {
    // Arrange
    AddRoutingFixture();
    await _ws.Service.Install(["./fixture/"], s_server);

    // Act
    await _ws.Service.Uninstall(["demo"], s_options);

    // Assert
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("plugins/Demo")));
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Install_FilePackageWithPlacements_WritesPlacementsAndUninstallRemovesThem() {
    // Arrange
    _ws.AddFile("pkg/manifest.json", WorkspaceHarness.Concrete("Tool", "1.0.0", "files", """
          "placements": [
            { "src": "bin/", "dest": "tools/" },
            { "src": "readme.txt", "dest": "docs/tool.txt" }
          ]
        """));
    _ws.AddFile("pkg/bin/tool.exe", "exe");
    _ws.AddFile("pkg/readme.txt", "readme");

    // Act
    await _ws.Service.Install(["pkg"], s_options);

    // Assert
    Assert.Equal("exe", _ws.Fs.File.ReadAllText(_ws.Path("tools/tool.exe")));
    Assert.Equal("readme", _ws.Fs.File.ReadAllText(_ws.Path("docs/tool.txt")));
    Assert.Equal(
        ["tools/tool.exe", "docs/tool.txt"], Assert.Single((await _ws.Lock()).Packages).Files);

    await _ws.Service.Uninstall(["Tool"], s_options);
    Assert.False(_ws.Exists("tools/tool.exe"));
    Assert.False(_ws.Exists("docs/tool.txt"));
  }

  [Fact]
  public async Task Install_McmodFile_InstallsConcreteZip() {
    // Arrange
    _ws.Fs.AddFile(_ws.Path("x.mcmod"), new MockFileData(WorkspaceHarness.Zip(new() {
      ["manifest.json"] = WorkspaceHarness.Concrete("Zipped", "2.0.0", "native"),
      ["Zipped.dll"] = "zipped",
    })));

    // Act
    await _ws.Service.Install(["./x.mcmod"], s_server);

    // Assert
    Assert.Equal("zipped", _ws.Fs.File.ReadAllText(_ws.Path("plugins/Zipped/Zipped.dll")));
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.IsType<ZipArchiveIdentity>(package.Archive);
  }

  [Fact]
  public async Task Install_PinnedPackageWithRelativeEdgeAfterCacheClean_ReResolvesNestedZip() {
    // Arrange: a nested ZIP whose package depends on a directory inside it.
    _ws.AddFile("outer/manifest.json", """
        {
          "formatVersion": 4,
          "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
          "artifacts": [ { "refs": ["./inner.mcmod"] } ]
        }
        """);
    _ws.Fs.AddFile(_ws.Path("outer/inner.mcmod"), new MockFileData(WorkspaceHarness.Zip(new() {
      ["manifest.json"] = WorkspaceHarness.Concrete("Inner", "1.0.0", "files", """
            "placements": [ { "src": "a.txt", "dest": "a.txt" } ],
            "installDependencies": [ { "refs": ["./dep/"] } ]
          """),
      ["a.txt"] = "a",
      ["dep/manifest.json"] = WorkspaceHarness.Concrete("Dep", "1.0.0", "files", """
            "placements": [ { "src": "b.txt", "dest": "b.txt" } ]
          """),
      ["dep/b.txt"] = "b",
    })));
    _ws.AddFile("other/manifest.json", WorkspaceHarness.Concrete("Other", "1.0.0", "files"));
    await _ws.Service.Install(["./outer/"], s_options);
    await _ws.Cache.Clean();

    // Act: the installed Inner is pinned; its edge ./dep/ needs its archive again.
    await _ws.Service.Install(["./other/"], s_options);

    // Assert
    Assert.Equal(
        ["Dep", "Inner", "Other"],
        (await _ws.Lock()).Packages.Select(p => p.Identity).Order());
    Assert.True(_ws.Exists("b.txt"));
  }

  [Fact]
  public async Task Operations_V3Lock_MigrateAndKeepEntries() {
    // Arrange: a v3 lock with lib installed explicitly, and its file.
    AddLibAndApp();
    string manifest = WorkspaceHarness.Tooth(Lib, "1.0.0", "lib/lib.dll");
    _ws.AddFile("tooth_lock.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [
            {
              "files": ["{{_ws.Path("lib/lib.dll").Replace("\\", "\\\\")}}"],
              "locked": true,
              "manifest": {{manifest}},
              "variant": ""
            }
          ]
        }
        """);
    _ws.AddFile("lib/lib.dll", "lib 1");

    // Act
    await _ws.Service.Install([$"{App}@1.0.0"], s_options);

    // Assert: lib 1.0.0 is kept (app needs >=1.0.0), the lock is migrated.
    Assert.False(_ws.Exists(LockStore.LegacyFileName));
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(
        [(Lib, "1.0.0", true), (App, "1.0.0", true)],
        lockFile.Packages.Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.Null(lockFile.Packages[0].Archive);

    await _ws.Service.Uninstall([App, Lib], s_options);
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Uninstall_V3LockWithTwoVariants_RemovesOnlyTheNamedVariant() {
    // Arrange
    string manifest = $$"""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "{{Lib}}",
          "version": "1.0.0",
          "variants": [
            { "label": "a", "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "a.dll", "dest": "a.dll" } ] } ] },
            { "label": "b", "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "b.dll", "dest": "b.dll" } ] } ] }
          ]
        }
        """;
    string Entry(string variant) => $$"""
        {
          "files": ["{{variant}}.dll"],
          "locked": true,
          "manifest": {{manifest}},
          "variant": "{{variant}}"
        }
        """;
    _ws.AddFile("tooth_lock.json", $$"""
        { "format_version": 3, "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [ {{Entry("a")}}, {{Entry("b")}} ] }
        """);
    _ws.AddFile("a.dll", "a");
    _ws.AddFile("b.dll", "b");

    // Act
    IReadOnlyList<InstalledPackage> listed = await _ws.Service.List();
    await _ws.Service.Uninstall([$"{Lib}#a"], s_options);

    // Assert
    Assert.Equal(2, listed.Count);
    Assert.False(_ws.Exists("a.dll"));
    Assert.True(_ws.Exists("b.dll"));
    LockedPackage remaining = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal($"{Lib}#b", remaining.Chain[0].Ref);
  }

  [Fact]
  public async Task Install_ToothTagDeclaresOtherVersion_UsesTagVersionAndWarns() {
    // Arrange
    _ws.AddTag(Lib, "2.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.9.0", "lib/lib.dll"),
      ["file.dll"] = "lib",
    });

    // Act
    await _ws.Service.Install([Lib], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal("2.0.0", package.Version);
    Assert.Equal("2.0.0", package.Manifest.GetProperty("version").GetString());
    Assert.Contains(_ws.Warnings, w => w.Contains("using the tag version 2.0.0"));
  }

  [Fact]
  public async Task View_SpecRef_PrintsRootDocumentOfNewestTag() {
    // Arrange
    AddLibAndApp();

    // Act
    string json = await _ws.Service.View(App);

    // Assert
    Assert.Equal("2.0.0", JsonDocument.Parse(json).RootElement.GetProperty("version").GetString());
  }

  [Fact]
  public async Task Versions_SpecRef_ListsVersionsLowestFirst() {
    AddLibAndApp();

    Assert.Equal(["1.0.0", "2.0.0"], await _ws.Service.Versions(App));
  }

  /// lib 1.0.0 and 1.1.0; app 1.0.0 (lib >=1.0.0) and 2.0.0 (lib >=1.1.0).
  private void AddLibAndApp() {
    foreach (string version in new[] { "1.0.0", "1.1.0" }) {
      _ws.AddTag(Lib, version, new() {
        ["tooth.json"] = WorkspaceHarness.Tooth(Lib, version, "lib/lib.dll"),
        ["file.dll"] = $"lib {version}",
      });
    }

    _ws.AddTag(App, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(App, "1.0.0", "app/app.dll",
          $$"""{ "{{Lib}}": ">=1.0.0" }"""),
      ["file.dll"] = "app 1",
    });
    _ws.AddTag(App, "2.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(App, "2.0.0", "app/app.dll",
          $$"""{ "{{Lib}}": ">=1.1.0" }"""),
      ["file.dll"] = "app 2",
    });
  }

  /// fixture/: a routing document selecting ./server/ or ./client/ by platform.
  private void AddRoutingFixture() {
    _ws.AddFile("fixture/manifest.json", """
        {
          "formatVersion": 4,
          "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
          "artifacts": [
            { "platform": "server", "refs": ["./server/"] },
            { "platform": "client", "refs": ["./client/"] }
          ]
        }
        """);
    _ws.AddFile("fixture/server/manifest.json",
        WorkspaceHarness.Concrete("Demo", "1.0.0", "native", "\"platform\": \"server\""));
    _ws.AddFile("fixture/server/Demo.dll", "server");
    _ws.AddFile("fixture/client/manifest.json",
        WorkspaceHarness.Concrete("Demo", "1.0.0", "native", "\"platform\": \"client\""));
    _ws.AddFile("fixture/client/Demo.dll", "client");
  }
}
