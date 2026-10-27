using System.Text;
using Lip.Core.Mcmod;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Mcmod.Archives;

public class MountSourceTests {
  private static Mock<ISource> Source(params string[] keys) {
    Mock<ISource> source = new();
    source.Setup(s => s.Keys).Returns(keys);
    source.Setup(s => s.OpenRead(It.IsAny<string>()))
        .ReturnsAsync((string key) => new MemoryStream(Encoding.UTF8.GetBytes($"<{key}>")));
    return source;
  }

  private static async Task<string> Read(ISource source, string key) {
    using StreamReader reader = new(await source.OpenRead(key));
    return await reader.ReadToEndAsync();
  }

  [Fact]
  public void Keys_SeveralSources_AreUnderTheirMountsInOrder() {
    // Arrange
    MountSource source = new([("0", Source("b", "a/c").Object), ("1", Source("").Object)]);

    // Act
    List<string> keys = [.. source.Keys];

    // Assert
    Assert.Equal(["0/b", "0/a/c", "1"], keys);
  }

  [Fact]
  public async Task OpenRead_MountedKey_ReadsFromItsSource() {
    // Arrange
    MountSource source = new([("0", Source("a/c").Object), ("1", Source("").Object)]);

    // Act & Assert
    Assert.Equal("<a/c>", await Read(source, "0/a/c"));
    Assert.Equal("<>", await Read(source, "1"));
    await Assert.ThrowsAsync<ArgumentException>(() => source.OpenRead("2/x"));
  }

  [Fact]
  public async Task Prefetch_Keys_ForwardsEachSourcesKeysOnce() {
    // Arrange
    Mock<ISource> first = Source("a", "b");
    Mock<ISource> second = Source("c");
    List<string> firstKeys = [];
    first.Setup(s => s.Prefetch(It.IsAny<IEnumerable<string>>()))
        .Callback((IEnumerable<string> keys) => firstKeys.AddRange(keys))
        .Returns(Task.CompletedTask);
    MountSource source = new([("0", first.Object), ("1", second.Object), ("2", first.Object)]);

    // Act
    await source.Prefetch(["0/a", "1/c", "2/b"]);

    // Assert
    Assert.Equal(["a", "b"], firstKeys);
    first.Verify(s => s.Prefetch(It.IsAny<IEnumerable<string>>()), Times.Once);
    second.Verify(s => s.Prefetch(It.Is<IEnumerable<string>>(k => k.SequenceEqual(new[] { "c" }))));
  }

  [Theory]
  [InlineData("")]
  [InlineData("a/b")]
  public void Constructor_InvalidMount_Throws(string mount) {
    // Act & Assert
    Assert.Throws<ArgumentException>(() => new MountSource([(mount, Source().Object)]));
  }

  [Fact]
  public void Constructor_DuplicateMount_Throws() {
    // Act & Assert
    Assert.Throws<ArgumentException>(
        () => new MountSource([("0", Source().Object), ("0", Source().Object)]));
  }
}
