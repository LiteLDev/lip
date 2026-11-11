using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

public class CacheServiceTests {
  private static string GetCachePath() => LipDirectories.Cache;

  [Fact]
  public async Task GetOrCreateFile_NonExistent_CreatesAndCallsFactory() {
    // Arrange
    string cachePath = GetCachePath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(cachePath, "placeholder.txt"), new MockFileData("") }
        }, cachePath);
    var mockUserInteraction = new Mock<IUserInteraction>();
    CacheService service = new(mockFileSystem, mockUserInteraction.Object);
    bool factoryCalled = false;

    // Act
    IFileInfo result = await service.GetOrCreateFile("test-key", async file => {
      mockFileSystem.File.Create(file.FullName).Dispose();
      factoryCalled = true;
      await Task.CompletedTask;
    });

    // Assert
    Assert.True(factoryCalled);
    Assert.NotNull(result);
  }

  /// protocol/lip-userspace.md, "Cache": an entry is written atomically, through a &lt;N&gt;.tmp
  /// file in the cache directory.
  [Fact]
  public async Task GetOrCreateFile_NonExistent_FactoryWritesTmpFileInCache() {
    // Arrange
    MockFileSystem mockFileSystem = new();
    CacheService service = new(mockFileSystem, new Mock<IUserInteraction>().Object);
    string? written = null;

    // Act
    IFileInfo result = await service.GetOrCreateFile(CacheKeys.Http("https://example.com/a.zip"), file => {
      written = file.FullName;
      mockFileSystem.File.WriteAllText(file.FullName, "content");
      return Task.CompletedTask;
    });

    // Assert
    Assert.Equal(GetCachePath(), Path.GetDirectoryName(written));
    Assert.EndsWith(".tmp", written);
    Assert.Equal([result.FullName], mockFileSystem.Directory.GetFileSystemEntries(GetCachePath()));
  }

  [Fact]
  public void CacheKeys_Http_IsSha256OfUrl() {
    // Arrange
    string url = "https://example.com/a.zip";
    string expected = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));

    // Act & Assert
    Assert.Equal($"http-{expected}", CacheKeys.Http(url));
  }

  [Fact]
  public async Task GetOrCreateFile_CreatedConcurrently_ReturnsExistingFile() {
    // Arrange
    string cachePath = GetCachePath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), cachePath);
    var mockUserInteraction = new Mock<IUserInteraction>();
    CacheService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    IFileInfo result = await service.GetOrCreateFile("test-key", async file => {
      mockFileSystem.File.WriteAllText(file.FullName, "second");
      // Another writer stores the same key before this one moves its file into place.
      await service.GetOrCreateFile("test-key", other => {
        mockFileSystem.File.WriteAllText(other.FullName, "first");
        return Task.CompletedTask;
      });
    });

    // Assert
    Assert.Equal("first", mockFileSystem.File.ReadAllText(result.FullName));
  }

  [Fact]
  public async Task Clean_DeletesCacheDirectory() {
    // Arrange
    string cachePath = GetCachePath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(cachePath, "somefile.txt"), new MockFileData("cached data") }
        });
    var mockUserInteraction = new Mock<IUserInteraction>();
    CacheService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    await service.Clean();

    // Assert - directory should be deleted
    Assert.False(mockFileSystem.Directory.Exists(cachePath));
  }

  [Fact]
  public async Task TryGetFile_Missing_ReturnsNullWithoutCreatingAnything() {
    // Arrange
    MockFileSystem mockFileSystem = new();
    CacheService service = new(mockFileSystem, new Mock<IUserInteraction>().Object);

    // Act
    IFileInfo? result = await service.TryGetFile("git-abc");

    // Assert
    Assert.Null(result);
    Assert.False(mockFileSystem.Directory.Exists(GetCachePath()));
  }

  [Fact]
  public async Task TryGetFile_Cached_ReturnsFile() {
    // Arrange
    string key = "git-abc";
    string path = Path.Combine(GetCachePath(), Uri.EscapeDataString(key));
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData> {
      { path, new MockFileData("content") },
    });
    CacheService service = new(mockFileSystem, new Mock<IUserInteraction>().Object);

    // Act
    IFileInfo? result = await service.TryGetFile(key);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(path, result.FullName);
  }
}
