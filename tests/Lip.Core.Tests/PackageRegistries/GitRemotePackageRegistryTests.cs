using System.Text;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.PackageRegistries;
using Moq;
using Semver;

namespace Lip.Core.Tests.PackageRegistries;

public class GitRemotePackageRegistryTests {
  private const string _repoUrl = "https://github.com/test/pkg.git";

  private static readonly PackageId _packageId = new("github.com/test/pkg", "");

  private static Mock<IGitRemoteClient> CreateClient(params GitRemoteRef[] refs) {
    Mock<IGitRemoteClient> mock = new();
    mock.Setup(c => c.ListRefs(
            It.Is<Url>(u => u.ToString() == _repoUrl),
            It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "refs/tags/v" }))))
        .ReturnsAsync(refs);
    return mock;
  }

  private static GitRemoteRef Tag(string name, string oid = "0000", string? peeled = null) =>
      new($"refs/tags/{name}", oid, peeled);

  private static string ManifestJson(string version) => $$"""
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "github.com/test/pkg",
        "version": "{{version}}",
        "info": { "name": "Test Package" },
        "variants": []
      }
      """;

  private static void SetupManifest(Mock<IGitRemoteClient> mock, string commit, string blobOid, string version) {
    mock.Setup(c => c.ListTree(It.Is<Url>(u => u.ToString() == _repoUrl), commit))
        .ReturnsAsync([
            new GitTreeEntry("README.md", 0x81A4, "readme"),
            new GitTreeEntry("sub/tooth.json", 0x81A4, "nested"),
            new GitTreeEntry("tooth.json", 0x81A4, blobOid),
        ]);
    mock.Setup(c => c.FetchBlobs(
            It.Is<Url>(u => u.ToString() == _repoUrl),
            It.Is<IReadOnlyCollection<string>>(o => o.SequenceEqual(new[] { blobOid })),
            It.IsAny<Func<string, Stream, Task>>()))
        .Returns(async (Url _, IReadOnlyCollection<string> _, Func<string, Stream, Task> onBlob) => {
          using MemoryStream stream = new(Encoding.UTF8.GetBytes(ManifestJson(version)));
          await onBlob(blobOid, stream);
        });
  }

  public static TheoryData<string[], string[]> RealWorldTagLists => new() {
    // github.com/LiteLDev/LeviLamina (excerpt): major >= 2 tags are listed by goproxy as +incompatible.
    {
      ["v0.13.5", "v1.0.0-rc.1", "v1.0.0", "v1.9.9", "v26.10.0", "v26.10.14", "v26.51.4"],
      ["0.13.5", "1.0.0-rc.1", "1.0.0", "1.9.9", "26.10.0", "26.10.14", "26.51.4"]
    },
    // github.com/LiteLDev/bedrock-runtime-data (excerpt): prereleases plus non-semver tags.
    {
      ["1.21.0.03", "1.21.0.03-server", "1.21.70.04-server.1", "old", "v26.10.4-build.6", "v26.32.2-client.1", "v26.32.2-server.3"],
      ["26.10.4-build.6", "26.32.2-client.1", "26.32.2-server.3"]
    },
    // github.com/VincentZyuApps/levilamina-plugin-mclistener-ws-server (excerpt): build metadata is dropped.
    {
      ["v0.6.3-alpha.1+20260605", "v0.6.3-alpha.3", "v0.6.5-alpha.31"],
      ["0.6.3-alpha.1", "0.6.3-alpha.3", "0.6.5-alpha.31"]
    },
    // github.com/wo55555/Playback (excerpt): build-like prerelease labels.
    {
      ["v0.1.0-alpha.1", "v0.3.3-mc26.10", "v0.3.3-mc26.50"],
      ["0.1.0-alpha.1", "0.3.3-mc26.10", "0.3.3-mc26.50"]
    },
    // github.com/kuangby/FuckMovementAuthority, github.com/carefly/RLXWarp, github.com/Nico6719/YEssential:
    // tags with an uppercase V, four components or no v prefix are not versions (goproxy ignores them as well).
    {
      ["V26.51.2", "v1.7.0.1", "2.0.0", "2.10.7-test", "v1.7.0"],
      ["1.7.0"]
    },
  };

  [Theory]
  [MemberData(nameof(RealWorldTagLists))]
  public async Task GetAvailableVersions_RealWorldTags_ParsesLikeGoModuleProxy(string[] tags, string[] expected) {
    // Arrange
    GitRemotePackageRegistry registry = new(CreateClient([.. tags.Select(t => Tag(t))]).Object);

    // Act
    List<SemVersion> result = [.. await registry.GetAvailableVersions(_packageId)];

    // Assert
    Assert.Equal(expected.Select(v => SemVersion.Parse(v)).Order(SemVersion.PrecedenceComparer), result);
  }

  [Fact]
  public async Task GetAvailableVersions_CanonicalizesShortTagsAndDedupes() {
    // Arrange
    GitRemotePackageRegistry registry = new(CreateClient(
        Tag("v1.2"),
        Tag("v1.2.0"),
        Tag("v2"),
        Tag("v1.0.0+build.1"),
        Tag("v1.0.0+build.2"),
        Tag("v01.0.0"),
        Tag("v1.0.0-01"),
        Tag("v1.0.0^{}")).Object);

    // Act
    List<SemVersion> result = [.. await registry.GetAvailableVersions(_packageId)];

    // Assert
    Assert.Equal([SemVersion.Parse("1.0.0"), SemVersion.Parse("1.2.0"), SemVersion.Parse("2.0.0")], result);
  }

  [Fact]
  public async Task GetAvailableVersions_IsSortedOldestFirst() {
    // Arrange
    GitRemotePackageRegistry registry = new(CreateClient(
        Tag("v1.10.0"), Tag("v1.2.0"), Tag("v1.2.0-rc.1"), Tag("v0.9.0")).Object);

    // Act
    List<SemVersion> result = [.. await registry.GetAvailableVersions(_packageId)];

    // Assert
    Assert.Equal(
        [SemVersion.Parse("0.9.0"), SemVersion.Parse("1.2.0-rc.1"), SemVersion.Parse("1.2.0"), SemVersion.Parse("1.10.0")],
        result);
  }

  [Fact]
  public async Task GetAvailableVersions_MemoizesRefsAcrossVariantsAndManifests() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient(Tag("v1.0.0", "c1"));
    SetupManifest(client, "c1", "b1", "1.0.0");
    GitRemotePackageRegistry registry = new(client.Object);

    // Act
    await registry.GetAvailableVersions(_packageId);
    await registry.GetAvailableVersions(new PackageId("github.com/test/pkg", "client"));
    await registry.GetPackageManifest(new PackageSpec(_packageId, SemVersion.Parse("1.0.0")));

    // Assert
    client.Verify(c => c.ListRefs(It.IsAny<Url>(), It.IsAny<IEnumerable<string>>()), Times.Once);
  }

  [Fact]
  public async Task GetAvailableVersions_DoesNotCacheFailures() {
    // Arrange
    Mock<IGitRemoteClient> client = new();
    client.SetupSequence(c => c.ListRefs(It.IsAny<Url>(), It.IsAny<IEnumerable<string>>()))
        .ThrowsAsync(new HttpRequestException("boom"))
        .ReturnsAsync([Tag("v1.0.0")]);
    GitRemotePackageRegistry registry = new(client.Object);

    // Act & Assert
    await Assert.ThrowsAsync<HttpRequestException>(() => registry.GetAvailableVersions(_packageId));
    Assert.Equal([SemVersion.Parse("1.0.0")], [.. await registry.GetAvailableVersions(_packageId)]);
  }

  [Fact]
  public async Task GetPackageManifest_LightweightTag_FetchesRootToothJson() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient(Tag("v1.0.0", "commit1"));
    SetupManifest(client, "commit1", "blob1", "1.0.0");
    GitRemotePackageRegistry registry = new(client.Object);

    // Act
    PackageManifest manifest = await registry.GetPackageManifest(
        new PackageSpec(_packageId, SemVersion.Parse("1.0.0")));

    // Assert
    Assert.Equal("github.com/test/pkg", manifest.Path);
    Assert.Equal(SemVersion.Parse("1.0.0"), manifest.Version);
  }

  [Fact]
  public async Task GetPackageManifest_AnnotatedTag_UsesPeeledCommit() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient(Tag("v2.0.0", "tagobj", "commit2"));
    SetupManifest(client, "commit2", "blob2", "2.0.0");
    GitRemotePackageRegistry registry = new(client.Object);

    // Act
    PackageManifest manifest = await registry.GetPackageManifest(
        new PackageSpec(_packageId, SemVersion.Parse("2.0.0")));

    // Assert
    Assert.Equal(SemVersion.Parse("2.0.0"), manifest.Version);
    client.Verify(c => c.ListTree(It.IsAny<Url>(), "tagobj"), Times.Never);
  }

  [Fact]
  public async Task ResolveTag_NonCanonicalTag_MapsBackToActualRef() {
    // Arrange
    GitRemotePackageRegistry registry = new(CreateClient(
        Tag("v1.2", "short"), Tag("v1.3.0+meta", "meta"), Tag("v1.4", "short14"), Tag("v1.4.0", "full14")).Object);

    // Act & Assert
    Assert.Equal("refs/tags/v1.2",
        (await registry.ResolveTag(new PackageSpec(_packageId, SemVersion.Parse("1.2.0")))).Name);
    Assert.Equal("meta", await registry.ResolveCommit(new PackageSpec(_packageId, SemVersion.Parse("1.3.0"))));
    Assert.Equal("full14", await registry.ResolveCommit(new PackageSpec(_packageId, SemVersion.Parse("1.4.0"))));
  }

  [Fact]
  public async Task GetPackageManifest_MissingTag_Throws() {
    // Arrange
    GitRemotePackageRegistry registry = new(CreateClient(Tag("v1.0.0")).Object);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        registry.GetPackageManifest(new PackageSpec(_packageId, SemVersion.Parse("9.9.9"))));
    Assert.Contains("9.9.9", ex.Message);
  }

  [Fact]
  public async Task GetPackageManifest_MissingToothJson_Throws() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient(Tag("v1.0.0", "c1"));
    client.Setup(c => c.ListTree(It.IsAny<Url>(), "c1"))
        .ReturnsAsync([new GitTreeEntry("sub/tooth.json", 0x81A4, "nested")]);
    GitRemotePackageRegistry registry = new(client.Object);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        registry.GetPackageManifest(new PackageSpec(_packageId, SemVersion.Parse("1.0.0"))));
    Assert.Contains("tooth.json", ex.Message);
    client.Verify(c => c.FetchBlobs(
        It.IsAny<Url>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()), Times.Never);
  }

  [Fact]
  public async Task GetPackageManifest_SymlinkToothJson_Throws() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient(Tag("v1.0.0", "c1"));
    client.Setup(c => c.ListTree(It.IsAny<Url>(), "c1"))
        .ReturnsAsync([new GitTreeEntry("tooth.json", 0xA000, "link")]);
    GitRemotePackageRegistry registry = new(client.Object);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        registry.GetPackageManifest(new PackageSpec(_packageId, SemVersion.Parse("1.0.0"))));
    Assert.Contains("120000", ex.Message);
  }

  [Theory]
  [InlineData("refs/tags/v1.2.3", "1.2.3")]
  [InlineData("refs/tags/v1", "1.0.0")]
  [InlineData("refs/tags/v2.0.0+incompatible", "2.0.0")]
  [InlineData("refs/tags/1.2.3", null)]
  [InlineData("refs/heads/v1.2.3", null)]
  [InlineData("refs/tags/v1.2-rc.1", null)]
  public void ParseTagVersion_ParsesLikeGoSemver(string refName, string? expected) {
    Assert.Equal(expected is null ? null : SemVersion.Parse(expected), GitRemotePackageRegistry.ParseTagVersion(refName));
  }

  [Fact]
  public void GetRepoUrl_ReturnsCanonicalHttpsUrl() {
    Assert.Equal(_repoUrl, GitRemotePackageRegistry.GetRepoUrl(_packageId).ToString());
  }
}
