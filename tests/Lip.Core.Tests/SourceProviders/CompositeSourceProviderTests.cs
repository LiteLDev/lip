using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Sources;

public class CompositeSourceTests {
  [Fact]
  public void Keys_AggregatesAndDistinctsKeys() {
    // Arrange
    Mock<ISource> mockProvider1 = new();
    mockProvider1.Setup(p => p.Keys).Returns(["file1.txt", "common.txt"]);

    Mock<ISource> mockProvider2 = new();
    mockProvider2.Setup(p => p.Keys).Returns(["file2.txt", "common.txt"]);

    CompositeSource provider = new([mockProvider1.Object, mockProvider2.Object]);

    // Act
    List<string> keys = [.. provider.Keys];

    // Assert
    Assert.Equal(3, keys.Count);
    Assert.Contains("file1.txt", keys);
    Assert.Contains("file2.txt", keys);
    Assert.Contains("common.txt", keys);
  }

  [Fact]
  public async Task OpenRead_ReturnsFromFirstProviderWithKey() {
    // Arrange
    Mock<ISource> mockProvider1 = new();
    mockProvider1.Setup(p => p.Keys).Returns(["file1.txt"]);
    mockProvider1.Setup(p => p.OpenRead("file1.txt"))
        .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content1")));

    Mock<ISource> mockProvider2 = new();
    mockProvider2.Setup(p => p.Keys).Returns(["file2.txt"]);

    CompositeSource provider = new([mockProvider1.Object, mockProvider2.Object]);

    // Act
    using Stream stream = await provider.OpenRead("file1.txt");
    using StreamReader reader = new(stream);
    string content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal("content1", content);
  }

  [Fact]
  public async Task OpenRead_KeyNotFound_ThrowsArgumentException() {
    // Arrange
    CompositeSource provider = new([]);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => provider.OpenRead("missing"));
  }

  [Fact]
  public async Task Prefetch_ForwardsEachKeyToFirstProviderServingIt() {
    // Arrange
    Mock<ISource> mockProvider1 = new();
    mockProvider1.Setup(p => p.Keys).Returns(["file1.txt", "common.txt"]);
    mockProvider1.Setup(p => p.Prefetch(It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);

    Mock<ISource> mockProvider2 = new();
    mockProvider2.Setup(p => p.Keys).Returns(["file2.txt", "common.txt"]);
    mockProvider2.Setup(p => p.Prefetch(It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);

    Mock<ISource> mockProvider3 = new();
    mockProvider3.Setup(p => p.Keys).Returns(["file3.txt"]);

    CompositeSource provider = new([mockProvider1.Object, mockProvider2.Object, mockProvider3.Object]);

    // Act
    await provider.Prefetch(["common.txt", "file2.txt", "file1.txt", "missing.txt"]);

    // Assert
    mockProvider1.Verify(p => p.Prefetch(It.Is<IEnumerable<string>>(
        k => k.Order().SequenceEqual(new[] { "common.txt", "file1.txt" }))), Times.Once);
    mockProvider2.Verify(p => p.Prefetch(It.Is<IEnumerable<string>>(
        k => k.SequenceEqual(new[] { "file2.txt" }))), Times.Once);
    mockProvider3.Verify(p => p.Prefetch(It.IsAny<IEnumerable<string>>()), Times.Never);
  }
}
