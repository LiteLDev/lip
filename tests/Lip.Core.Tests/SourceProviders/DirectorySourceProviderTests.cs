using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Sources;

namespace Lip.Core.Tests.Sources;

public class DirectorySourceTests {
  [Fact]
  public void Keys_ReturnsAllFiles() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(testDir, "file1.txt"), new MockFileData("content1") },
            { Path.Combine(testDir, "subdir", "file2.txt"), new MockFileData("content2") }
        });
    IDirectoryInfo dirInfo = mockFileSystem.DirectoryInfo.New(testDir);
    DirectorySource provider = new(dirInfo);

    // Act
    List<string> keys = [.. provider.Keys];

    // Assert
    Assert.Equal(2, keys.Count);
    Assert.Contains("file1.txt", keys);
    Assert.Contains("subdir/file2.txt", keys);
  }

  [Fact]
  public async Task OpenRead_ValidKey_ReturnsStream() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(testDir, "file.txt"), new MockFileData("test content") }
        });
    IDirectoryInfo dirInfo = mockFileSystem.DirectoryInfo.New(testDir);
    DirectorySource provider = new(dirInfo);

    // Act
    using Stream stream = await provider.OpenRead("file.txt");
    using StreamReader reader = new(stream);
    string content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal("test content", content);
  }

  [Fact]
  public async Task OpenRead_InvalidKey_ThrowsArgumentException() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(testDir, "file.txt"), new MockFileData("content") }
        });
    IDirectoryInfo dirInfo = mockFileSystem.DirectoryInfo.New(testDir);
    DirectorySource provider = new(dirInfo);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => provider.OpenRead("nonexistent.txt"));
  }

  [Fact]
  public void Keys_ExcludesGitEntries() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(testDir, "tooth.json"), new MockFileData("{}") },
            { Path.Combine(testDir, ".git", "HEAD"), new MockFileData("ref") },
            { Path.Combine(testDir, ".git", "objects", "ab", "cdef"), new MockFileData("object") },
            { Path.Combine(testDir, "submodule", ".git"), new MockFileData("gitdir: ../.git/modules/submodule") },
            { Path.Combine(testDir, "submodule", "file.txt"), new MockFileData("content") },
            { Path.Combine(testDir, ".gitignore"), new MockFileData("bin/") },
        });
    IDirectoryInfo dirInfo = mockFileSystem.DirectoryInfo.New(testDir);
    DirectorySource provider = new(dirInfo);

    // Act
    List<string> keys = [.. provider.Keys];

    // Assert
    Assert.Equal([".gitignore", "submodule/file.txt", "tooth.json"], keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task OpenRead_NestedKeyWithForwardSlash_ReturnsStream() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(testDir, "lib", "file.js"), new MockFileData("nested content") }
        });
    IDirectoryInfo dirInfo = mockFileSystem.DirectoryInfo.New(testDir);
    DirectorySource provider = new(dirInfo);

    // Act
    using Stream stream = await provider.OpenRead("lib/file.js");
    using StreamReader reader = new(stream);
    string content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal("nested content", content);
  }

  [Theory]
  [InlineData(".git/HEAD")]
  [InlineData("../outside.txt")]
  [InlineData("lib")]
  public async Task OpenRead_ExcludedOrNonFileKey_ThrowsArgumentException(string key) {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(testDir, ".git", "HEAD"), new MockFileData("ref") },
            { Path.Combine(testDir, "lib", "file.js"), new MockFileData("content") },
            { Path.Combine(root, "outside.txt"), new MockFileData("secret") },
        });
    IDirectoryInfo dirInfo = mockFileSystem.DirectoryInfo.New(testDir);
    DirectorySource provider = new(dirInfo);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => provider.OpenRead(key));
  }
}
