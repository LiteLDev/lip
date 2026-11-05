using System.Runtime.CompilerServices;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;

namespace Lip.Core.Tests.Mcmod.Resolution;

/// GraphSolver consumes candidates lazily (IResolver.ResolveLazy), so that a remote repository
/// with many version tags only opens the tags the search reaches.
public class GraphSolverLazyTests {
  private const string Base = "github.com/base/base";

  private static readonly LipEnvironment s_environment = TestCandidates.Environment;

  [Fact]
  public async Task Solve_RequestWithManyVersions_ProducesOnlyTheFirstCandidate() {
    // Arrange
    CountingResolver resolver = new();
    resolver.Add(Base, 300);

    // Act
    IReadOnlyList<Candidate> result = await new GraphSolver(resolver).Solve(
        [new PackageRequest(Ref.Parse(Base), null)], [], s_environment);

    // Assert
    Assert.Equal("299.0.0", Assert.Single(result).Package.Version.ToString());
    Assert.Equal(1, resolver.Produced);
  }

  [Fact]
  public async Task Solve_DependencySatisfiedByInstalledMember_DoesNotEnumerateOtherVersions() {
    // Arrange
    CountingResolver resolver = new();
    resolver.Add(Base, 300);
    resolver.Add("github.com/mod/mod", 1,
        dependencies: [new ToothDependency(Ref.Parse(Base), VersionRange.Parse("*"))]);
    Candidate installed = TestCandidates.Make(Base, "5.0.0");

    // Act
    IReadOnlyList<Candidate> result = await new GraphSolver(resolver).Solve(
        [new PackageRequest(Ref.Parse("github.com/mod/mod"), null)],
        [installed],
        s_environment);

    // Assert: the dependency is satisfied by the installed Base 5.0.0 without walking 300
    // versions.
    Assert.Equal([$"{Base}@5.0.0", "github.com/mod/mod@0.0.0"],
        result.Select(c => $"{c.Package.Identity}@{c.Package.Version}"));
    Assert.True(resolver.Produced <= 3, $"produced {resolver.Produced} candidates");
  }

  [Fact]
  public async Task Solve_NewestVersionsClash_ProducesCandidatesOnlyUntilOneFits() {
    // Arrange: the five newest versions of Base are mods named like an installed mod.
    CountingResolver resolver = new();
    resolver.Add(Base, 300, mod: v => v >= 295 ? "Taken" : "Base");
    Candidate installed = TestCandidates.Make(
        "github.com/other/other", "1.0.0", mcmod: WorkspaceHarness.Mcmod("taken"));

    // Act
    IReadOnlyList<Candidate> result = await new GraphSolver(resolver).Solve(
        [new PackageRequest(Ref.Parse(Base), null)], [installed], s_environment);

    // Assert
    Assert.Equal("294.0.0", result[0].Package.Version.ToString());
    Assert.Equal(6, resolver.Produced);
  }

  /// Produces the candidates of a source one by one, counting them.
  private sealed class CountingResolver : IResolver {
    private readonly Dictionary<string, List<Candidate>> _candidates = [];

    public int Produced { get; private set; }

    /// Adds the versions count-1.0.0 down to 0.0.0 of a remote repository; mod names the mod
    /// name of each major version, or null for a file package.
    public void Add(
        string spec,
        int count,
        IReadOnlyList<ToothDependency>? dependencies = null,
        Func<int, string?>? mod = null) {
      _candidates[spec] = [
        .. Enumerable.Range(0, count).Reverse().Select(i => TestCandidates.Make(
            spec,
            $"{i}.0.0",
            dependencies: dependencies,
            mcmod: mod?.Invoke(i) is string name ? WorkspaceHarness.Mcmod(name) : null)),
      ];
    }

    public Task<ResolveResult> Resolve(
        Ref source,
        IVersionConstraint? range,
        LipEnvironment environment,
        bool clientCompat = false) => throw new NotSupportedException();

    public async IAsyncEnumerable<Candidate> ResolveLazy(
        Ref source,
        IVersionConstraint? range,
        LipEnvironment environment,
        bool clientCompat = false,
        Action<string>? onDiagnostic = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
      foreach (Candidate candidate in _candidates.GetValueOrDefault(source.ToString(), [])) {
        if (range is null || range.IsSatisfiedBy(candidate.Package.Version)) {
          Produced++;
          await Task.Yield();
          yield return candidate;
        }
      }
    }
  }
}
