using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// End-to-end tests of install, uninstall, update and list (protocol/cli.md §5.1–§5.4,
/// protocol/workspace.md §3–§5 and §7.1) through the whole pipeline of WorkspaceHarness.
public class McmodServiceTests {
  private const string Lib = "example.com/lib";
  private const string App = "example.com/app";

  private static readonly OperationOptions _options = new(Rid: WorkspaceHarness.Runtime);

  private readonly WorkspaceHarness _ws = new();

  #region Install

  [Fact]
  public async Task Install_RemoteRepository_RecordsWorkspaceFile() {
    // Arrange
    string commit = _ws.Git.AddTag(Lib, "v1.0.0", new() {
      ["lip.yaml"] = LibPackageFile("1.0.0"),
      ["lib.dll"] = "lib 1.0.0",
    });

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert
    Assert.Equal("lib 1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(4, lockFile.Format);
    Assert.Equal("server", lockFile.Platform);
    Assert.Equal(WorkspaceHarness.Runtime, lockFile.Runtime);
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal((Lib, "", "1.0.0", true, Lib), (
        package.Id, package.Variant, package.Version, package.Explicit, package.Source));
    Assert.Equal(commit, package.Commit);
    Assert.Equal(["lib/lib.dll"], package.Files);
    Assert.True(JsonNode.DeepEquals(
        Yaml(LibPackageFile("1.0.0")), JsonNode.Parse(package.Manifest.GetRawText())));
  }

  [Fact]
  public async Task Install_RemoteRepository_WritesTheMembersOfTheSchema() {
    // Arrange
    AddLib("1.0.0");
    _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/x", "1.0.0", WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
      ["x.txt"] = "x",
    });

    // Act
    await _ws.Service.Install([Lib, "x.zip"], _options);

    // Assert: "commit" only for a package from a remote repository.
    using JsonDocument document = YamlJson.Parse(_ws.Fs.File.ReadAllBytes(_ws.LockPath));
    JsonElement[] packages = [.. document.RootElement.GetProperty("packages").EnumerateArray()];
    Assert.Equal(
        ["manifest", "variant", "explicit", "ref", "commit", "files"],
        packages[0].EnumerateObject().Select(p => p.Name));
    Assert.Equal(
        ["manifest", "variant", "explicit", "ref", "files"],
        packages[1].EnumerateObject().Select(p => p.Name));
  }

  [Fact]
  public async Task Install_LocalZip_RecordsFileUriSourceAndNoCommit() {
    // Arrange: a self asset placing through plugins/, and a zip asset.
    byte[] extra = WorkspaceHarness.Zip(new() { ["y.txt"] = "y" });
    _ws.AddDownload("https://example.com/extra.zip", extra);
    string packageFile = WorkspaceHarness.LipYaml("example.com/pkg", "1.0.0", """
        "assets": [
          { "type": "self", "place": [ { "src": "x.txt", "dest": "mods/pkg/x.txt" } ] },
          {
            "type": "zip",
            "urls": [ "https://example.com/extra.zip" ],
            "place": [ { "src": "y.txt", "dest": "extra/y.txt" } ]
          }
        ]
        """);
    string path = _ws.AddZip("pkg.zip", new() { ["lip.yaml"] = packageFile, ["x.txt"] = "x" });

    // Act
    await _ws.Service.Install(["pkg.zip"], _options);

    // Assert
    Assert.Equal("x", _ws.Fs.File.ReadAllText(_ws.Path("mods/pkg/x.txt")));
    Assert.Equal("y", _ws.Fs.File.ReadAllText(_ws.Path("extra/y.txt")));
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    string uri = Ref.FromFile(path).ToString();
    Assert.StartsWith("file:", uri);
    Assert.Equal(uri, package.Source);
    Assert.Null(package.Commit);
    Assert.Equal(["mods/pkg/x.txt", "extra/y.txt"], package.Files);
    Assert.True(JsonNode.DeepEquals(
        Yaml(packageFile), JsonNode.Parse(package.Manifest.GetRawText())));
  }

  [Fact]
  public async Task Install_NewestVersion_OpensOnlyTheNewestTag() {
    // Arrange
    for (int i = 0; i < 20; i++) {
      AddLib($"1.{i}.0");
    }

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert
    Assert.Equal("lib 1.19.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    Assert.Single(_ws.Git.ListedTrees.Distinct());
  }

  [Fact]
  public async Task Install_WithDependency_InstallsDependencyPackage() {
    // Arrange
    AddLibAndApp();

    // Act
    await _ws.Service.Install([$"{App}@1.0.0"], _options);

    // Assert: in installation order, the dependency first.
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "1.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    LockFile lockFile = await _ws.Lock();
    Assert.Equal([Lib, $"{App}@1.0.0"], lockFile.Packages.Select(p => p.Source));
    Assert.True(_ws.Exists("lib/lib.dll"));
    Assert.True(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Install_FormatThreeDependency_InstallsBoth() {
    // Arrange: app of format 4 depends on lib of format 3.
    _ws.AddTag(Lib, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.0.0", "lib/lib.dll"),
      ["file.dll"] = "lib 1.0.0",
    });
    AddApp("1.0.0", ">=1.0.0");

    // Act
    await _ws.Service.Install([App], _options);

    // Assert
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(
        [(Lib, false), (App, true)], lockFile.Packages.Select(p => (p.Id, p.Explicit)));
    Assert.Equal("lib 1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));

    // The tooth.json is recorded as the package file derived from it.
    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse($$"""
            {
              "format": 4,
              "id": "{{Lib}}",
              "version": "1.0.0",
              "variants": {
                "": [ {
                  "platform": "server",
                  "runtime": "{{WorkspaceHarness.Runtime}}",
                  "assets": [
                    { "type": "self", "place": [ { "src": "file.dll", "dest": "lib/lib.dll" } ] }
                  ]
                } ]
              }
            }
            """),
        JsonNode.Parse(lockFile.Packages[0].Manifest.GetRawText())));
  }

  [Fact]
  public async Task Install_FormatThreeWithOtherVersionThanItsTag_RecordsTheTagVersion() {
    // Arrange
    _ws.AddTag(Lib, "1.2.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "0.9.0", "lib/lib.dll"),
      ["file.dll"] = "lib",
    });

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal("1.2.0", package.Version);
    Assert.Equal("1.2.0", package.Manifest.GetProperty("version").GetString());
    Assert.Equal([(Lib, "1.2.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
  }

  [Fact]
  public async Task Install_AlreadyExplicit_Throws() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([Lib], _options);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Lib], _options));
    Assert.Contains("already explicitly installed", ex.Message);
  }

  [Fact]
  public async Task Install_TwoArgumentsOfOneIdentity_ThrowsWithoutChanges() {
    // Arrange
    AddLibAndApp();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Lib, $"{Lib}@1.0.0"], _options));
    Assert.False(_ws.Exists("lib/lib.dll"));
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
  }

  [Fact]
  public async Task Install_DependencyPackage_MarksItExplicit() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], _options);

    // Act
    await _ws.Service.Install([$"{Lib}@1.1.0"], _options);

    // Assert: kept as it is, now with the source of the request.
    LockedPackage lib = (await _ws.Lock()).Packages.Single(p => p.Id == Lib);
    Assert.Equal((true, "1.1.0", $"{Lib}@1.1.0"), (lib.Explicit, lib.Version, lib.Source));
  }

  [Fact]
  public async Task Install_DependencyPackageThatFitsTheRequest_KeepsItsVersion() {
    // Arrange: lib 1.0.0 is a dependency package; lib 1.1.0 is newer.
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);
    await _ws.Service.Install([$"{App}@1.0.0"], _options);
    await _ws.Service.Uninstall([Lib], _options);
    _ws.Restart();

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert: it only becomes explicitly installed (protocol/cli.md §5.1).
    LockedPackage lib = (await _ws.Lock()).Packages.Single(p => p.Id == Lib);
    Assert.Equal((true, "1.0.0", Lib), (lib.Explicit, lib.Version, lib.Source));
    Assert.Equal("lib 1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Install_DependencyPackagesChosenAgain_KeepTheInstalledVersionsThatFit() {
    // Arrange: a needs d and e, installed at 1.0.0; b needs e ^2.0.0.
    AddPackage("example.com/d", "1.0.0");
    AddPackage("example.com/e", "1.0.0");
    AddPackage("example.com/a", "1.0.0", "example.com/d", ">=1.0.0", "example.com/e", ">=1.0.0");
    await _ws.Service.Install(["example.com/a"], _options);
    AddPackage("example.com/d", "2.0.0");
    AddPackage("example.com/e", "2.0.0");
    AddPackage("example.com/b", "1.0.0", "example.com/e", "^2.0.0");
    _ws.Restart();

    // Act
    await _ws.Service.Install(["example.com/b"], _options);

    // Assert: only e changes version (protocol/cli.md §5.1).
    Assert.Equal(
        [("example.com/d", "1.0.0"), ("example.com/e", "2.0.0")],
        (await _ws.Service.List()).Where(p => !p.Explicit).Select(p => (p.Identity, p.Version)));
  }

  [Fact]
  public async Task Install_InstalledDependencyTooOld_ReplacesIt() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);
    await _ws.Service.Install([$"{App}@1.0.0"], _options);
    await _ws.Service.Uninstall([Lib], _options);
    Assert.Equal(
        [(Lib, "1.0.0", false), (App, "1.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));

    // Act: app 2.0.0 needs lib >=1.1.0, so the installed lib cannot stay.
    await _ws.Service.Update([$"{App}@2.0.0"], _options);

    // Assert
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "2.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.Equal("lib 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Install_NoDependencies_InstallsOnlyTheRequest() {
    // Arrange
    AddLibAndApp();

    // Act
    await _ws.Service.Install([App], _options with { NoDependencies = true });

    // Assert
    LockedPackage app = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((App, "2.0.0", true), (app.Id, app.Version, app.Explicit));
    Assert.False(_ws.Exists("lib/lib.dll"));
    Assert.True(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Install_NoDependenciesOfInstalledDependency_OnlyMarksIt() {
    // Arrange
    AddLibAndApp(hook: true);
    await _ws.Service.Install([App], _options);
    LockedPackage before = (await _ws.Lock()).Packages.Single(p => p.Id == Lib);

    // Act
    await _ws.Service.Install([Lib], _options with { NoDependencies = true });

    // Assert: same identity, version and commit, so nothing is reinstalled.
    LockedPackage after = (await _ws.Lock()).Packages.Single(p => p.Id == Lib);
    Assert.True(after.Explicit);
    Assert.Equal(before.Commit, after.Commit);
    Assert.Equal(before.Files, after.Files);
    _ws.CommandRunner.Verify(r => r.Run("lib-installed", _ws.Root), Times.Once);
  }

  [Fact]
  public async Task Install_NoDependenciesOtherVersion_ReplacesInstalledPackageOnly() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([$"{App}@1.0.0", $"{Lib}@1.0.0"], _options);

    // Act
    await _ws.Service.Install([$"{Lib}@1.1.0"], _options with { NoDependencies = true });

    // Assert
    Assert.Equal(
        [(App, "1.0.0", true), (Lib, "1.1.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)).Order());
    Assert.Equal("lib 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Install_NoDependenciesDryRunOfInstalledDependency_ReportsMarkingIt() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], _options);
    List<string> infos = [];
    _ws.Ui.Setup(u => u.PrintInfo(It.IsAny<string>()))
        .Callback((string message) => infos.Add(message))
        .Returns(Task.CompletedTask);

    // Act
    await _ws.Service.Install([Lib], _options with { NoDependencies = true, DryRun = true });

    // Assert: protocol/cli.md §4, a dry run reports the packages it would mark as explicitly
    // installed.
    Assert.Contains(infos, m => m.StartsWith($"Dry run: would mark package '{Lib}'"));
    Assert.False((await _ws.Lock()).Packages.Single(p => p.Id == Lib).Explicit);
  }

  [Fact]
  public async Task Install_DryRun_ChangesNothing() {
    // Arrange
    AddLibAndApp(hook: true);
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act
    await _ws.Service.Install([App], _options with { DryRun = true });

    // Assert
    Assert.Equal(before, WorkspaceSnapshot());
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path(".lip")));
    _ws.CommandRunner.Verify(
        r => r.Run(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Install_IgnoreScripts_RunsNoHook() {
    // Arrange
    AddLibAndApp(hook: true);

    // Act
    await _ws.Service.Install([Lib], _options with { IgnoreScripts = true });

    // Assert
    Assert.True(_ws.Exists("lib/lib.dll"));
    _ws.CommandRunner.Verify(
        r => r.Run(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Install_VariantsTogether_RecordsVariantIdentities() {
    // Arrange
    AddVariantsPackage();

    // Act
    await _ws.Service.Install([$"{Lib}#a", $"{Lib}#b"], _options);

    // Assert
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(
        [($"{Lib}#a", "a", $"{Lib}#a"), ($"{Lib}#b", "b", $"{Lib}#b")],
        lockFile.Packages.Select(p => (p.Identity, p.Variant, p.Source)));
    Assert.Equal("a", _ws.Fs.File.ReadAllText(_ws.Path("a.dll")));
    Assert.Equal("b", _ws.Fs.File.ReadAllText(_ws.Path("b.dll")));
  }

  [Fact]
  public async Task Install_BranchRef_RecordsCommitAndUpdateFollowsHead() {
    // Arrange
    string first = _ws.Git.AddBranch(Lib, "main", new() {
      ["lip.yaml"] = LibPackageFile("1.2.0"),
      ["lib.dll"] = "head 1",
    });

    // Act
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);
    LockedPackage installed = Assert.Single((await _ws.Lock()).Packages);
    string second = _ws.Git.AddBranch(Lib, "main", new() {
      ["lip.yaml"] = LibPackageFile("1.2.0"),
      ["lib.dll"] = "head 2",
    });
    _ws.Restart();
    await _ws.Service.Update([], _options);

    // Assert: the version is the package file's, as a branch has no tag version; the new commit
    // differs, so the package is replaced.
    Assert.Equal(("1.2.0", $"{Lib}@branch:main"), (installed.Version, installed.Source));
    Assert.Equal(first, installed.Commit);
    Assert.Equal("head 2", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    LockedPackage updated = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(second, updated.Commit);
    Assert.Equal($"{Lib}@branch:main", updated.Source);
  }

  [Fact]
  public async Task Install_TagAndRevRefs_UsePackageFileVersionAndExactCommit() {
    // Arrange
    _ws.Git.AddTag(Lib, "nightly", new() {
      ["lip.yaml"] = LibPackageFile("3.0.0-nightly"),
      ["lib.dll"] = "nightly",
    });
    string rev = _ws.Git.AddBranch(App, "dev", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(App, "0.1.0"),
    });
    _ws.Git.AddBranch(App, "dev", new() { ["lip.yaml"] = WorkspaceHarness.LipYaml(App, "0.2.0") });

    // Act
    await _ws.Service.Install([$"{Lib}@tag:nightly", $"{App}@rev:{rev}"], _options);

    // Assert: the rev stays at the commit the branch has moved away from.
    Assert.Equal(
        [(App, "0.1.0", $"{App}@rev:{rev}"), (Lib, "3.0.0-nightly", $"{Lib}@tag:nightly")],
        (await _ws.Lock()).Packages.Select(p => (p.Id, p.Version, p.Source)).Order());
  }

  [Fact]
  public async Task Install_FormatThreeTagDeclaresOtherVersion_UsesTagVersionAndWarns() {
    // Arrange
    _ws.AddTag(Lib, "2.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.9.0", "lib/lib.dll"),
      ["file.dll"] = "lib",
    });

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal("2.0.0", package.Version);
    Assert.NotEmpty(_ws.Warnings);
  }

  [Fact]
  public async Task Install_ClientPlatformRecorded_ThrowsWithoutChanges() {
    // Arrange
    AddLibAndApp();
    _ws.AddFile(".lip/workspace.yaml", """
        { "format": 4, "platform": "client", "runtime": "win-x64", "packages": [] }
        """);
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Lib], _options));
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_RidOption_RecordsRuntime() {
    // Arrange: lib has entries for win-x64 and linux-x64.
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = $$"""
          {
            "format": 4,
            "id": "{{Lib}}",
            "version": "1.0.0",
            "variants": {
              "": [
                { "platform": "server", "runtime": "win-x64",
                  "assets": [ { "type": "self", "place": [ { "src": "win.dll", "dest": "lib.dll" } ] } ] },
                { "platform": "server", "runtime": "linux-x64",
                  "assets": [ { "type": "self", "place": [ { "src": "linux.so", "dest": "lib.so" } ] } ] }
              ]
            }
          }
          """,
      ["win.dll"] = "win",
      ["linux.so"] = "linux",
    });

    // Act
    await _ws.Service.Install([Lib], _options with { Rid = "linux-x64" });

    // Assert
    Assert.Equal("linux", _ws.Fs.File.ReadAllText(_ws.Path("lib.so")));
    Assert.False(_ws.Exists("lib.dll"));
    Assert.Equal("linux-x64", (await _ws.Lock()).Runtime);
  }

  #endregion

  #region Uninstall

  [Fact]
  public async Task Uninstall_ExplicitPackage_RemovesItsUnneededDependencies() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], _options);

    // Act
    await _ws.Service.Uninstall([App], _options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("lib/lib.dll"));
    Assert.False(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Uninstall_DependencyOfAnotherPackage_KeepsItAsDependency() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.1.0", App], _options);

    // Act
    await _ws.Service.Uninstall([Lib], _options);

    // Assert
    Assert.False((await _ws.Lock()).Packages.Single(p => p.Id == Lib).Explicit);
    Assert.True(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Uninstall_DependencyPackage_ThrowsUnlessNoDependencies() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], _options);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall([Lib], _options));
    await _ws.Service.Uninstall([Lib], _options with { NoDependencies = true });
    Assert.Equal([App], (await _ws.Lock()).Packages.Select(p => p.Identity));
    Assert.False(_ws.Exists("lib/lib.dll"));
    Assert.True(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Uninstall_NoDependenciesOfExplicitPackage_KeepsItsDependencies() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], _options);

    // Act
    await _ws.Service.Uninstall([App], _options with { NoDependencies = true });

    // Assert
    LockedPackage lib = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((Lib, false), (lib.Id, lib.Explicit));
    Assert.True(_ws.Exists("lib/lib.dll"));
    Assert.False(_ws.Exists("app/app.dll"));
  }

  [Fact]
  public async Task Uninstall_NotInstalled_Throws() {
    AddLibAndApp();
    await _ws.Service.Install([Lib], _options);

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall([App], _options));
    Assert.Single((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Uninstall_IdentityInOtherCase_RemovesPackage() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([Lib], _options);

    // Act
    await _ws.Service.Uninstall(["Example.COM/Lib"], _options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Uninstall_SourceWithoutVariant_MatchesEveryVariant() {
    // Arrange: the identities are lib#a and lib#b, so only rule 2 of protocol/cli.md §5.2 matches.
    AddVariantsPackage();
    await _ws.Service.Install([$"{Lib}#a", $"{Lib}#b"], _options);

    // Act
    await _ws.Service.Uninstall([Lib], _options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("a.dll"));
    Assert.False(_ws.Exists("b.dll"));
  }

  [Fact]
  public async Task Uninstall_IdentityWithVariant_RemovesOnlyThatVariant() {
    // Arrange
    AddVariantsPackage();
    await _ws.Service.Install([$"{Lib}#a", $"{Lib}#b"], _options);

    // Act
    await _ws.Service.Uninstall([$"{Lib}#a"], _options);

    // Assert
    Assert.False(_ws.Exists("a.dll"));
    Assert.Equal("b", _ws.Fs.File.ReadAllText(_ws.Path("b.dll")));
    LockedPackage remaining = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(($"{Lib}#b", true), (remaining.Identity, remaining.Explicit));
  }

  [Fact]
  public async Task Uninstall_SourceWithRef_MatchesOnlyThatRef() {
    // Arrange: lib installed from lib@1.0.0, whose identity is lib.
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);

    // Act & Assert: another ref names nothing; the same ref, in another case, names it.
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall([$"{Lib}@1.1.0"], _options));
    await _ws.Service.Uninstall(["example.com/LIB@1.0.0"], _options);
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Uninstall_SourceWithoutRef_MatchesEveryRef() {
    // Arrange: the identity is example.com/lib; the argument is the location in other case
    // with a variant-free, ref-free source.
    _ws.Git.AddBranch(Lib, "main", new() { ["lip.yaml"] = LibPackageFile("1.5.0"), ["lib.dll"] = "x" });
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);

    // Act
    await _ws.Service.Uninstall([Lib], _options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Theory]
  [InlineData("./src")]
  [InlineData("src")]
  public async Task Uninstall_ByLocalPath_RemovesPackageInstalledFromIt(string argument) {
    // Arrange
    AddLocalDirectory("src", "1.0.0", "x");
    await _ws.Service.Install(["./src"], _options);

    // Act
    await _ws.Service.Uninstall([argument], _options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("out/x.txt"));
  }

  [Fact]
  public async Task Uninstall_ByLocalPathInOtherCase_RemovesPackageInstalledFromIt() {
    // Elsewhere "./SRC" names no existing path, so it is an invalid package argument
    // (protocol/cli.md §3).
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows paths are case-insensitive.");

    // Arrange
    AddLocalDirectory("src", "1.0.0", "x");
    await _ws.Service.Install(["./src"], _options);

    // Act
    await _ws.Service.Uninstall(["./SRC"], _options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("out/x.txt"));
  }

  [Fact]
  public async Task Install_PreservedFileDeletedByPreInstall_RecordsItAsOwned() {
    // Arrange: the file matching preserve exists when the operation is planned, but pre_install
    // deletes it, so it is written and owned (protocol/workspace.md §4).
    _ws.AddFile("cfg.ini", "user");
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0",
          WorkspaceHarness.SelfAsset("cfg.ini", "cfg.ini")
              + """, "preserve": [ "cfg.ini" ], "hooks": { "pre_install": [ "reset" ] }"""),
      ["cfg.ini"] = "default",
    });
    _ws.CommandRunner.Setup(r => r.Run("reset", _ws.Root))
        .Callback(() => _ws.Fs.File.Delete(_ws.Path("cfg.ini")))
        .Returns(Task.CompletedTask);

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert
    Assert.Equal("default", _ws.Fs.File.ReadAllText(_ws.Path("cfg.ini")));
    Assert.Equal(["cfg.ini"], Assert.Single((await _ws.Lock()).Packages).Files);
  }

  [Fact]
  public async Task Install_PreInstallBeforeAnyChange_RunsWithTheJournal() {
    // Arrange: mods exists, so pre_install is the first thing the operation does; the journal
    // exists before it runs (protocol/workspace.md §7.4 steps 5 and 6).
    _ws.Fs.Directory.CreateDirectory(_ws.Path("mods"));
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0",
          WorkspaceHarness.SelfAsset("a.txt", "a.txt")
              + """, "hooks": { "pre_install": [ "check" ] }"""),
      ["a.txt"] = "a",
    });
    bool? journalExisted = null;
    _ws.CommandRunner.Setup(r => r.Run("check", _ws.Root))
        .Callback(() => journalExisted = _ws.Fs.File.Exists(_ws.JournalPath))
        .Returns(Task.CompletedTask);

    // Act
    await _ws.Service.Install([Lib], _options);

    // Assert
    Assert.True(journalExisted);
    Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
  }

  [Fact]
  public async Task Uninstall_OnlyHookCommands_RunsThemWithTheJournal() {
    // Arrange: lib owns no file, so uninstalling it changes nothing but runs pre_uninstall
    // (protocol/workspace.md §7.4 step 4 does not apply).
    _ws.Fs.Directory.CreateDirectory(_ws.Path("mods"));
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0", """ "hooks": { "pre_uninstall": [ "check" ] }"""),
    });
    await _ws.Service.Install([Lib], _options);
    bool? journalExisted = null;
    _ws.CommandRunner.Setup(r => r.Run("check", _ws.Root))
        .Callback(() => journalExisted = _ws.Fs.File.Exists(_ws.JournalPath))
        .Returns(Task.CompletedTask);

    // Act
    await _ws.Service.Uninstall([Lib], _options);

    // Assert
    Assert.True(journalExisted);
    Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Install_PreservedFileWrittenAfterHookThenWrittenByAnother_FailsAndUndoes() {
    // Arrange: lib keeps cfg.ini as planned, but its pre_install deletes it, so lib writes and
    // owns it; app, installed after lib, then writing it is a failure (protocol/workspace.md §4).
    _ws.AddFile("cfg.ini", "user");
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0",
          WorkspaceHarness.SelfAsset("cfg.ini", "cfg.ini")
              + """, "preserve": [ "cfg.ini" ], "hooks": { "pre_install": [ "reset" ] }"""),
      ["cfg.ini"] = "lib",
    });
    _ws.AddTag(App, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          App, "1.0.0",
          $$""" "deps": { "{{Lib}}": "1.0.0" }, """
              + WorkspaceHarness.SelfAsset("cfg.ini", "cfg.ini")),
      ["cfg.ini"] = "app",
    });
    _ws.CommandRunner.Setup(r => r.Run("reset", _ws.Root))
        .Callback(() => _ws.Fs.File.Delete(_ws.Path("cfg.ini")))
        .Returns(Task.CompletedTask);

    // Act & Assert
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install([App], _options));
    Assert.Contains("cfg.ini", ex.Message);
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
    Assert.False(_ws.Exists("cfg.ini"));
    Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
  }

  [Fact]
  public async Task Install_WriteOfFileWrittenByEarlierPackage_FailsAtTheWrite() {
    // Arrange: lib's pre_install deletes cfg.ini, which lib was to keep, so lib writes and owns
    // it; app writing it then is the failure itself (protocol/workspace.md §4), so app's
    // post_install never runs.
    _ws.AddFile("cfg.ini", "user");
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0",
          WorkspaceHarness.SelfAsset("cfg.ini", "cfg.ini")
              + """, "preserve": [ "cfg.ini" ], "hooks": { "pre_install": [ "reset" ] }"""),
      ["cfg.ini"] = "lib",
    });
    _ws.AddTag(App, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          App, "1.0.0",
          $$""" "deps": { "{{Lib}}": "1.0.0" }, """
              + WorkspaceHarness.SelfAsset("cfg.ini", "cfg.ini")
              + """, "hooks": { "post_install": [ "app-post" ] }"""),
      ["cfg.ini"] = "app",
    });
    _ws.CommandRunner.Setup(r => r.Run("reset", _ws.Root))
        .Callback(() => _ws.Fs.File.Delete(_ws.Path("cfg.ini")))
        .Returns(Task.CompletedTask);

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install([App], _options));

    // Assert
    Assert.Contains($"'cfg.ini', which belongs to {Lib}", ex.Message);
    _ws.CommandRunner.Verify(r => r.Run("app-post", It.IsAny<string>()), Times.Never);
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
    Assert.False(_ws.Exists("cfg.ini"));
  }

  [Fact]
  public async Task Uninstall_PurgeMatchesFileThatEarlierHookRestored_DeletesIt() {
    // Arrange: app's post_uninstall restores server.exe, as LeviLamina restores
    // bedrock_server.exe, before lib, uninstalled after it, purges it (protocol/workspace.md §6
    // rule 4).
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0",
          WorkspaceHarness.SelfAsset("lib.dll", "lib/lib.dll") + """, "purge": [ "server.exe" ]"""),
      ["lib.dll"] = "lib",
    });
    _ws.AddTag(App, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          App, "1.0.0",
          $$""" "deps": { "{{Lib}}": "1.0.0" }, """
              + WorkspaceHarness.SelfAsset("app.dll", "app/app.dll")
              + """, "hooks": { "post_uninstall": [ "restore" ] }"""),
      ["app.dll"] = "app",
    });
    await _ws.Service.Install([App], _options);
    _ws.CommandRunner.Setup(r => r.Run("restore", _ws.Root))
        .Callback(() => _ws.AddFile("server.exe", "server"))
        .Returns(Task.CompletedTask);

    // Act
    await _ws.Service.Uninstall([App], _options);

    // Assert
    _ws.CommandRunner.Verify(r => r.Run("restore", _ws.Root), Times.Once);
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("server.exe"));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("lib")));
  }

  [Fact]
  public async Task Uninstall_PurgeMatchesFileThatHookCreated_FailureRestoresIt() {
    // Arrange: lib purges server.exe, which app's post_uninstall restores; lib's post_uninstall
    // then fails, so the operation is undone, the restored file included.
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          Lib, "1.0.0",
          WorkspaceHarness.SelfAsset("lib.dll", "lib/lib.dll")
              + """, "purge": [ "server.exe" ], "hooks": { "post_uninstall": [ "fail" ] }"""),
      ["lib.dll"] = "lib",
    });
    _ws.AddTag(App, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          App, "1.0.0",
          $$""" "deps": { "{{Lib}}": "1.0.0" }, """
              + WorkspaceHarness.SelfAsset("app.dll", "app/app.dll")
              + """, "hooks": { "post_uninstall": [ "restore" ] }"""),
      ["app.dll"] = "app",
    });
    await _ws.Service.Install([App], _options);
    _ws.CommandRunner.Setup(r => r.Run("restore", _ws.Root))
        .Callback(() => _ws.AddFile("server.exe", "server"))
        .Returns(Task.CompletedTask);
    _ws.CommandRunner.Setup(r => r.Run("fail", _ws.Root))
        .ThrowsAsync(new InvalidOperationException("hook failed"));

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Uninstall([App], _options));
    Assert.Equal(2, (await _ws.Lock()).Packages.Count);
    Assert.Equal("server", _ws.Fs.File.ReadAllText(_ws.Path("server.exe")));
    Assert.True(_ws.Exists("lib/lib.dll"));
  }

  [Fact]
  public async Task Uninstall_ByDeletedLocalPath_FailsWithoutChanges() {
    // Arrange: a local path that does not exist is an invalid package argument
    // (protocol/cli.md §3), which names no installed package (§5.2).
    string path = _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/x", "1.0.0", WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
      ["x.txt"] = "x",
    });
    await _ws.Service.Install(["x.zip"], _options);
    _ws.Fs.File.Delete(path);
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
        () => _ws.Service.Uninstall(["./x.zip"], _options));
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Uninstall_FileUriArgument_FailsWithoutChanges() {
    // Arrange: an argument starting with "file://" is invalid (protocol/cli.md §3 rule 1), even
    // when it is the recorded source.
    AddLocalDirectory("src", "1.0.0", "x");
    await _ws.Service.Install(["./src"], _options);
    string source = Assert.Single((await _ws.Lock()).Packages).Source;
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
        () => _ws.Service.Uninstall([source], _options));
    Assert.Equal(before, WorkspaceSnapshot());
  }

  #endregion

  #region Update

  [Fact]
  public async Task Update_NoArguments_UpdatesExplicitPackagesToNewest() {
    // Arrange
    AddLibAndApp(withApp2: false);
    await _ws.Service.Install([App], _options);
    AddApp("2.0.0", ">=1.1.0");
    _ws.Restart();

    // Act
    await _ws.Service.Update([], _options);

    // Assert
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "2.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.Equal("app 2.0.0", _ws.Fs.File.ReadAllText(_ws.Path("app/app.dll")));
  }

  [Fact]
  public async Task Update_NothingDiffers_ReplacesNothing() {
    // Arrange
    AddLibAndApp(hook: true);
    await _ws.Service.Install([Lib], _options);
    _ws.Restart();

    // Act
    await _ws.Service.Update([], _options);
    await _ws.Service.Update([Lib], _options);

    // Assert: the post_install hook ran only for the install.
    _ws.CommandRunner.Verify(r => r.Run("lib-installed", _ws.Root), Times.Once);
    Assert.Equal("1.1.0", Assert.Single((await _ws.Lock()).Packages).Version);
  }

  [Fact]
  public async Task Update_TagMovedToOtherCommit_ReplacesPackage() {
    // Arrange
    AddLib("1.0.0");
    await _ws.Service.Install([Lib], _options);
    string moved = _ws.Git.AddTag(Lib, "v1.0.0", new() {
      ["lip.yaml"] = LibPackageFile("1.0.0"),
      ["lib.dll"] = "lib moved",
    });
    _ws.Restart();

    // Act
    await _ws.Service.Update([], _options);

    // Assert: same version, other commit.
    Assert.Equal("lib moved", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    Assert.Equal(moved, Assert.Single((await _ws.Lock()).Packages).Commit);
  }

  [Fact]
  public async Task Update_BranchAtTheSameCommit_ReplacesNothing() {
    // Arrange
    string commit = _ws.Git.AddBranch(Lib, "main", new() {
      ["lip.yaml"] = LibPackageFile("1.0.0", hook: true),
      ["lib.dll"] = "head",
    });
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);
    _ws.Restart();

    // Act
    await _ws.Service.Update([], _options);

    // Assert
    _ws.CommandRunner.Verify(r => r.Run("lib-installed", _ws.Root), Times.Once);
    Assert.Equal(commit, Assert.Single((await _ws.Lock()).Packages).Commit);
  }

  [Fact]
  public async Task Install_NoDependenciesOfSameVersionAtOtherCommit_ReplacesIt() {
    // Arrange
    AddLib("1.0.0", hook: true);
    await _ws.Service.Install([Lib], _options);
    string moved = _ws.Git.AddTag(Lib, "v1.0.0", new() {
      ["lip.yaml"] = LibPackageFile("1.0.0", hook: true),
      ["lib.dll"] = "lib moved",
    });
    _ws.Restart();

    // Act
    await _ws.Service.Install([Lib], _options with { NoDependencies = true });

    // Assert
    Assert.Equal("lib moved", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
    Assert.Equal(moved, Assert.Single((await _ws.Lock()).Packages).Commit);
    _ws.CommandRunner.Verify(r => r.Run("lib-installed", _ws.Root), Times.Exactly(2));
  }

  [Fact]
  public async Task Install_InstalledPackageWhoseTagMoved_IsKeptWithItsRecordedPackageFile() {
    // Arrange: mod a is installed; its tag then moves to a package file whose mod is named B.
    AddMod("example.com/a", "A");
    await _ws.Service.Install(["example.com/a"], _options);
    _ws.AddDownload("https://example.com/moved.mcmod", WorkspaceHarness.Mcmod("B"));
    _ws.AddTag("example.com/a", "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml("example.com/a", "1.0.0",
          """ "assets": [ { "type": "mcmod", "urls": [ "https://example.com/moved.mcmod" ] } ]"""),
    });
    AddMod("example.com/b", "B");
    _ws.Restart();

    // Act: the installed package is reinstalled with the package file the workspace file records,
    // so its mod name is still A, and b's mod name B does not clash with it.
    await _ws.Service.Install(["example.com/b"], _options);

    // Assert
    Assert.True(_ws.Exists("mods/A/A.dll"));
    Assert.True(_ws.Exists("mods/B/B.dll"));
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(
        ["https://example.com/A.mcmod"],
        lockFile.Packages[0].Manifest.GetProperty("variants").GetProperty("")[0]
            .GetProperty("assets")[0].GetProperty("urls").EnumerateArray()
            .Select(u => u.GetString()));
  }

  [Fact]
  public async Task Update_VersionRefWhoseTagMoved_KeepsTheRecordedCommit() {
    // Arrange
    AddLib("1.0.0");
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);
    _ws.Git.AddTag(Lib, "v1.0.0", new() { ["lip.yaml"] = LibPackageFile("1.0.0"), ["lib.dll"] = "moved" });
    AddLib("1.1.0");
    _ws.Restart();

    // Act
    await _ws.Service.Update([], _options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(("1.0.0", $"{Lib}@1.0.0"), (package.Version, package.Source));
    Assert.Equal("lib 1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Update_SourceWithoutRef_MovesBackToVersionTags() {
    // Arrange
    AddLibAndApp();
    _ws.Git.AddBranch(Lib, "main", new() { ["lip.yaml"] = LibPackageFile("1.5.0"), ["lib.dll"] = "head" });
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);

    // Act
    await _ws.Service.Update([Lib], _options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(("1.1.0", Lib, true), (package.Version, package.Source, package.Explicit));
    Assert.Equal("lib 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  [Fact]
  public async Task Update_AssetChanged_ReplacesNothing() {
    // Arrange: same tag, but the asset its URL serves changes.
    _ws.AddDownload("https://example.com/data.zip", WorkspaceHarness.Zip(new() { ["d.txt"] = "1" }));
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(Lib, "1.0.0", """
          "assets": [ { "type": "zip", "urls": [ "https://example.com/data.zip" ],
            "place": [ { "src": "d.txt", "dest": "d.txt" } ] } ]
          """),
    });
    await _ws.Service.Install([Lib], _options);
    byte[] changed = WorkspaceHarness.Zip(new() { ["d.txt"] = "2" });
    _ws.AddDownload("https://example.com/data.zip", changed);
    ClearCache();
    _ws.Restart();

    // Act
    await _ws.Service.Update([], _options);

    // Assert: same identity, version and commit (protocol/workspace.md §5).
    Assert.Equal("1", _ws.Fs.File.ReadAllText(_ws.Path("d.txt")));
  }

  [Fact]
  public async Task Update_LocalZipOfSameVersion_ReplacesNothing() {
    // Arrange
    _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/x", "1.0.0", WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
      ["x.txt"] = "v1",
    });
    await _ws.Service.Install(["x.zip"], _options);
    _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/x", "1.0.0", WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
      ["x.txt"] = "v2",
    });

    // Act
    await _ws.Service.Update([], _options);

    // Assert: a ZIP archive has no commit, and the version is the same.
    Assert.Equal("v1", _ws.Fs.File.ReadAllText(_ws.Path("x.txt")));
  }

  [Fact]
  public async Task Update_LocalDirectoryOfSameVersion_ReplacesNothing() {
    // Arrange
    AddLocalDirectory("src", "1.0.0", "v1");
    await _ws.Service.Install(["./src"], _options);
    AddLocalDirectory("src", "1.0.0", "v2");

    // Act
    await _ws.Service.Update([], _options);

    // Assert
    Assert.Equal("v1", _ws.Fs.File.ReadAllText(_ws.Path("out/x.txt")));

    // Act: a new version is a new package.
    AddLocalDirectory("src", "1.0.1", "v3");
    await _ws.Service.Update([], _options);

    // Assert
    Assert.Equal("v3", _ws.Fs.File.ReadAllText(_ws.Path("out/x.txt")));
    Assert.Equal("1.0.1", Assert.Single((await _ws.Lock()).Packages).Version);
  }

  [Fact]
  public async Task Update_NoArgumentsSourceGivesOtherIdentity_ReplacesThePackage() {
    // Arrange: the local ZIP file now holds a package of another id.
    _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/x", "1.0.0", WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
      ["x.txt"] = "x",
    });
    await _ws.Service.Install(["x.zip"], _options);
    _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/y", "1.0.0", WorkspaceHarness.SelfAsset("y.txt", "y.txt")),
      ["y.txt"] = "y",
    });

    // Act
    await _ws.Service.Update([], _options);

    // Assert: it differs in identity (protocol/workspace.md §5 "Updating").
    Assert.Equal(
        [("example.com/y", "1.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.False(_ws.Exists("x.txt"));
    Assert.Equal("y", _ws.Fs.File.ReadAllText(_ws.Path("y.txt")));
  }

  [Fact]
  public async Task Update_NoArgumentsTwoSourcesGiveSameIdentity_Fails() {
    // Arrange: a.zip now holds b 2.0.0, while b.zip still holds b 1.0.0.
    _ws.AddZip("a.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/a", "1.0.0", WorkspaceHarness.SelfAsset("a.txt", "a.txt")),
      ["a.txt"] = "a",
    });
    _ws.AddZip("b.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/b", "1.0.0", WorkspaceHarness.SelfAsset("b.txt", "b.txt")),
      ["b.txt"] = "b1",
    });
    await _ws.Service.Install(["a.zip", "b.zip"], _options);
    _ws.AddZip("a.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/b", "2.0.0", WorkspaceHarness.SelfAsset("b.txt", "b.txt")),
      ["b.txt"] = "b2",
    });

    // Act & Assert: both requests cannot be satisfied by one package of that identity
    // (protocol/workspace.md §3).
    await Assert.ThrowsAsync<DependencyResolutionException>(
        () => _ws.Service.Update([], _options));
    Assert.Equal(
        [("example.com/a", "1.0.0", true), ("example.com/b", "1.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.Equal("b1", _ws.Fs.File.ReadAllText(_ws.Path("b.txt")));
  }

  [Fact]
  public async Task Update_NoArgumentsTwoSourcesGiveSamePackage_ReplacesWithIt() {
    // Arrange: a.zip now holds the package b.zip holds.
    _ws.AddZip("a.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/a", "1.0.0", WorkspaceHarness.SelfAsset("a.txt", "a.txt")),
      ["a.txt"] = "a",
    });
    Dictionary<string, string> b = new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/b", "1.0.0", WorkspaceHarness.SelfAsset("b.txt", "b.txt")),
      ["b.txt"] = "b",
    };
    _ws.AddZip("b.zip", b);
    await _ws.Service.Install(["a.zip", "b.zip"], _options);
    _ws.AddZip("a.zip", b);

    // Act
    await _ws.Service.Update([], _options);

    // Assert: one package satisfies both requests.
    Assert.Equal(
        [("example.com/b", "1.0.0", true)],
        (await _ws.Service.List()).Select(p => (p.Identity, p.Version, p.Explicit)));
    Assert.False(_ws.Exists("a.txt"));
  }

  [Fact]
  public async Task Update_LocalPathGone_KeepsPackageAndWarns() {
    // Arrange
    string path = _ws.AddZip("x.zip", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          "example.com/x", "1.0.0", WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
      ["x.txt"] = "v1",
    });
    await _ws.Service.Install([path], _options);
    _ws.Fs.File.Delete(path);

    // Act
    await _ws.Service.Update([], _options);

    // Assert
    Assert.Equal("v1", _ws.Fs.File.ReadAllText(_ws.Path("x.txt")));
    Assert.Contains(_ws.Warnings, w => w.Contains("does not exist any more"));
    Assert.Single((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Update_Dependency_BecomesExplicitlyInstalled() {
    // Arrange
    AddLibAndApp();
    await _ws.Service.Install([App], _options);

    // Act
    await _ws.Service.Update([Lib], _options);

    // Assert
    Assert.True((await _ws.Lock()).Packages.Single(p => p.Id == Lib).Explicit);
  }

  [Fact]
  public async Task Update_TwoArgumentsOfOneIdentity_UpdatesItByTheFirst() {
    // Arrange: protocol/cli.md §5.3 defines no failure for this.
    AddLibAndApp();
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);

    // Act
    await _ws.Service.Update([Lib, $"{Lib}@1.0.0"], _options);

    // Assert
    LockedPackage lib = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(("1.1.0", Lib), (lib.Version, lib.Source));
  }

  [Fact]
  public async Task Update_NotInstalled_Throws() {
    AddLibAndApp();

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Update([App], _options));
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
  }

  #endregion

  #region Reinstalling and list

  [Fact]
  public async Task Install_ModWithDependency_WritesItsManifestAsItIs() {
    // Arrange
    AddMod("example.com/base", "Base");
    AddMod("example.com/top", "Top", """ "deps": { "example.com/base": "^1.0.0" }, """);

    // Act
    await _ws.Service.Install(["example.com/top"], _options);

    // Assert: lip reads only the "name" of manifest.json and never changes it.
    using JsonDocument top = JsonDocument.Parse(
        _ws.Fs.File.ReadAllText(_ws.Path("mods/Top/manifest.json")));
    Assert.Equal("Top", top.RootElement.GetProperty("name").GetString());
    Assert.False(top.RootElement.TryGetProperty("dependencies", out _));
    Assert.True(_ws.Exists("mods/Base/manifest.json"));
  }

  [Fact]
  public async Task List_ReturnsPackagesInInstallationOrder() {
    // Arrange
    AddLibAndApp();
    AddVariantsPackage("example.com/var");
    await _ws.Service.Install([App, "example.com/var#a"], _options);

    // Act
    IReadOnlyList<InstalledPackage> packages = await _ws.Service.List();

    // Assert
    Assert.Equal(
        [(Lib, "1.1.0", false), (App, "2.0.0", true), ("example.com/var#a", "1.0.0", true)],
        packages.Select(p => (p.Identity, p.Version, p.Explicit)));
  }

  [Fact]
  public async Task List_NewWorkspace_IsEmpty() {
    Assert.Empty(await _ws.Service.List());
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path(".lip")));
  }

  #endregion

  #region Environment

  [Fact]
  public async Task Install_InstalledPackageWithoutEntryForTheRuntime_FailsWithoutChanges() {
    // Arrange: lib has an entry for win-x64 only, so it is no candidate for linux-x64
    // (protocol/workspace.md §3).
    AddLib("1.0.0");
    await _ws.Service.Install([Lib], _options);
    const string any = "example.com/any";
    _ws.AddTag(any, "1.0.0", new() {
      ["lip.yaml"] = $$"""
          {
            "format": 4,
            "id": "{{any}}",
            "version": "1.0.0",
            "variants": { "": [ { "platform": "server", "runtime": "linux-x64" } ] }
          }
          """,
    });
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([any], _options with { Rid = "linux-x64" }));

    // Assert
    Assert.Contains($"'{Lib}'", ex.Message);
    Assert.Equal(before, WorkspaceSnapshot());
  }

  #endregion

  #region Versions

  [Fact]
  public async Task Versions_RemoteRepository_ListsVersionsLowestFirst() {
    // Arrange
    AddLib("1.1.0");
    AddLib("1.0.0");

    // Act
    IReadOnlyList<string> versions = await _ws.Service.Versions(Lib);

    // Assert
    Assert.Equal(["1.0.0", "1.1.0"], versions);
  }

  [Theory]
  [InlineData("example.com/lib@1.0.0")]
  [InlineData("file:///lib")]
  [InlineData("http://example.com/lib")]
  [InlineData("https://example.com/lib.zip")]
  [InlineData("lib.zip")]
  public async Task Versions_NotARemoteRepositoryWithoutRef_Throws(string argument) {
    // Act & Assert (protocol/cli.md §5.5)
    await Assert.ThrowsAsync<ArgumentException>(() => _ws.Service.Versions(argument));
  }

  [Fact]
  public async Task Versions_ArgumentNamingAnExistingDirectory_IsALocalPathAndThrows() {
    // Arrange: such an argument is a local path (protocol/cli.md §3 rule 2).
    AddLib("1.0.0");
    _ws.Fs.AddDirectory(_ws.Path(Lib));

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => _ws.Service.Versions(Lib));
  }

  #endregion

  #region Invalid workspace files

  [Theory]
  [InlineData(".lip/workspace.yaml", """{ "format": "bf45b1", "platform": "server", "packages": [] }""")]
  [InlineData(".lip/workspace.yaml", """{ "format_version": 3, "packages": [] }""")]
  [InlineData(".lip/workspace.yaml", "format: 4\nplatform: server\nruntime: win-x64\npackages: [ { } ]\n")]
  public async Task Operations_InvalidWorkspaceFile_FailWithoutChanges(string file, string content) {
    // Arrange
    AddLibAndApp();
    _ws.AddFile(file, content);
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Install([Lib], _options));
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Uninstall([Lib], _options));
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Update([], _options));
    await Assert.ThrowsAnyAsync<Exception>(_ws.Service.List);
    Assert.Equal(before, WorkspaceSnapshot());
    Assert.False(_ws.Exists("lib/lib.dll"));
  }

  #endregion

  /// The files, directories and links of the workspace, without lip's cache outside it.
  private SortedDictionary<string, string> WorkspaceSnapshot() => new(
      _ws.Snapshot().Where(e => e.Key.StartsWith(_ws.Root, StringComparison.Ordinal))
          .ToDictionary(e => e.Key, e => e.Value),
      StringComparer.Ordinal);

  /// Deletes lip's cache, as lip cache clean in another process does.
  private void ClearCache() {
    if (_ws.Fs.Directory.Exists(LipDirectories.Cache)) {
      _ws.Fs.Directory.Delete(LipDirectories.Cache, recursive: true);
    }
  }


  /// A YAML file as the JSON value it represents.
  private static JsonNode? Yaml(string yaml) {
    using JsonDocument document = YamlJson.Parse(yaml);
    return JsonNode.Parse(document.RootElement.GetRawText());
  }

  private static string LibPackageFile(string version, bool hook = false) => WorkspaceHarness.LipYaml(
      Lib,
      version,
      WorkspaceHarness.SelfAsset("lib.dll", "lib/lib.dll")
          + (hook ? """, "hooks": { "post_install": [ "lib-installed" ] }""" : ""));

  private void AddLib(string version, bool hook = false) => _ws.AddTag(Lib, version, new() {
    ["lip.yaml"] = LibPackageFile(version, hook),
    ["lib.dll"] = $"lib {version}",
  });

  private void AddApp(string version, string libRange) => _ws.AddTag(App, version, new() {
    ["lip.yaml"] = WorkspaceHarness.LipYaml(
        App,
        version,
        $$""" "deps": { "{{Lib}}": "{{libRange}}" }, """
            + WorkspaceHarness.SelfAsset("app.dll", "app/app.dll")),
    ["app.dll"] = $"app {version}",
  });

  /// A version of a package placing &lt;name&gt;.txt, with dependencies given as pairs of
  /// source and range.
  private void AddPackage(string id, string version, params string[] dependencies) {
    string name = id[(id.LastIndexOf('/') + 1)..];
    string deps = string.Join(", ", dependencies.Chunk(2).Select(d => $"\"{d[0]}\": \"{d[1]}\""));
    _ws.AddTag(id, version, new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(
          id,
          version,
          $$""" "deps": { {{deps}} }, """ + WorkspaceHarness.SelfAsset($"{name}.txt", $"{name}.txt")),
      [$"{name}.txt"] = $"{name} {version}",
    });
  }

  /// lib 1.0.0 and 1.1.0; app 1.0.0 (lib >=1.0.0) and 2.0.0 (lib >=1.1.0).
  private void AddLibAndApp(bool withApp2 = true, bool hook = false) {
    AddLib("1.0.0", hook);
    AddLib("1.1.0", hook);
    AddApp("1.0.0", ">=1.0.0");
    if (withApp2) {
      AddApp("2.0.0", ">=1.1.0");
    }
  }

  /// Version 1.0.0 of a package whose variants "a" and "b" place a.dll and b.dll.
  private void AddVariantsPackage(string id = Lib) => _ws.AddTag(id, "1.0.0", new() {
    ["lip.yaml"] = $$"""
        {
          "format": 4,
          "id": "{{id}}",
          "version": "1.0.0",
          "variants": {
            "a": [ { "platform": "server", "runtime": "{{WorkspaceHarness.Runtime}}",
              "assets": [ { "type": "self", "place": [ { "src": "a.dll", "dest": "a.dll" } ] } ] } ],
            "b": [ { "platform": "server", "runtime": "{{WorkspaceHarness.Runtime}}",
              "assets": [ { "type": "self", "place": [ { "src": "b.dll", "dest": "b.dll" } ] } ] } ]
          }
        }
        """,
    ["a.dll"] = "a",
    ["b.dll"] = "b",
  });

  /// A local directory holding a package example.com/local placing x.txt at out/x.txt.
  private void AddLocalDirectory(string relative, string version, string content) {
    _ws.AddFile($"{relative}/lip.yaml", WorkspaceHarness.LipYaml(
        "example.com/local", version, WorkspaceHarness.SelfAsset("x.txt", "out/x.txt")));
    _ws.AddFile($"{relative}/x.txt", content);
  }

  /// Version 1.0.0 of a mod: a package whose asset of type "mcmod" is
  /// https://example.com/&lt;name&gt;.mcmod; entry holds extra members, each followed by a comma.
  private void AddMod(string id, string name, string entry = "") {
    string url = $"https://example.com/{name}.mcmod";
    _ws.AddDownload(url, WorkspaceHarness.Mcmod(name));
    _ws.AddTag(id, "1.0.0", new() {
      ["lip.yaml"] = WorkspaceHarness.LipYaml(id, "1.0.0",
          $$"""{{entry}} "assets": [ { "type": "mcmod", "urls": [ "{{url}}" ] } ]"""),
    });
  }
}
