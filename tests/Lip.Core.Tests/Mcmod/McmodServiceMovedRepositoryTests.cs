using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// A tooth.json repository that moved (transferred from one GitHub organisation to another): the
/// old path redirects to the new one, older tags name the old path in "tooth" and newer tags the
/// new one. The package keeps its identity, its tooth name (spec §10.2 rule 1), but is requested
/// by the new path from then on.
public class McmodServiceMovedRepositoryTests {
  private const string Old = "github.com/LiteLDev/Mod";
  private const string New = "github.com/MiracleForest/Mod";
  private const string App = "example.com/app";
  private const string Fork = "github.com/Someone/Mod";

  // The identities of the packages: their tooth names.
  private const string OldId = "Mod";
  private const string NewId = "Mod";
  private const string AppId = "app";

  private static readonly OperationOptions s_options = new();

  private readonly WorkspaceHarness _ws = new();

  public McmodServiceMovedRepositoryTests() {
    _ws.Git.AddRedirect(Old, New);
  }

  [Fact]
  public async Task Update_NoArguments_ReplacesPackageByTheNewPathKeepingPreservedFiles() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    _ws.AddFile("mod/config.json", "edited by the user");

    // Act
    await _ws.Service.Update([], s_options);

    // Assert: the package is requested by its new path from now on.
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0", true), (package.Identity, package.Version, package.Explicit));
    AssertRecordedFrom(New, package);
    Assert.Equal($"{New}@1.1.0", package.ArchiveKey);
    Assert.Equal("mod 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("mod/mod.dll")));
    Assert.Equal("edited by the user", _ws.Fs.File.ReadAllText(_ws.Path("mod/config.json")));
    VerifyInfo($"{Old} redirects to {New}");
    Assert.Empty(_ws.Warnings);
  }

  [Fact]
  public async Task Update_AfterTheReplacement_ResolvesTheNewPathWithoutWarningAndKeepsThePackage() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    await _ws.Service.Update([], s_options);
    LockFile before = await _ws.Lock();
    _ws.Ui.Invocations.Clear();

    // Act
    await _ws.Service.Update([], s_options);

    // Assert
    Assert.Equal(
        before.Packages.Select(p => (p.Identity, p.Version, p.ArchiveKey, p.Chain[0].Ref)),
        (await _ws.Lock()).Packages.Select(
            p => (p.Identity, p.Version, p.ArchiveKey, p.Chain[0].Ref)));
    Assert.Empty(_ws.Warnings);
    _ws.Ui.Verify(u => u.PrintInfo(It.Is<string>(m => m.Contains("redirects"))), Times.Never);
    _ws.Ui.Verify(u => u.PrintInfo(It.Is<string>(m => m.StartsWith("Installing"))), Times.Never);
  }

  [Fact]
  public async Task Update_LockOfV036RecordingTheOldPath_RecordsTheNewPathKeepingPreservedFiles() {
    // Arrange: lip v0.36.0 replaced the package under its new identity, but kept requesting it
    // by the old path, which only works through the redirect.
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    _ws.Git.ReportsRedirects = false;
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    await _ws.Service.Update([], s_options);
    LockedPackage installed = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0", $"{Old}@1.1.0"),
        (installed.Identity, installed.Version, installed.ArchiveKey));
    AssertRecordedFrom(Old, installed);
    _ws.AddFile("mod/config.json", "edited by the user");
    _ws.Git.ReportsRedirects = true;
    _ws.Warnings.Clear();

    // Act
    await _ws.Service.Update([], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0", true), (package.Identity, package.Version, package.Explicit));
    AssertRecordedFrom(New, package);
    Assert.Equal($"{New}@1.1.0", package.ArchiveKey);
    Assert.Equal(installed.Files, package.Files);
    Assert.Equal("mod 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("mod/mod.dll")));
    Assert.Equal("edited by the user", _ws.Fs.File.ReadAllText(_ws.Path("mod/config.json")));
    Assert.Empty(_ws.Warnings);
  }

  [Fact]
  public async Task Update_LockOfV036RecordingTheOldPath_DryRunChangesNothing() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    _ws.Git.ReportsRedirects = false;
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    await _ws.Service.Update([], s_options);
    _ws.Git.ReportsRedirects = true;
    List<KeyValuePair<string, string>> before = WorkspaceSnapshot();

    // Act
    await _ws.Service.Update([], s_options with { DryRun = true });

    // Assert
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Theory]
  [InlineData("tag:v1.1.0")]
  [InlineData("branch:main")]
  public async Task Update_PinnedToTheOldPath_KeepsThePinnedRef(string gitRef) {
    // Arrange: a pinned package stands in for the versions of its spec only (spec §6), and
    // dependencies of other packages name the old path, so the pinned ref keeps it.
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    _ws.Git.AddBranch(New, "main", VersionFiles("1.1.0", New));
    await _ws.Service.Install([$"{Old}@{gitRef}"], s_options);

    // Act
    await _ws.Service.Update([], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0"), (package.Identity, package.Version));
    AssertRecordedFrom($"{Old}@{gitRef}", package);
    Assert.StartsWith($"{Old}@rev:", package.ArchiveKey, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Install_DependencyOnTheOldPath_IsSatisfiedByThePackagePinnedByIt() {
    // Arrange
    _ws.Git.AddBranch(New, "main", VersionFiles("1.2.0", New));
    await _ws.Service.Install([$"{Old}@branch:main"], s_options);
    AddApp($$"""{ "{{Old}}": ">=1.0.0" }""");

    // Act
    await _ws.Service.Install([App], s_options);

    // Assert
    LockedPackage kept = Assert.Single((await _ws.Lock()).Packages, p => p.Identity == NewId);
    AssertRecordedFrom($"{Old}@branch:main", kept);
  }

  [Fact]
  public async Task Update_NamedByThePinnedOldPathWhoseBranchMoved_ReplacesThePackage() {
    // Arrange: the branch declared the old path, then the new one, at a version without a tag.
    _ws.Git.AddBranch(New, "main", VersionFiles("1.0.0", Old));
    await _ws.Service.Install([$"{Old}@branch:main"], s_options);
    _ws.Git.AddBranch(New, "main", VersionFiles("1.2.0", New));

    // Act
    await _ws.Service.Update([$"{Old}@branch:main"], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.2.0", true), (package.Identity, package.Version, package.Explicit));
    AssertRecordedFrom($"{Old}@branch:main", package);
  }

  [Fact]
  public async Task Update_NamedByTheOldPath_FindsTheInstalledPackageWithoutAskingForTags() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    _ws.Git.ListedRefs.Clear();

    // Act
    await _ws.Service.Update([Old], s_options);

    // Assert: the installed package is the one installed from the requested path; MovedFrom
    // does not compare the tags of both paths (one listing of "refs/tags/v1.0.0" each).
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0"), (package.Identity, package.Version));
    AssertRecordedFrom(New, package);
    Assert.DoesNotContain(_ws.Git.ListedRefs, r => r.EndsWith("refs/tags/v1.0.0"));
  }

  [Fact]
  public async Task Install_NoDependenciesByTheOldPath_RecordsTheNewPath() {
    // Arrange
    AddVersion("1.0.0", New);

    // Act
    await _ws.Service.Install([Old], s_options with { NoDependencies = true });

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.0.0", true), (package.Identity, package.Version, package.Explicit));
    AssertRecordedFrom(New, package);
  }

  [Fact]
  public async Task Uninstall_ByTheOldPathItWasInstalledBy_UninstallsThePackage() {
    // Arrange
    AddVersion("1.0.0", New);
    await _ws.Service.Install([Old], s_options);

    // Act
    await _ws.Service.Uninstall([Old], s_options);

    // Assert
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Exists("mod/mod.dll"));
  }

  [Fact]
  public async Task Uninstall_ByAForkPath_FailsAsNotInstalled() {
    // Arrange
    AddVersion("1.0.0", New);
    _ws.Git.CopyRef(New, Fork, "refs/tags/v1.0.0");
    await _ws.Service.Install([New], s_options);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall([Fork], s_options));
    Assert.Contains("is not installed", ex.Message);
  }

  [Fact]
  public async Task Install_OldVersionByTheNewPath_KeepsTheNewPath() {
    // Arrange: the old path redirects to the new one, not the other way round, so an older
    // version that still declares the old path is not requested by it.
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);

    // Act
    await _ws.Service.Install([$"{New}@1.0.0"], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((OldId, "1.0.0"), (package.Identity, package.Version));
    AssertRecordedFrom(New, package);
    Assert.Equal($"{New}@1.0.0", package.ArchiveKey);
    Assert.Contains(_ws.Warnings, w => w.Contains($"declares the package path '{Old}'"));
  }

  [Fact]
  public async Task Install_ForkWhoseToothDeclaresTheOriginal_KeepsTheFork() {
    // Arrange: a fork has the tag of the original at the same commit, whose tooth.json names
    // the original path; the fork does not redirect there.
    AddVersion("1.0.0", New);
    _ws.Git.CopyRef(New, Fork, "refs/tags/v1.0.0");

    // Act
    await _ws.Service.Install([Fork], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(NewId, package.Identity);
    AssertRecordedFrom(Fork, package);
    Assert.Equal($"{Fork}@1.0.0", package.ArchiveKey);
    Assert.Contains(_ws.Warnings, w => w.Contains($"declares the package path '{New}'"));
  }

  [Fact]
  public async Task Install_UnrelatedRepositoryWhoseToothDeclaresAnotherPath_KeepsThePath() {
    // Arrange
    const string other = "example.com/someone/Mod";
    AddVersion("1.0.0", New);
    AddVersion("1.0.0", New, repository: other);

    // Act
    await _ws.Service.Install([other], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(NewId, package.Identity);
    AssertRecordedFrom(other, package);
    Assert.Contains(_ws.Warnings, w => w.Contains($"declares the package path '{New}'"));
  }

  [Theory]
  [InlineData(Old)]
  [InlineData(New)]
  public async Task Update_NamedByEitherPath_ReplacesPackageByTheNewPath(string argument) {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);

    // Act
    await _ws.Service.Update([argument], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0", true), (package.Identity, package.Version, package.Explicit));
    Assert.Equal("mod 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("mod/mod.dll")));
  }

  [Fact]
  public async Task Update_LabelledVariant_KeepsTheLabel() {
    // Arrange
    AddVersion("1.0.0", Old, label: "server");
    AddVersion("1.1.0", New, label: "server");
    await _ws.Service.Install([$"{Old}#server@1.0.0"], s_options);

    // Act
    await _ws.Service.Update([], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(($"{NewId}-server", "1.1.0"), (package.Identity, package.Version));
    AssertRecordedFrom($"{New}#server", package);
    Assert.Equal($"{New}@1.1.0", package.ArchiveKey);
  }

  [Fact]
  public async Task Update_DryRun_ReportsTheReplacementAndChangesNothing() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    List<KeyValuePair<string, string>> before = WorkspaceSnapshot();

    // Act
    await _ws.Service.Update([], s_options with { DryRun = true });

    // Assert
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Update_NamedPackageOfAnotherRepositoryOfTheSameName_ReplacesThePackage() {
    // Arrange: another repository of the same name has the same identity (spec §10.2 rule 1).
    AddVersion("1.0.0", Old);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    const string other = "example.com/someone/Mod";
    _ws.AddTag(other, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(other, "1.0.0", "other/mod.dll"),
      ["file.dll"] = "other",
    });

    // Act
    await _ws.Service.Update([other], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(NewId, package.Identity);
    Assert.Equal(other, Assert.Single(package.Chain).Ref);
    Assert.Equal("other", _ws.Fs.File.ReadAllText(_ws.Path("other/mod.dll")));
    Assert.False(_ws.Exists("mod/mod.dll"));
  }

  [Fact]
  public async Task Update_NamedForkOfTheSameName_ReplacesThePackageWithTheFork() {
    // Arrange: a fork made after v1.0.0 has that tag at the same commit, and released its own
    // v1.1.0 naming the fork's path; it has the same identity (spec §10.2 rule 1).
    AddVersion("1.0.0", Old);
    AddFork();
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);

    // Act
    await _ws.Service.Update([Fork], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0"), (package.Identity, package.Version));
    AssertRecordedFrom(Fork, package);
  }

  [Fact]
  public async Task Install_ForkWhileOriginalIsExplicit_IsNotReportedAsTheSamePackage() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddFork();
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);

    // Act
    Exception ex = await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Install([Fork], s_options));

    // Assert: the fork is another package (its files clash with the installed one); lip must
    // not tell the user that updating the original installs the fork.
    Assert.DoesNotContain("already explicitly installed as", ex.Message);
    _ws.Ui.Verify(u => u.PrintInfo(It.Is<string>(m => m.Contains("is now"))), Times.Never);
  }

  [Fact]
  public async Task Install_OldPath_RequestsThePackageByThePathItsToothDeclares() {
    // Arrange
    AddVersion("1.0.0", New);

    // Act
    await _ws.Service.Install([Old], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(NewId, package.Identity);
    AssertRecordedFrom(New, package);
    Assert.Equal($"{New}@1.0.0", package.ArchiveKey);
    VerifyInfo($"{Old} redirects to {New}");
    Assert.Empty(_ws.Warnings);
  }

  [Fact]
  public async Task Install_OldPathWithoutRedirectInformation_IdentifiesThePackageByItsToothAndWarns() {
    // Arrange: lip cannot tell that the old path redirects (as lip v0.36.0 did not ask).
    AddVersion("1.0.0", New);
    _ws.Git.ReportsRedirects = false;

    // Act
    await _ws.Service.Install([Old], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal(NewId, package.Identity);
    AssertRecordedFrom(Old, package);
    Assert.Contains(_ws.Warnings, w => w.Contains($"declares the package path '{New}'"));
  }

  [Fact]
  public async Task Install_BothPaths_FailsAsOnePackage() {
    // Arrange
    AddVersion("1.0.0", New);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Old, New], s_options));
    Assert.Contains("Multiple requested packages", ex.Message);
  }

  [Theory]
  [InlineData(Old)]
  [InlineData(New)]
  public async Task Install_EitherPathWhileInstalledByTheOldPath_FailsAsAlreadyInstalled(
      string argument) {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    List<KeyValuePair<string, string>> before = WorkspaceSnapshot();

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([argument], s_options));
    Assert.Contains($"Cannot install package '{NewId}' because it is already", ex.Message);
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_NewPathWhileInstalledAsADependency_ReplacesTheDependency() {
    // Arrange: app depends on the old path; the package is installed as a dependency under the
    // identity of its old version.
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    AddApp($$"""{ "{{Old}}": ">=1.0.0" }""");
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);
    await _ws.Service.Install([App], s_options);
    await _ws.Service.Uninstall([Old], s_options);
    Assert.Equal(
        [(AppId, "1.0.0", true), (OldId, "1.0.0", false)],
        (await _ws.Lock()).Packages.Select(p => (p.Identity, p.Version, p.Explicit)).Order());

    // Act
    await _ws.Service.Install([New], s_options);

    // Assert
    Assert.Equal(
        [(AppId, "1.0.0", true), (NewId, "1.1.0", true)],
        (await _ws.Lock()).Packages.Select(p => (p.Identity, p.Version, p.Explicit)).Order());
    Assert.Equal("mod 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("mod/mod.dll")));
  }

  [Fact]
  public async Task Install_NoDependencies_ReplacesTheInstalledPackage() {
    // Arrange
    AddVersion("1.0.0", Old);
    AddVersion("1.1.0", New);
    await _ws.Service.Install([$"{Old}@1.0.0"], s_options);

    // Act
    await _ws.Service.Install([New], s_options with { NoDependencies = true });

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0"), (package.Identity, package.Version));
  }

  [Theory]
  [InlineData(Old)]
  [InlineData(New)]
  public async Task Install_DependencyOnEitherPath_IsSatisfiedByThePackageFromTheOther(
      string dependency) {
    // Arrange: the package is installed from one path; app depends on the other.
    AddVersion("1.0.0", New);
    string installedFrom = dependency == Old ? New : Old;
    await _ws.Service.Install([installedFrom], s_options);
    LockedPackage installed = Assert.Single((await _ws.Lock()).Packages);
    AddApp($$"""{ "{{dependency}}": ">=1.0.0" }""");

    // Act
    await _ws.Service.Install([App], s_options);

    // Assert: the installed package stays as it is (requested by the path its tooth.json
    // declares, whichever path it was installed by).
    LockFile lockFile = await _ws.Lock();
    Assert.Equal(2, lockFile.Packages.Count);
    LockedPackage kept = Assert.Single(lockFile.Packages, p => p.Identity == NewId);
    Assert.Equal(installed.ArchiveKey, kept.ArchiveKey);
    AssertRecordedFrom(New, kept);
  }

  [Fact]
  public async Task Install_DependencyOnTheNewPath_IsSatisfiedByThePackageOfV036FromTheOldPath() {
    // Arrange: lip v0.36.0 recorded the old path.
    AddVersion("1.0.0", New);
    _ws.Git.ReportsRedirects = false;
    await _ws.Service.Install([Old], s_options);
    _ws.Git.ReportsRedirects = true;
    LockedPackage installed = Assert.Single((await _ws.Lock()).Packages);
    AddApp($$"""{ "{{New}}": ">=1.0.0" }""");

    // Act
    await _ws.Service.Install([App], s_options);

    // Assert: the installed package stays as it is.
    LockedPackage kept = Assert.Single((await _ws.Lock()).Packages, p => p.Identity == NewId);
    Assert.Equal(installed.ArchiveKey, kept.ArchiveKey);
    AssertRecordedFrom(Old, kept);
  }

  [Fact]
  public async Task Update_V035ToothLockRecordingTheRequestedPath_ReplacesPackage() {
    // Arrange: lip v0.35.0 recorded the requested path as "tooth", whatever the tooth.json
    // declared; here the old path, which the tag already declares as the new one.
    AddVersion("1.0.0", New);
    string manifest = WorkspaceHarness.Tooth(Old, "1.0.0", "mod/mod.dll");
    _ws.AddFile("tooth_lock.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "packages": [
            {
              "files": ["mod/mod.dll"],
              "locked": true,
              "manifest": {{manifest}},
              "variant": ""
            }
          ]
        }
        """);
    _ws.AddFile("mod/mod.dll", "mod 1.0.0");
    LockedPackage migrated = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((OldId, Old, $"{Old}@1.0.0"),
        (migrated.Identity, migrated.Chain[0].Ref, migrated.ArchiveKey));
    AddVersion("1.1.0", New);

    // Act
    await _ws.Service.Update([], s_options);

    // Assert
    LockedPackage package = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal((NewId, "1.1.0"), (package.Identity, package.Version));
    Assert.Equal("mod 1.1.0", _ws.Fs.File.ReadAllText(_ws.Path("mod/mod.dll")));
    Assert.False(_ws.Exists(LockStore.LegacyFileName));
  }

  /// Asserts that a package is requested again (lip update) by the given ref, and that its
  /// archive comes from that ref's repository.
  private static void AssertRecordedFrom(string reference, LockedPackage package) {
    Assert.Equal(reference, Assert.Single(package.Chain).Ref);
    string spec = Ref.Parse(reference).Target;
    Assert.Equal(
        ArchiveProvider.GetRepoUrl(spec),
        Assert.IsType<GitArchiveIdentity>(package.Archive).RepoUrl);
  }

  /// A fork of the new repository: tag v1.0.0 at the commit of the original, and its own v1.1.0
  /// whose tooth.json names the fork's path.
  private void AddFork() {
    _ws.Git.CopyRef(New, Fork, "refs/tags/v1.0.0");
    AddVersion("1.1.0", Fork, repository: Fork);
  }

  /// Adds tag v&lt;version&gt; to a repository (the new one by default), whose tooth.json names
  /// the given path. It places mod/mod.dll and mod/config.json, which it preserves.
  private void AddVersion(
      string version, string tooth, string label = "", string repository = New) =>
      _ws.AddTag(repository, version, VersionFiles(version, tooth, label));

  private static Dictionary<string, string> VersionFiles(
      string version, string tooth, string label = "") => new() {
        ["tooth.json"] = $$"""
          {
            "format_version": 3,
            "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
            "tooth": "{{tooth}}",
            "version": "{{version}}",
            "variants": [
              {
                "label": "{{label}}",
                "assets": [
                  {
                    "type": "self",
                    "placements": [
                      { "type": "file", "src": "file.dll", "dest": "mod/mod.dll" },
                      { "type": "file", "src": "config.json", "dest": "mod/config.json" }
                    ]
                  }
                ],
                "preserve_files": ["mod/config.json"]
              }
            ]
          }
          """,
        ["file.dll"] = $"mod {version}",
        ["config.json"] = "{}",
      };

  private void AddApp(string dependencies) {
    _ws.AddTag(App, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(App, "1.0.0", "app/app.dll", dependencies),
      ["file.dll"] = "app",
    });
  }

  /// The files, directories and links of the workspace (lip may cache what it fetched).
  private List<KeyValuePair<string, string>> WorkspaceSnapshot() =>
      [.. _ws.Snapshot().Where(e => e.Key.StartsWith(_ws.Root, StringComparison.Ordinal))];

  private void VerifyInfo(string fragment) =>
      _ws.Ui.Verify(u => u.PrintInfo(It.Is<string>(m => m.Contains(fragment))), Times.Once);
}
