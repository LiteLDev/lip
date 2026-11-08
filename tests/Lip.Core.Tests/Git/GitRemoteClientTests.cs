using System.Text;
using Flurl;
using Flurl.Http.Testing;
using Lip.Core.Git;

namespace Lip.Core.Tests.Git;

public class GitRemoteClientTests {
  private const string RepoUrl = "https://example.com/owner/repo.git";

  private static readonly byte[] s_readme = Encoding.UTF8.GetBytes("# readme\n");
  private static readonly byte[] s_script = Encoding.UTF8.GetBytes("#!/bin/sh\necho hi\n");
  private static readonly byte[] s_packageFile = Encoding.UTF8.GetBytes("format: 4\n");

  [Fact]
  public async Task ListRefs_ParsesRefsAndPeeledTags() {
    // Arrange
    using HttpTest httpTest = new();
    string a = new('a', 40);
    string b = new('b', 40);
    string c = new('c', 40);
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.PktLines(
        $"{a} refs/tags/v1.0.0 peeled:{b}",
        $"{c} refs/tags/v1.1.0",
        $"{c} refs/heads/main",
        "FLUSH")));
    GitRemoteClient client = new(githubProxy: null);

    // Act
    IReadOnlyList<GitRemoteRef> refs = await client.ListRefs(new Url(RepoUrl), ["refs/tags/v"]);

    // Assert
    Assert.Equal([
      new GitRemoteRef("refs/tags/v1.0.0", a, b),
      new GitRemoteRef("refs/tags/v1.1.0", c, null),
    ], refs);
    httpTest.ShouldHaveCalled($"{RepoUrl}/info/refs?service=git-upload-pack")
        .WithVerb(HttpMethod.Get)
        .WithHeader("Git-Protocol", "version=2")
        .WithHeader("User-Agent", "git/2.0 (lip)");
    string body = await RequestBody(httpTest, 1);
    Assert.Contains("command=ls-refs\n", body);
    Assert.Contains("ref-prefix refs/tags/v\n", body);
    Assert.Contains("peel\n", body);
    httpTest.ShouldHaveCalled($"{RepoUrl}/git-upload-pack")
        .WithVerb(HttpMethod.Post)
        .WithContentType("application/x-git-upload-pack-request");
  }

  [Fact]
  public async Task ListRefs_ServerWithoutV2_Throws() {
    using HttpTest httpTest = new();
    httpTest.RespondWith(() => Content(PackBuilder.PktLines(
        "# service=git-upload-pack", "FLUSH",
        $"{new string('a', 40)} HEAD\0multi_ack side-band-64k", "FLUSH")));
    GitRemoteClient client = new(githubProxy: null);

    GitProtocolException ex = await Assert.ThrowsAsync<GitProtocolException>(
        () => client.ListRefs(new Url(RepoUrl), []));
    Assert.Contains("version 2", ex.Message);
  }

  [Fact]
  public async Task ListRefs_HttpError_Throws() {
    using HttpTest httpTest = new();
    httpTest.RespondWith("not found", 404);
    GitRemoteClient client = new(githubProxy: null);

    await Assert.ThrowsAsync<HttpRequestException>(() => client.ListRefs(new Url(RepoUrl), []));
  }

  [Fact]
  public async Task ListRefs_GithubUnauthorized_SaysMissingOrPrivateAndKeepsStatus() {
    using HttpTest httpTest = new();
    httpTest.RespondWith("Repository not found.", 401);
    GitRemoteClient client = new(githubProxy: null);

    HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
        () => client.ListRefs(new Url("https://github.com/owner/missing.git"), []));

    Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
    Assert.Contains("does not exist or is private", ex.Message);
    HttpRequestException inner = Assert.IsType<HttpRequestException>(ex.InnerException);
    Assert.Equal("Repository not found.", inner.Data["Response body"]);
  }

  [Fact]
  public async Task ListRefs_GithubProxy_RewritesUrl() {
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.PktLines("FLUSH")));
    GitRemoteClient client = new(new Url("https://proxy.example.com/gh"));

    await client.ListRefs(new Url("https://github.com/LiteLDev/LeviLamina.git"), []);

    httpTest.ShouldHaveCalled(
        "https://proxy.example.com/gh/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack");
    httpTest.ShouldHaveCalled("https://proxy.example.com/gh/LiteLDev/LeviLamina.git/git-upload-pack");
  }

  [Fact]
  public async Task ListRefs_GithubProxyFails_FallsBackToGithubForEachRequest() {
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("https://proxy.example.com/*").RespondWith("not found", 404);
    httpTest.ForCallsTo("https://github.com/*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("https://github.com/*/git-upload-pack").RespondWith(() => Content(PackBuilder.PktLines(
        "0123456789abcdef0123456789abcdef01234567 refs/tags/v1.0.0", "FLUSH")));
    GitRemoteClient client = new(new Url("https://proxy.example.com/gh"));
    Url repoUrl = new("https://github.com/LiteLDev/LeviLamina.git");

    IReadOnlyList<GitRemoteRef> first = await client.ListRefs(repoUrl, ["refs/tags/v"]);
    IReadOnlyList<GitRemoteRef> second = await client.ListRefs(repoUrl, ["refs/tags/v"]);

    Assert.Equal("refs/tags/v1.0.0", Assert.Single(first).Name);
    Assert.Equal(first, second);
    // A request that fails through the proxy goes to GitHub directly; the next one tries the
    // proxy again (protocol/userspace.md §2).
    httpTest.ShouldHaveCalled("https://proxy.example.com/*").Times(2);
    httpTest.ShouldHaveCalled("https://github.com/LiteLDev/LeviLamina.git/git-upload-pack").Times(2);
  }

  [Fact]
  public async Task ListRefs_GithubProxyAndGithubFail_ThrowsAggregateWithBothErrors() {
    using HttpTest httpTest = new();
    httpTest.RespondWith("not found", 404);
    GitRemoteClient client = new(new Url("https://proxy.example.com/gh"));

    AggregateException ex = await Assert.ThrowsAsync<AggregateException>(() =>
        client.ListRefs(new Url("https://github.com/LiteLDev/LeviLamina.git"), []));

    Assert.Equal(2, ex.InnerExceptions.Count);
    Assert.Contains("GitHub proxy proxy.example.com and then GitHub directly", ex.Message);
    Assert.All(ex.InnerExceptions, e => Assert.Equal(
        System.Net.HttpStatusCode.NotFound, Assert.IsType<HttpRequestException>(e).StatusCode));
  }

  [Fact]
  public async Task FetchBlobs_GithubProxySendsIncompletePack_FetchesMissingBlobsDirectly() {
    (byte[] withoutBlobs, _, Dictionary<string, string> blobIds) = CreateRepositoryPack(includeBlobs: false);
    (byte[] withBlobs, _, _) = CreateRepositoryPack(includeBlobs: true);
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("https://proxy.example.com/*/git-upload-pack")
        .RespondWith(() => Content(PackBuilder.FetchResponse(withoutBlobs)));
    httpTest.ForCallsTo("https://github.com/*/git-upload-pack")
        .RespondWith(() => Content(PackBuilder.FetchResponse(withBlobs)));
    GitRemoteClient client = new(new Url("https://proxy.example.com/gh"));

    List<string> received = [];
    await client.FetchBlobs(
        new Url("https://github.com/owner/repo.git"),
        [blobIds["lip.toml"], blobIds["README.md"]],
        (objectId, _) => {
          received.Add(objectId);
          return Task.CompletedTask;
        });

    Assert.Equal(new[] { blobIds["README.md"], blobIds["lip.toml"] }.Order(), received.Order());
    httpTest.ShouldHaveCalled("https://github.com/owner/repo.git/git-upload-pack").Times(1);
  }

  [Theory]
  [InlineData(404)]
  [InlineData(429)]
  [InlineData(502)]
  public async Task ListRefs_HttpError_DoesNotRetry(int status) {
    using HttpTest httpTest = new();
    httpTest.RespondWith("error", status);
    GitRemoteClient client = new(githubProxy: null);

    await Assert.ThrowsAsync<HttpRequestException>(() => client.ListRefs(new Url(RepoUrl), []));

    httpTest.ShouldHaveCalled($"{RepoUrl}/info/refs*").Times(1);
  }

  [Fact]
  public async Task ListRefs_RedirectedRepository_PostsToRedirectTarget() {
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("https://github.com/Old/Name.git/info/refs*")
        .RespondWith("", 301, new { Location = "https://github.com/New/Name.git/info/refs?service=git-upload-pack" });
    httpTest.ForCallsTo("https://github.com/New/Name.git/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.PktLines("FLUSH")));
    GitRemoteClient client = new(githubProxy: null);

    await client.ListRefs(new Url("https://github.com/Old/Name.git"), []);

    // Without Git-Protocol the redirect target would answer with protocol version 0.
    httpTest.ShouldHaveCalled("https://github.com/New/Name.git/info/refs*")
        .WithHeader("Git-Protocol", "version=2")
        .WithHeader("User-Agent", "git/2.0 (lip)");
    httpTest.ShouldHaveCalled("https://github.com/New/Name.git/git-upload-pack");
    httpTest.ShouldNotHaveCalled("https://github.com/Old/Name.git/git-upload-pack");
  }

  [Fact]
  public async Task ListTree_SameCommitTwice_FetchesOnceAcrossGithubCaseVariants() {
    (byte[] pack, string tagId, _) = CreateRepositoryPack(includeBlobs: false);
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.FetchResponse(pack)));
    GitRemoteClient client = new(githubProxy: null);

    IReadOnlyList<GitTreeEntry> first = await client.ListTree(new Url("https://github.com/Owner/Repo.git"), tagId);
    IReadOnlyList<GitTreeEntry> second = await client.ListTree(new Url("https://github.com/owner/repo"), tagId);

    Assert.Same(first, second);
    httpTest.ShouldHaveCalled("*/git-upload-pack").Times(1);
  }

  [Fact]
  public async Task ListTree_WithFilter_ReturnsBlobEntriesThroughAnnotatedTag() {
    // Arrange
    (byte[] pack, string tagId, Dictionary<string, string> blobIds) = CreateRepositoryPack(includeBlobs: false);
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.FetchResponse(pack)));
    GitRemoteClient client = new(githubProxy: null);

    // Act
    IReadOnlyList<GitTreeEntry> entries = await client.ListTree(new Url(RepoUrl), tagId.ToUpperInvariant());

    // Assert
    Assert.Equal([
      new GitTreeEntry("README.md", 100644, blobIds["README.md"]),
      new GitTreeEntry("bin/link", 120000, blobIds["README.md"]),
      new GitTreeEntry("bin/run.sh", 100755, blobIds["bin/run.sh"]),
      new GitTreeEntry("lip.toml", 100644, blobIds["lip.toml"]),
    ], entries);
    Assert.Equal(100644, entries.Single(e => e.Path == "lip.toml").Mode);

    string body = await RequestBody(httpTest, 1);
    Assert.Contains($"want {tagId}\n", body);
    Assert.Contains("deepen 1\n", body);
    Assert.Contains("filter blob:none\n", body);
    Assert.Contains("no-progress\n", body);
    Assert.Contains("done\n", body);
  }

  [Theory]
  [InlineData("..")]
  [InlineData(".")]
  [InlineData("")]
  [InlineData("a/b")]
  [InlineData(@"a\b")]
  [InlineData(@"..\..\x")]
  public async Task ListTree_UnsafeEntryName_Throws(string name) {
    byte[] tree = PackBuilder.Tree(("100644", name, new string('a', 40)));
    byte[] commit = PackBuilder.Commit(PackBuilder.ObjectId(GitObjectType.Tree, tree));
    PackBuilder builder = new();
    builder.AddObject(GitObjectType.Commit, commit);
    builder.AddObject(GitObjectType.Tree, tree);
    byte[] pack = builder.Build();
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.FetchResponse(pack)));
    GitRemoteClient client = new(githubProxy: null);

    await Assert.ThrowsAsync<GitProtocolException>(() => client.ListTree(
        new Url(RepoUrl), PackBuilder.ObjectId(GitObjectType.Commit, commit)));
  }

  [Theory]
  [InlineData(".git")]
  [InlineData("a b.")]
  public async Task ListTree_NameGitDoesNotCheckOut_IsListed(string name) {
    // protocol/package.md §1 restricts the names in a git archive only by "", ".", "..", '/'
    // and '\'.
    string blobId = new('a', 40);
    byte[] tree = PackBuilder.Tree(("100644", name, blobId));
    byte[] commit = PackBuilder.Commit(PackBuilder.ObjectId(GitObjectType.Tree, tree));
    PackBuilder builder = new();
    builder.AddObject(GitObjectType.Commit, commit);
    builder.AddObject(GitObjectType.Tree, tree);
    byte[] pack = builder.Build();
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.FetchResponse(pack)));
    GitRemoteClient client = new(githubProxy: null);

    IReadOnlyList<GitTreeEntry> entries = await client.ListTree(
        new Url(RepoUrl), PackBuilder.ObjectId(GitObjectType.Commit, commit));

    Assert.Equal([new GitTreeEntry(name, 100644, blobId)], entries);
  }

  [Theory]
  [InlineData(100644, true)]
  [InlineData(100755, true)]
  [InlineData(100664, false)]
  [InlineData(120000, false)]
  public void IsRegularFile_OnlyModes100644And100755(int mode, bool expected) {
    Assert.Equal(expected, new GitTreeEntry("a", mode, new string('a', 40)).IsRegularFile);
  }

  [Fact]
  public async Task ListTree_ErrorBand_Throws() {
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.PktLines(
        "packfile", (byte[])[3, .. "upload-pack: not our ref"u8], "FLUSH")));
    GitRemoteClient client = new(githubProxy: null);

    GitProtocolException ex = await Assert.ThrowsAsync<GitProtocolException>(
        () => client.ListTree(new Url(RepoUrl), new string('a', 40)));
    Assert.Contains($"has no commit {new string('a', 40)}", ex.Message);
    Assert.Contains("not our ref", ex.InnerException!.Message);
  }

  [Fact]
  public async Task ListTree_ErrLine_Throws() {
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack").RespondWith(() => Content(PackBuilder.PktLines(
        "ERR something broke", "FLUSH")));
    GitRemoteClient client = new(githubProxy: null);

    GitProtocolException ex = await Assert.ThrowsAsync<GitProtocolException>(
        () => client.ListTree(new Url(RepoUrl), new string('a', 40)));
    Assert.Contains("example.com reported an error", ex.Message);
    Assert.Contains("something broke", ex.InnerException!.Message);
  }

  [Fact]
  public async Task ListTree_NotAnObjectId_Throws() {
    GitRemoteClient client = new(githubProxy: null);

    await Assert.ThrowsAsync<ArgumentException>(() => client.ListTree(new Url(RepoUrl), "main"));
  }

  [Theory]
  [InlineData("shallow")]
  [InlineData("filter")]
  [InlineData("")]
  public async Task ListTree_WithoutShallowPartialFetch_ThrowsWithoutFetching(string fetchFeatures) {
    // Arrange
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement(fetchFeatures)));
    GitRemoteClient client = new(githubProxy: null);

    // Act & Assert: the repository is never cloned in full.
    await Assert.ThrowsAsync<GitProtocolException>(
        () => client.ListTree(new Url(RepoUrl), new string('a', 40)));
    httpTest.ShouldNotHaveCalled("*/git-upload-pack");
  }

  [Fact]
  public async Task FetchBlobs_ReturnsEachBlobOnce() {
    // Arrange
    PackBuilder builder = new();
    long baseOffset = builder.AddObject(GitObjectType.Blob, s_readme);
    byte[] modified = Encoding.UTF8.GetBytes("# readme\nmore\n");
    builder.AddOfsDelta(baseOffset,
        PackBuilder.Delta(s_readme.Length, modified.Length, (0, s_readme.Length), "more\n"u8.ToArray()));
    string readmeId = PackBuilder.ObjectId(GitObjectType.Blob, s_readme);
    string modifiedId = PackBuilder.ObjectId(GitObjectType.Blob, modified);

    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack")
        .RespondWith(() => Content(PackBuilder.FetchResponse(builder.Build(), withShallowInfo: false)));
    GitRemoteClient client = new(githubProxy: null);
    Dictionary<string, byte[]> blobs = [];

    // Act
    await client.FetchBlobs(new Url(RepoUrl), [readmeId, modifiedId, readmeId], async (id, stream) => {
      MemoryStream copy = new();
      await stream.CopyToAsync(copy);
      blobs.Add(id, copy.ToArray());
    });

    // Assert
    Assert.Equal(s_readme, blobs[readmeId]);
    Assert.Equal(modified, blobs[modifiedId]);
    string body = await RequestBody(httpTest, 1);
    Assert.Contains($"want {readmeId}\n", body);
    Assert.Contains($"want {modifiedId}\n", body);
    Assert.DoesNotContain("deepen", body);
  }

  [Fact]
  public async Task FetchBlobs_ManyIds_BatchesRequests() {
    using HttpTest httpTest = new();
    httpTest.ForCallsTo("*/info/refs*").RespondWith(() => Content(PackBuilder.Advertisement()));
    httpTest.ForCallsTo("*/git-upload-pack")
        .RespondWith(() => Content(PackBuilder.FetchResponse(new PackBuilder().Build(), false)));
    GitRemoteClient client = new(githubProxy: null);
    string[] ids = [.. Enumerable.Range(0, 300).Select(i => i.ToString("x40"))];

    await Assert.ThrowsAsync<GitProtocolException>(
        () => client.FetchBlobs(new Url(RepoUrl), ids, (_, _) => Task.CompletedTask));

    Assert.Equal(3, httpTest.CallLog.Count);
  }

  [Fact]
  public async Task FetchBlobs_Empty_MakesNoRequests() {
    using HttpTest httpTest = new();
    GitRemoteClient client = new(githubProxy: null);

    await client.FetchBlobs(new Url(RepoUrl), [], (_, _) => Task.CompletedTask);

    httpTest.ShouldNotHaveMadeACall();
  }

  private static (byte[] Pack, string TagId, Dictionary<string, string> BlobIds) CreateRepositoryPack(
      bool includeBlobs) {
    string readmeId = PackBuilder.ObjectId(GitObjectType.Blob, s_readme);
    string scriptId = PackBuilder.ObjectId(GitObjectType.Blob, s_script);
    string packageFileId = PackBuilder.ObjectId(GitObjectType.Blob, s_packageFile);
    byte[] binTree = PackBuilder.Tree(("120000", "link", readmeId), ("100755", "run.sh", scriptId));
    string binTreeId = PackBuilder.ObjectId(GitObjectType.Tree, binTree);
    byte[] rootTree = PackBuilder.Tree(
        ("100644", "README.md", readmeId),
        ("40000", "bin", binTreeId),
        ("160000", "submodule", new string('d', 40)),
        ("100644", "lip.toml", packageFileId));
    string rootTreeId = PackBuilder.ObjectId(GitObjectType.Tree, rootTree);
    byte[] commit = PackBuilder.Commit(rootTreeId);
    byte[] tag = PackBuilder.Tag(PackBuilder.ObjectId(GitObjectType.Commit, commit));

    PackBuilder builder = new();
    builder.AddObject(GitObjectType.Tag, tag);
    builder.AddObject(GitObjectType.Commit, commit);
    builder.AddObject(GitObjectType.Tree, rootTree);
    builder.AddObject(GitObjectType.Tree, binTree);
    if (includeBlobs) {
      builder.AddObject(GitObjectType.Blob, s_readme);
      builder.AddObject(GitObjectType.Blob, s_script);
      builder.AddObject(GitObjectType.Blob, s_packageFile);
    }

    return (builder.Build(), PackBuilder.ObjectId(GitObjectType.Tag, tag), new() {
      ["README.md"] = readmeId,
      ["bin/run.sh"] = scriptId,
      ["lip.toml"] = packageFileId,
    });
  }

  private static ByteArrayContent Content(byte[] bytes) {
    return new ByteArrayContent(bytes);
  }

  private static async Task<string> RequestBody(HttpTest httpTest, int callIndex) {
    return await httpTest.CallLog[callIndex].HttpRequestMessage.Content!.ReadAsStringAsync();
  }
}
