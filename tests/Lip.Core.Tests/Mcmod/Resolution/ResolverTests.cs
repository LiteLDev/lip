using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Resolution;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Resolution;

public class ResolverTests {
  private const string _legacyMoney = "github.com/LiteLDev/LegacyMoney";
  private const string _legacyMoneyTemplate = "https://github.com/LiteLDev/LegacyMoney/releases/"
      + "download/v{{version}}/LegacyMoney-{{platform}}-{{runtime}}.mcmod";

  private static readonly LipEnvironment _server = new(McmodPlatform.Server, "win-x64");
  private static readonly LipEnvironment _client = new(McmodPlatform.Client, "win-x64");

  private readonly FakeArchives _archives = new();
  private readonly List<string> _warnings = [];

  private Resolver CreateResolver() => new(_archives, _archives, new ToothCompat(), _warnings.Add);

  private static ArchiveHandle Workspace => new(
      "/workspace", Mock.Of<ISource>(), new ZipArchiveIdentity(TestIdentity.Sha256, "/workspace"));

  private static PackageArchiveDocument PackageDoc(
      string name,
      string version,
      McmodPlatform? platform = null,
      string? runtime = null) => new(
      new PackageDocument {
        Id = name,
        Version = SemVersion.Parse(version),
        IsMod = true,
        Platform = platform,
        Runtime = runtime,
      },
      JsonDocument.Parse($"{{\"name\":\"{name}\"}}").RootElement.Clone());

  private static MetaArchiveDocument Meta(params MetaTarget[] targets) =>
      new(new MetaDocument { Targets = targets });

  private static MetaArchiveDocument Meta(string version, params MetaTarget[] targets) =>
      new(new MetaDocument { Version = SemVersion.Parse(version), Targets = targets });

  private static MetaTarget Art(params string[] refs) => new() { Source = refs };

  /// A tooth.json with one variant per label, for every platform.
  private static ToothArchiveDocument Tooth(string path, string version, params string[] labels) =>
      new(
          new PackageManifest {
            Path = path,
            Version = SemVersion.Parse(version),
            Variants = [.. labels.Select(l => new PackageManifestVariant { Label = l })],
          },
          JsonDocument.Parse("{}").RootElement.Clone());

  private static IVersionConstraint Range(string range) =>
      new SemVersionRangeConstraint(SemVersionRange.Parse(range));

  private void AddLegacyMoney() {
    _archives.AddTag(_legacyMoney, "v0.22.0", Meta("0.22.0", new MetaTarget {
      Platforms = [McmodPlatform.Server, McmodPlatform.Client],
      Runtimes = ["win-x64"],
      Source = [_legacyMoneyTemplate],
    }));
    const string download = "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/";
    _archives.AddUrl(download + "LegacyMoney-server-win-x64.mcmod",
        PackageDoc("LegacyMoney", "0.22.0", McmodPlatform.Server, "win-x64"));
    _archives.AddUrl(download + "LegacyMoney-client-win-x64.mcmod",
        PackageDoc("LegacyMoney", "0.22.0", McmodPlatform.Client, "win-x64"));
  }

  [Fact]
  public async Task Resolve_SpecMetaToUrlTemplate_ProducesPackageWithChain() {
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
    Assert.Null(candidate.Chain[1].TargetIndex);
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
    Assert.StartsWith("github.com/LiteLDev/LegacyMoney@v0.22.0: no target", diagnostic);
  }

  [Fact]
  public async Task Resolve_TransientOpenFailure_IsRetriedByLaterResolve() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", PackageDoc("A", "1.0.0"));
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
  public async Task Resolve_PackagePlatformDiffers_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", PackageDoc("A", "1.0.0", McmodPlatform.Client));

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
  public async Task Resolve_VariantMeta_FollowsRelativeDirectory(string variant) {
    // Arrange
    const string spec = "github.com/LiteLDev/LegacyScriptEngine";
    _archives.AddTag(spec, "v1.0.0", Meta(
        new MetaTarget { Variant = "quickjs", Source = ["./quickjs/"] },
        new MetaTarget { Variant = "lua", Source = ["./lua/"] }));
    _archives.AddRelative($"{spec}@1.0.0", "./quickjs/", PackageDoc("lse-quickjs", "1.0.0"));
    _archives.AddRelative($"{spec}@1.0.0", "./lua/", PackageDoc("lse-lua", "1.0.0"));
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
        Meta(new MetaTarget { Variant = "lua", Source = ["./lua/"] }));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod#quickjs"), Workspace, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("variant \"quickjs\"", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_VariantOnPackage_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/a.mcmod", PackageDoc("A", "1.0.0"));

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
    _archives.AddUrl("https://example.com/r.mcmod", Meta(
        Art("https://example.com/generic.mcmod"),
        new MetaTarget { Runtimes = ["win-x64"], Source = ["https://example.com/win.mcmod"] }));
    _archives.AddUrl("https://example.com/generic.mcmod", PackageDoc("Generic", "1.0.0"));
    _archives.AddUrl("https://example.com/win.mcmod", PackageDoc("Win", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("Win", candidate.Package.Identity);
    Assert.Equal(1, candidate.Chain[0].TargetIndex);
  }

  [Fact]
  public async Task Resolve_TiedArtifacts_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Meta(
        new MetaTarget { Runtimes = ["win-x64"], Source = ["https://example.com/a.mcmod"] },
        new MetaTarget {
          Platforms = [McmodPlatform.Server],
          Source = ["https://example.com/b.mcmod"],
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
    _archives.AddUrl("https://example.com/r.mcmod", Meta(Art(
        "https://example.com/missing.mcmod",
        "https://example.com/b.mcmod",
        "https://example.com/a.mcmod")));
    _archives.AddUrl("https://example.com/a.mcmod", PackageDoc("A", "1.0.0"));
    _archives.AddUrl("https://example.com/b.mcmod", PackageDoc("B", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Assert.Equal(["B", "A"], result.Candidates.Select(c => c.Package.Identity));
    string diagnostic = Assert.Single(result.Diagnostics);
    Assert.StartsWith(
        "https://example.com/r.mcmod[target 0] → https://example.com/missing.mcmod: ",
        diagnostic);
  }

  [Fact]
  public async Task Resolve_MetaWithoutVersionAtTag_ExpandsTheTagVersion() {
    // Arrange
    _archives.AddTag(_legacyMoney, "v0.22.0", Meta(new MetaTarget { Source = [_legacyMoneyTemplate] }));
    _archives.AddUrl(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-server-win-x64.mcmod",
        PackageDoc("LegacyMoney", "0.22.0", McmodPlatform.Server, "win-x64"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse(_legacyMoney), Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.EndsWith("/v0.22.0/LegacyMoney-server-win-x64.mcmod", candidate.Archive.Key);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_MetaWithVersionOutsideGit_ExpandsTheDocumentVersion() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod",
        Meta("1.2.3", Art("https://example.com/{{version}}.mcmod")));
    _archives.AddUrl("https://example.com/1.2.3.mcmod", PackageDoc("Pkg", "1.2.3"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("https://example.com/r.mcmod"), Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("https://example.com/1.2.3.mcmod", candidate.Archive.Key);
  }

  [Fact]
  public async Task Resolve_MissingTemplateKey_NoCandidate() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod",
        Meta(Art("https://example.com/{{version}}.mcmod")));

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
    _archives.AddUrl("https://example.com/a.mcmod", Meta(Art("https://example.com/b.mcmod")));
    _archives.AddUrl("https://example.com/b.mcmod", Meta(Art("https://example.com/a.mcmod")));

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
    _archives.AddUrl("https://example.com/r.mcmod", Meta(
        new MetaTarget { Source = ["https://example.com/r.mcmod#lua"] },
        new MetaTarget { Variant = "lua", Source = ["./lua/"] }));
    _archives.AddRelative("https://example.com/r.mcmod", "./lua/", PackageDoc("Lua", "1.0.0"));

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
          Meta(Art($"https://example.com/{i + 1}.mcmod")));
    }

    _archives.AddUrl($"https://example.com/{hops}.mcmod", PackageDoc("End", "1.0.0"));

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
  public async Task Resolve_ToothArchive_ConvertsSelectedVariant() {
    // Arrange
    ToothArchiveDocument tooth = Tooth("github.com/user/tooth", "1.2.0", "", "client");
    _archives.AddTag("github.com/user/tooth", "v1.2.0", tooth);

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("github.com/user/tooth#client"), Workspace, null, _client,
        toothClientCompat: true);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    McmodPackage package = Assert.IsType<McmodPackage>(candidate.Package);
    Assert.Equal(ToothCompat.KindName, package.Kind);
    Assert.Equal("tooth-client", package.Identity);
    Assert.Equal("client", package.Variant);
    Assert.True(package.Document.IsFilePackage);
    Assert.Equal("github.com/user/tooth@1.2.0", candidate.Archive.Key);
    Assert.IsType<MountSource>(candidate.Archive.Source);
    Assert.Equal(Ref.Parse("github.com/user/tooth#client"), Assert.Single(candidate.Chain).Ref);
  }

  [Fact]
  public async Task Resolve_ToothNoVariant_NoCandidate() {
    // Arrange
    _archives.AddTag(
        "github.com/user/tooth", "v1.2.0", Tooth("github.com/user/tooth", "1.2.0", "client"));

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
    _archives.AddUrl("https://example.com/t.zip", Tooth("github.com/user/tooth", "1.2.0", ""));

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
    _archives.AddTag("github.com/user/pkg", "v1.0.0", PackageDoc("Pkg", "1.0.1"));
    _archives.AddTag("github.com/user/routed", "v1.0.0",
        Meta("1.0.1", Art("https://example.com/x.mcmod")));

    // Act
    ResolveResult package = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/pkg"), Workspace, null, _server);
    ResolveResult meta = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/routed"), Workspace, null, _server);

    // Assert
    Assert.Empty(package.Candidates);
    Assert.Contains("differs from the tag version", Assert.Single(package.Diagnostics));
    Assert.Empty(meta.Candidates);
    Assert.Contains("differs from the tag version", Assert.Single(meta.Diagnostics));
  }

  [Fact]
  public async Task Resolve_ToothVersionDiffersFromTag_UsesTagVersionAndWarns() {
    // Arrange
    _archives.AddTag("github.com/user/tooth", "v1.0.0", Tooth("github.com/user/tooth", "1.0.1", ""));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Ref.Parse("github.com/user/tooth"), Workspace, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal(SemVersion.Parse("1.0.0"), candidate.Package.Version);
    Assert.Equal("1.0.0", candidate.Package.Manifest.GetProperty("version").GetString());
    Assert.Contains("using the tag version 1.0.0", Assert.Single(_warnings));
  }

  [Fact]
  public async Task Resolve_RelativeArchiveInsideTag_SkipsTagVersionCheck() {
    // Arrange
    _archives.AddTag("github.com/user/pkg", "v1.0.0", Meta(Art("./sub/")));
    _archives.AddRelative("github.com/user/pkg@1.0.0", "./sub/", PackageDoc("Sub", "9.0.0"));

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
      _archives.AddTag("github.com/user/pkg", $"v{version}", PackageDoc("Pkg", version));
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
  public async Task Resolve_RangeAppliesThroughMeta() {
    // Arrange
    _archives.AddUrl("https://example.com/r.mcmod", Meta(Art("github.com/user/pkg")));
    _archives.AddTag("github.com/user/pkg", "v2.0.0", PackageDoc("Pkg", "2.0.0"));
    _archives.AddTag("github.com/user/pkg", "v1.0.0", PackageDoc("Pkg", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("https://example.com/r.mcmod"), Workspace, Range("<2.0.0"), _server);

    // Assert
    Assert.Equal("1.0.0", Assert.Single(result.Candidates).Package.Version.ToString());
  }

  [Fact]
  public async Task Resolve_NoTagSatisfiesRange_NoCandidate() {
    // Arrange
    _archives.AddTag("github.com/user/pkg", "v1.0.0", PackageDoc("Pkg", "1.0.0"));

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
      _archives.AddTag("github.com/user/pkg", $"v1.0.{i}", PackageDoc("Pkg", $"1.0.{i}"));
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

  [Fact]
  public async Task Resolve_BranchRef_ResolvesHeadCommitAndRecordsIt() {
    // Arrange: the branch routes to the release of the version it declares.
    const string commit = "0123456789abcdef0123456789abcdef01234567";
    AddLegacyMoney();
    _archives.AddGitRef(_legacyMoney, "branch:main", commit, Meta(
        "0.22.0", new MetaTarget { Source = [_legacyMoneyTemplate] }));
    Ref reference = Ref.Parse($"{_legacyMoney}@branch:main");

    // Act
    ResolveResult result = await CreateResolver().Resolve(reference, null, null, _server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal(new ChainHop(reference, 0, commit), candidate.Chain[0]);
    Assert.Equal([$"{_legacyMoney}@rev:{commit}"], _archives.OpenedVersions);
    Assert.Equal(0, _archives.ListCount);
  }

  [Theory]
  [InlineData("tag:v1.0.0", "2.0.0", false)]
  [InlineData("tag:v2.0.0", "2.0.0", true)]
  [InlineData("tag:latest", "2.0.0", true)]
  [InlineData("branch:main", "2.0.0", true)]
  public async Task Resolve_GitRef_ChecksDocumentAgainstSemVerTagNameOnly(
      string gitRef, string documentVersion, bool expectCandidate) {
    // Arrange
    const string spec = "example.com/x";
    _archives.AddGitRef(spec, gitRef, new string('a', 40), PackageDoc("X", documentVersion));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse($"{spec}@{gitRef}"), null, null, _server);

    // Assert
    Assert.Equal(expectCandidate ? 1 : 0, result.Candidates.Count);
  }

  [Fact]
  public async Task Resolve_GitRefOutsideRange_NoCandidate() {
    // Arrange
    const string spec = "example.com/x";
    _archives.AddGitRef(spec, "branch:main", new string('a', 40), PackageDoc("X", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse($"{spec}@branch:main"), null, Range(">=2.0.0"), _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("does not satisfy", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_MissingBranch_NoCandidateWithDiagnostic() {
    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("example.com/x@branch:gone"), null, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("branch:gone", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_RelativeRefWithoutBase_NoCandidate() {
    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Ref.Parse("./pkg/"), null, null, _server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  /// In-memory archives: url, relative and spec targets mapped to documents. A null document
  /// stands for an invalid archive.
  private sealed class FakeArchives : IArchiveProvider, IArchiveReader {
    private readonly Dictionary<string, ArchiveDocument?> _documents = [];
    private readonly Dictionary<string, List<SpecVersion>> _tags = [];
    private readonly Dictionary<string, string> _gitRefs = [];

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

    /// Adds a git ref ("branch:main", "tag:x") of a spec repository pointing to a commit.
    public void AddGitRef(string spec, string gitRef, string commit, ArchiveDocument document) {
      _gitRefs[$"{spec}@{gitRef}"] = commit;
      _documents[$"{spec}@rev:{commit}"] = document;
    }

    public Task<ArchiveHandle> Open(Ref target, ArchiveHandle? baseArchive) {
      string key = target.Kind == RefKind.Relative
          ? $"{baseArchive?.Key}!{target.Target}"
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

    public Task<string> ResolveGitRef(string spec, GitRef gitRef) =>
        gitRef.Kind == GitRefKind.Rev ? Task.FromResult(gitRef.Name)
        : _gitRefs.TryGetValue($"{spec}@{gitRef}", out string? commit) ? Task.FromResult(commit)
        : throw new ArchiveUnavailableException($"no {gitRef} in {spec}");

    public Task<bool> RedirectsTo(string spec, string target) => Task.FromResult(false);

    public Task<ArchiveHandle> OpenCommit(string spec, string commit) {
      string key = $"{spec}@rev:{commit}";
      OpenedVersions.Add(key);
      return _documents.ContainsKey(key)
          ? Task.FromResult(new ArchiveHandle(
              key, Mock.Of<ISource>(), new GitArchiveIdentity($"https://{spec}.git", commit, "")))
          : throw new ArchiveUnavailableException($"no commit {commit} in {spec}");
    }

    public Task<ArchiveHandle> OpenLocked(string key, ArchiveIdentity identity) =>
        throw new NotSupportedException();

    public Task<ISource> OpenAsset(IReadOnlyList<string> urls, bool isArchive) =>
        throw new NotSupportedException();

    public Task<ArchiveDocument> Read(ArchiveHandle archive) {
      ReadCounts[archive.Key] = ReadCounts.GetValueOrDefault(archive.Key) + 1;
      return _documents[archive.Key] is ArchiveDocument document
          ? Task.FromResult(document)
          : throw new ArchiveUnavailableException($"invalid document in {archive.Key}");
    }

    private static ArchiveHandle Handle(string key) =>
        new(key, Mock.Of<ISource>(), new ZipArchiveIdentity(TestIdentity.Sha256, key));
  }
}
