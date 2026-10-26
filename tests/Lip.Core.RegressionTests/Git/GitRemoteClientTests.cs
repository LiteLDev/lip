using System.Text.Json;
using Flurl;
using Lip.Core.Git;
using Semver;

namespace Lip.Core.RegressionTests.Git;

public class GitRemoteClientTests {
  private const long MaxBytesTransferred = 4 * 1024 * 1024;

  [Theory]
  [InlineData("https://github.com/LiteLDev/LeviLamina.git", 90)]
  [InlineData("https://github.com/LiteLDev/LegacyScriptEngine.git", 100)]
  [InlineData("https://github.com/LiteLDev/MoreDimensions", 20)]
  public async Task ListRefsListTreeFetchBlobs_LatestTag_ReadsToothJson(string repo, int minTagCount) {
    // Arrange
    Url repoUrl = new(repo);
    using GitRemoteClient client = new(githubProxy: null);

    // Act
    IReadOnlyList<GitRemoteRef> refs = await client.ListRefs(repoUrl, ["refs/tags/v"]);
    GitRemoteRef latest = refs
        .Where(r => SemVersion.TryParse(r.Name["refs/tags/v".Length..], out _))
        .MaxBy(r => SemVersion.Parse(r.Name["refs/tags/v".Length..]), SemVersion.PrecedenceComparer)!;

    // Pass the ref's own object id: for annotated tags this exercises tag peeling.
    IReadOnlyList<GitTreeEntry> tree = await client.ListTree(repoUrl, latest.ObjectId);
    GitTreeEntry tooth = Assert.Single(tree, e => e.Path == "tooth.json");

    JsonDocument? manifest = null;
    await client.FetchBlobs(repoUrl, [tooth.ObjectId], async (objectId, stream) => {
      Assert.Equal(tooth.ObjectId, objectId);
      manifest = await JsonDocument.ParseAsync(stream);
    });

    // Assert
    TestContext.Current.TestOutputHelper?.WriteLine(
        $"{repo}: {refs.Count} tags, latest {latest.Name}, {tree.Count} files, "
        + $"{client.BytesReceived} bytes received");
    Assert.True(refs.Count >= minTagCount);
    Assert.All(refs, r => Assert.StartsWith("refs/tags/v", r.Name));
    Assert.Equal(100644, tooth.Mode);
    Assert.NotNull(manifest);
    Assert.True(manifest.RootElement.TryGetProperty("format_version", out _));
    Assert.True(client.BytesReceived < MaxBytesTransferred);
  }

  [Fact]
  public async Task ListTree_PeeledAndTagObjectIds_ReturnSameEntries() {
    Url repoUrl = new("https://github.com/LiteLDev/LeviLamina.git");
    using GitRemoteClient client = new(githubProxy: null);

    GitRemoteRef tag = (await client.ListRefs(repoUrl, ["refs/tags/v1.0.0"]))
        .Single(r => r.Name == "refs/tags/v1.0.0");
    IReadOnlyList<GitTreeEntry> byTag = await client.ListTree(repoUrl, tag.ObjectId);
    IReadOnlyList<GitTreeEntry> byCommit =
        await client.ListTree(repoUrl, tag.PeeledObjectId ?? tag.ObjectId);

    Assert.Equal(byTag, byCommit);
    Assert.NotEmpty(byTag);
  }

  [Fact]
  public async Task FetchBlobs_ManyBlobs_ReturnsVerifiedContent() {
    Url repoUrl = new("https://github.com/LiteLDev/LegacyScriptEngine.git");
    using GitRemoteClient client = new(githubProxy: null);
    GitRemoteRef head = (await client.ListRefs(repoUrl, ["refs/heads/"]))[0];
    IReadOnlyList<GitTreeEntry> tree = await client.ListTree(repoUrl, head.ObjectId);
    List<string> blobIds = [.. tree.Select(e => e.ObjectId).Distinct().Take(300)];

    HashSet<string> received = [];
    await client.FetchBlobs(repoUrl, blobIds, async (objectId, stream) => {
      MemoryStream content = new();
      await stream.CopyToAsync(content);
      Assert.Equal(objectId, GitObject.ComputeObjectId(GitObjectType.Blob, content.ToArray()));
      Assert.True(received.Add(objectId));
    });

    Assert.Equal(blobIds.Count, received.Count);
  }
}
