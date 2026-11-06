using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Sources;

public class GitTreeSourceTests {
  private static readonly Url _repoUrl = new("https://example.com/owner/repo");
  private const string _commit = "0123456789abcdef0123456789abcdef01234567";

  // Maps git blob object ids to their content.
  private static readonly Dictionary<string, string> _blobs =
      new[] { "{}", "readme", "a", "b", "c", "exe", "a.dll" }.ToDictionary(Oid);

  private static readonly List<GitTreeEntry> _tree = [
    new("lip.yaml", 100644, Oid("{}")),
    new("a.dll", 100644, Oid("a")),
    new("sub/b.txt", 100644, Oid("b")),
    new("sub/c.txt", 100644, Oid("c")),
    new("run.sh", 100755, Oid("exe")),
    new("link.dll", 120000, Oid("a.dll")),
    new("group.txt", 100664, Oid("readme")),
  ];

  private static string Oid(string content) {
    byte[] bytes = Encoding.UTF8.GetBytes(content);
    return Convert.ToHexStringLower(SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"), .. bytes]));
  }

  private static Mock<IGitRemoteClient> CreateClient() {
    Mock<IGitRemoteClient> client = new();
    client.Setup(c => c.ListTree(_repoUrl, _commit)).ReturnsAsync(_tree);
    client.Setup(c => c.FetchBlobs(_repoUrl, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()))
        .Returns(async (Url _, IReadOnlyCollection<string> oids, Func<string, Stream, Task> onBlob) => {
          foreach (string oid in oids) {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(_blobs[oid]));
            await onBlob(oid, stream);
          }
        });

    return client;
  }

  private static void VerifyFetchBlobsCalls(Mock<IGitRemoteClient> client, Times times) =>
      client.Verify(c => c.FetchBlobs(
          It.IsAny<Url>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()), times);

  private static async Task<string> ReadAll(Stream stream) {
    using StreamReader reader = new(stream);
    return await reader.ReadToEndAsync();
  }

  [Fact]
  public void Keys_ReturnsModes100644And100755Only() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource source = new(client.Object, _repoUrl, _commit);

    // Act
    List<string> keys = [.. source.Keys];

    // Assert: the symlink (120000) and the group-writable file (100664) are not part of a git
    // archive (protocol/package.md §1).
    Assert.Equal(["lip.yaml", "a.dll", "sub/b.txt", "sub/c.txt", "run.sh"], keys);
  }

  [Fact]
  public async Task Keys_MultipleAccesses_ListTreeCalledOnce() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource source = await GitTreeSource.Create(client.Object, _repoUrl, _commit);

    // Act
    _ = source.Keys.ToList();
    _ = source.Keys.ToList();
    using Stream stream = await source.OpenRead("a.dll");
    await source.Prefetch(["sub/b.txt"]);

    // Assert
    client.Verify(c => c.ListTree(_repoUrl, _commit), Times.Once);
  }

  [Fact]
  public async Task Keys_ListTreeFailedOnce_Retries() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    client.SetupSequence(c => c.ListTree(_repoUrl, _commit))
        .ThrowsAsync(new HttpRequestException())
        .ReturnsAsync(_tree);
    GitTreeSource source = new(client.Object, _repoUrl, _commit);

    // Act & Assert
    await Assert.ThrowsAsync<HttpRequestException>(() => source.OpenRead("a.dll"));
    Assert.Equal(5, source.Keys.Count());
    Assert.Equal(5, source.Keys.Count());
    client.Verify(c => c.ListTree(_repoUrl, _commit), Times.Exactly(2));
  }

  [Fact]
  public async Task OpenRead_SameKeyTwice_FetchesOnlyThatBlobEachTime() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource source = new(client.Object, _repoUrl, _commit);

    // Act
    string first;
    using (Stream stream = await source.OpenRead("sub/b.txt")) {
      first = await ReadAll(stream);
    }
    string second;
    using (Stream stream = await source.OpenRead("sub/b.txt")) {
      second = await ReadAll(stream);
    }

    // Assert
    Assert.Equal("b", first);
    Assert.Equal("b", second);
    client.Verify(c => c.FetchBlobs(
        _repoUrl,
        It.Is<IReadOnlyCollection<string>>(o => o.SequenceEqual(new[] { Oid("b") })),
        It.IsAny<Func<string, Stream, Task>>()), Times.Exactly(2));
    client.Verify(c => c.ListTree(_repoUrl, _commit), Times.Once);
    client.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task OpenRead_AnotherInstance_FetchesAgain() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource first = new(client.Object, _repoUrl, _commit);
    await first.Prefetch(["lip.yaml"]);
    GitTreeSource second = new(client.Object, _repoUrl, _commit);

    // Act
    using Stream result = await second.OpenRead("lip.yaml");

    // Assert
    Assert.Equal("{}", await ReadAll(result));
    VerifyFetchBlobsCalls(client, Times.Exactly(2));
  }

  [Fact]
  public async Task Prefetch_MultipleKeys_FetchesOnlyThoseInSingleCallAndOpenReadUsesThem() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource source = new(client.Object, _repoUrl, _commit);
    string[] keys = ["a.dll", "sub/b.txt", "run.sh"];

    // Act
    await source.Prefetch(keys);
    List<string> contents = [];
    foreach (string key in keys) {
      using Stream stream = await source.OpenRead(key);
      contents.Add(await ReadAll(stream));
    }
    await source.Prefetch(keys);

    // Assert
    Assert.Equal(["a", "b", "exe"], contents);
    client.Verify(c => c.FetchBlobs(
        _repoUrl,
        It.Is<IReadOnlyCollection<string>>(o =>
            o.Count == 3 && o.ToHashSet().SetEquals(new[] { Oid("a"), Oid("b"), Oid("exe") })),
        It.IsAny<Func<string, Stream, Task>>()), Times.Once);
    VerifyFetchBlobsCalls(client, Times.Once());
  }

  [Fact]
  public async Task Prefetch_PartiallyPrefetched_FetchesOnlyMissingBlobs() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource source = new(client.Object, _repoUrl, _commit);
    await source.Prefetch(["a.dll"]);

    // Act
    await source.Prefetch(["a.dll", "sub/b.txt"]);

    // Assert
    client.Verify(c => c.FetchBlobs(
        _repoUrl,
        It.Is<IReadOnlyCollection<string>>(o => o.SequenceEqual(new[] { Oid("b") })),
        It.IsAny<Func<string, Stream, Task>>()), Times.Once);
  }

  [Fact]
  public async Task OpenRead_HashMismatch_Throws() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    client.Setup(c => c.FetchBlobs(_repoUrl, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()))
        .Returns(async (Url _, IReadOnlyCollection<string> oids, Func<string, Stream, Task> onBlob) => {
          foreach (string oid in oids) {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes("tampered"));
            await onBlob(oid, stream);
          }
        });
    GitTreeSource source = new(client.Object, _repoUrl, _commit);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidDataException>(() => source.OpenRead("a.dll"));
    await Assert.ThrowsAsync<InvalidDataException>(() => source.Prefetch(["a.dll"]));
    await Assert.ThrowsAsync<InvalidDataException>(() => source.OpenRead("a.dll"));
  }

  [Theory]
  [InlineData("missing.txt")]
  [InlineData("link.dll")]
  [InlineData("plugin/a.dll")]
  public async Task OpenRead_UnknownKey_Throws(string key) {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    GitTreeSource source = new(client.Object, _repoUrl, _commit);

    // Act & Assert
    ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => source.OpenRead(key));
    Assert.Contains($"There is no file '{key}'", ex.Message);
    Assert.DoesNotContain("(Parameter", ex.Message);
    await Assert.ThrowsAsync<ArgumentException>(() => source.Prefetch([key]));
    VerifyFetchBlobsCalls(client, Times.Never());
  }

  [Fact]
  public async Task OpenRead_BlobNotReturned_Throws() {
    // Arrange
    Mock<IGitRemoteClient> client = CreateClient();
    client.Setup(c => c.FetchBlobs(_repoUrl, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()))
        .Returns(Task.CompletedTask);
    GitTreeSource source = new(client.Object, _repoUrl, _commit);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(() => source.OpenRead("a.dll"));
    await Assert.ThrowsAsync<InvalidOperationException>(() => source.Prefetch(["a.dll"]));
  }
}
