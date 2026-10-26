using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

public class CacheServiceTests {
  private static string GetCachePath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lip", "cache");

  [Fact]
  public async Task GetOrCreateDirectory_NonExistent_CreatesAndCallsFactory() {
    // Arrange
    string cachePath = GetCachePath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), cachePath);
    var mockUserInteraction = new Mock<IUserInteraction>();
    CacheService service = new(mockFileSystem, mockUserInteraction.Object);
    bool factoryCalled = false;

    // Act
    IDirectoryInfo result = await service.GetOrCreateDirectory("test-key", async dir => {
      mockFileSystem.Directory.CreateDirectory(dir.FullName);
      factoryCalled = true;
      await Task.CompletedTask;
    });

    // Assert
    Assert.True(factoryCalled);
    Assert.NotNull(result);
  }

  [Fact]
  public async Task GetOrCreateDirectory_Exists_DoesNotCallFactory() {
    // Arrange
    string cachePath = GetCachePath();
    string safeKey = Uri.EscapeDataString("test-key");
    string targetPath = Path.Combine(cachePath, safeKey);

    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(targetPath, "placeholder.txt"), new MockFileData("") }
        });
    var mockUserInteraction = new Mock<IUserInteraction>();
    CacheService service = new(mockFileSystem, mockUserInteraction.Object);
    bool factoryCalled = false;

    // Act
    IDirectoryInfo result = await service.GetOrCreateDirectory("test-key", async dir => {
      factoryCalled = true;
      await Task.CompletedTask;
    });

    // Assert
    Assert.False(factoryCalled);
  }

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
    IFileInfo? result = await service.TryGetFile("git-blob:abc");

    // Assert
    Assert.Null(result);
    Assert.False(mockFileSystem.Directory.Exists(GetCachePath()));
  }

  [Fact]
  public async Task TryGetFile_Cached_ReturnsFile() {
    // Arrange
    string key = "git-blob:abc";
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
