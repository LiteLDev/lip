using System.Text;
using Lip.Core.Mcmod;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod;

/// The package file at the root of an archive (protocol/lip-package.md, "Terms";
/// protocol/lip.toml.md), or its tooth.json (protocol/lip-compatibility.md, "tooth.json").
public class ArchiveReaderTests {
  private static readonly LipEnvironment _environment =
      new(McmodSide.Server, WorkspaceHarness.Platform);

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
    return new ArchiveHandle("test", source.Object);

  }

  [Fact]
  public async Task Read_PackageFile_ReturnsIt() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          "github.com/user/repo", "1.2.3", WorkspaceHarness.SelfAsset("a.dll", "plugins/a.dll")),
      ["manifest.json"] = "{}",
    });

    // Act
    PackageDocument document = await new ArchiveReader().Read(archive);

    // Assert
    Assert.False(document.IsLegacy);
    Assert.Null(document.Legacy);
    Assert.Equal("github.com/user/repo", document.Id);
    Assert.Equal(SemVersion.Parse("1.2.3"), document.Version);
    PackageFile file = document.ReadFor(_environment);
    Assert.Same(document.File, file);
    PackageEntry entry = Assert.Single(file.Variants[""]);
    Assert.Same(entry, file.SelectEntry("", _environment));
  }

  [Fact]
  public async Task Read_PackageFileAndToothJson_ReadsThePackageFile() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() {
      ["lip.toml"] = WorkspaceHarness.LipToml("github.com/user/repo", "4.0.0"),
      ["tooth.json"] = _toothV3,
    });

    // Act
    PackageDocument document = await new ArchiveReader().Read(archive);

    // Assert
    Assert.False(document.IsLegacy);
    Assert.Equal(SemVersion.Parse("4.0.0"), document.Version);
  }

  [Fact]
  public async Task Read_InvalidPackageFileAndValidToothJson_ThrowsArchiveUnavailable() {
    // Arrange: an archive with a package file is a package only when that is valid.
    ArchiveHandle archive = CreateArchive(new() {
      ["lip.toml"] = "format = 4\n",
      ["tooth.json"] = _toothV3,
    });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }

  [Fact]
  public async Task Read_HandWrittenPackageFile_ReturnsIt() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() {
      ["lip.toml"] = """
          # A comment.
          format = 4
          id = "github.com/user/repo"
          version = "1.0.0"

          [info]
          name = "Repo"
          tags = ["a", 'b']

          [[variants.""]]
          side = "server"
          platform = "win-x64"
          deps = { "github.com/user/dep" = ">=1.0.0" }

          [[variants."".assets]]
          type = "self"
          place = [{ src = "a.dll", dest = "plugins/a.dll" }]
          """,
    });

    // Act
    PackageDocument document = await new ArchiveReader().Read(archive);

    // Assert
    PackageEntry entry = Assert.Single(document.File!.Variants[""]);
    Assert.Equal("github.com/user/dep", Assert.Single(entry.Dependencies).Source.Target);
    Assert.Equal("plugins/a.dll", Assert.Single(Assert.Single(entry.Assets).Placements).Dest);
    Assert.Equal("Repo", document.Json.GetProperty("info").GetProperty("name").GetString());
  }

  [Theory]
  [InlineData(_toothV1, "2.14.1")]
  [InlineData(_toothV2, "1.1.0")]
  [InlineData(_toothV3, "1.0.0")]
  public async Task Read_ToothJson_ReadsAsFormat3(string json, string version) {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["tooth.json"] = json });

    // Act
    PackageDocument document = await new ArchiveReader().Read(archive);

    // Assert
    Assert.True(document.IsLegacy);
    Assert.Null(document.File);
    Assert.NotNull(document.Legacy);
    Assert.Equal("github.com/user/repo", document.Id);
    Assert.Equal(SemVersion.Parse(version), document.Version);
  }

  [Fact]
  public async Task Read_Format3_DerivesEntriesForTheEnvironment() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["tooth.json"] = _toothV3 });
    PackageDocument document = await new ArchiveReader().Read(archive);

    // Act
    PackageFile file = document.ReadFor(_environment);

    // Assert
    Assert.Equal("github.com/user/repo", file.Id);
    PackageEntry entry = Assert.IsType<PackageEntry>(file.SelectEntry("", _environment));
  }

  [Fact]
  public async Task Read_NoPackageFile_ThrowsArchiveUnavailable() {
    // Arrange: a manifest.json does not make an archive a package.
    ArchiveHandle archive = CreateArchive(new() {
      ["manifest.json"] = """{ "name": "x", "entry": "x.dll", "type": "native" }""",
    });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }

  [Fact]
  public async Task Read_PackageFileNotAtTheRoot_ThrowsArchiveUnavailable() {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() {
      ["sub/lip.toml"] = WorkspaceHarness.LipToml("github.com/user/repo", "1.0.0"),
    });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }

  [Theory]
  [InlineData("not a table")]
  [InlineData("[]")]
  [InlineData("")]
  [InlineData("{ \"format\": 4 }")]
  [InlineData("id = \"github.com/user/repo\"\nversion = \"1.0.0\"\nvariants = {}")]
  [InlineData("format = 5\nid = \"github.com/user/repo\"\nversion = \"1.0.0\"\nvariants = {}")]
  [InlineData("format = \"4\"\nid = \"github.com/user/repo\"\nversion = \"1.0.0\"\nvariants = {}")]
  [InlineData("format = 4\nid = \"github.com/user/repo\"\nversion = \"1.0.0\"")]
  [InlineData("format = 4\nid = \"github.com/user/repo\"\nversion = \"v1.0.0\"\nvariants = {}")]
  [InlineData("format = 4\nformat = 4\nid = \"github.com/user/repo\"\nversion = \"1.0.0\"\nvariants = {}")]
  [InlineData("format = 4\nid = \"github.com/user/repo\"\nversion = 1979-05-27\nvariants = {}")]
  public async Task Read_InvalidPackageFile_ThrowsArchiveUnavailable(string toml) {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["lip.toml"] = toml });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("""{ "tooth": "github.com/user/repo", "version": "1.0.0" }""")]
  [InlineData("""{ "format_version": 5, "tooth": "github.com/user/repo", "version": "1.0.0" }""")]
  [InlineData("""{ "format_version": "3" }""")]
  [InlineData("""{ "format_version": 4, "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102", "tooth": "github.com/user/repo", "version": "1.0.0", "variants": {} }""")]
  [InlineData("""{ "format_version": 3, "tooth": "github.com/user/repo" }""")]
  public async Task Read_InvalidToothJson_ThrowsArchiveUnavailable(string json) {
    // Arrange
    ArchiveHandle archive = CreateArchive(new() { ["tooth.json"] = json });

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => new ArchiveReader().Read(archive));
  }
}
