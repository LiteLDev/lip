using System.Runtime.CompilerServices;
using System.Text.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Resolution;

/// GraphSolver consumes candidates lazily (IResolver.ResolveLazy).
public class GraphSolverLazyTests {
  private const string Base = "github.com/base/base";

  private static readonly LipEnvironment s_environment = new(McmodPlatform.Server, "win-x64");

  private static readonly ArchiveHandle s_workspace = new(
      "C:/workspace", Mock.Of<ISource>(), new ZipArchiveIdentity(TestIdentity.Sha256, "C:/workspace"));

  [Fact]
  public async Task Solve_RequestWithManyVersions_ProducesOnlyTheFirstCandidate() {
    // Arrange
    CountingResolver resolver = new();
    resolver.Add(Base, "Base", 300, edges: []);

    // Act
    IReadOnlyList<Candidate> result = await new GraphSolver(resolver).Solve(
        [new PackageRequest(Ref.Parse(Base), null, s_workspace)], [], s_environment);

    // Assert
    Assert.Equal("299.0.0", Assert.Single(result).Package.Version.ToString());
    Assert.Equal(1, resolver.Produced);
  }

  [Fact]
  public async Task Solve_EdgeSatisfiedByPinnedMember_DoesNotEnumerateOtherVersions() {
    // Arrange
    CountingResolver resolver = new();
    resolver.Add(Base, "Base", 300, edges: []);
    resolver.Add("github.com/mod/mod", "Mod", 1, edges: [
      new InstallEdge(null, null, [Ref.Parse(Base)]),
    ]);
    Candidate pinned = CountingResolver.Make("Base", "5.0.0", $"{Base}@5.0.0", []);

    // Act
    IReadOnlyList<Candidate> result = await new GraphSolver(resolver).Solve(
        [new PackageRequest(Ref.Parse("github.com/mod/mod"), null, s_workspace)],
        [pinned],
        s_environment);

    // Assert: Mod's edge is satisfied by the pinned Base 5.0.0 without walking 300 versions.
    Assert.Equal(["Base@5.0.0", "Mod@0.0.0"],
        result.Select(c => $"{c.Package.Identity}@{c.Package.Version}"));
    Assert.True(resolver.Produced <= 3, $"produced {resolver.Produced} candidates");
  }

  private sealed class CountingResolver : IResolver {
    private readonly Dictionary<string, List<Candidate>> _candidates = [];

    public int Produced { get; private set; }

    public void Add(string spec, string name, int count, IReadOnlyList<InstallEdge> edges) {
      _candidates[spec] = [
        .. Enumerable.Range(0, count).Reverse()
            .Select(i => Make(name, $"{i}.0.0", $"{spec}@{i}.0.0", edges)),
      ];
    }

    public static Candidate Make(
        string name, string version, string key, IReadOnlyList<InstallEdge> edges) {
      Mock<IPackage> package = new();
      package.SetupGet(p => p.Kind).Returns("mcmod");
      package.SetupGet(p => p.Identity).Returns(name);
      package.SetupGet(p => p.Version).Returns(SemVersion.Parse(version));
      package.SetupGet(p => p.InstallEdges).Returns(edges);
      package.SetupGet(p => p.Conflicts).Returns([]);
      package.SetupGet(p => p.Manifest).Returns(JsonDocument.Parse("{}").RootElement);
      return new Candidate(
          package.Object,
          new ArchiveHandle(key, Mock.Of<ISource>(), new ZipArchiveIdentity(TestIdentity.Sha256, key)),
          []);
    }

    public Task<ResolveResult> Resolve(
        Ref reference,
        ArchiveHandle? baseArchive,
        IVersionConstraint? range,
        LipEnvironment environment,
        bool toothClientCompat = false) => throw new NotSupportedException();

    public async IAsyncEnumerable<Candidate> ResolveLazy(
        Ref reference,
        ArchiveHandle? baseArchive,
        IVersionConstraint? range,
        LipEnvironment environment,
        bool toothClientCompat = false,
        Action<string>? onDiagnostic = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
      foreach (Candidate candidate in _candidates.GetValueOrDefault(reference.Target, [])) {
        if (range is null || range.IsSatisfiedBy(candidate.Package.Version)) {
          Produced++;
          await Task.Yield();
          yield return candidate;
        }
      }
    }
  }
}
