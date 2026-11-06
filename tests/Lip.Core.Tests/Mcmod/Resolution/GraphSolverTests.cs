using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Resolution;

/// The package set S (protocol/workspace.md §3) and the installation order (§5).
public class GraphSolverTests {
  private const string LeviLamina = "github.com/LiteLDev/LeviLamina";

  private static readonly LipEnvironment s_environment = TestCandidates.Environment;

  [Fact]
  public async Task Solve_SingleRequest_PicksFirstCandidate() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/a/a", ["2.0.0", "1.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/a/a@2.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_LeviLaminaWithManyVersionsAndTenMods_PicksNewestCommonVersion() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions(LeviLamina, [
      .. Enumerable.Range(0, 3).SelectMany(major => Enumerable.Range(0, 100)
          .Select(minor => $"{major}.{minor}.0")).Reverse(),
    ]);

    List<PackageRequest> requests = [];
    for (int i = 0; i < 10; i++) {
      string spec = $"github.com/mods/mod{i}";
      resolver.Add(Package(spec, "2.0.0",
          dependencies: [Dependency(LeviLamina, $">=1.{i * 5}.0 <2.{(i * 5) + 50}.0")]));
      resolver.Add(Package(spec, "1.0.0", dependencies: [Dependency(LeviLamina)]));
      requests.Add(Request(spec));
    }

    // The newest Old needs an ancient LeviLamina, so Old must fall back to 1.0.0.
    resolver.Add(Package("github.com/mods/old", "2.0.0",
        dependencies: [Dependency(LeviLamina, "<1.0.0")]));
    resolver.Add(Package("github.com/mods/old", "1.0.0",
        dependencies: [Dependency(LeviLamina, ">=2.0.0 <3.0.0")]));
    requests.Add(Request("github.com/mods/old"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(requests, [], s_environment)).Order;

    // Assert
    Assert.Equal($"{LeviLamina}@2.49.0", Names(result)[0]);
    Assert.Contains("github.com/mods/old@1.0.0", Names(result));
    Assert.All(Enumerable.Range(0, 10),
        i => Assert.Contains($"github.com/mods/mod{i}@2.0.0", Names(result)));
    Assert.Equal(12, result.Count);
    Assert.Equal(resolver.Calls.Count, resolver.Calls.Distinct().Count());
  }

  [Fact]
  public async Task Solve_DependencyOfLegacyEntry_IsResolvedWithClientCompat() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/legacy/pkg", "1.0.0", legacy: true,
        dependencies: [Dependency("github.com/new/lib#client", compat: true)]));
    resolver.Add(Package("github.com/new/lib", "4.0.0", source: "github.com/new/lib#client"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/legacy/pkg")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/new/lib@4.0.0", "github.com/legacy/pkg@1.0.0"], Names(result));
    Assert.Contains(resolver.Calls, c => c.Ref == "github.com/new/lib#client" && c.Compat);
    Assert.Contains(resolver.Calls, c => c.Ref == "github.com/legacy/pkg" && !c.Compat);
  }

  // Identities (protocol/package.md §4, protocol/workspace.md §3 rule 3)

  [Fact]
  public async Task Solve_DefaultVariantDependsOnItsVariants_SolvesThreeIdentities() {
    // Arrange: the variants of one package file have the identities id and id#variant.
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/t/t", "1.0.0",
        dependencies: [Dependency("github.com/t/t#b"), Dependency("github.com/t/t#a")]));
    resolver.Add(Package("github.com/t/t", "1.0.0", variant: "a"));
    resolver.Add(Package("github.com/t/t", "1.0.0", variant: "b"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/t/t")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/t/t#a@1.0.0", "github.com/t/t#b@1.0.0", "github.com/t/t@1.0.0"],
        Names(result));
  }

  [Fact]
  public async Task SolveGraph_TwoVariantsRequested_SolvesBothWithSharedDependency() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/t/t", "1.0.0", variant: "a",
        dependencies: [Dependency("github.com/l/l")]));
    resolver.Add(Package("github.com/t/t", "1.0.0", variant: "b",
        dependencies: [Dependency("github.com/l/l")]));
    resolver.AddVersions("github.com/l/l", ["1.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    SolvedGraph graph = await solver.SolveGraph(
        [Request("github.com/t/t#a"), Request("github.com/t/t#b")], [], s_environment);

    // Assert
    Assert.Equal(["github.com/l/l@1.0.0", "github.com/t/t#a@1.0.0", "github.com/t/t#b@1.0.0"],
        Names(graph.Order));
    Assert.Equal([1, 2], graph.RequestSatisfiers);
    Assert.Empty(graph.Dependencies[0]);
    Assert.Equal([0], graph.Dependencies[1]);
    Assert.Equal([0], graph.Dependencies[2]);
  }

  [Theory]
  [InlineData("github.com/x/x")]
  [InlineData("github.com/X/x")]
  public async Task Solve_TwoSourcesOfSameIdentity_Fails(string otherId) {
    // Arrange: a URL whose package file names the package id of a remote repository.
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/x/x", ["1.0.0"]);
    resolver.Add(Package(otherId, "1.0.0", source: "https://example.com/x.zip"));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph(
            [Request("github.com/x/x"), Request("https://example.com/x.zip")], [], s_environment));

    // Assert
    Assert.Contains($"{otherId}@1.0.0 (from https://example.com/x.zip@1.0.0) has the same "
        + "identity as github.com/x/x@1.0.0 (from github.com/x/x@1.0.0)", exception.Message);
  }

  [Fact]
  public async Task Solve_SameIdentityClash_BacktracksToOtherVersion() {
    // Arrange: A@2 needs a URL package of the identity of B, which is requested at 2.0.0 only;
    // A@1 does not.
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "2.0.0",
        dependencies: [Dependency("https://example.com/b.zip")]));
    resolver.Add(Package("github.com/a/a", "1.0.0"));
    resolver.Add(Package("github.com/b/b", "1.0.0", source: "https://example.com/b.zip"));
    resolver.AddVersions("github.com/b/b", ["2.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a"), Request("github.com/b/b")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/a/a@1.0.0", "github.com/b/b@2.0.0"], Names(result));
  }

  // Mod names (protocol/package.md §4, protocol/workspace.md §3 rule 3)

  [Fact]
  public async Task Solve_ModNameClash_BacktracksToOlderVersion() {
    // Arrange: A@2 is a mod named like B's mod (ignoring case); A@1 has its own mod name.
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "2.0.0", mod: "Shared"));
    resolver.Add(Package("github.com/a/a", "1.0.0", mod: "A"));
    resolver.Add(Package("github.com/b/b", "1.0.0", mod: "sHARED"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a"), Request("github.com/b/b")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/a/a@1.0.0", "github.com/b/b@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_ModNameClashDeepInGraph_BacktracksEarlierChoice() {
    // Arrange: X@2 needs Y, a mod named like the requested Z; X falls back to 1.
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/x/x", "2.0.0", dependencies: [Dependency("github.com/y/y")]));
    resolver.Add(Package("github.com/x/x", "1.0.0"));
    resolver.Add(Package("github.com/y/y", "1.0.0", mod: "Z"));
    resolver.Add(Package("github.com/z/z", "1.0.0", mod: "z"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/x/x"), Request("github.com/z/z")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/x/x@1.0.0", "github.com/z/z@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_ModNameClashUnavoidable_ListsBlockedCandidates() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "1.0.0", mod: "Foo"));
    resolver.Add(Package("github.com/b/b", "1.0.0", mod: "FOO"));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph(
            [Request("github.com/a/a"), Request("github.com/b/b")], [], s_environment));

    // Assert
    Assert.Contains("You requested github.com/b/b (any version), but no version of it fits: "
        + "github.com/b/b@1.0.0 (from github.com/b/b@1.0.0) is a mod of the same mod name 'FOO' "
        + "as github.com/a/a@1.0.0 (from github.com/a/a@1.0.0).", exception.Message);
  }

  [Fact]
  public async Task Solve_ModAmongFilePackages_DownloadsOnlyItsMcmodAssetOnce() {
    // Arrange: only mods have mod names, read from their asset of type "mcmod".
    FakeResolver resolver = new();
    List<string> downloads = [];
    resolver.Add(Package("github.com/a/a", "1.0.0", mod: "Same", downloads: downloads));
    resolver.Add(Package("github.com/b/b", "1.0.0"));
    resolver.Add(Package("github.com/c/c", "1.0.0"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a"), Request("github.com/b/b"), Request("github.com/c/c")],
        [],
        s_environment)).Order;

    // Assert
    Assert.Equal(3, result.Count);
    Assert.Equal(["https://example.com/github.com/a/a/1.0.0.mcmod"], downloads);
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("""{ "entry": "x.dll" }""")]
  [InlineData("""{ "name": "bad name" }""")]
  [InlineData("""{ "name": 1 }""")]
  [InlineData("""[ "name" ]""")]
  [InlineData("not json")]
  public async Task Solve_InvalidModManifest_BlocksCandidate(string manifest) {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "2.0.0", mcmod: manifest == "missing"
        ? WorkspaceHarness.Zip(new() { ["A.dll"] = "" })
        : WorkspaceHarness.Zip(new() { ["manifest.json"] = manifest })));
    resolver.Add(Package("github.com/a/a", "1.0.0", mod: "A"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/a/a@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_OnlyCandidateHasNoModManifest_FailsNamingIt() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "1.0.0",
        mcmod: WorkspaceHarness.Zip(new() { ["A.dll"] = "" })));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph(
            [Request("github.com/a/a")], [], s_environment));

    // Assert
    Assert.Contains("Cannot install github.com/a/a 1.0.0: its asset of type \"mcmod\" has no"
        + " manifest.json", exception.Message);
  }

  [Fact]
  public async Task Solve_McmodAssetCannotBeObtained_BlocksCandidate() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "2.0.0", mod: "A", unavailable: true));
    resolver.Add(Package("github.com/a/a", "1.0.0", mod: "A"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/a/a@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_InstalledModOfSameModName_BlocksCandidate() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "2.0.0", mod: "Taken"));
    resolver.Add(Package("github.com/a/a", "1.0.0", mod: "A"));
    Candidate installed = Package("github.com/other/other", "1.0.0", mod: "taken");
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a")], [installed], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/a/a@1.0.0", "github.com/other/other@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_InstalledModsOfSameModName_Throws() {
    // Arrange
    Candidate a = Package("github.com/a/a", "1.0.0", mod: "Same");
    Candidate b = Package("github.com/b/b", "1.0.0", mod: "same");
    GraphSolver solver = new(new FakeResolver());

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph([], [a, b], s_environment));

    // Assert
    Assert.Contains("are mods of the same mod name", exception.Message);
  }

  [Fact]
  public async Task Solve_InstalledPackagesOfSameIdentity_Throws() {
    // Arrange
    Candidate a = Package("github.com/a/a", "1.0.0");
    Candidate b = Package("github.com/A/a", "2.0.0");
    GraphSolver solver = new(new FakeResolver());

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph([], [a, b], s_environment));

    // Assert
    Assert.Contains("have the same identity", exception.Message);
  }

  // Installed packages

  [Fact]
  public async Task Solve_InstalledWithSameIdentity_BlocksRequest() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/x/x", ["2.0.0"]);
    Candidate installed = Package("github.com/X/x", "1.0.0");
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph(
            [Request("github.com/x/x")], [installed], s_environment));

    // Assert
    Assert.Contains("github.com/x/x@2.0.0 (from github.com/x/x@2.0.0) has the same identity as "
        + "github.com/X/x@1.0.0 (from github.com/X/x@1.0.0) (installed)", exception.Message);
  }

  [Fact]
  public async Task Solve_InstalledSamePackage_SatisfiesRequestAndInstalledDependencies() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/x/x", ["2.0.0", "1.0.0"]);
    resolver.AddVersions("github.com/dep/dep", ["1.0.0"]);
    Candidate installedX = Package("github.com/x/x", "1.0.0");
    Candidate installedP = Package("github.com/p/p", "1.0.0",
        dependencies: [Dependency("github.com/dep/dep")]);
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/x/x")], [installedX, installedP], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/dep/dep@1.0.0", "github.com/p/p@1.0.0", "github.com/x/x@1.0.0"],
        Names(result));
    Assert.Same(installedX, result[2]);
  }

  [Fact]
  public async Task Solve_SamePackageSatisfiesSeveralDependencies_AddsItOnce() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/a/a", "1.0.0",
        dependencies: [Dependency(LeviLamina), Dependency(LeviLamina.ToLowerInvariant())]));
    resolver.AddVersions(LeviLamina, ["1.0.0"]);
    resolver.Add(Package(LeviLamina, "1.0.0", source: LeviLamina.ToLowerInvariant()));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/a/a"), Request(LeviLamina)], [], s_environment)).Order;

    // Assert
    Assert.Equal([$"{LeviLamina}@1.0.0", "github.com/a/a@1.0.0"], Names(result));
  }

  // Refs: stand-in (protocol/workspace.md §3)

  [Fact]
  public async Task Solve_DependencyWithoutRefBeforeRef_PackageOfRefSatisfiesBoth() {
    // Arrange: A requires S (any version) and B; B requires S at branch main. The package at
    // the branch stands in for the versions of S, so it satisfies both.
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/s/s", ["2.0.0", "1.0.0"]);
    resolver.Add(Package("github.com/s/s", "2.5.0", source: "github.com/s/s@branch:main"));
    resolver.Add(Package("github.com/a/a", "1.0.0",
        dependencies: [Dependency("github.com/s/s"), Dependency("github.com/b/b")]));
    resolver.Add(Package("github.com/b/b", "1.0.0",
        dependencies: [Dependency("github.com/s/s@branch:main")]));
    GraphSolver solver = new(resolver);

    // Act
    SolvedGraph graph = await solver.SolveGraph(
        [Request("github.com/a/a")], [], s_environment);

    // Assert
    Assert.Equal(["github.com/s/s@2.5.0", "github.com/b/b@1.0.0", "github.com/a/a@1.0.0"],
        Names(graph.Order));
    Assert.Equal(new[] { 0, 1 }, graph.Dependencies[2].Order());
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Solve_RequestsWithAndWithoutRef_PackageOfRefStandsIn(bool refFirst) {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/s/s", ["2.0.0", "1.0.0"]);
    resolver.Add(Package("github.com/s/s", "2.5.0", source: "github.com/s/s@rev:"
        + new string('a', 40)));
    PackageRequest pinned = Request($"github.com/s/s@rev:{new string('a', 40)}");
    PackageRequest plain = Request("github.com/s/s");
    GraphSolver solver = new(resolver);

    // Act
    SolvedGraph graph = await solver.SolveGraph(
        refFirst ? [pinned, plain] : [plain, pinned], [], s_environment);

    // Assert
    Assert.Equal(["github.com/s/s@2.5.0"], Names(graph.Order));
    Assert.Equal([0, 0], graph.RequestSatisfiers);
  }

  [Fact]
  public async Task Solve_DependencyWithoutRefOutsideVersionOfRef_Fails() {
    // Arrange: as above, but A needs S below 2.0.0, which the package at the branch is not.
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/s/s", ["2.0.0", "1.0.0"]);
    resolver.Add(Package("github.com/s/s", "2.5.0", source: "github.com/s/s@branch:main"));
    resolver.Add(Package("github.com/a/a", "1.0.0",
        dependencies: [Dependency("github.com/s/s", "<2.0.0"), Dependency("github.com/b/b")]));
    resolver.Add(Package("github.com/b/b", "1.0.0",
        dependencies: [Dependency("github.com/s/s@branch:main")]));
    GraphSolver solver = new(resolver);

    // Act & Assert
    await Assert.ThrowsAsync<DependencyResolutionException>(() => solver.SolveGraph(
        [Request("github.com/a/a")], [], s_environment));
  }

  [Fact]
  public async Task Solve_TwoRefsOfSameRepository_DoNotStandInForEachOther() {
    // Arrange: a source with a ref is satisfied only by its own candidates.
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/s/s", "2.5.0", source: "github.com/s/s@branch:main"));
    resolver.Add(Package("github.com/s/s", "1.0.0", source: "github.com/s/s@1.0.0"));
    GraphSolver solver = new(resolver);

    // Act & Assert
    await Assert.ThrowsAsync<DependencyResolutionException>(() => solver.SolveGraph(
        [Request("github.com/s/s@branch:main"), Request("github.com/s/s@1.0.0")],
        [],
        s_environment));
  }

  [Fact]
  public async Task Solve_PackageOfRef_DoesNotStandInForOtherVariant() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/s/s", "2.5.0", source: "github.com/s/s@branch:main"));
    resolver.Add(Package("github.com/s/s", "2.0.0", variant: "lua"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/s/s@branch:main"), Request("github.com/s/s#lua")],
        [],
        s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/s/s@2.5.0", "github.com/s/s#lua@2.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_InstalledPackageOfRef_StandsInForClientCompatDependency() {
    // Arrange: a tooth.json dependency on t#client names the variant "" of the package t, which
    // has a package file (protocol/compatibility.md §2), so the installed t from a branch
    // stands in for it.
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/t/t", "1.0.0", source: "github.com/t/t#client"));
    resolver.Add(Package("github.com/r/r", "1.0.0", legacy: true,
        dependencies: [Dependency("github.com/t/t#client", compat: true)]));
    Candidate installed = Package("github.com/t/t", "2.5.0", source: "github.com/t/t@branch:main");
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/r/r")], [installed], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/t/t@2.5.0", "github.com/r/r@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_InstalledPackageOfRef_DoesNotStandInForClientVariantWithoutCompat() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add(Package("github.com/t/t", "1.0.0", variant: "client"));
    resolver.Add(Package("github.com/r/r", "1.0.0",
        dependencies: [Dependency("github.com/t/t#client")]));
    Candidate installed = Package("github.com/t/t", "2.5.0", source: "github.com/t/t@branch:main");
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/r/r")], [installed], s_environment)).Order;

    // Assert
    Assert.Equal(["github.com/t/t@2.5.0", "github.com/t/t#client@1.0.0", "github.com/r/r@1.0.0"],
        Names(result));
  }

  // Failures

  [Fact]
  public async Task Solve_Unsatisfiable_ThrowsWithFailingDependency() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions(LeviLamina, ["1.2.0", "1.1.0"]);
    resolver.Add(Package("github.com/mods/mod", "1.0.0",
        dependencies: [Dependency(LeviLamina, ">=99.0.0")]));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph(
            [Request("github.com/mods/mod")], [], s_environment));

    // Assert
    Assert.Contains($"github.com/mods/mod@1.0.0 requires {LeviLamina} >=99.0.0, but no version of "
        + "it is available", exception.Message);
  }

  [Fact]
  public async Task Solve_ManyPackagesAndVersions_CompletesQuickly() {
    // Arrange: 50 packages with 100 versions each; every version depends on the next two
    // packages with a range excluding their newest 10 versions.
    FakeResolver resolver = new();
    for (int i = 0; i < 50; i++) {
      string spec = $"github.com/p/p{i}";
      for (int v = 99; v >= 0; v--) {
        List<PackageDependency> dependencies = [];
        for (int j = i + 1; j <= Math.Min(i + 2, 49); j++) {
          dependencies.Add(Dependency($"github.com/p/p{j}", "<90.0.0"));
        }

        resolver.Add(Package(spec, $"{v}.0.0", dependencies: dependencies));
      }
    }

    GraphSolver solver = new(resolver);
    Stopwatch stopwatch = Stopwatch.StartNew();

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/p/p0")], [], s_environment)).Order;

    // Assert
    stopwatch.Stop();
    Assert.Equal(50, result.Count);
    Assert.Equal("github.com/p/p49@89.0.0", Names(result)[0]);
    Assert.Equal("github.com/p/p0@99.0.0", Names(result)[^1]);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
  }

  [Fact]
  public async Task Solve_DeepUnsatisfiableChain_FailsQuickly() {
    // Arrange: 50 packages × 100 versions in a chain; the last one needs a missing package.
    // Naive backtracking would try 100^49 combinations.
    FakeResolver resolver = new();
    for (int i = 0; i < 50; i++) {
      string spec = $"github.com/p/p{i}";
      for (int v = 99; v >= 0; v--) {
        PackageDependency dependency = Dependency(
            i == 49 ? "github.com/missing/missing" : $"github.com/p/p{i + 1}");
        resolver.Add(Package(spec, $"{v}.0.0", dependencies: [dependency]));
      }
    }

    GraphSolver solver = new(resolver);
    Stopwatch stopwatch = Stopwatch.StartNew();

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.SolveGraph(
            [Request("github.com/p/p0")], [], s_environment));

    // Assert
    stopwatch.Stop();
    Assert.Contains("github.com/p/p49@99.0.0 requires github.com/missing/missing *, "
        + "but no version of it is available", exception.Message);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
  }

  [Fact]
  public async Task Solve_LongDependencyChain_DoesNotOverflowStack() {
    // Arrange
    const int length = 5000;
    FakeResolver resolver = new();
    for (int i = 0; i < length; i++) {
      resolver.Add(Package($"github.com/c/c{i}", "1.0.0",
          dependencies: i + 1 < length ? [Dependency($"github.com/c/c{i + 1}")] : []));
    }

    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("github.com/c/c0")], [], s_environment)).Order;

    // Assert
    Assert.Equal(length, result.Count);
    Assert.Equal($"github.com/c/c{length - 1}@1.0.0", Names(result)[0]);
    Assert.Equal("github.com/c/c0@1.0.0", Names(result)[^1]);
  }

  // Installation order (protocol/workspace.md §5)

  [Fact]
  public async Task Solve_DependencyCycle_OrdersCycleByLowercaseIdentity() {
    // Arrange: Zed -> beta <-> Alpha -> Base.
    FakeResolver resolver = new();
    resolver.Add(Package("example.com/Zed", "1.0.0", dependencies: [Dependency("example.com/beta")]));
    resolver.Add(Package("example.com/beta", "1.0.0",
        dependencies: [Dependency("example.com/Alpha")]));
    resolver.Add(Package("example.com/Alpha", "1.0.0",
        dependencies: [Dependency("example.com/beta"), Dependency("example.com/Base")]));
    resolver.Add(Package("example.com/Base", "1.0.0"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = (await solver.SolveGraph(
        [Request("example.com/Zed")], [], s_environment)).Order;

    // Assert
    Assert.Equal(["example.com/Base@1.0.0", "example.com/Alpha@1.0.0", "example.com/beta@1.0.0",
        "example.com/Zed@1.0.0"], Names(result));
  }

  [Fact]
  public void InstallOrder_Cycle_OrdersByLowercaseIdentityNotOrdinal() {
    // Arrange: ordinal order would put "B" before "a"; '#' sorts before letters.
    Candidate b = Package("example.com/B", "1.0.0");
    Candidate a = Package("example.com/a", "1.0.0");
    Candidate variant = Package("example.com/a", "1.0.0", variant: "x");

    // Act
    IReadOnlyList<Candidate> order = GraphSolver.InstallOrder(
        [b, variant, a], c => c == a ? [b, variant] : c == b ? [variant] : [a]);

    // Assert
    Assert.Equal(["example.com/a@1.0.0", "example.com/a#x@1.0.0", "example.com/B@1.0.0"],
        Names(order));
  }

  [Fact]
  public void InstallOrder_DisconnectedPackages_OrdersByLowercaseIdentity() {
    // Arrange
    Candidate b = Package("example.com/b", "1.0.0");
    Candidate a = Package("example.com/A", "1.0.0");
    Candidate c = Package("example.com/C", "1.0.0");

    // Act
    IReadOnlyList<Candidate> order = GraphSolver.InstallOrder([c, b, a], _ => []);

    // Assert
    Assert.Equal(["example.com/A@1.0.0", "example.com/b@1.0.0", "example.com/C@1.0.0"],
        Names(order));
  }

  [Fact]
  public void InstallOrder_DependencyOutsideSet_IsIgnored() {
    // Arrange
    Candidate a = Package("example.com/a", "1.0.0");
    Candidate outside = Package("example.com/z", "1.0.0");
    Candidate b = Package("example.com/b", "1.0.0");

    // Act
    IReadOnlyList<Candidate> order = GraphSolver.InstallOrder(
        [b, a], c => c == a ? [b, outside] : []);

    // Assert
    Assert.Equal(["example.com/b@1.0.0", "example.com/a@1.0.0"], Names(order));
  }

  private static string[] Names(IEnumerable<Candidate> candidates) =>
      [.. candidates.Select(c => $"{c.Package.Identity}@{c.Package.Version}")];

  private static PackageRequest Request(string source) =>
      new(Ref.Parse(source, allowFile: false), null);

  private static PackageDependency Dependency(
      string source, string range = "*", bool compat = false) =>
      new(Ref.Parse(source, allowFile: false), VersionRange.Parse(range)) { ClientCompat = compat };

  private static Candidate Package(
      string id,
      string version,
      string? source = null,
      string variant = "",
      IReadOnlyList<PackageDependency>? dependencies = null,
      string? mod = null,
      byte[]? mcmod = null,
      bool legacy = false,
      bool unavailable = false,
      List<string>? downloads = null) =>
      TestCandidates.Make(id, version, source, variant, dependencies,
          mod is null ? mcmod : WorkspaceHarness.Mcmod(mod), legacy, unavailable, downloads);

  private sealed record ResolveCall(string Ref, string? Range, bool Compat);

  /// Maps a source, as written, to its candidates in order, and filters them by range like the
  /// real resolver.
  private sealed class FakeResolver : IResolver {
    private readonly Dictionary<string, List<Candidate>> _candidates = [];

    public List<ResolveCall> Calls { get; } = [];

    /// Adds a candidate of its source.
    public void Add(Candidate candidate) {
      string key = candidate.Source.ToString();
      if (!_candidates.TryGetValue(key, out List<Candidate>? list)) {
        list = [];
        _candidates[key] = list;
      }

      list.Add(candidate);
    }

    public void AddVersions(string spec, IEnumerable<string> versions) {
      foreach (string version in versions) {
        Add(Package(spec, version));
      }
    }

    public async IAsyncEnumerable<Candidate> ResolveLazy(
        Ref source,
        IVersionConstraint? range,
        LipEnvironment environment,
        bool clientCompat = false,
        Action<string>? onDiagnostic = null) {
      Calls.Add(new ResolveCall(source.ToString(), range?.ToString(), clientCompat));
      await Task.CompletedTask;
      foreach (Candidate candidate in _candidates.GetValueOrDefault(source.ToString(), [])) {
        if (range is null || range.IsSatisfiedBy(candidate.Package.Version)) {
          yield return candidate;
        }
      }
    }
  }
}

/// Candidates for solver tests: a package with one entry for TestCandidates.Environment.
internal static class TestCandidates {
  public static readonly LipEnvironment Environment = new(McmodPlatform.Server, "win-x64");

  /// The JSON of every entry, which the solver never reads.
  private static readonly JsonElement s_emptyEntry = JsonDocument.Parse("{}").RootElement.Clone();

  /// The provider of packages without assets, which is never asked for one.
  private static readonly IArchiveProvider s_noAssets =
      new Mock<IArchiveProvider>(MockBehavior.Strict).Object;

  /// The files of every archive, which the solver never reads.
  private static readonly Lip.Core.Sources.ISource s_archiveSource =
      Mock.Of<Lip.Core.Sources.ISource>(MockBehavior.Strict);

  /// A candidate of the package id[#variant]@version resolved for source (by default the
  /// package id with the variant), in the git archive of a commit named after its source and
  /// version. With mcmod, the package is a mod whose asset of type "mcmod" has those bytes; it
  /// is downloaded (recorded in downloads) when first opened, or fails to be when unavailable.
  public static Candidate Make(
      string id,
      string version,
      string? source = null,
      string variant = "",
      IReadOnlyList<PackageDependency>? dependencies = null,
      byte[]? mcmod = null,
      bool legacy = false,
      bool unavailable = false,
      List<string>? downloads = null) {
    Ref reference = Ref.Parse(
        source ?? (variant == "" ? id : $"{id}#{variant}"), allowFile: false);
    string key = $"{reference}@{version}";
    string url = $"https://example.com/{id}/{version}.mcmod";
    List<EntryAsset> assets = mcmod is null ? [] : [new EntryAsset(AssetType.Mcmod, [url], [])];
    PackageEntry entry = new() {
      Platform = Environment.Platform,
      Runtime = Environment.Runtime,
      Dependencies = dependencies ?? [],
      Assets = assets,
      Derived = legacy,
      Json = s_emptyEntry,
    };

    Package package = new(id, variant, SemVersion.Parse(version), entry) {
      Manifest = Manifest(id, version, variant, entry),
    };
    if (mcmod is null) {
      return Candidate.Create(
          package,
          Archive(key, reference),
          reference,
          s_noAssets);
    }

    Mock<IArchiveProvider> provider = new();
    provider
        .Setup(p => p.OpenAsset(It.IsAny<IReadOnlyList<string>>()))
        .Returns((IReadOnlyList<string> urls) => {
          downloads?.Add(urls[0]);
          return unavailable
              ? throw new ArchiveUnavailableException($"404 {urls[0]}")
              : Task.FromResult<Lip.Core.Sources.ISource>(new ZipSource(mcmod!));
        });

    return Candidate.Create(
        package,
        Archive(key, reference),
        reference,
        provider.Object);
  }

  /// The package file of a package with just one entry.
  public static JsonElement Manifest(
      string id, string version, string variant, PackageEntry entry) =>
      JsonSerializer.SerializeToElement(new JsonObject {
        [PackageFormat.FormatKey] = PackageFormat.Format,
        ["id"] = id,
        ["version"] = version,
        ["variants"] = new JsonObject {
          [variant] = new JsonArray(JsonNode.Parse(entry.Json.GetRawText())),
        },
      });

  /// The git archive of a candidate, of a commit named after its key (ignoring ASCII case).
  private static ArchiveHandle Archive(string key, Ref reference) => new(
      key,
      s_archiveSource,
      Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(PackageNames.Fold(key)))));
}
