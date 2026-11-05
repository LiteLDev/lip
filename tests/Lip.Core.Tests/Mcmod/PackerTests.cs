using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

public class PackerTests {
  private const string Meta = """
      {
        "format": "bf45b1",
        "version": "0.22.0",
        "targets": [
          { "platform": ["server", "client"], "runtime": "win-x64",
            "source": "https://example.com/LegacyMoney-{{platform}}-{{runtime}}.mcmod" }
        ]
      }
      """;

  private static JsonObject Pack(string json) => Packer.Pack(Encoding.UTF8.GetBytes(json));

  [Fact]
  public void Pack_PackageDocument_ReturnsItAsIs() {
    // Arrange
    const string json = """
        { "format": "bf45b1", "name": "m", "version": "1.0.0", "entry": "m.dll",
          "type": "native", "passive": true, "platform": "server" }
        """;

    // Act & Assert
    Assert.Equal(
        """{"format":"bf45b1","name":"m","version":"1.0.0","entry":"m.dll","type":"native","passive":true,"platform":"server"}""",
        Pack(json).ToJsonString());
  }

  [Fact]
  public void Pack_MetaDocument_Throws() {
    // Act & Assert
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Pack(Meta));
    Assert.Contains("meta package", ex.Message);
  }

  [Fact]
  public void Pack_InvalidPackageDocument_Throws() {
    // Act & Assert
    ManifestFormatException ex = Assert.Throws<ManifestFormatException>(
        () => Pack("""{ "format": "bf45b1", "name": "m", "version": "1.0.0", "entry": "m.dll" }"""));
    Assert.Contains("$.type", ex.Message);
  }

  [Fact]
  public void Pack_LeviLaminaManifest_AsksToMigrateFirst() {
    // Act & Assert
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => Pack("""{ "name": "m", "version": "1.0.0" }"""));
    Assert.Contains("lip migrate", ex.Message);
  }

  [Fact]
  public async Task WriteArchive_PacksTheDirectoryWithTheDocumentAsManifest() {
    // Arrange
    string root = OperatingSystem.IsWindows() ? @"C:\build" : "/build";
    MockFileSystem fs = new();
    fs.AddFile(fs.Path.Join(root, "manifest.json"), new MockFileData("old"));
    fs.AddFile(fs.Path.Join(root, "LegacyMoney.dll"), new MockFileData("dll"));
    fs.AddFile(fs.Path.Join(root, "lang", "en_US.json"), new MockFileData("{}"));
    fs.AddFile(fs.Path.Join(root, ".git", "HEAD"), new MockFileData("ref"));
    fs.AddFile(fs.Path.Join(root, "out.mcmod"), new MockFileData("previous"));
    JsonObject document = new() { ["format"] = "bf45b1", ["name"] = "m", ["version"] = "1.0.0" };

    // Act
    await Packer.WriteArchive(fs, root, fs.Path.Join(root, "out.mcmod"), document);

    // Assert
    using ZipArchive zip = new(new MemoryStream(fs.File.ReadAllBytes(fs.Path.Join(root, "out.mcmod"))));
    Assert.Equal(
        ["LegacyMoney.dll", "lang/en_US.json", "manifest.json"],
        zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    using StreamReader reader = new(zip.GetEntry("manifest.json")!.Open());
    Assert.Equal(
        "{\n  \"format\": \"bf45b1\",\n  \"name\": \"m\",\n  \"version\": \"1.0.0\"\n}\n",
        await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    Assert.False(fs.File.Exists(fs.Path.Join(root, "out.mcmod.tmp")));
  }
}
