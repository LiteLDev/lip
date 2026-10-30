using System.Diagnostics;
using System.Text.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Resolution;

public class GraphSolverTests {
  private const string LeviLamina = "github.com/LiteLDev/LeviLamina";

  private static readonly LipEnvironment s_environment = new(McmodPlatform.Server, "win-x64");

  private static readonly ArchiveHandle s_workspace = Archive("C:/workspace");

  [Fact]
  public async Task Solve_SingleRequest_PicksFirstCandidate() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/a/a", "A", ["2.0.0", "1.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/a/a")], [], s_environment);

    // Assert
    Assert.Equal(["A@2.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_LeviLaminaWithManyVersionsAndTenMods_PicksNewestCommonVersion() {
    // Arrange
    FakeResolver resolver = new();
    string[] levilaminaVersions = [
      .. Enumerable.Range(0, 3).SelectMany(major => Enumerable.Range(0, 100)
          .Select(minor => $"{major}.{minor}.0")).Reverse(),
    ];
    resolver.AddVersions(LeviLamina, "LeviLamina", levilaminaVersions);

    List<PackageRequest> requests = [];
    for (int i = 0; i < 10; i++) {
      string spec = $"github.com/mods/mod{i}";
      resolver.Add(spec, Package($"Mod{i}", "2.0.0", spec,
          edges: [Edge(LeviLamina, Between($"1.{i * 5}.0", $"2.{(i * 5) + 50}.0"))]));
      resolver.Add(spec, Package($"Mod{i}", "1.0.0", spec, edges: [Edge(LeviLamina)]));
      requests.Add(Request(spec));
    }

    // The newest Old needs an ancient LeviLamina, so Old must fall back to 1.0.0.
    resolver.Add("github.com/mods/old", Package("Old", "2.0.0", "github.com/mods/old",
        edges: [Edge(LeviLamina, Between("0.0.0", "1.0.0"))]));
    resolver.Add("github.com/mods/old", Package("Old", "1.0.0", "github.com/mods/old",
        edges: [Edge(LeviLamina, Between("2.0.0", "3.0.0"))]));
    requests.Add(Request("github.com/mods/old"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(requests, [], s_environment);

    // Assert
    Assert.Equal("LeviLamina@2.49.0", Names(result)[0]);
    Assert.Contains("Old@1.0.0", Names(result));
    Assert.All(Enumerable.Range(0, 10), i => Assert.Contains($"Mod{i}@2.0.0", Names(result)));
    Assert.Equal(12, result.Count);
    Assert.Equal(resolver.Calls.Count, resolver.Calls.Distinct().Count());
  }

  [Fact]
  public async Task Solve_ToothPackageDependsOnPathWithoutName_UsesCompatFlagAndAnyIdentity() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add("github.com/legacy/tooth", Package("github.com/legacy/tooth", "1.0.0",
        "github.com/legacy/tooth", kind: "tooth",
        edges: [new InstallEdge(null, null, [Ref.Parse("github.com/new/lib")], true)]));
    resolver.Add("github.com/new/lib", Package("NewLib", "4.0.0", "github.com/new/lib"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/legacy/tooth")], [], s_environment);

    // Assert
    Assert.Equal(["NewLib@4.0.0", "github.com/legacy/tooth@1.0.0"], Names(result));
    Assert.Contains(resolver.Calls, c => c.Ref == "github.com/new/lib" && c.Compat);
  }

  [Fact]
  public async Task Solve_NamedInstallDependency_SkipsCandidatesWithOtherIdentity() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add("github.com/a/a", Package("A", "1.0.0", "github.com/a/a",
        edges: [new InstallEdge("foo", null, [Ref.Parse("./sub/")])]));
    resolver.Add("github.com/a/a@1.0.0!sub", Package("Bar", "1.0.0", "github.com/a/a@1.0.0!sub"));
    resolver.Add("github.com/a/a@1.0.0!sub", Package("FOO", "1.0.0", "github.com/a/a@1.0.0!sub"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/a/a")], [], s_environment);

    // Assert
    Assert.Equal(["FOO@1.0.0", "A@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_ConflictsWithNewerVersions_BacktracksToOlderVersions() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add("github.com/a/a", Package("A", "1.0.0", "github.com/a/a",
        conflicts: [new Dep { Name = "b", Version = Between("2.0.0", "99.0.0") }]));
    resolver.AddVersions("github.com/b/b", "B", ["3.0.0", "2.0.0", "1.0.0"]);
    resolver.Add("github.com/c/c", Package("C", "2.0.0", "github.com/c/c",
        conflicts: [new Dep { Name = "A" }]));
    resolver.Add("github.com/c/c", Package("C", "1.0.0", "github.com/c/c"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/a/a"), Request("github.com/b/b"), Request("github.com/c/c")],
        [],
        s_environment);

    // Assert
    Assert.Equal(["A@1.0.0", "B@1.0.0", "C@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_ConflictDeepInGraph_BacktracksEarlierChoice() {
    // Arrange: X@2 needs Y, which conflicts with Z; Z is requested, so X falls back to 1.
    FakeResolver resolver = new();
    resolver.Add("github.com/x/x", Package("X", "2.0.0", "github.com/x/x",
        edges: [Edge("github.com/y/y")]));
    resolver.Add("github.com/x/x", Package("X", "1.0.0", "github.com/x/x"));
    resolver.Add("github.com/y/y", Package("Y", "1.0.0", "github.com/y/y",
        conflicts: [new Dep { Name = "Z" }]));
    resolver.Add("github.com/z/z", Package("Z", "1.0.0", "github.com/z/z"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/x/x"), Request("github.com/z/z")], [], s_environment);

    // Assert
    Assert.Equal(["X@1.0.0", "Z@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_Unsatisfiable_ThrowsWithFailingEdgeAndResolverDiagnostics() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions(LeviLamina, "LeviLamina", ["1.2.0", "1.1.0"]);
    resolver.Add("github.com/mods/mod", Package("Mod", "1.0.0", "github.com/mods/mod",
        edges: [Edge(LeviLamina, Between("99.0.0", "100.0.0"))]));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/mods/mod")], [], s_environment));

    // Assert
    Assert.Contains($"Mod@1.0.0 requires a package [99.0.0, 100.0.0) from {LeviLamina}: "
        + "no candidates", exception.Message);
    Assert.Contains(exception.Diagnostics, d => d.Contains($"{LeviLamina}: no version matches"));
  }

  [Fact]
  public async Task Solve_UnsatisfiableDueToConflict_ListsBlockedCandidates() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add("github.com/a/a", Package("A", "1.0.0", "github.com/a/a",
        conflicts: [new Dep { Name = "B" }]));
    resolver.Add("github.com/b/b", Package("B", "1.0.0", "github.com/b/b"));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/a/a"), Request("github.com/b/b")], [], s_environment));

    // Assert
    Assert.Contains("request github.com/b/b *: no candidate fits (A@1.0.0 conflicts with B@1.0.0)",
        exception.Message);
  }

  [Fact]
  public async Task Solve_DependencyCycle_OrdersCycleByLowercaseIdentity() {
    // Arrange: Zed -> beta <-> Alpha -> Base.
    FakeResolver resolver = new();
    resolver.Add("github.com/z/z", Package("Zed", "1.0.0", "github.com/z/z",
        edges: [Edge("github.com/b/b")]));
    resolver.Add("github.com/b/b", Package("beta", "1.0.0", "github.com/b/b",
        edges: [Edge("github.com/a/a")]));
    resolver.Add("github.com/a/a", Package("Alpha", "1.0.0", "github.com/a/a",
        edges: [Edge("github.com/b/b"), Edge("github.com/base/base")]));
    resolver.Add("github.com/base/base", Package("Base", "1.0.0", "github.com/base/base"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/z/z")], [], s_environment);

    // Assert
    Assert.Equal(["Base@1.0.0", "Alpha@1.0.0", "beta@1.0.0", "Zed@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_PinnedWithSameIdentity_BlocksRequest() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/x/x", "X", ["2.0.0"]);
    Candidate pinned = ToCandidate(Package("x", "1.0.0", "github.com/x/x"));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/x/x")], [pinned], s_environment));

    // Assert
    Assert.Contains(
        "X@2.0.0 (from github.com/x/x@2.0.0) has the same identity as x@1.0.0 "
            + "(from github.com/x/x@1.0.0) (installed)",
        exception.Message);
  }

  [Fact]
  public async Task Solve_DefaultVariantDependsOnItsLabelledVariants_SolvesThreePackages() {
    // Arrange: a converted tooth.json like LegacyScriptEngine; its variants share one archive.
    FakeResolver resolver = new();
    resolver.Add("github.com/t/t", Variant("t", "", [Edge("github.com/t/t#a"),
        Edge("github.com/t/t#b")]));
    resolver.Add("github.com/t/t#a", Variant("t#a", "a"));
    resolver.Add("github.com/t/t#b", Variant("t#b", "b"));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/t/t")], [], s_environment);

    // Assert
    Assert.Equal(["t#a@1.0.0", "t#b@1.0.0", "t@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_TwoLabelledVariantsRequested_SolvesBoth() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add("github.com/t/t#a", Variant("t#a", "a", [Edge("github.com/l/l")]));
    resolver.Add("github.com/t/t#b", Variant("t#b", "b", [Edge("github.com/l/l")]));
    resolver.AddVersions("github.com/l/l", "L", ["1.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    SolvedGraph graph = await solver.SolveGraph(
        [Request("github.com/t/t#a"), Request("github.com/t/t#b")], [], s_environment);

    // Assert
    Assert.Equal(["L@1.0.0", "t#a@1.0.0", "t#b@1.0.0"], Names(graph.Order));
    Assert.Equal([1, 2], graph.RequestSatisfiers);
  }

  [Fact]
  public async Task Solve_PinnedToothDefaultVariant_DoesNotStandInForClientVariant() {
    // Arrange: "client" means "" only for archives that use manifest.json (spec §9.3).
    FakeResolver resolver = new();
    resolver.Add("github.com/t/t#client", Variant("t#client", "client"));
    resolver.Add("github.com/r/r", Package("R", "1.0.0", "github.com/r/r", edges: [
      new InstallEdge(null, null, [Ref.Parse("github.com/t/t#client")], ToothClientCompat: true),
    ]));
    Candidate pinned = new(
        Variant("t", ""),
        Archive("github.com/t/t@rev:abc"),
        [new ChainHop(Ref.Parse("github.com/t/t@branch:main"), null)]);
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/r/r")], [pinned], s_environment);

    // Assert
    Assert.Equal(["t@1.0.0", "t#client@1.0.0", "R@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_VariantOfSameArchiveWithSameIdentity_FailsInsteadOfMerging() {
    // Arrange: two packages of one archive and one identity; the root does not satisfy the
    // edge to its sibling.
    FakeResolver resolver = new();
    resolver.Add("github.com/t/t", Variant("t", "", [Edge("github.com/t/t#a")]));
    resolver.Add("github.com/t/t#a", Variant("t", "a"));
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/t/t")], [], s_environment));

    // Assert
    Assert.Contains(
        "t@1.0.0 (from github.com/t/t@1.0.0, variant \"a\") has the same identity as t@1.0.0 "
            + "(from github.com/t/t@1.0.0)",
        exception.Message);
  }

  [Fact]
  public async Task Solve_TwoArchivesWithSameIdentity_FailsWithBothSources() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/x/x", "dup", ["1.0.0"]);
    resolver.AddVersions("github.com/y/y", "dup", ["1.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/x/x"), Request("github.com/y/y")], [], s_environment));

    // Assert
    Assert.Contains(
        "dup@1.0.0 (from github.com/y/y@1.0.0) has the same identity as dup@1.0.0 "
            + "(from github.com/x/x@1.0.0)",
        exception.Message);
  }

  [Fact]
  public async Task Solve_PinnedSamePackage_SatisfiesRequestAndPinnedEdges() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/x/x", "X", ["2.0.0", "1.0.0"]);
    resolver.AddVersions("github.com/dep/dep", "Dep", ["1.0.0"]);
    Candidate pinnedX = ToCandidate(Package("X", "1.0.0", "github.com/x/x"));
    Candidate pinnedP = ToCandidate(Package("P", "1.0.0", "github.com/p/p",
        edges: [Edge("github.com/dep/dep")]));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/x/x")], [pinnedX, pinnedP], s_environment);

    // Assert
    Assert.Equal(["Dep@1.0.0", "P@1.0.0", "X@1.0.0"], Names(result));
    Assert.Same(pinnedX, result[2]);
  }

  [Fact]
  public async Task Solve_MemberRoutedFromTagOfOtherVersion_SatisfiesEdge() {
    // Arrange: the routing document at tag v1.0.0 has no version and routes to R 1.0.1, which
    // is installed; Mod's edge must accept it although no tag is named v1.0.1 (spec §5.2, §6).
    FakeResolver resolver = new();
    resolver.Add("github.com/r/r", Package("R", "2.0.0", "github.com/r/r"));
    FakePackage routed = Package("R", "1.0.1", "github.com/r/r") with {
      ArchiveKey = "https://example.com/R-1.0.1.mcmod",
    };
    resolver.Add("github.com/r/r", routed, tag: "1.0.0");
    resolver.Add("github.com/mod/mod", Package("Mod", "1.0.0", "github.com/mod/mod",
        edges: [Edge("github.com/r/r", Between("1.0.0", "3.0.0"))]));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/mod/mod")], [ToCandidate(routed)], s_environment);

    // Assert
    Assert.Equal(["R@1.0.1", "Mod@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_MemberWithNameOfOlderVersion_SatisfiesEdge() {
    // Arrange: the package was renamed from Old to New in 2.0.0; Old 1.0.0 is installed and New
    // conflicts with it, so Mod's edge is satisfied only by the installed Old (spec §6).
    FakeResolver resolver = new();
    resolver.Add("github.com/n/n", Package("New", "2.0.0", "github.com/n/n",
        conflicts: [new Dep { Name = "Old" }]));
    FakePackage old = Package("Old", "1.0.0", "github.com/n/n");
    resolver.Add("github.com/n/n", old);
    resolver.Add("github.com/mod/mod", Package("Mod", "1.0.0", "github.com/mod/mod",
        edges: [Edge("github.com/n/n")]));
    GraphSolver solver = new(resolver);

    // Act
    SolvedGraph graph = await solver.SolveGraph(
        [Request("github.com/mod/mod")], [ToCandidate(old)], s_environment);

    // Assert
    Assert.Equal(["Old@1.0.0", "Mod@1.0.0"], Names(graph.Order));
    Assert.Equal([0], graph.Dependencies[1]);
  }

  [Fact]
  public async Task Solve_UnpinnedEdgeBeforePinnedEdge_PinnedPackageSatisfiesBoth() {
    // Arrange: A requires S (any version) and B; B requires S at branch main. The package at
    // the branch stands in for the versions of S (spec §6), so it satisfies both edges.
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/s/s", "S", ["2.0.0", "1.0.0"]);
    resolver.Add("github.com/s/s@branch:main", Package("S", "2.5.0", "github.com/s/s") with {
      ArchiveKey = "github.com/s/s@rev:0123456789abcdef0123456789abcdef01234567",
    });
    resolver.Add("github.com/a/a", Package("A", "1.0.0", "github.com/a/a",
        edges: [Edge("github.com/s/s"), Edge("github.com/b/b")]));
    resolver.Add("github.com/b/b", Package("B", "1.0.0", "github.com/b/b",
        edges: [Edge("github.com/s/s@branch:main")]));
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/a/a")], [], s_environment);

    // Assert
    Assert.Equal(["S@2.5.0", "B@1.0.0", "A@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_UnpinnedEdgeOutsidePinnedVersion_Fails() {
    // Arrange: as above, but A needs S below 2.0.0, which the package at the branch is not.
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/s/s", "S", ["2.0.0", "1.0.0"]);
    resolver.Add("github.com/s/s@branch:main", Package("S", "2.5.0", "github.com/s/s") with {
      ArchiveKey = "github.com/s/s@rev:0123456789abcdef0123456789abcdef01234567",
    });
    resolver.Add("github.com/a/a", Package("A", "1.0.0", "github.com/a/a",
        edges: [Edge("github.com/s/s", Between("1.0.0", "2.0.0")), Edge("github.com/b/b")]));
    resolver.Add("github.com/b/b", Package("B", "1.0.0", "github.com/b/b",
        edges: [Edge("github.com/s/s@branch:main")]));
    GraphSolver solver = new(resolver);

    // Act & Assert
    await Assert.ThrowsAsync<DependencyResolutionException>(() => solver.Solve(
        [Request("github.com/a/a")], [], s_environment));
  }

  [Fact]
  public async Task Solve_PinnedPackagesConflict_Throws() {
    // Arrange
    FakeResolver resolver = new();
    Candidate a = ToCandidate(Package("A", "1.0.0", "github.com/a/a",
        conflicts: [new Dep { Name = "B" }]));
    Candidate b = ToCandidate(Package("B", "1.0.0", "github.com/b/b"));
    GraphSolver solver = new(resolver);

    // Act & Assert
    await Assert.ThrowsAsync<DependencyResolutionException>(
        () => solver.Solve([], [a, b], s_environment));
  }

  [Fact]
  public async Task Solve_SamePackageSatisfiesSeveralEdges_AddsItOnce() {
    // Arrange
    FakeResolver resolver = new();
    resolver.Add("github.com/a/a", Package("A", "1.0.0", "github.com/a/a",
        edges: [Edge(LeviLamina), new InstallEdge("LeviLamina", null, [Ref.Parse(LeviLamina)])]));
    resolver.AddVersions(LeviLamina, "LeviLamina", ["1.0.0"]);
    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/a/a"), Request(LeviLamina)], [], s_environment);

    // Assert
    Assert.Equal(["LeviLamina@1.0.0", "A@1.0.0"], Names(result));
  }

  [Fact]
  public async Task Solve_ManyPackagesAndVersions_CompletesQuickly() {
    // Arrange: 50 packages with 100 versions each; every version depends on the next two
    // packages with a range excluding their newest 10 versions.
    FakeResolver resolver = new();
    for (int i = 0; i < 50; i++) {
      string spec = $"github.com/p/p{i}";
      for (int v = 99; v >= 0; v--) {
        List<InstallEdge> edges = [];
        for (int j = i + 1; j <= Math.Min(i + 2, 49); j++) {
          edges.Add(Edge($"github.com/p/p{j}", Between("0.0.0", "90.0.0")));
        }

        resolver.Add(spec, Package($"P{i}", $"{v}.0.0", spec, edges: edges));
      }
    }

    // The step bound keeps the test meaningful on slow machines.
    GraphSolver solver = new(resolver, maxSteps: 10_000);
    Stopwatch stopwatch = Stopwatch.StartNew();

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/p/p0")], [], s_environment);

    // Assert
    stopwatch.Stop();
    Assert.Equal(50, result.Count);
    Assert.Equal("P49@89.0.0", Names(result)[0]);
    Assert.Equal("P0@99.0.0", Names(result)[^1]);
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
        InstallEdge edge = Edge(i == 49 ? "github.com/missing/missing" : $"github.com/p/p{i + 1}");
        resolver.Add(spec, Package($"P{i}", $"{v}.0.0", spec, edges: [edge]));
      }
    }

    // The step bound keeps the test meaningful on slow machines.
    GraphSolver solver = new(resolver, maxSteps: 10_000);
    Stopwatch stopwatch = Stopwatch.StartNew();

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/p/p0")], [], s_environment));

    // Assert
    stopwatch.Stop();
    Assert.Contains("P49@99.0.0 requires a package * from github.com/missing/missing: "
        + "no candidates", exception.Message);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
  }

  [Fact]
  public async Task Solve_LongDependencyChain_DoesNotOverflowStack() {
    // Arrange
    const int length = 5000;
    FakeResolver resolver = new();
    for (int i = 0; i < length; i++) {
      string spec = $"github.com/c/c{i}";
      resolver.Add(spec, Package($"C{i}", "1.0.0", spec,
          edges: i + 1 < length ? [Edge($"github.com/c/c{i + 1}")] : []));
    }

    GraphSolver solver = new(resolver);

    // Act
    IReadOnlyList<Candidate> result = await solver.Solve(
        [Request("github.com/c/c0")], [], s_environment);

    // Assert
    Assert.Equal(length, result.Count);
    Assert.Equal($"C{length - 1}@1.0.0", Names(result)[0]);
    Assert.Equal("C0@1.0.0", Names(result)[^1]);
  }

  [Fact]
  public async Task Solve_StepLimitExceeded_ThrowsWithClearMessage() {
    // Arrange
    FakeResolver resolver = new();
    resolver.AddVersions("github.com/a/a", "A", ["3.0.0", "2.0.0", "1.0.0"]);
    resolver.Add("github.com/b/b", Package("B", "1.0.0", "github.com/b/b",
        conflicts: [new Dep { Name = "A" }]));
    GraphSolver solver = new(resolver, maxSteps: 2);

    // Act
    DependencyResolutionException exception = await Assert.ThrowsAsync<
        DependencyResolutionException>(() => solver.Solve(
            [Request("github.com/b/b"), Request("github.com/a/a")], [], s_environment));

    // Assert
    Assert.StartsWith("Gave up resolving dependencies after examining 2 candidates",
        exception.Message);
  }

  [Fact]
  public void InstallOrder_DisconnectedPackages_OrdersByLowercaseIdentity() {
    // Arrange
    Candidate b = ToCandidate(Package("b", "1.0.0", "github.com/b/b"));
    Candidate a = ToCandidate(Package("A", "1.0.0", "github.com/a/a"));
    Candidate c = ToCandidate(Package("C", "1.0.0", "github.com/c/c"));

    // Act
    IReadOnlyList<Candidate> order = GraphSolver.InstallOrder([c, b, a], _ => []);

    // Assert
    Assert.Equal(["A@1.0.0", "b@1.0.0", "C@1.0.0"], Names(order));
  }

  [Fact]
  public void InstallOrder_DependencyOutsideSet_IsIgnored() {
    // Arrange
    Candidate a = ToCandidate(Package("A", "1.0.0", "github.com/a/a"));
    Candidate outside = ToCandidate(Package("Z", "1.0.0", "github.com/z/z"));
    Candidate b = ToCandidate(Package("B", "1.0.0", "github.com/b/b"));

    // Act
    IReadOnlyList<Candidate> order = GraphSolver.InstallOrder(
        [b, a], c => c == a ? [b, outside] : []);

    // Assert
    Assert.Equal(["B@1.0.0", "A@1.0.0"], Names(order));
  }

  private static string[] Names(IEnumerable<Candidate> candidates) {
    return [.. candidates.Select(c => $"{c.Package.Identity}@{c.Package.Version}")];
  }

  private static ArchiveHandle Archive(string key) {
    return new ArchiveHandle(key, Mock.Of<ISource>(), new ZipArchiveIdentity(TestIdentity.Sha256, key));
  }

  private static PackageRequest Request(string reference) {
    return new PackageRequest(Ref.Parse(reference), null, s_workspace);
  }

  private static InstallEdge Edge(string reference, IVersionConstraint? range = null) {
    return new InstallEdge(null, range, [Ref.Parse(reference)]);
  }

  private static HalfOpen Between(string min, string max) {
    return new HalfOpen(SemVersion.Parse(min), SemVersion.Parse(max));
  }

  private static FakePackage Package(
      string name,
      string version,
      string spec,
      string kind = "mcmod",
      IReadOnlyList<InstallEdge>? edges = null,
      IReadOnlyList<Dep>? conflicts = null) {
    return new FakePackage(kind, name, SemVersion.Parse(version), edges ?? [], conflicts ?? [],
        $"{spec}@{version}");
  }

  /// A package of the converted tooth.json at github.com/t/t@1.0.0 (one archive for every
  /// variant).
  private static FakePackage Variant(
      string name, string variant, IReadOnlyList<InstallEdge>? edges = null) {
    return Package(name, "1.0.0", "github.com/t/t", "tooth", edges) with { Variant = variant };
  }

  private static Candidate ToCandidate(FakePackage package) {
    return new Candidate(package, Archive(package.ArchiveKey), []);
  }

  /// [Min, Max).
  private sealed record HalfOpen(SemVersion Min, SemVersion Max) : IVersionConstraint {
    public bool IsSatisfiedBy(SemVersion version) {
      return version.ComparePrecedenceTo(Min) >= 0 && version.ComparePrecedenceTo(Max) < 0;
    }

    public override string ToString() {
      return $"[{Min}, {Max})";
    }
  }

  private sealed record FakePackage(
      string Kind,
      string Identity,
      SemVersion Version,
      IReadOnlyList<InstallEdge> InstallEdges,
      IReadOnlyList<Dep> Conflicts,
      string ArchiveKey,
      string Variant = "") : IPackage {
    public JsonElement Manifest => default;
  }

  private sealed record ResolveCall(string Ref, string Base, string? Range, bool Compat);

  /// Maps a spec ref (or "<base key>!<path>" for relative refs) to its candidates in order and
  /// filters them by range, like the real resolver for spec refs.
  private sealed class FakeResolver : IResolver {
    private readonly Dictionary<string, List<(FakePackage Package, SemVersion? Tag)>> _packages = [];

    public List<ResolveCall> Calls { get; } = [];

    /// Adds a candidate; tag is the version of the tag it is reached from when that differs
    /// from the package's (a routing document without a version), which the range must accept
    /// as well (spec §5.2 step 1).
    public void Add(string location, FakePackage package, string? tag = null) {
      if (!_packages.TryGetValue(location, out List<(FakePackage, SemVersion?)>? list)) {
        list = [];
        _packages[location] = list;
      }

      list.Add((package, tag is null ? null : SemVersion.Parse(tag)));
    }

    public void AddVersions(string spec, string name, IEnumerable<string> versions) {
      foreach (string version in versions) {
        Add(spec, Package(name, version, spec));
      }
    }

    public Task<ResolveResult> Resolve(
        Ref reference,
        ArchiveHandle? baseArchive,
        IVersionConstraint? range,
        LipEnvironment environment,
        bool toothClientCompat = false) {
      Calls.Add(new ResolveCall(reference.ToString(), baseArchive?.Key ?? "", range?.ToString(),
          toothClientCompat));
      string location = reference.Kind == RefKind.Relative
          ? $"{baseArchive?.Key}!{reference.RelativePath}"
          : reference.GitRef is not null ? reference.ToString()
          : reference.Variant == "" ? reference.Target : $"{reference.Target}#{reference.Variant}";
      List<Candidate> candidates = [
        .. _packages.GetValueOrDefault(location, [])
            .Where(p => range is null || (range.IsSatisfiedBy(p.Package.Version)
                && (p.Tag is null || range.IsSatisfiedBy(p.Tag))))
            .Select(p => reference.GitRef is null
                ? ToCandidate(p.Package)
                : new Candidate(p.Package, Archive(p.Package.ArchiveKey),
                    [new ChainHop(reference, null)])),
      ];
      List<string> diagnostics = candidates.Count == 0
          ? [$"{location}: no version matches {range?.ToString() ?? "*"}"]
          : [];
      return Task.FromResult(new ResolveResult(candidates, diagnostics));
    }
  }
}
