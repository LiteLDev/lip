using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

public class PackerTests {
  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private const string Meta = """
      {
        "format": "bf45b1",
        "id": "LegacyMoney",
        "version": "0.22.0",
        "info": { "name": "Legacy Money" },
        "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
        "preserve": ["config/"],
        "targets": [
          { "platform": ["server", "client"], "runtime": "win-x64",
            "source": "https://example.com/{{id}}-{{platform}}-{{runtime}}.mcmod" },
          { "platform": "server", "runtime": ["linux-x64", "linux-arm64"],
            "mod": { "loader": "native", "entry": "libLegacyMoney.so" },
            "source": "https://example.com/{{id}}-{{platform}}-{{runtime}}.mcmod" },
          { "variant": "preset", "id": "LegacyMoney-preset", "mod": null, "preserve": null,
            "files": [ { "src": "server.properties", "dest": "server.properties" } ],
            "source": "https://example.com/{{id}}.zip" }
        ]
      }
      """;

  private static JsonObject Pack(string json, string variant, LipEnvironment environment) =>
      Packer.Pack(Encoding.UTF8.GetBytes(json), variant, environment);

  [Fact]
  public void Pack_MetaDocument_SetsTheSelectorsOfTheTarget() {
    // Act
    JsonObject document = Pack(Meta, "", _serverWin);

    // Assert
    Assert.Equal(
        """{"format":"bf45b1","id":"LegacyMoney","version":"0.22.0","info":{"name":"Legacy Money"},"mod":{"loader":"native","entry":"LegacyMoney.dll"},"preserve":["config/"],"platform":"server","runtime":"win-x64"}""",
        document.ToJsonString());
  }

  [Fact]
  public void Pack_TargetOverrides_ReplaceWholeValues() {
    // Act
    JsonObject document = Pack(Meta, "", new(McmodPlatform.Server, "linux-arm64"));

    // Assert
    Assert.Equal("libLegacyMoney.so", (string?)document["mod"]!["entry"]);
    Assert.Equal("linux-arm64", (string?)document["runtime"]);
  }

  [Fact]
  public void Pack_NullOverrides_RemoveKeys() {
    // Act
    JsonObject document = Pack(Meta, "preset", new(McmodPlatform.Client, "osx-arm64"));

    // Assert
    Assert.Equal(
        """{"format":"bf45b1","id":"LegacyMoney-preset","version":"0.22.0","info":{"name":"Legacy Money"},"files":[{"src":"server.properties","dest":"server.properties"}]}""",
        document.ToJsonString());
    Assert.IsType<PackageDocument>(new DocumentParser().Parse(Packer.Serialize(document)));
  }

  [Fact]
  public void Pack_NoTargetForTheEnvironment_Throws() {
    // Act & Assert
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => Pack(Meta, "", new(McmodPlatform.Client, "linux-x64")));
    Assert.Contains("No target for variant \"\"", ex.Message);
  }

  [Fact]
  public void Pack_ResultNotAPackageDocument_Throws() {
    // Arrange
    const string json = """
        { "format": "bf45b1", "id": "m", "version": "1.0.0",
          "mod": { "loader": "native", "entry": "m.dll" },
          "targets": [ { "files": [], "source": "./a/" } ] }
        """;

    // Act & Assert
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => Pack(json, "", _serverWin));
    Assert.Contains("invalid package document", ex.Message);
  }

  [Fact]
  public void Pack_PackageDocument_ReturnsItUnlessAVariantIsAsked() {
    // Arrange
    const string json = """{ "format": "bf45b1", "id": "m", "version": "1.0.0" }""";

    // Act & Assert
    Assert.Equal(
        """{"format":"bf45b1","id":"m","version":"1.0.0"}""",
        Pack(json, "", _serverWin).ToJsonString());
    Assert.Throws<InvalidOperationException>(() => Pack(json, "x", _serverWin));
  }

  [Fact]
  public void Pack_LeviLaminaManifest_AsksToMigrateFirst() {
    // Act & Assert
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => Pack("""{ "name": "m", "version": "1.0.0" }""", "", _serverWin));
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
    JsonObject document = new() { ["format"] = "bf45b1", ["id"] = "m", ["version"] = "1.0.0" };

    // Act
    await Packer.WriteArchive(fs, root, fs.Path.Join(root, "out.mcmod"), document);

    // Assert
    using ZipArchive zip = new(new MemoryStream(fs.File.ReadAllBytes(fs.Path.Join(root, "out.mcmod"))));
    Assert.Equal(
        ["LegacyMoney.dll", "lang/en_US.json", "manifest.json"],
        zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    using StreamReader reader = new(zip.GetEntry("manifest.json")!.Open());
    Assert.Equal(
        "{\n  \"format\": \"bf45b1\",\n  \"id\": \"m\",\n  \"version\": \"1.0.0\"\n}\n",
        await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    Assert.False(fs.File.Exists(fs.Path.Join(root, "out.mcmod.tmp")));
  }
}
