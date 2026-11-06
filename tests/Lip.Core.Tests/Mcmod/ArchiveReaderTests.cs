using System.Text;
using Lip.Core.Mcmod;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod;

/// The tooth file at the root of an archive (protocol/package.md §1, §3, §5).
public class ArchiveReaderTests {
  private static readonly LipEnvironment _environment =
      new(McmodPlatform.Server, WorkspaceHarness.Runtime);

  private const string _toothV3 = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "github.com/user/repo",
        "version": "1.0.0",
        "variants": [{ "label": "", "platform": "" }]
      }
      """;

  private const string _toothV2 = """
      {
        "format_version": 2,
        "tooth": "github.com/user/repo",
        "version": "1.1.0",
        "info": { "name": "repo", "description": "", "author": "", "tags": [] }
      }
      """;

  private const string _toothV1 = """
      {
        "format_version": 1,
        "tooth": "github.com/user/repo",
        "version": "2.14.1",
        "dependencies": {},
        "information": { "name": "repo" },
        "placement": [ { "source": "src/*", "destination": "declaration/*" } ]
      }
      """;

  private static ArchiveHandle CreateArchive(Dictionary<string, string> files) {
    Mock<ISource> source = new();
    source.Setup(s => s.Keys).Returns(files.Keys);
    source.Setup(s => s.OpenRead(It.IsAny<string>()))
        .ReturnsAsync((string key) => new MemoryStream(Encoding.UTF8.GetBytes(files[key])));
    source.Setup(s => s.ReadUncached(It.IsAny<string>()))
        .ReturnsAsync((string key) => Encoding.UTF8.GetBytes(files[key]));
    return new ArchiveHandle("test", source.Object);

  }

  [Fact]
  public async Task Read_Format4_ReturnsTheToothFile() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() {
      ["tooth.json"] = WorkspaceHarness.Tooth4(
          "github.com/user/repo", "1.2.3", WorkspaceHarness.SelfAsset("a.dll", "plugins/a.dll")),
      ["manifest.json"] = "{}",
    });

    // Act
    ToothDocument document = await new ArchiveReader().Read(archive);

    // Assert
    Assert.Equal(4, document.FormatVersion);
    Assert.False(document.IsLegacy);
    Assert.Null(document.Legacy);
    Assert.Equal("github.com/user/repo", document.Tooth);
    Assert.Equal(SemVersion.Parse("1.2.3"), document.Version);
    ToothFile file = document.ReadFor(_environment);
    Assert.Same(document.File, file);
    ToothEntry entry = Assert.Single(file.Variants[""]);
    Assert.False(entry.Derived);
    Assert.Same(entry, file.SelectEntry("", _environment));
  }

  [Theory]
  [InlineData(_toothV1, 1, "2.14.1")]
  [InlineData(_toothV2, 2, "1.1.0")]
  [InlineData(_toothV3, 3, "1.0.0")]
  public async Task Read_EarlierFormat_ReadsAsFormat3(string json, int formatVersion, string version) {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["tooth.json"] = json });

    // Act
    ToothDocument document = await new ArchiveReader().Read(archive);

    // Assert
    Assert.Equal(formatVersion, document.FormatVersion);
    Assert.True(document.IsLegacy);
    Assert.Null(document.File);
    Assert.NotNull(document.Legacy);
    Assert.Equal("github.com/user/repo", document.Tooth);
    Assert.Equal(SemVersion.Parse(version), document.Version);
  }

  [Fact]
  public async Task Read_Format3_DerivesEntriesForTheEnvironment() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["tooth.json"] = _toothV3 });
    ToothDocument document = await new ArchiveReader().Read(archive);

    // Act
    ToothFile file = document.ReadFor(_environment);

    // Assert
    Assert.Equal("github.com/user/repo", file.Tooth);
    ToothEntry entry = Assert.IsType<ToothEntry>(file.SelectEntry("", _environment));
    Assert.True(entry.Derived);
  }

  [Fact]
  public async Task Read_NoToothFile_ThrowsArchiveUnavailable() {
    // Arrange: a manifest.json does not make an archive a package.
    ArchiveHandle archive = CreateArchive(new() {
      ["manifest.json"] = """{ "name": "x", "entry": "x.dll", "type": "native" }""",
    });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }

  [Fact]
  public async Task Read_ToothFileNotAtTheRoot_ThrowsArchiveUnavailable() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() {
      ["sub/tooth.json"] = WorkspaceHarness.Tooth4("github.com/user/repo", "1.0.0"),
    });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("""{ "tooth": "github.com/user/repo", "version": "1.0.0" }""")]
  [InlineData("""{ "format_version": 5, "tooth": "github.com/user/repo", "version": "1.0.0" }""")]
  [InlineData("""{ "format_version": "4" }""")]
  [InlineData("""{ "format_version": 4, "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102", "tooth": "github.com/user/repo", "version": "1.0.0" }""")]
  [InlineData("""{ "format_version": 4, "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102", "tooth": "github.com/user/repo", "version": "v1.0.0", "variants": {} }""")]
  [InlineData("""{ "format_version": 3, "tooth": "github.com/user/repo" }""")]
  public async Task Read_InvalidToothFile_ThrowsArchiveUnavailable(string json) {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["tooth.json"] = json });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }
}
