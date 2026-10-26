using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
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
    new("README.md", 100644, Oid("readme")),
    new("tooth.json", 100644, Oid("{}")),
    new("plugin/tooth.json", 100644, Oid("{}")),
    new("plugin/a.dll", 100644, Oid("a")),
    new("plugin/sub/b.txt", 100644, Oid("b")),
    new("plugin/sub/c.txt", 100644, Oid("c")),
    new("plugin/run.sh", 100755, Oid("exe")),
    new("plugin/link.dll", 120000, Oid("a.dll")),
    new("pluginx/other.txt", 100644, Oid("c")),
  ];

  private static string Oid(string content) {
    byte[] bytes = Encoding.UTF8.GetBytes(content);
    return Convert.ToHexStringLower(SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"), .. bytes]));
  }

  private static (Mock<IGitRemoteClient>, CacheService) CreateMocks(MockFileSystem? fileSystem = null) {
    Mock<IGitRemoteClient> client = new();
    client.Setup(c => c.ListTree(_repoUrl, _commit)).ReturnsAsync(_tree);
    client.Setup(c => c.FetchBlobs(_repoUrl, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()))
        .Returns(async (Url _, IReadOnlyCollection<string> oids, Func<string, Stream, Task> onBlob) => {
          foreach (string oid in oids) {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(_blobs[oid]));
            await onBlob(oid, stream);
          }
        });

    CacheService cacheService = new(fileSystem ?? new MockFileSystem(), new Mock<IUserInteraction>().Object);

    return (client, cacheService);
  }

  private static void VerifyFetchBlobsCalls(Mock<IGitRemoteClient> client, Times times) =>
      client.Verify(c => c.FetchBlobs(
          It.IsAny<Url>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()), times);

  private static async Task<string> ReadAll(Stream stream) {
    using StreamReader reader = new(stream);
    return await reader.ReadToEndAsync();
  }

  [Fact]
  public void Keys_NoSubdirectory_ReturnsRegularFilesOnly() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit);

    // Act
    List<string> keys = [.. source.Keys];

    // Assert
    Assert.Equal(8, keys.Count);
    Assert.Contains("plugin/sub/b.txt", keys);
    Assert.Contains("plugin/run.sh", keys);
    Assert.DoesNotContain("plugin/link.dll", keys);
  }

  [Theory]
  [InlineData("plugin")]
  [InlineData("plugin/")]
  [InlineData("/plugin")]
  [InlineData("./plugin")]
  [InlineData("plugin\\")]
  public void Keys_Subdirectory_ReturnsRelativeKeysUnderSubdirectory(string subdirectory) {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, subdirectory);

    // Act
    List<string> keys = [.. source.Keys];

    // Assert
    Assert.Equal(["tooth.json", "a.dll", "sub/b.txt", "sub/c.txt", "run.sh"], keys);
  }

  [Fact]
  public void Constructor_ParentSubdirectory_Throws() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();

    // Act & Assert
    Assert.Throws<ArgumentException>(() => new GitTreeSource(client.Object, cacheService, _repoUrl, _commit, "../x"));
  }

  [Fact]
  public async Task Keys_MultipleAccesses_ListTreeCalledOnce() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = await GitTreeSource.Create(client.Object, cacheService, _repoUrl, _commit, "plugin");

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
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    client.SetupSequence(c => c.ListTree(_repoUrl, _commit))
        .ThrowsAsync(new HttpRequestException())
        .ReturnsAsync(_tree);
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");

    // Act & Assert
    await Assert.ThrowsAsync<HttpRequestException>(() => source.OpenRead("a.dll"));
    Assert.Equal(5, source.Keys.Count());
    Assert.Equal(5, source.Keys.Count());
    client.Verify(c => c.ListTree(_repoUrl, _commit), Times.Exactly(2));
  }

  [Fact]
  public async Task OpenRead_SameKeyTwice_FetchesOnceThenHitsCache() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");

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
        It.IsAny<Func<string, Stream, Task>>()), Times.Once);
    client.Verify(c => c.ListTree(_repoUrl, _commit), Times.Once);
    client.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task OpenRead_CachedByAnotherInstance_DoesNotFetch() {
    // Arrange
    MockFileSystem fileSystem = new();
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks(fileSystem);
    GitTreeSource first = new(client.Object, cacheService, _repoUrl, _commit);
    using (Stream stream = await first.OpenRead("tooth.json")) { }
    GitTreeSource second = new(client.Object, cacheService, _repoUrl, _commit, "plugin");

    // Act
    using Stream result = await second.OpenRead("tooth.json");
    await second.Prefetch(["tooth.json"]);

    // Assert
    Assert.Equal("{}", await ReadAll(result));
    VerifyFetchBlobsCalls(client, Times.Once());
  }

  [Fact]
  public async Task OpenRead_HashMismatch_ThrowsAndDoesNotCache() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    client.Setup(c => c.FetchBlobs(_repoUrl, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()))
        .Returns(async (Url _, IReadOnlyCollection<string> oids, Func<string, Stream, Task> onBlob) => {
          foreach (string oid in oids) {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes("tampered"));
            await onBlob(oid, stream);
          }
        });
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");

    // Act & Assert
    await Assert.ThrowsAsync<InvalidDataException>(() => source.OpenRead("a.dll"));
    await Assert.ThrowsAsync<InvalidDataException>(() => source.Prefetch(["a.dll"]));
    await Assert.ThrowsAsync<InvalidDataException>(() => source.OpenRead("a.dll"));
  }

  [Fact]
  public async Task Prefetch_MultipleKeys_FetchesInSingleCallAndOpenReadHitsCache() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");
    string[] keys = ["a.dll", "sub/b.txt", "sub/c.txt", "run.sh"];

    // Act
    await source.Prefetch(keys);
    List<string> contents = [];
    foreach (string key in keys) {
      using Stream stream = await source.OpenRead(key);
      contents.Add(await ReadAll(stream));
    }
    await source.Prefetch(keys);

    // Assert
    Assert.Equal(["a", "b", "c", "exe"], contents);
    client.Verify(c => c.FetchBlobs(
        _repoUrl,
        It.Is<IReadOnlyCollection<string>>(o =>
            o.Count == 4 && o.ToHashSet().SetEquals(new[] { Oid("a"), Oid("b"), Oid("c"), Oid("exe") })),
        It.IsAny<Func<string, Stream, Task>>()), Times.Once);
    VerifyFetchBlobsCalls(client, Times.Once());
  }

  [Fact]
  public async Task Prefetch_PartiallyCached_FetchesOnlyMissingBlobs() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");
    using (Stream stream = await source.OpenRead("a.dll")) { }

    // Act
    await source.Prefetch(["a.dll", "sub/b.txt"]);

    // Assert
    client.Verify(c => c.FetchBlobs(
        _repoUrl,
        It.Is<IReadOnlyCollection<string>>(o => o.SequenceEqual(new[] { Oid("b") })),
        It.IsAny<Func<string, Stream, Task>>()), Times.Once);
  }

  [Theory]
  [InlineData("missing.txt")]
  [InlineData("link.dll")]
  [InlineData("plugin/a.dll")]
  public async Task OpenRead_UnknownKey_Throws(string key) {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");

    // Act & Assert
    ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => source.OpenRead(key));
    Assert.Equal("key", ex.ParamName);
    await Assert.ThrowsAsync<ArgumentException>(() => source.Prefetch([key]));
    VerifyFetchBlobsCalls(client, Times.Never());
  }

  [Fact]
  public async Task OpenRead_BlobNotReturned_Throws() {
    // Arrange
    (Mock<IGitRemoteClient> client, CacheService cacheService) = CreateMocks();
    client.Setup(c => c.FetchBlobs(_repoUrl, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Stream, Task>>()))
        .Returns(Task.CompletedTask);
    GitTreeSource source = new(client.Object, cacheService, _repoUrl, _commit, "plugin");

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(() => source.OpenRead("a.dll"));
    await Assert.ThrowsAsync<InvalidOperationException>(() => source.Prefetch(["a.dll"]));
  }
}
