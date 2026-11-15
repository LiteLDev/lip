using System.Text.Json.Nodes;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

/// What lip list tells of an installed package beyond its identity and version: the source
/// that the state file records (protocol/state.json.md), and the package arguments made of it,
/// with which lip update makes the request of the package again (protocol/lip-update.md,
/// protocol/lip-workspace.md, "Operations") and lip pkg view shows what is installed
/// (protocol/lip-pkg.md).
public class InstalledPackageTests {
  private const string Lib = "example.com/lib";
  private const string Pkg = "example.com/pkg";
  private const string Url = "https://example.com/pkg.zip";
  private const string Commit = "0123456789abcdef0123456789abcdef01234567";

  private static readonly OperationOptions _options = new();

  private readonly WorkspaceHarness _ws = new();

  private static string LibPackageFile(string version) => WorkspaceHarness.LipToml(
      Lib, version, WorkspaceHarness.SelfAsset("lib.dll", "lib/lib.dll"));

  private static Dictionary<string, string> LibFiles(string version, string content) => new() {
    ["lip.toml"] = LibPackageFile(version),
    ["lib.dll"] = content,
  };

  private static Dictionary<string, string> PkgFiles(string version) => new() {
    ["lip.toml"] = WorkspaceHarness.LipToml(
        Pkg, version, WorkspaceHarness.SelfAsset("x.txt", "x.txt")),
    ["x.txt"] = version,
  };

  private async Task<InstalledPackage> Listed() => Assert.Single(await _ws.Service.List());

  [Fact]
  public async Task List_CarriesTheVariantTheSourceAndTheCommitOfTheStateFile() {
    // Arrange
    string commit = _ws.Git.AddBranch(Lib, "main", LibFiles("1.2.0", "head"));
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);
    _ws.Restart();
    int listedRefs = _ws.Git.ListedRefs.Count;
    int listedTrees = _ws.Git.ListedTrees.Count;

    // Act
    InstalledPackage package = await Listed();

    // Assert: read of the state file alone, without the network.
    Assert.Equal(
        (Lib, "1.2.0", true, "", $"{Lib}@branch:main", commit),
        (package.Identity, package.Version, package.Explicit, package.Variant, package.Source,
            package.Commit));
    Assert.Equal(
        (listedRefs, listedTrees), (_ws.Git.ListedRefs.Count, _ws.Git.ListedTrees.Count));
  }

  /// protocol/lip-update.md: giving the source without its ref would move the package back to
  /// the version tags, which the identity of the package does.
  [Fact]
  public async Task UpdateArgument_OfABranch_KeepsThePackageOnItsBranch() {
    // Arrange
    _ws.Git.AddBranch(Lib, "main", LibFiles("1.2.0", "head 1"));
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);
    _ws.AddTag(Lib, "9.0.0", LibFiles("9.0.0", "tag"));
    string second = _ws.Git.AddBranch(Lib, "main", LibFiles("1.2.0", "head 2"));
    _ws.Restart();
    InstalledPackage package = await Listed();

    // Act
    await _ws.Service.Update([package.UpdateArgument], _options);

    // Assert
    Assert.Equal($"{Lib}@branch:main", package.UpdateArgument);
    InstalledPackage updated = await Listed();
    Assert.Equal(
        ("1.2.0", $"{Lib}@branch:main", second),
        (updated.Version, updated.Source, updated.Commit));
    Assert.Equal("head 2", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  /// The identity of a package of a URL names no repository, so lip update of it fails; its
  /// source is downloaded again.
  [Fact]
  public async Task UpdateArgument_OfAUrl_DownloadsItAgain() {
    // Arrange
    _ws.AddDownload(Url, WorkspaceHarness.Zip(PkgFiles("1.0.0")));
    await _ws.Service.Install([Url], _options);
    _ws.AddDownload(Url, WorkspaceHarness.Zip(PkgFiles("2.0.0")));
    _ws.Restart();
    InstalledPackage package = await Listed();

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Update([package.Identity], _options with { DryRun = true }));
    await _ws.Service.Update([package.UpdateArgument], _options);

    Assert.Equal(Url, package.UpdateArgument);
    InstalledPackage updated = await Listed();
    Assert.Equal((Pkg, "2.0.0", Url), (updated.Identity, updated.Version, updated.Source));
    Assert.Equal("2.0.0", _ws.Fs.File.ReadAllText(_ws.Path("x.txt")));
  }

  [Fact]
  public async Task UpdateArgument_OfALocalPath_ReadsItAgain() {
    // Arrange
    string path = _ws.AddZip("packages/pkg.zip", PkgFiles("1.0.0"));
    await _ws.Service.Install([path], _options);
    _ws.AddZip("packages/pkg.zip", PkgFiles("2.0.0"));
    _ws.Restart();
    InstalledPackage package = await Listed();

    // Act
    await _ws.Service.Update([package.UpdateArgument], _options);

    // Assert: the argument is the path, not the file: URI that the state file records.
    Assert.Equal(path, package.UpdateArgument);
    Assert.StartsWith("file:", package.Source);
    Assert.Equal("2.0.0", (await Listed()).Version);
    Assert.Equal(package.Source, (await Listed()).Source);
  }

  [Fact]
  public async Task ViewArgument_OfABranch_ShowsTheCommitThatIsInstalled() {
    // Arrange: the branch has moved on since.
    string commit = _ws.Git.AddBranch(Lib, "main", LibFiles("1.2.0", "head 1"));
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);
    _ws.Git.AddBranch(Lib, "main", LibFiles("1.3.0", "head 2"));
    _ws.Restart();
    InstalledPackage package = await Listed();

    // Act
    JsonNode result = JsonNode.Parse(await _ws.Service.View(package.ViewArgument))!;

    // Assert
    Assert.Equal($"{Lib}@rev:{commit}", package.ViewArgument);
    Assert.Equal("1.2.0", (string?)result["manifest"]!["version"]);
  }

  [Fact]
  public async Task ViewArgument_OfALocalPath_ShowsThatPath() {
    // Arrange
    string path = _ws.AddZip("packages/pkg.zip", PkgFiles("1.0.0"));
    await _ws.Service.Install([path], _options);
    InstalledPackage package = await Listed();

    // Act
    JsonNode result = JsonNode.Parse(await _ws.Service.View(package.ViewArgument))!;

    // Assert
    Assert.Equal(path, package.ViewArgument);
    Assert.Equal((Pkg, "1.0.0"), (
        (string?)result["manifest"]!["id"], (string?)result["manifest"]!["version"]));
  }

  [Fact]
  public async Task ViewArgument_OfTheVersionTags_ShowsTheInstalledVersion_NotTheNewest() {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", LibFiles("1.0.0", "1"));
    await _ws.Service.Install([Lib], _options);
    _ws.AddTag(Lib, "2.0.0", LibFiles("2.0.0", "2"));
    _ws.Restart();
    InstalledPackage package = await Listed();

    // Act
    JsonNode result = JsonNode.Parse(await _ws.Service.View(package.ViewArgument))!;

    // Assert
    Assert.Equal($"{Lib}@1.0.0", package.ViewArgument);
    Assert.Equal(Lib, package.UpdateArgument);
    Assert.Equal("1.0.0", (string?)result["manifest"]!["version"]);
  }

  /// A mod that was installed at one version is updated to the highest version tag, by its
  /// own update and by the update of all, which lip update without operands does not do.
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Update_OfAModInstalledAtOneVersion_MovesItToTheHighestVersion(bool all) {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", LibFiles("1.0.0", "1"));
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);
    _ws.AddTag(Lib, "1.5.0", LibFiles("1.5.0", "1.5"));
    _ws.AddTag(Lib, "2.0.0", LibFiles("2.0.0", "2"));
    _ws.Restart();
    InstalledPackage package = await Listed();

    // Act: without operands nothing changes; then as the interface updates.
    OperationResult kept = await _ws.Service.Update([], _options with { DryRun = true });
    OperationResult plan = all
        ? await _ws.Service.UpdateMods(_options with { DryRun = true })
        : await _ws.Service.Update([package.UpdateArgument], _options with { DryRun = true });
    _ws.Restart();
    OperationResult result = all
        ? await _ws.Service.UpdateMods(_options)
        : await _ws.Service.Update([package.UpdateArgument], _options);

    // Assert
    Assert.Equal(($"{Lib}@1.0.0", Lib), (package.Source, package.UpdateArgument));
    Assert.Equal(OperationResult.Unchanged, kept);
    Assert.Equal((OperationResult.Changed, OperationResult.Changed), (plan, result));
    InstalledPackage updated = await Listed();
    Assert.Equal(("2.0.0", Lib, true), (updated.Version, updated.Source, updated.Explicit));
    Assert.Equal("2", _ws.Fs.File.ReadAllText(_ws.Path("lib/lib.dll")));
  }

  /// A mod that was installed at one version, which is the highest: its update, as the
  /// interface asks for it, would change nothing but the source that the state file records,
  /// which the result says, so that the interface need not ask the user to confirm it.
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Update_OfAModInstalledAtItsHighestVersion_WouldOnlyRecordAnotherSource(
      bool all) {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", LibFiles("1.0.0", "1"));
    await _ws.Service.Install([$"{Lib}@1.0.0"], _options);
    _ws.Restart();
    InstalledPackage package = await Listed();
    string state = _ws.Fs.File.ReadAllText(_ws.LockPath);

    // Act
    OperationResult plan = all
        ? await _ws.Service.UpdateMods(_options with { DryRun = true })
        : await _ws.Service.Update([package.UpdateArgument], _options with { DryRun = true });

    // Assert
    Assert.Equal(
        new OperationResult(Changes: true, OnlyStateFile: true, OnlySources: true), plan);
    Assert.Equal(state, _ws.Fs.File.ReadAllText(_ws.LockPath));
    Assert.Equal($"{Lib}@1.0.0", (await Listed()).Source);
  }

  /// One plan and its result name a package one way: as a package argument names it, so a
  /// package of github.com/LiteLDev by its short name in every line, and any other by its
  /// identity; and the operation says "Installing" of what its dry run said "would install".
  [Fact]
  public async Task Messages_OfAPlanAndOfItsResult_NameEachPackageOneWay() {
    // Arrange
    const string Money = "github.com/LiteLDev/Money";
    _ws.AddTag(Money, "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          Money, "1.0.0", WorkspaceHarness.SelfAsset("money.dll", "money/money.dll")),
      ["money.dll"] = "money",
    });
    _ws.AddTag(Lib, "1.0.0", LibFiles("1.0.0", "1"));

    // Act
    await _ws.Service.Install([Money, Lib], _options with { DryRun = true });
    List<string> plan = [.. _ws.Infos];
    _ws.Infos.Clear();
    await _ws.Service.Install([Money, Lib], _options);
    List<string> result = [.. _ws.Infos];
    _ws.Infos.Clear();
    await _ws.Service.Uninstall([Money], _options with { DryRun = true });
    await _ws.Service.Uninstall([Money], _options);

    // Assert
    Assert.Contains("Resolving Money...", plan);
    Assert.Contains($"Resolving {Lib}...", plan);
    Assert.Contains("Dry run: would install Money 1.0.0 (1 file)", plan);
    Assert.Contains($"Dry run: would install {Lib} 1.0.0 (1 file)", plan);
    Assert.Contains(" Installing Money 1.0.0".TrimStart(), result);
    Assert.Contains($"Installing {Lib} 1.0.0", result);
    Assert.Contains("Dry run: would uninstall Money 1.0.0 (1 file)", _ws.Infos);
    Assert.Contains("Uninstalling Money 1.0.0", _ws.Infos);
    Assert.All(
        [.. plan, .. result, .. _ws.Infos],
        line => {
          Assert.DoesNotContain(Money, line);
          Assert.DoesNotContain("package '", line);
        });
  }

  /// lip pkg view writes JSON that reads as the package file does: only what JSON requires
  /// is escaped.
  [Fact]
  public async Task View_WritesRangesAndOtherTextAsTheyAre() {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          Lib, "1.0.0",
          """
          "deps": { "example.com/dep": ">=1.0.0 <2.0.0" },
          "assets": [ { "type": "self",
            "place": [ { "src": "a&b+c 中文.dll", "dest": "x/a.dll" } ] } ]
          """),
    });

    // Act
    string json = await _ws.Service.View(Lib);

    // Assert
    Assert.Contains("\">=1.0.0 <2.0.0\"", json);
    Assert.Contains("a&b+c 中文.dll", json);
    Assert.DoesNotContain("\\u", json);
    JsonNode result = JsonNode.Parse(json)!;
    Assert.Equal(["manifest", "versions"], result.AsObject().Select(m => m.Key));
  }

  [Fact]
  public async Task UpdateMods_KeepsAModWhoseLocalPathIsGone_AndUpdatesTheOthers() {
    // Arrange: a mod of a local path, one of a branch and one of a URL.
    string path = _ws.AddZip("packages/pkg.zip", PkgFiles("1.0.0"));
    await _ws.Service.Install([path], _options);
    _ws.Git.AddBranch(Lib, "main", LibFiles("1.2.0", "head 1"));
    await _ws.Service.Install([$"{Lib}@branch:main"], _options);
    const string other = "example.com/other";
    Dictionary<string, string> Other(string version) => new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          other, version, WorkspaceHarness.SelfAsset("o.txt", "o.txt")),
      ["o.txt"] = version,
    };
    _ws.AddDownload(Url, WorkspaceHarness.Zip(Other("1.0.0")));
    await _ws.Service.Install([Url], _options);
    _ws.Fs.File.Delete(path);
    _ws.AddTag(Lib, "9.0.0", LibFiles("9.0.0", "tag"));
    string head = _ws.Git.AddBranch(Lib, "main", LibFiles("1.2.0", "head 2"));
    _ws.AddDownload(Url, WorkspaceHarness.Zip(Other("2.0.0")));
    _ws.Restart();
    _ws.Warnings.Clear();

    // Act
    await _ws.Service.UpdateMods(_options);

    // Assert
    Dictionary<string, InstalledPackage> packages =
        (await _ws.Service.List()).ToDictionary(p => p.Identity);
    Assert.Equal("1.0.0", packages[Pkg].Version);
    Assert.Equal("1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("x.txt")));
    Assert.Contains(_ws.Warnings, w => w.Contains(Pkg, StringComparison.Ordinal)
        && w.Contains("does not exist any more", StringComparison.Ordinal));
    Assert.Equal(
        ($"{Lib}@branch:main", head), (packages[Lib].Source, packages[Lib].Commit));
    Assert.Equal(("2.0.0", Url), (packages[other].Version, packages[other].Source));
  }

  [Fact]
  public async Task UpdateMods_WithoutModsToUpdate_ChangesNothing() {
    // Arrange: the game alone, which is no operand of lip update.
    _ws.AddGame();
    string before = _ws.Fs.File.ReadAllText(_ws.LockPath);

    // Act
    OperationResult result = await _ws.Service.UpdateMods(_options);

    // Assert
    Assert.Equal(OperationResult.Unchanged, result);
    Assert.Equal(before, _ws.Fs.File.ReadAllText(_ws.LockPath));
  }

  /// What is made of a recorded source, whatever the system: a local path is tested above.
  [Theory]
  // The version tags of a repository, with a variant.
  [InlineData(
      "example.com/lib#cuda", Commit, "example.com/lib#cuda", "example.com/lib#cuda@1.2.0",
      "example.com/lib#cuda", null)]
  // A version: the update leaves it for the version tags, and nothing tells it from the
  // repository.
  [InlineData(
      "example.com/lib@1.2.0", Commit, "example.com/lib", "example.com/lib@1.2.0",
      "example.com/lib", null)]
  [InlineData(
      "example.com/lib#cuda@branch:main", Commit, "example.com/lib#cuda@branch:main",
      "example.com/lib#cuda@rev:" + Commit, "example.com/lib#cuda", "branch:main")]
  // A branch whose commit is not known is shown as the branch is now.
  [InlineData(
      "example.com/lib@branch:main", null, "example.com/lib@branch:main",
      "example.com/lib@branch:main", "example.com/lib", "branch:main")]
  // A tag may have moved too.
  [InlineData(
      "example.com/lib@tag:nightly", Commit, "example.com/lib@tag:nightly",
      "example.com/lib@rev:" + Commit, "example.com/lib", "tag:nightly")]
  [InlineData(
      "example.com/lib@rev:" + Commit, Commit, "example.com/lib@rev:" + Commit,
      "example.com/lib@rev:" + Commit, "example.com/lib", "rev:" + Commit)]
  [InlineData(
      "https://example.com/a.zip#cuda", null, "https://example.com/a.zip#cuda",
      "https://example.com/a.zip#cuda", null, "https://example.com/a.zip")]
  // The game, which is of no commit, at its version.
  [InlineData(
      "github.com/LiteLDev/bds#linux", null, "github.com/LiteLDev/bds#linux",
      "github.com/LiteLDev/bds#linux@1.2.0", "github.com/LiteLDev/bds#linux", null)]
  // What is no source: the identity stands in for it.
  [InlineData(
      "not a source", null, "example.com/lib#cuda", "example.com/lib#cuda@1.2.0",
      "example.com/lib#cuda", null)]
  public void Arguments_AreMadeOfTheRecordedSource(
      string source, string? commit, string update, string view, string? repository,
      string? note) {
    // Arrange
    InstalledPackage package = new("example.com/lib#cuda", "1.2.0", true) {
      Variant = "cuda",
      Source = source,
      Commit = commit,
    };

    // Act & Assert
    Assert.Equal(
        (update, view, repository, note),
        (package.UpdateArgument, package.ViewArgument, package.Repository, package.SourceNote));
  }

  [Fact]
  public void LocalPath_IsNamedByItsPath_WithTheVariant() {
    // Arrange
    string path = OperatingSystem.IsWindows() ? @"C:\my packages\a#b.zip" : "/my packages/a#b.zip";
    InstalledPackage package = new("example.com/pkg#cuda", "1.0.0", true) {
      Variant = "cuda",
      Source = Ref.FromFile(path, "cuda").ToString(),
    };

    // Act & Assert
    Assert.Equal(
        ($"{path}#cuda", $"{path}#cuda", null, path),
        (package.UpdateArgument, package.ViewArgument, package.Repository, package.SourceNote));
  }

  [Fact]
  public void Listed_PutsTheModsOfTheUserFirst_ThenDependencies_ThenTheGameAndTheLoader() {
    // Arrange: in an installation order, and in another, as after an update of a mod.
    InstalledPackage[] packages = [
      new("example.com/other-dependency", "1.0.0", false),
      new("github.com/liteldev/levilamina", "1.0.0", true),
      new("example.com/Second", "1.0.0", true),
      new("example.com/dependency", "1.0.0", false),
      new("github.com/LiteLDev/bds#linux", "1.21.0", true),
      new("example.com/first", "1.0.0", true),
      new("example.com/third", "1.0.0", true),
    ];
    InstalledPackage[] reordered = [.. packages.Reverse()];

    // Act
    IReadOnlyList<InstalledPackage> listed = InstalledPackage.Listed(packages);

    // Assert: each group by identity, ignoring case, whatever the order of the state file.
    Assert.Equal(
        [
          "example.com/first", "example.com/Second", "example.com/third",
          "example.com/dependency", "example.com/other-dependency",
          "github.com/LiteLDev/bds#linux", "github.com/liteldev/levilamina",
        ],
        listed.Select(p => p.Identity));
    Assert.Equal(listed, InstalledPackage.Listed(reordered));
    Assert.Equal(
        [(true, false), (false, true)], listed.TakeLast(2).Select(p => (p.IsGame, p.IsLoader)));
  }
}
