using System.Text;
using Lip.Core.Mcmod;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class ArchiveReaderTests {
  private const string _toothJson = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "github.com/user/repo",
        "version": "1.0.0",
        "variants": [{ "label": "", "platform": "" }]
      }
      """;

  private static ArchiveHandle CreateArchive(Dictionary<string, string> files) {
    Mock<ISource> source = new();
    source.Setup(s => s.Keys).Returns(files.Keys);
    source.Setup(s => s.OpenRead(It.IsAny<string>()))
        .ReturnsAsync((string key) => new MemoryStream(Encoding.UTF8.GetBytes(files[key])));
    return new ArchiveHandle("test", source.Object, new ZipArchiveIdentity(TestIdentity.Sha256, "/test"));
  }

  [Fact]
  public async Task Read_ToothJsonPresent_PrefersToothJson() {
    // Arrange
    Mock<IDocumentParser> parser = new();
    ArchiveHandle archive = CreateArchive(new() {
      ["tooth.json"] = _toothJson,
      ["manifest.json"] = "{}",
    });

    // Act
    ArchiveDocument document = await new ArchiveReader(parser.Object).Read(archive);

    // Assert
    ToothArchiveDocument tooth = Assert.IsType<ToothArchiveDocument>(document);
    Assert.Equal("github.com/user/repo", tooth.Manifest.Path);
    parser.Verify(p => p.Parse(It.IsAny<byte[]>()), Times.Never);
  }

  [Fact]
  public async Task Read_ManifestJson_UsesParser() {
    // Arrange
    Mock<IDocumentParser> parser = new();
    parser.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(new ConcreteDocument {
      IsMarked = true,
      Name = "x",
      Version = SemVersion.Parse("1.0.0"),
      Type = "files",
    });
    ArchiveHandle archive = CreateArchive(new() { ["manifest.json"] = "{\"name\":\"x\"}" });

    // Act
    ArchiveDocument document = await new ArchiveReader(parser.Object).Read(archive);

    // Assert
    ConcreteArchiveDocument concrete = Assert.IsType<ConcreteArchiveDocument>(document);
    Assert.Equal("x", concrete.Manifest.GetProperty("name").GetString());
  }

  [Fact]
  public async Task Read_InvalidDocument_ThrowsArchiveUnavailable() {
    // Arrange
    Mock<IDocumentParser> parser = new();
    parser.Setup(p => p.Parse(It.IsAny<byte[]>())).Throws(new ManifestFormatException("bad"));
    ArchiveHandle archive = CreateArchive(new() { ["manifest.json"] = "{}" });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => new ArchiveReader(parser.Object).Read(archive));
  }

  [Fact]
  public async Task Read_NoDocument_ThrowsArchiveUnavailable() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["readme.md"] = "" });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => new ArchiveReader(new Mock<IDocumentParser>().Object).Read(archive));
  }
}
