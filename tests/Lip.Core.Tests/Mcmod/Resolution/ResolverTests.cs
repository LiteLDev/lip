using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Resolution;

public class ResolverTests {
  private const string _legacyMoney = "github.com/LiteLDev/LegacyMoney";
  private const string _legacyMoneyTemplate = "https://github.com/LiteLDev/LegacyMoney/releases/"
      + "download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod";

  private static readonly LipEnvironment _server = new(McmodPlatform.Server, "win-x64");
  private static readonly LipEnvironment _client = new(McmodPlatform.Client, "win-x64");
  private static readonly LipEnvironment _unknownPlatform = new(null, "win-x64");

  private readonly FakeArchives _archives = new();
  private readonly Mock<IToothCompat> _toothCompat = new();

  private Resolver CreateResolver() => new(_archives, _archives, _toothCompat.Object);

  private static ArchiveHandle Workspace => new(
      "/workspace", Mock.Of<ISource>(), new DirectoryArchiveIdentity("/workspace"));

  private static ConcreteArchiveDocument Concrete(
      string name,
      string version,
      PackagePlatform platform = PackagePlatform.Universal,
      string? rid = null) => new(
      new ConcreteDocument {
        IsMarked = true,
        Name = name,
        Version = SemVersion.Parse(version),
        Type = "native",
        Platform = platform,
        Rid = rid,
      },
      JsonDocument.Parse($"{{\"name\":\"{name}\"}}").RootElement.Clone());

  private static RoutingArchiveDocument Routing(
      params Artifact[] artifacts) => Routing(null, null, artifacts);

  private static RoutingArchiveDocument Routing(
      string? name,
      string? version,
      params Artifact[] artifacts) => new(new RoutingDocument {
        IsMarked = true,
        Name = name,
        Version = version is null ? null : SemVersion.Parse(version),
        Artifacts = artifacts,
      });

  private static Artifact Art(params string[] refs) => new() { Refs = refs };

  private static ToothArchiveDocument Tooth(string path, string version) => new(
      new PackageManifest { Path = path, Version = SemVersion.Parse(version) },
      JsonDocument.Parse("{}").RootElement.Clone());

  private static IVersionConstraint Range(string range) =>
      new SemVersionRangeConstraint(SemVersionRange.Parse(range));

  private void AddLegacyMoney() {
    _archives.AddTag(_legacyMoney, "v0.22.0", Routing("LegacyMoney", "0.22.0", new Artifact {
      Platforms = [McmodPlatform.Server, McmodPlatform.Client],
      Rids = ["win-x64"],
      Refs = [_legacyMoneyTemplate],
    }));
    const string download = "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/";
    _archives.AddUrl(download + "LegacyMoney-server-win-x64.mcmod",
        Concrete("LegacyMoney", "0.22.0", PackagePlatform.Server, "win-x64"));
    _archives.AddUrl(download + "LegacyMoney-client-win-x64.mcmod",
        Concrete("LegacyMoney", "0.22.0", PackagePlatform.Client, "win-x64"));
  }

  [Fact]
  public async Task Resolve_SpecRoutingToUrlTemplate_ProducesConcreteWithChain() {
    // Arrange
    AddLegacyMoney();
    Ref reference = Ref.Parse(_legacyMoney);

    // Act
    ResolveResult result = await CreateResolver().Resolve(reference, Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("LegacyMoney", candidate.Package.Identity);
    Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-server-win-x64.mcmod",
        candidate.Archive.Key);
    Assert.Equal(2, candidate.Chain.Count);
    Assert.Equal(new ChainHop(reference, 0), candidate.Chain[0]);
    Assert.Equal(candidate.Archive.Key, candidate.Chain[1].Ref.Target);
    Assert.Null(candidate.Chain[1].ArtifactIndex);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_ClientEnvironment_SelectsClientMcmod() {
    // Arrange
    AddLegacyMoney();

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse(_legacyMoney), Workspace, null, _client);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.EndsWith("LegacyMoney-client-win-x64.mcmod", candidate.Archive.Key);
  }

  [Fact]
  public async Task Resolve_RidNotListed_NoCandidateWithDiagnostic() {
    // Arrange
    AddLegacyMoney();

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse(_legacyMoney), Workspace, null,
        new LipEnvironment(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.Empty(result.Candidates);
    string diagnostic = Assert.Single(result.Diagnostics);
    Assert.StartsWith("github.com/LiteLDev/LegacyMoney@v0.22.0: no artifact", diagnostic);
  }

  [Fact]
  public async Task Resolve_PlatformUnknownAndArtifactWritesPlatform_Throws() {
    // Arrange
    AddLegacyMoney();

    // Act & Assert
    await Assert.ThrowsAsync<PlatformUnknownException>(() => CreateResolver()
        .Resolve(Ref.Parse(_legacyMoney), Workspace, null, _unknownPlatform));
  }

  [Fact]
  public async Task Resolve_PlatformUnknownAndPlatformTemplate_Throws() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod",
        Routing(Art("https://example.com/{{platform}}.mcmod")));

    // Act & Assert
    await Assert.ThrowsAsync<PlatformUnknownException>(() => CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _unknownPlatform));
  }

  [Fact]
  public async Task Resolve_PlatformUnknownAndConcreteNotUniversal_Throws() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0", PackagePlatform.Server));

    // Act & Assert
    await Assert.ThrowsAsync<PlatformUnknownException>(() => CreateResolver()
        .Resolve(Ref.Parse("https://example.com/a.mcmod"), Workspace, null, _unknownPlatform));
  }

  [Fact]
  public async Task Resolve_PlatformUnknownAndConcreteUniversal_ProducesCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(Art("https://example.com/a.mcmod")));
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _unknownPlatform);

    // Assert
    Assert.Single(result.Candidates);
  }

  [Fact]
  public async Task Resolve_PlatformUnknownAndRidRulesOutPlatformArtifact_ProducesCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(
        new Artifact {
          Platforms = [McmodPlatform.Server],
          Rids = ["linux-x64"],
          Refs = ["https://example.com/linux.mcmod"],
        },
        Art("https://example.com/a.mcmod")));
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _unknownPlatform);

    // Assert
    Assert.Equal(1, Assert.Single(result.Candidates).Chain[0].ArtifactIndex);
  }

  [Fact]
  public async Task Resolve_TransientOpenFailure_IsRetriedByLaterResolve() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0"));
    _archives.FailOnce.Add("https://example.com/a.mcmod");
    Resolver resolver = CreateResolver();

    // Act
    ResolveResult first = await resolver
        .Resolve(Ref.Parse("https://example.com/a.mcmod"), Workspace, null, _server);
    ResolveResult second = await resolver
        .Resolve(Ref.Parse("https://example.com/a.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Empty(first.Candidates);
    Assert.Single(second.Candidates);
  }

  [Fact]
  public async Task Resolve_ConcretePlatformDiffers_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0", PackagePlatform.Client));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/a.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("platform client", Assert.Single(result.Diagnostics));
  }

  [Theory]
  [InlineData("quickjs")]
  [InlineData("lua")]
  public async Task Resolve_VariantRouting_FollowsRelativeDirectory(string variant) {
    // Arrange
    const string spec = "github.com/LiteLDev/LegacyScriptEngine";
    _archives.AddTag(spec, "v1.0.0", Routing(
        new Artifact { Variant = "quickjs", Refs = ["./quickjs/"] },
        new Artifact { Variant = "lua", Refs = ["./lua/"] }));
    _archives.AddRelative($"{spec}@1.0.0", "./quickjs/", Concrete("lse-quickjs", "1.0.0"));
    _archives.AddRelative($"{spec}@1.0.0", "./lua/", Concrete("lse-lua", "1.0.0"));
    Ref reference = Ref.Parse($"{spec}#{variant}");

    // Act
    ResolveResult result = await CreateResolver().Resolve(reference, Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal($"lse-{variant}", candidate.Package.Identity);
    Assert.Equal($"{spec}@1.0.0!./{variant}/", candidate.Archive.Key);
    Assert.Equal(
        [new ChainHop(reference, variant == "quickjs" ? 0 : 1),
         new ChainHop(Ref.Parse($"./{variant}/"), null)],
        candidate.Chain);
  }

  [Fact]
  public async Task Resolve_UnknownVariant_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod",
        Routing(new Artifact { Variant = "lua", Refs = ["./lua/"] }));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod#quickjs"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("variant \"quickjs\"", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_VariantOnConcrete_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/a.mcmod#lua"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_MostSpecificArtifact_Wins() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(
        Art("https://example.com/generic.mcmod"),
        new Artifact { Rids = ["win-x64"], Refs = ["https://example.com/win.mcmod"] }));
    _archives.AddUrl("https://example.com/generic.mcmod", Concrete("Generic", "1.0.0"));
    _archives.AddUrl("https://example.com/win.mcmod", Concrete("Win", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("Win", candidate.Package.Identity);
    Assert.Equal(1, candidate.Chain[0].ArtifactIndex);
  }

  [Fact]
  public async Task Resolve_TiedArtifacts_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(
        new Artifact { Rids = ["win-x64"], Refs = ["https://example.com/a.mcmod"] },
        new Artifact {
          Platforms = [McmodPlatform.Server],
          Refs = ["https://example.com/b.mcmod"],
        }));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("equally well", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_SeveralRefs_ConcatenatesInOrderAndSkipsFailures() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(Art(
        "https://example.com/missing.mcmod",
        "https://example.com/b.mcmod",
        "https://example.com/a.mcmod")));
    _archives.AddUrl("https://example.com/a.mcmod", Concrete("A", "1.0.0"));
    _archives.AddUrl("https://example.com/b.mcmod", Concrete("B", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Equal(["B", "A"], result.Candidates.Select(c => c.Package.Identity));
    string diagnostic = Assert.Single(result.Diagnostics);
    Assert.StartsWith(
        "https://example.com/r.mcmod[artifact 0] → https://example.com/missing.mcmod: ",
        diagnostic);
  }

  [Fact]
  public async Task Resolve_MissingTemplateKey_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod",
        Routing(Art("https://example.com/{{version}}.mcmod")));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("invalid document", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_InvalidDocument_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/bad.mcmod", null);

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/bad.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_Cycle_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", Routing(Art("https://example.com/b.mcmod")));
    _archives.AddUrl("https://example.com/b.mcmod", Routing(Art("https://example.com/a.mcmod")));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/a.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("cycle", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_SameArchiveDifferentVariant_IsNotACycle() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(
        new Artifact { Refs = ["https://example.com/r.mcmod#lua"] },
        new Artifact { Variant = "lua", Refs = ["./lua/"] }));
    _archives.AddRelative("https://example.com/r.mcmod", "./lua/", Concrete("Lua", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Equal(3, Assert.Single(result.Candidates).Chain.Count);
  }

  [Theory]
  [InlineData(8, true)]
  [InlineData(9, false)]
  public async Task Resolve_LongChain_LimitedToMaxHops(int hops, bool expectCandidate) {
    // Arrange
    for (int i = 1; i < hops; i++) {
      _archives.AddUrl($"https://example.com/{i}.mcmod",
          Routing(Art($"https://example.com/{i + 1}.mcmod")));
    }

    _archives.AddUrl($"https://example.com/{hops}.mcmod", Concrete("End", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/1.mcmod"), Workspace, null, _server);

    // Assert
    if (expectCandidate) {
      Assert.Equal(hops, Assert.Single(result.Candidates).Chain.Count);
    } else {
      Assert.Empty(result.Candidates);
      Assert.Contains("longer than 8 hops", Assert.Single(result.Diagnostics));
    }
  }

  [Fact]
  public async Task Resolve_ToothArchive_UsesToothCompatSelection() {
    // Arrange
    ToothArchiveDocument tooth = Tooth("github.com/user/tooth", "1.2.0");
    _archives.AddTag("github.com/user/tooth", "v1.2.0", tooth);
    Mock<IPackage> package = new();
    package.Setup(p => p.Version).Returns(SemVersion.Parse("1.2.0"));
    _toothCompat.Setup(t => t.Select(tooth, "client", _client)).Returns(package.Object);

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("github.com/user/tooth#client"), Workspace, null, _client,
        toothClientCompat: true);

    // Assert
    Assert.Same(package.Object, Assert.Single(result.Candidates).Package);
  }

  [Fact]
  public async Task Resolve_ToothNoVariant_NoCandidate() {
    // Arrange
    _archives.AddTag("github.com/user/tooth", "v1.2.0", Tooth("github.com/user/tooth", "1.2.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/tooth"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_ToothVersionOutsideRange_NoCandidate() {
    // Arrange
    ToothArchiveDocument tooth = Tooth("github.com/user/tooth", "1.2.0");
    _archives.AddUrl("https://example.com/t.zip", tooth);
    Mock<IPackage> package = new();
    package.Setup(p => p.Version).Returns(SemVersion.Parse("1.2.0"));
    _toothCompat.Setup(t => t.Select(tooth, "", _server)).Returns(package.Object);

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("https://example.com/t.zip"), Workspace, Range(">=2.0.0"), _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_ToothClientDepOnManifestArchive_UsesDefaultVariant() {
    // Arrange
    AddLegacyMoney();

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse(_legacyMoney + "#client"), Workspace, null, _client, toothClientCompat: true);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal(Ref.Parse(_legacyMoney), candidate.Chain[0].Ref);
    Assert.EndsWith("LegacyMoney-client-win-x64.mcmod", candidate.Archive.Key);
  }

  [Fact]
  public async Task Resolve_ClientVariantWithoutCompat_NoCandidate() {
    // Arrange
    AddLegacyMoney();

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse(_legacyMoney + "#client"), Workspace, null, _client);

    // Assert
    Assert.Empty(result.Candidates);
  }

  [Fact]
  public async Task Resolve_TagVersionMismatch_NoCandidate() {
    // Arrange
    _archives.AddTag("github.com/user/pkg", "v1.0.0", Concrete("Pkg", "1.0.1"));
    _archives.AddTag("github.com/user/routed", "v1.0.0",
        Routing("Routed", "1.0.1", Art("https://example.com/x.mcmod")));

    // Act
    ResolveResult concrete = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/pkg"), Workspace, null, _server);
    ResolveResult routing = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/routed"), Workspace, null, _server);

    // Assert
    Assert.Empty(concrete.Candidates);
    Assert.Contains("differs from the tag version", Assert.Single(concrete.Diagnostics));
    Assert.Empty(routing.Candidates);
    Assert.Contains("differs from the tag version", Assert.Single(routing.Diagnostics));
  }

  [Fact]
  public async Task Resolve_ToothVersionDiffersFromTag_IsNotRejected() {
    // Arrange
    ToothArchiveDocument tooth = Tooth("github.com/user/tooth", "1.0.1");
    _archives.AddTag("github.com/user/tooth", "v1.0.0", tooth);
    Mock<IPackage> package = new();
    package.Setup(p => p.Version).Returns(SemVersion.Parse("1.0.1"));
    _toothCompat.Setup(t => t.Select(tooth, "", _server)).Returns(package.Object);

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/tooth"), Workspace, null, _server);

    // Assert
    Assert.Single(result.Candidates);
  }

  [Fact]
  public async Task Resolve_RelativeArchiveInsideTag_SkipsTagVersionCheck() {
    // Arrange
    _archives.AddTag("github.com/user/pkg", "v1.0.0", Routing(Art("./sub/")));
    _archives.AddRelative("github.com/user/pkg@1.0.0", "./sub/", Concrete("Sub", "9.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/pkg"), Workspace, null, _server);

    // Assert
    Assert.Equal("Sub", Assert.Single(result.Candidates).Package.Identity);
  }

  [Fact]
  public async Task Resolve_RangeAcrossTags_FiltersAndOrdersByPrecedence() {
    // Arrange
    foreach (string version in new[] { "2.0.0", "1.5.0", "1.0.0", "0.9.0" }) {
      _archives.AddTag("github.com/user/pkg", $"v{version}", Concrete("Pkg", version));
    }

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("github.com/user/pkg"), Workspace, Range(">=1.0.0 <2.0.0"), _server);

    // Assert
    Assert.Equal(["1.5.0", "1.0.0"],
        result.Candidates.Select(c => c.Package.Version.ToString()));
    Assert.DoesNotContain("github.com/user/pkg@2.0.0", _archives.OpenedVersions);
  }

  [Fact]
  public async Task Resolve_RangeAppliesThroughRouting() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Routing(Art("github.com/user/pkg")));
    _archives.AddTag("github.com/user/pkg", "v2.0.0", Concrete("Pkg", "2.0.0"));
    _archives.AddTag("github.com/user/pkg", "v1.0.0", Concrete("Pkg", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("https://example.com/r.mcmod"), Workspace, Range("<2.0.0"), _server);

    // Assert
    Assert.Equal("1.0.0", Assert.Single(result.Candidates).Package.Version.ToString());
  }

  [Fact]
  public async Task Resolve_NoTagSatisfiesRange_NoCandidate() {
    // Arrange
    _archives.AddTag("github.com/user/pkg", "v1.0.0", Concrete("Pkg", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("github.com/user/pkg"), Workspace, Range(">=2.0.0"), _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("no tag version satisfies", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task ResolveLazy_FirstCandidateOnly_OpensOnlyFirstTag() {
    // Arrange
    for (int i = 100; i >= 1; i--) {
      _archives.AddTag("github.com/user/pkg", $"v1.0.{i}", Concrete("Pkg", $"1.0.{i}"));
    }

    // Act
    Candidate first = await CreateResolver()
        .ResolveLazy(
            Ref.Parse("github.com/user/pkg"), Workspace, null, _server,
            cancellationToken: TestContext.Current.CancellationToken)
        .FirstAsync(TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal("1.0.100", first.Package.Version.ToString());
    Assert.Equal(["github.com/user/pkg@1.0.100"], _archives.OpenedVersions);
  }

  [Fact]
  public async Task Resolve_SameRefTwice_ReadsArchiveOnce() {
    // Arrange
    AddLegacyMoney();
    Resolver resolver = CreateResolver();

    // Act
    await resolver.Resolve(Ref.Parse(_legacyMoney), Workspace, null, _server);
    ResolveResult second = await resolver
        .Resolve(Ref.Parse(_legacyMoney), Workspace, null, _server);

    // Assert
    Assert.Single(second.Candidates);
    Assert.All(_archives.ReadCounts.Values, count => Assert.Equal(1, count));
    Assert.Equal(1, _archives.ListCount);
  }

  /// In-memory archives: url, relative and spec targets mapped to documents. A null document
  /// stands for an invalid archive.
  private sealed class FakeArchives : IArchiveProvider, IArchiveReader {
    private readonly Dictionary<string, ArchiveDocument?> _documents = [];
    private readonly Dictionary<string, List<SpecVersion>> _tags = [];

    public List<string> OpenedVersions { get; } = [];

    public Dictionary<string, int> ReadCounts { get; } = [];

    public int ListCount { get; private set; }

    public HashSet<string> FailOnce { get; } = [];

    public void AddUrl(string url, ArchiveDocument? document) => _documents[url] = document;

    public void AddRelative(string baseKey, string target, ArchiveDocument document) =>
        _documents[$"{baseKey}!{target}"] = document;

    public void AddTag(string spec, string tag, ArchiveDocument document) {
      SpecVersion version = new(SemVersion.Parse(tag[1..]), tag);
      if (!_tags.TryGetValue(spec, out List<SpecVersion>? versions)) {
        _tags[spec] = versions = [];
      }

      versions.Add(version);
      versions.Sort((a, b) => b.Version.ComparePrecedenceTo(a.Version));
      _documents[$"{spec}@{version.Version}"] = document;
    }

    public ArchiveHandle OpenWorkspace(string workspaceRoot) => throw new NotSupportedException();

    public Task<ArchiveHandle> Open(Ref target, ArchiveHandle baseArchive) {
      string key = target.Kind == RefKind.Relative
          ? $"{baseArchive.Key}!{target.Target}"
          : target.Target;
      if (FailOnce.Remove(key)) {
        throw new ArchiveUnavailableException($"timeout {key}");
      }

      return _documents.ContainsKey(key)
          ? Task.FromResult(Handle(key))
          : throw new ArchiveUnavailableException($"404 {key}");
    }

    public Task<IReadOnlyList<SpecVersion>> ListVersions(string spec) {
      ListCount++;
      return _tags.TryGetValue(spec, out List<SpecVersion>? versions)
          ? Task.FromResult<IReadOnlyList<SpecVersion>>(versions)
          : throw new ArchiveUnavailableException($"no repository {spec}");
    }

    public Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version) {
      string key = $"{spec}@{version.Version}";
      OpenedVersions.Add(key);
      return Task.FromResult(Handle(key));
    }

    public Task<ArchiveHandle> OpenLocked(string key, ArchiveIdentity identity) =>
        throw new NotSupportedException();

    public Task<ArchiveDocument> Read(ArchiveHandle archive) {
      ReadCounts[archive.Key] = ReadCounts.GetValueOrDefault(archive.Key) + 1;
      return _documents[archive.Key] is ArchiveDocument document
          ? Task.FromResult(document)
          : throw new ArchiveUnavailableException($"invalid document in {archive.Key}");
    }

    private static ArchiveHandle Handle(string key) =>
        new(key, Mock.Of<ISource>(), new DirectoryArchiveIdentity(key));
  }
}
