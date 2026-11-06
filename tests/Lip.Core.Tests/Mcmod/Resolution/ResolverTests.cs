using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Tests.Mcmod.Resolution;

/// Resolve(source, range, environment) (protocol/package.md §4), with package files and with
/// tooth.json files (protocol/compatibility.md §2), over in-memory archives.
public class ResolverTests {
  private const string Spec = "github.com/user/pkg";
  private const string Url = "https://example.com/pkg.zip";
  private const string Format3Uuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment s_server = new(McmodPlatform.Server, "win-x64");
  private static readonly LipEnvironment s_client = new(McmodPlatform.Client, "win-x64");

  private readonly FakeArchives _archives = new();
  private readonly CountingReader _reader = new();
  private readonly List<string> _warnings = [];

  private Resolver CreateResolver() => new(_archives, _reader, _warnings.Add);

  /// A lip.yaml, as a YAML flow mapping; variants is the JSON object of "variants" without
  /// braces.
  private static string LipYaml(string id, string version, string variants) => $$"""
      {
        "format": 4,
        "id": "{{id}}",
        "version": "{{version}}",
        "variants": { {{variants}} }
      }
      """;

  /// A lip.yaml whose variant "" has one entry for (server, win-x64).
  private static string Simple4(string id, string version, string members = "") =>
      LipYaml(id, version, $"\"\": [ {Entry("server", "win-x64", members)} ]");

  /// An entry of a package file with the given extra members, comma-separated without braces.
  private static string Entry(string platform, string runtime, string members = "") =>
      $$"""{ "platform": "{{platform}}", "runtime": "{{runtime}}"{{(members == "" ? "" : ", " + members)}} }""";

  /// The member "deps" with one dependency on a marker repository, which tells entries
  /// apart.
  private static string Marker(string name) =>
      $"\"deps\": {{ \"example.com/marker/{name}\": \"*\" }}";

  /// A tooth.json of format 3 with the given variants (Variant3).
  private static string Tooth3(string tooth, string version, params string[] variants) => $$"""
      {
        "format_version": 3,
        "format_uuid": "{{Format3Uuid}}",
        "tooth": "{{tooth}}",
        "version": "{{version}}",
        "variants": [ {{string.Join(", ", variants)}} ]
      }
      """;

  /// A variant of format 3 depending on example.com/marker/&lt;marker&gt;.
  private static string Variant3(string label, string platform, string marker) => $$"""
      {
        "label": "{{label}}",
        "platform": "{{platform}}",
        "dependencies": { "example.com/marker/{{marker}}": ">=1.0.0" }
      }
      """;

  private static VersionRange Range(string range) => VersionRange.Parse(range);

  private static Ref Source(string source) => Ref.Parse(source, allowFile: false);

  private static string MarkerOf(Candidate candidate) =>
      Assert.Single(candidate.Package.Entry.Dependencies).Source.Target["example.com/marker/".Length..];

  // Entries (protocol/package.md §4 step 2)

  [Fact]
  public async Task Resolve_Url_SelectsEntryOfExactPlatformAndRuntime() {
    // Arrange
    _archives.AddUrl(Url, LipYaml(Spec, "1.0.0", $"""
        "": [
          {Entry("server", "linux-x64", Marker("server-linux"))},
          {Entry("client", "win-x64", Marker("client-win"))},
          {Entry("server", "win-x64", Marker("server-win"))},
          {Entry("server", "win-arm64", Marker("server-arm"))}
        ]
        """));

    // Act
    ResolveResult server = await CreateResolver().Resolve(Source(Url), null, s_server);
    ResolveResult client = await CreateResolver().Resolve(Source(Url), null, s_client);

    // Assert
    Candidate candidate = Assert.Single(server.Candidates);
    Assert.Equal("server-win", MarkerOf(candidate));
    Assert.Equal(McmodPlatform.Server, candidate.Package.Entry.Platform);
    Assert.Equal("win-x64", candidate.Package.Entry.Runtime);
    Assert.Equal(Spec, candidate.Package.Identity);
    Assert.Equal("", candidate.Package.Variant);
    Assert.Equal(SemVersion.Parse("1.0.0"), candidate.Package.Version);
    Assert.Equal(Source(Url), candidate.Source);
    Assert.False(candidate.Package.Entry.Derived);
    Assert.Empty(server.Diagnostics);
    Assert.Equal("client-win", MarkerOf(Assert.Single(client.Candidates)));
  }

  [Theory]
  [InlineData("linux-x64")]
  [InlineData("win")]
  [InlineData("WIN-X64")]
  public async Task Resolve_NoEntryForRuntime_NoCandidate(string runtime) {
    // Arrange: runtimes are compared exactly, not as RID graphs or ignoring case.
    _archives.AddUrl(Url, Simple4(Spec, "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Source(Url), null, new LipEnvironment(McmodPlatform.Server, runtime));

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("no entry of the variant", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_NoEntryForPlatform_NoCandidate() {
    // Arrange
    _archives.AddUrl(Url, Simple4(Spec, "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Url), null, s_client);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  [Theory]
  [InlineData("", "default")]
  [InlineData("lua", "lua")]
  [InlineData("node/v20", "node")]
  public async Task Resolve_Variant_SelectsEntryOfVariantOfSource(string variant, string marker) {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", LipYaml(Spec, "1.0.0", $"""
        "": [ {Entry("server", "win-x64", Marker("default"))} ],
        "lua": [ {Entry("server", "win-x64", Marker("lua"))} ],
        "node/v20": [ {Entry("server", "win-x64", Marker("node"))} ]
        """));
    Ref source = Source(Spec).WithVariant(variant);

    // Act
    ResolveResult result = await CreateResolver().Resolve(source, null, s_server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal(marker, MarkerOf(candidate));
    Assert.Equal(variant, candidate.Package.Variant);
    Assert.Equal(variant == "" ? Spec : $"{Spec}#{variant}", candidate.Package.Identity);
    Assert.Equal(source, candidate.Source);
  }

  [Fact]
  public async Task Resolve_UnknownVariant_NoCandidate() {
    // Arrange
    _archives.AddUrl(Url, LipYaml(Spec, "1.0.0", $"\"lua\": [ {Entry("server", "win-x64")} ]"));

    // Act
    ResolveResult withoutVariant = await CreateResolver().Resolve(Source(Url), null, s_server);
    ResolveResult otherVariant = await CreateResolver()
        .Resolve(Source(Url + "#quickjs"), null, s_server);

    // Assert
    Assert.Empty(withoutVariant.Candidates);
    Assert.Contains("variant \"\"", Assert.Single(withoutVariant.Diagnostics));
    Assert.Empty(otherVariant.Candidates);
    Assert.Contains("variant \"quickjs\"", Assert.Single(otherVariant.Diagnostics));
  }

  [Fact]
  public async Task Resolve_UrlOrLocalPath_TakesPackageIdOfPackageFile() {
    // Arrange: only a remote repository requires the package id to be its own.
    string local = Ref.FromFile(OperatingSystem.IsWindows() ? @"C:\pkgs\a.zip" : "/pkgs/a.zip")
        .Target;
    _archives.AddUrl(Url, Simple4("example.com/other", "1.0.0"));
    _archives.AddUrl(local, Simple4("example.com/local", "2.0.0"));

    // Act
    ResolveResult url = await CreateResolver().Resolve(Source(Url), null, s_server);
    ResolveResult file = await CreateResolver().Resolve(
        Ref.Parse(local, allowFile: true), null, s_server);

    // Assert
    Assert.Equal("example.com/other", Assert.Single(url.Candidates).Package.Identity);
    Assert.Equal("example.com/local", Assert.Single(file.Candidates).Package.Identity);
  }

  [Fact]
  public async Task Resolve_UrlWithVersionOutsideRange_NoCandidate() {
    // Arrange
    _archives.AddUrl(Url, Simple4(Spec, "1.2.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Url), Range(">=2.0.0"), s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("does not satisfy", Assert.Single(result.Diagnostics));
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("not json")]
  [InlineData("""{ "format_version": 5 }""")]
  [InlineData(null)]
  public async Task Resolve_ArchiveNotAPackage_NoCandidate(string? content) {
    // Arrange: null stands for an archive without a package file.
    _archives.AddUrl(Url, content);

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Url), null, s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Single(result.Diagnostics);
  }

  [Fact]
  public async Task Resolve_ArchiveCannotBeObtained_NoCandidate() {
    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Url), null, s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("404", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_TransientOpenFailure_IsRetriedByLaterResolve() {
    // Arrange
    _archives.AddUrl(Url, Simple4(Spec, "1.0.0"));
    _archives.FailOnce.Add(Url);
    Resolver resolver = CreateResolver();

    // Act
    ResolveResult first = await resolver.Resolve(Source(Url), null, s_server);
    ResolveResult second = await resolver.Resolve(Source(Url), null, s_server);

    // Assert
    Assert.Empty(first.Candidates);
    Assert.Single(second.Candidates);
  }

  // Remote repositories (protocol/package.md §2.1)

  [Fact]
  public async Task Resolve_RemoteRepository_TakesEveryVersionTagInPrecedenceOrder() {
    // Arrange
    foreach (string version in new[] { "1.0.0", "2.0.0", "1.10.0", "1.2.0" }) {
      _archives.AddTag(Spec, $"v{version}", Simple4(Spec, version));
    }

    _archives.AddTag(Spec, "latest", Simple4(Spec, "9.0.0"));
    _archives.AddTag(Spec, "1.5.0", Simple4(Spec, "1.5.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), null, s_server);

    // Assert
    Assert.Equal(["2.0.0", "1.10.0", "1.2.0", "1.0.0"],
        result.Candidates.Select(c => c.Package.Version.ToString()));
    Assert.All(result.Candidates, c => Assert.NotNull(c.Archive.Commit));
  }

  [Fact]
  public async Task Resolve_RemoteRepositoryWithoutVersionTags_NoCandidate() {
    // Arrange
    _archives.AddTag(Spec, "latest", Simple4(Spec, "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), null, s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("no version tags", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_PackageIdOfOtherRepository_NoCandidate() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Simple4("github.com/user/other", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), null, s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("declares the package id github.com/user/other",
        Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_PackageIdDiffersInCase_IsCandidateWithPackageIdOfPackageFile() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Simple4("github.com/User/Pkg", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Source("github.com/USER/pkg"), null, s_server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("github.com/User/Pkg", candidate.Package.Id);
    Assert.Equal("github.com/User/Pkg", candidate.Package.Identity);
  }

  [Theory]
  [InlineData("1.0.1")]
  [InlineData("1.0.0-rc.1")]
  public async Task Resolve_Format4VersionDiffersFromTagVersion_NoCandidate(string declared) {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, declared));
    _archives.AddTag(Spec, "v0.9.0", Simple4(Spec, "0.9.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), null, s_server);

    // Assert
    Assert.Equal("0.9.0", Assert.Single(result.Candidates).Package.Version.ToString());
    Assert.Contains("not the tag version 1.0.0", Assert.Single(result.Diagnostics));
    Assert.Empty(_warnings);
  }

  [Fact]
  public async Task Resolve_Format3VersionDiffersFromTagVersion_TakesTagVersionAndWarnsOnce() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", WorkspaceHarness.Tooth(Spec, "1.0.1", "file.dll"));
    Resolver resolver = CreateResolver();

    // Act
    ResolveResult first = await resolver.Resolve(Source(Spec), null, s_server);
    ResolveResult second = await resolver.Resolve(Source(Spec), Range(">=1.0.0"), s_server);

    // Assert
    Assert.Equal(SemVersion.Parse("1.0.0"), Assert.Single(first.Candidates).Package.Version);
    Assert.Equal(SemVersion.Parse("1.0.0"), Assert.Single(second.Candidates).Package.Version);
    Assert.Empty(first.Diagnostics);
    Assert.Contains("using the tag version 1.0.0", Assert.Single(_warnings));
  }

  [Fact]
  public async Task Resolve_Format3VersionOutsideRangeButTagVersionInside_IsCandidate() {
    // Arrange: the range applies to the tag version, the version of the package.
    _archives.AddTag(Spec, "v2.0.0", WorkspaceHarness.Tooth(Spec, "1.0.0", "file.dll"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), Range("^2.0.0"), s_server);

    // Assert
    Assert.Equal("2.0.0", Assert.Single(result.Candidates).Package.Version.ToString());
  }

  [Fact]
  public async Task Resolve_Format3FromUrl_TakesDeclaredVersionWithoutWarning() {
    // Arrange
    _archives.AddUrl(Url, WorkspaceHarness.Tooth(Spec, "1.0.1", "file.dll"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Url), null, s_server);

    // Assert
    Assert.Equal("1.0.1", Assert.Single(result.Candidates).Package.Version.ToString());
    Assert.Empty(_warnings);
  }

  // Ranges (protocol/package.md §1: SemVersionRange.Parse semantics)

  [Theory]
  [InlineData(">=1.0.0 <2.0.0", "1.5.0,1.0.0")]
  [InlineData("^1.0.0", "1.5.0,1.0.0")]
  [InlineData("~1.0.0", "1.0.0")]
  [InlineData("1.*", "1.5.0,1.0.0")]
  [InlineData("*", "2.0.0,1.5.0,1.0.0,0.9.0")]
  [InlineData("*-*", "2.0.0,2.0.0-beta.1,1.5.0,1.0.0,0.9.0")]
  [InlineData("0.9.0 || 2.0.0", "2.0.0,0.9.0")]
  [InlineData(">=2.0.0-0", "2.0.0,2.0.0-beta.1")]
  public async Task Resolve_Range_FiltersTagVersions(string range, string expected) {
    // Arrange
    foreach (string version in new[] { "2.0.0", "2.0.0-beta.1", "1.5.0", "1.0.0", "0.9.0" }) {
      _archives.AddTag(Spec, $"v{version}", Simple4(Spec, version));
    }

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), Range(range), s_server);

    // Assert
    Assert.Equal(expected.Split(','), result.Candidates.Select(c => c.Package.Version.ToString()));
  }

  [Fact]
  public async Task Resolve_Range_OpensOnlyTagsItMatches() {
    // Arrange
    foreach (string version in new[] { "2.0.0", "1.5.0", "1.0.0" }) {
      _archives.AddTag(Spec, $"v{version}", Simple4(Spec, version));
    }

    // Act
    await CreateResolver().Resolve(Source(Spec), Range("<2.0.0"), s_server);

    // Assert
    Assert.Equal([$"{Spec}@v1.5.0", $"{Spec}@v1.0.0"], _archives.Opened);
  }

  [Fact]
  public async Task Resolve_NoTagSatisfiesRange_NoCandidate() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), Range(">=2.0.0"), s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("no tag version satisfies >=2.0.0", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task ResolveLazy_FirstCandidateOnly_OpensOnlyFirstTag() {
    // Arrange
    for (int i = 100; i >= 1; i--) {
      _archives.AddTag(Spec, $"v1.0.{i}", Simple4(Spec, $"1.0.{i}"));
    }

    // Act
    Candidate first = await CreateResolver()
        .ResolveLazy(Source(Spec), null, s_server)
        .FirstAsync(TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal("1.0.100", first.Package.Version.ToString());
    Assert.Equal([$"{Spec}@v1.0.100"], _archives.Opened);
  }

  [Fact]
  public async Task Resolve_SameSourceTwice_ListsAndReadsOnce() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));
    _archives.AddTag(Spec, "v2.0.0", Simple4(Spec, "2.0.0"));
    Resolver resolver = CreateResolver();

    // Act
    await resolver.Resolve(Source(Spec), null, s_server);
    ResolveResult second = await resolver.Resolve(Source(Spec), Range("^1.0.0"), s_server);

    // Assert
    Assert.Single(second.Candidates);
    Assert.All(_reader.Counts.Values, count => Assert.Equal(1, count));
    Assert.Equal(1, _archives.ListCount);
  }

  // Refs (protocol/package.md §2.1)

  [Fact]
  public async Task Resolve_VersionRef_OpensCommitOfTagByIdentity() {
    // Arrange
    string commit = _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));
    _archives.AddTag(Spec, "v2.0.0", Simple4(Spec, "2.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Source($"{Spec}@1.0.0"), null, s_server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("1.0.0", candidate.Package.Version.ToString());
    Assert.Equal(commit, candidate.Archive.Commit);
    Assert.Equal($"{Spec}@1.0.0", candidate.Archive.Key);
    Assert.Equal(Source($"{Spec}@1.0.0"), candidate.Source);
    Assert.Equal(0, _archives.ListCount);
    Assert.Equal([$"{Spec}@rev:{commit}"], _archives.Opened);
  }

  [Theory]
  [InlineData("1.0.0", "1.0.0", true)]
  [InlineData("1.0.0", "1.0.1", false)]
  [InlineData("tag:v1.0.0", "1.0.0", true)]
  [InlineData("tag:v1.0.0", "2.0.0", false)]
  [InlineData("tag:latest", "2.0.0", true)]
  [InlineData("tag:1.0.0", "2.0.0", true)]
  [InlineData("branch:main", "2.0.0", true)]
  [InlineData("rev", "2.0.0", true)]
  public async Task Resolve_Ref_RequiresTagVersionOnlyForVersionTags(
      string gitRef, string declared, bool expectCandidate) {
    // Arrange
    string commit = gitRef.StartsWith("branch:", StringComparison.Ordinal)
        ? _archives.AddBranch(Spec, gitRef["branch:".Length..], Simple4(Spec, declared))
        : _archives.AddTag(Spec,
            gitRef == "rev" ? "v9.9.9" : gitRef.StartsWith("tag:", StringComparison.Ordinal)
                ? gitRef["tag:".Length..] : $"v{gitRef}",
            Simple4(Spec, declared));
    string source = gitRef == "rev" ? $"{Spec}@rev:{commit}" : $"{Spec}@{gitRef}";

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(source), null, s_server);

    // Assert
    Assert.Equal(expectCandidate ? 1 : 0, result.Candidates.Count);
    if (expectCandidate) {
      Assert.Equal(declared, result.Candidates[0].Package.Version.ToString());
      Assert.Equal(commit, result.Candidates[0].Archive.Commit);
    }
  }

  [Theory]
  [InlineData("tag:v2.0.0", "2.0.0")]
  [InlineData("2.0.0", "2.0.0")]
  [InlineData("branch:main", "1.0.0")]
  public async Task Resolve_Format3AtRef_TakesTagVersionOnlyWhenItHasOne(
      string gitRef, string expected) {
    // Arrange
    string tooth = WorkspaceHarness.Tooth(Spec, "1.0.0", "file.dll");
    _archives.AddTag(Spec, "v2.0.0", tooth);
    _archives.AddBranch(Spec, "main", tooth);

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Source($"{Spec}@{gitRef}"), null, s_server);

    // Assert
    Assert.Equal(expected, Assert.Single(result.Candidates).Package.Version.ToString());
  }

  [Fact]
  public async Task Resolve_BranchRef_ResolvesOnceAndOpensCommit() {
    // Arrange
    string commit = _archives.AddBranch(Spec, "main", Simple4(Spec, "3.0.0-dev"));
    Resolver resolver = CreateResolver();

    // Act
    ResolveResult first = await resolver.Resolve(Source($"{Spec}@branch:main"), null, s_server);
    _archives.AddBranch(Spec, "main", Simple4(Spec, "3.0.1-dev"));
    ResolveResult second = await resolver.Resolve(Source($"{Spec}@branch:main"), null, s_server);

    // Assert: one operation sees one commit of the branch.
    Assert.Equal("3.0.0-dev", Assert.Single(first.Candidates).Package.Version.ToString());
    Assert.Equal("3.0.0-dev", Assert.Single(second.Candidates).Package.Version.ToString());
    Assert.Equal([$"branch:main"], _archives.ResolvedRefs);
    Assert.Equal([$"{Spec}@rev:{commit}"], _archives.Opened);
    Assert.Equal(0, _archives.ListCount);
  }

  [Fact]
  public async Task Resolve_RefOutsideRange_NoCandidate() {
    // Arrange
    _archives.AddBranch(Spec, "main", Simple4(Spec, "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Source($"{Spec}@branch:main"), Range(">=2.0.0"), s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("does not satisfy", Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task Resolve_RefOfOtherPackageId_NoCandidate() {
    // Arrange
    _archives.AddBranch(Spec, "main", Simple4("github.com/user/fork", "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Source($"{Spec}@branch:main"), null, s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains("declares the package id", Assert.Single(result.Diagnostics));
  }

  [Theory]
  [InlineData("branch:gone")]
  [InlineData("tag:gone")]
  [InlineData("3.0.0")]
  public async Task Resolve_MissingRef_NoCandidateWithDiagnostic(string gitRef) {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));

    // Act
    ResolveResult result = await CreateResolver()
        .Resolve(Source($"{Spec}@{gitRef}"), null, s_server);

    // Assert
    Assert.Empty(result.Candidates);
    Assert.Contains(gitRef, Assert.Single(result.Diagnostics));
  }

  [Fact]
  public async Task PinCommit_VersionRef_ResolvesToRecordedCommitAfterTagMoved() {
    // Arrange (protocol/workspace.md §5: @<version> gives the same commit when updating)
    string recorded = _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));
    _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));
    Resolver resolver = CreateResolver();

    // Act: the workspace recorded the source with another case and an uppercase commit.
    resolver.PinCommit(Source("github.com/USER/pkg@1.0.0"), recorded.ToUpperInvariant());
    ResolveResult result = await resolver.Resolve(Source($"{Spec}@1.0.0"), null, s_server);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal(recorded, candidate.Archive.Commit);
    Assert.Empty(_archives.ResolvedRefs);
  }

  [Theory]
  [InlineData("branch:main")]
  [InlineData("tag:v1.0.0")]
  public async Task PinCommit_BranchOrTagRef_IsIgnored(string gitRef) {
    // Arrange (protocol/workspace.md §5: branch: and tag: give the commit they point to now)
    string old = _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));
    _archives.AddBranch(Spec, "main", Simple4(Spec, "1.0.0"));
    string now = _archives.AddTag(Spec, "v1.0.0", Simple4(Spec, "1.0.0"));
    now = gitRef == "branch:main" ? _archives.AddBranch(Spec, "main", Simple4(Spec, "1.0.0")) : now;
    Resolver resolver = CreateResolver();

    // Act
    resolver.PinCommit(Source($"{Spec}@{gitRef}"), old);
    ResolveResult result = await resolver.Resolve(Source($"{Spec}@{gitRef}"), null, s_server);

    // Assert
    Assert.Equal(now, Assert.Single(result.Candidates).Archive.Commit);
  }

  // tooth.json (protocol/compatibility.md §2)

  [Fact]
  public async Task Resolve_Format3_DerivesEntryForEnvironment() {
    // Arrange: variants whose platform does not match the runtime are left out.
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0",
        Variant3("", "linux-x64", "linux"),
        Variant3("", "win-x64", "win")));

    // Act
    ResolveResult server = await CreateResolver().Resolve(Source(Spec), null, s_server);
    ResolveResult linux = await CreateResolver().Resolve(
        Source(Spec), null, new LipEnvironment(McmodPlatform.Server, "linux-x64"));
    ResolveResult osx = await CreateResolver().Resolve(
        Source(Spec), null, new LipEnvironment(McmodPlatform.Server, "osx-arm64"));

    // Assert
    Candidate candidate = Assert.Single(server.Candidates);
    Assert.True(candidate.Package.Entry.Derived);
    Assert.Equal(McmodPlatform.Server, candidate.Package.Entry.Platform);
    Assert.Equal("win-x64", candidate.Package.Entry.Runtime);
    Assert.Equal("win", MarkerOf(candidate));
    Assert.Equal(Spec, candidate.Package.Identity);
    Assert.Equal("linux", MarkerOf(Assert.Single(linux.Candidates)));
    Assert.Empty(osx.Candidates);
  }

  [Fact]
  public async Task Resolve_Format3_MergesVariantsOfOneLabel() {
    // Arrange: a variant without platform and one matching the runtime by glob are merged.
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0",
        Variant3("", "", "any"),
        Variant3("", "win-*", "win"),
        Variant3("", "linux-x64", "linux")));

    // Act
    ResolveResult result = await CreateResolver().Resolve(Source(Spec), null, s_server);

    // Assert
    Assert.Equal(["example.com/marker/any", "example.com/marker/win"],
        Assert.Single(result.Candidates).Package.Entry.Dependencies.Select(d => d.Source.Target));
  }

  [Fact]
  public async Task Resolve_Format3OnClient_DefaultVariantIsDerivedFromClientLabel() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0",
        Variant3("", "", "default"),
        Variant3("client", "", "client")));

    // Act
    ResolveResult server = await CreateResolver().Resolve(Source(Spec), null, s_server);
    ResolveResult client = await CreateResolver().Resolve(Source(Spec), null, s_client);
    ResolveResult clientLabel = await CreateResolver()
        .Resolve(Source($"{Spec}#client"), null, s_client);
    ResolveResult serverLabel = await CreateResolver()
        .Resolve(Source($"{Spec}#client"), null, s_server);

    // Assert
    Assert.Equal("default", MarkerOf(Assert.Single(server.Candidates)));
    Candidate candidate = Assert.Single(client.Candidates);
    Assert.Equal("client", MarkerOf(candidate));
    Assert.Equal(McmodPlatform.Client, candidate.Package.Entry.Platform);
    Assert.Equal(Spec, candidate.Package.Identity);
    Assert.Equal("client", MarkerOf(Assert.Single(clientLabel.Candidates)));
    Assert.Equal($"{Spec}#client", clientLabel.Candidates[0].Package.Identity);
    Assert.Equal("client", MarkerOf(Assert.Single(serverLabel.Candidates)));
  }

  [Fact]
  public async Task Resolve_Format3OnClientWithoutDefaultLabel_DefaultVariantIsClientLabel() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0", Variant3("client", "", "client")));

    // Act
    ResolveResult client = await CreateResolver().Resolve(Source(Spec), null, s_client);
    ResolveResult server = await CreateResolver().Resolve(Source(Spec), null, s_server);

    // Assert
    Assert.Equal("client", MarkerOf(Assert.Single(client.Candidates)));
    Assert.Empty(server.Candidates);
  }

  [Fact]
  public async Task Resolve_Format3OnClientWithClientLabelForOtherRuntime_HasNoDefaultVariant() {
    // Arrange: the label "client" exists but none of its variants matches the runtime, so the
    // variant "" derived from it does not exist either.
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0",
        Variant3("", "", "default"),
        Variant3("client", "linux-x64", "client")));

    // Act
    ResolveResult client = await CreateResolver().Resolve(Source(Spec), null, s_client);

    // Assert
    Assert.Empty(client.Candidates);
  }

  [Fact]
  public async Task Resolve_Format3_DependenciesAreClientCompat() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0", Variant3("", "", "a")));
    _archives.AddUrl(Url, Simple4("example.com/b", "1.0.0", Marker("b")));

    // Act
    ResolveResult legacy = await CreateResolver().Resolve(Source(Spec), null, s_server);
    ResolveResult format4 = await CreateResolver().Resolve(Source(Url), null, s_server);

    // Assert
    PackageDependency dependency = Assert.Single(legacy.Candidates[0].Package.Entry.Dependencies);
    Assert.True(dependency.ClientCompat);
    Assert.Equal(">=1.0.0", dependency.Range.Text);
    Assert.False(Assert.Single(format4.Candidates[0].Package.Entry.Dependencies).ClientCompat);
  }

  [Fact]
  public async Task Resolve_ClientCompatOnFormat4_NamesDefaultVariant() {
    // Arrange: a format 3 dependency on "<repo>#client" of a format 4 package.
    _archives.AddTag(Spec, "v1.0.0", LipYaml(Spec, "1.0.0", $"""
        "": [ {Entry("client", "win-x64", Marker("default"))} ],
        "client": [ {Entry("client", "win-x64", Marker("client"))} ]
        """));

    // Act
    ResolveResult compat = await CreateResolver().Resolve(
        Source($"{Spec}#client"), null, s_client, clientCompat: true);
    ResolveResult plain = await CreateResolver()
        .Resolve(Source($"{Spec}#client"), null, s_client);

    // Assert
    Candidate candidate = Assert.Single(compat.Candidates);
    Assert.Equal("default", MarkerOf(candidate));
    Assert.Equal("", candidate.Package.Variant);
    Assert.Equal(Spec, candidate.Package.Identity);
    Assert.Equal("client", MarkerOf(Assert.Single(plain.Candidates)));
    Assert.Equal($"{Spec}#client", plain.Candidates[0].Package.Identity);
  }

  [Fact]
  public async Task Resolve_ClientCompatOnFormat3_KeepsClientVariant() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", Tooth3(Spec, "1.0.0",
        Variant3("", "", "default"),
        Variant3("client", "", "client")));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Source($"{Spec}#client"), null, s_server, clientCompat: true);

    // Assert
    Candidate candidate = Assert.Single(result.Candidates);
    Assert.Equal("client", MarkerOf(candidate));
    Assert.Equal($"{Spec}#client", candidate.Package.Identity);
  }

  [Fact]
  public async Task Resolve_ClientCompatOnOtherVariant_KeepsVariant() {
    // Arrange
    _archives.AddTag(Spec, "v1.0.0", LipYaml(Spec, "1.0.0", $"""
        "": [ {Entry("server", "win-x64", Marker("default"))} ]
        """));

    // Act
    ResolveResult result = await CreateResolver().Resolve(
        Source($"{Spec}#lua"), null, s_server, clientCompat: true);

    // Assert
    Assert.Empty(result.Candidates);
  }

  /// Counts the reads of each archive by the real ArchiveReader.
  private sealed class CountingReader : IArchiveReader {
    private readonly ArchiveReader _inner = new();

    public Dictionary<string, int> Counts { get; } = [];

    public Task<PackageDocument> Read(ArchiveHandle archive) {
      Counts[archive.Key] = Counts.GetValueOrDefault(archive.Key) + 1;
      return _inner.Read(archive);
    }
  }

  /// In-memory archives: URLs and local paths, and remote repositories with tags, branches and
  /// commits. Each archive holds its lip.yaml or tooth.json only (FileName).
  private sealed class FakeArchives : IArchiveProvider {
    private readonly Dictionary<string, string?> _urls = [];
    private readonly Dictionary<string, Repository> _repositories = [];
    private int _commits;

    /// The archives opened from remote repositories, in order.
    public List<string> Opened { get; } = [];

    /// The refs resolved to commits, in order.
    public List<string> ResolvedRefs { get; } = [];

    public int ListCount { get; private set; }

    public HashSet<string> FailOnce { get; } = [];

    /// Adds the archive of a URL or a file: URI; null stands for an archive without
    /// a package file.
    public void AddUrl(string url, string? content) => _urls[url] = content;

    /// Adds (or moves) a tag to a new commit; returns the commit.
    public string AddTag(string spec, string tag, string content) {
      string commit = AddCommit(spec, content);
      Get(spec).Tags[tag] = commit;
      return commit;
    }

    /// Adds (or moves) a branch to a new commit; returns the commit.
    public string AddBranch(string spec, string branch, string content) {
      string commit = AddCommit(spec, content);
      Get(spec).Branches[branch] = commit;
      return commit;
    }

    public Task Forget(string url) => Task.CompletedTask;

    public Task<ArchiveHandle> Open(Ref target) {
      if (FailOnce.Remove(target.Target)) {
        throw new ArchiveUnavailableException($"timeout {target.Target}");
      }

      return _urls.TryGetValue(target.Target, out string? content)
          ? Task.FromResult(Handle(target.Target, content, commit: null))
          : throw new ArchiveUnavailableException($"404 {target.Target}");
    }

    public Task<IReadOnlyList<SpecVersion>> ListVersions(string spec) {
      ListCount++;
      List<SpecVersion> versions = [
        .. Get(spec).Tags.Keys
            .Select(t => (Tag: t, Version: ArchiveProvider.ParseVersionTag($"refs/tags/{t}")))
            .Where(t => t.Version is not null)
            .Select(t => new SpecVersion(t.Version!, t.Tag))
            .OrderByDescending(v => v.Version, SemVersion.PrecedenceComparer)
            .ThenByDescending(v => v.Tag, StringComparer.Ordinal),
      ];
      return Task.FromResult<IReadOnlyList<SpecVersion>>(versions);
    }

    public Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version) {
      Opened.Add($"{spec}@{version.Tag}");
      return Task.FromResult(GitHandle($"{spec}@{version.Version}", spec,
          Get(spec).Tags[version.Tag]));
    }

    public Task<string> ResolveGitRef(string spec, GitRef gitRef) {
      ResolvedRefs.Add(gitRef.ToString());
      Repository repository = Get(spec);
      string? commit = gitRef.Kind switch {
        GitRefKind.Version => repository.Tags.GetValueOrDefault($"v{gitRef.Name}"),
        GitRefKind.Branch => repository.Branches.GetValueOrDefault(gitRef.Name),
        GitRefKind.Tag => repository.Tags.GetValueOrDefault(gitRef.Name),
        _ => repository.Commits.ContainsKey(gitRef.Name) ? gitRef.Name : null,
      };
      return commit is not null
          ? Task.FromResult(commit)
          : throw new ArchiveUnavailableException($"{spec} has no {gitRef}");
    }

    public Task<ArchiveHandle> OpenCommit(string spec, string commit) {
      Opened.Add($"{spec}@rev:{commit}");
      return Task.FromResult(GitHandle($"{spec}@rev:{commit}", spec, commit));
    }

    public Task<Lip.Core.Sources.ISource> OpenAsset(IReadOnlyList<string> urls) =>
        throw new NotSupportedException();

    private string AddCommit(string spec, string content) {
      string commit = (++_commits).ToString("x40");
      Get(spec).Commits[commit] = content;
      return commit;
    }

    private Repository Get(string spec) {
      string key = PackageNames.Fold(spec);
      if (!_repositories.TryGetValue(key, out Repository? repository)) {
        repository = new Repository();
        _repositories[key] = repository;
      }

      return repository;
    }

    private ArchiveHandle GitHandle(string key, string spec, string commit) =>
        Handle(key, Get(spec).Commits[commit], commit);

    /// The name of the file holding content: tooth.json for a tooth.json (with "format_version"),
    /// lip.yaml otherwise.
    private static string FileName(string content) =>
        content.Contains("\"format_version\"", StringComparison.Ordinal) ? "tooth.json" : "lip.yaml";

    private static ArchiveHandle Handle(string key, string? content, string? commit) =>
        new(key,
            new ZipSource(WorkspaceHarness.Zip(content is null
                ? new() { ["readme.txt"] = "" }
                : new() { [FileName(content)] = content })),
            commit);

    private sealed class Repository {
      public Dictionary<string, string> Tags { get; } = [];

      public Dictionary<string, string> Branches { get; } = [];

      public Dictionary<string, string> Commits { get; } = [];
    }
  }
}

/// Every candidate of a source, in order, with the diagnostics reported on the way.
internal sealed record ResolveResult(List<Candidate> Candidates, List<string> Diagnostics);

internal static class ResolverTestExtensions {
  /// Collects the whole candidate sequence of ResolveLazy.
  public static async Task<ResolveResult> Resolve(
      this Resolver resolver,
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool clientCompat = false) {
    ResolveResult result = new([], []);
    await foreach (Candidate candidate in resolver.ResolveLazy(
        source, range, environment, clientCompat, result.Diagnostics.Add)) {
      result.Candidates.Add(candidate);
    }

    return result;
  }
}
