using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Lip.Core.Tests.Mcmod;
using Moq;

namespace Lip.Core.Tests.PublicApi;

public class LipClientTests {
  private readonly WorkspaceHarness _ws = new();
  private readonly Mock<ICacheService> _cacheService = new();
  private readonly Mock<IConfigService> _configService = new();
  private readonly LipClient _client;

  public LipClientTests() {
    _ws.Fs.Directory.SetCurrentDirectory(_ws.Root);
    _client = new LipClient(
        _ws.Fs, _ws.Ui.Object, _cacheService.Object, _configService.Object, _ws.Service);
  }

  [Fact]
  public async Task CacheClean_CallsService() {
    await _client.CacheClean();
    _cacheService.Verify(s => s.Clean(), Times.Once);
  }

  [Fact]
  public async Task ConfigGet_ReturnsValue() {
    _configService.Setup(s => s.Get("github_proxy")).ReturnsAsync("https://proxy.com");

    string result = await _client.ConfigGet("github_proxy");

    Assert.Equal("https://proxy.com", result);
  }

  [Fact]
  public async Task ConfigSet_SavesConfig() {
    await _client.ConfigSet("github_proxy", "https://new_proxy.com");

    _configService.Verify(s => s.Set("github_proxy", "https://new_proxy.com"), Times.Once);
  }

  [Fact]
  public async Task ConfigDelete_RemovesKey() {
    await _client.ConfigDelete("github_proxy");

    _configService.Verify(s => s.Delete("github_proxy"), Times.Once);
  }

  [Fact]
  public async Task ConfigList_ReturnsAllKeys() {
    _configService.Setup(s => s.List()).ReturnsAsync(
        new Dictionary<string, string> { { "github_proxy", "https://proxy.com" } });

    IDictionary<string, string> result = await _client.ConfigList();

    Assert.Equal("https://proxy.com", result["github_proxy"]);
  }

  #region Init

  /// protocol/cli.md §5.7: asks until the answer is a Go module path, then writes exactly the
  /// members of the document it gives.
  [Fact]
  public async Task Init_AsksToothPathUntilValid_WritesToothFile() {
    // Arrange
    _ws.Ui.SetupSequence(u => u.Ask(It.IsAny<string>()))
        .ReturnsAsync("not a module path")
        .ReturnsAsync("  github.com/owner/repo  ");

    // Act
    await _client.Init();

    // Assert
    JsonNode expected = JsonNode.Parse("""
        {
          "format_version": 4,
          "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
          "tooth": "github.com/owner/repo",
          "version": "0.1.0",
          "variants": { "": [ { "platform": "server", "runtime": "win-x64" } ] }
        }
        """)!;
    byte[] bytes = _ws.Fs.File.ReadAllBytes(_ws.Path("tooth.json"));
    Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(bytes)));
    Assert.Equal("github.com/owner/repo", ToothParser.Parse(bytes).Tooth);
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Exactly(2));
    Assert.Contains(_ws.Warnings, w => w.Contains("not a module path"));
  }

  [Fact]
  public async Task Init_ToothFileExists_ThrowsWithoutAskingOrWriting() {
    // Arrange
    _ws.AddFile("tooth.json", "{}");

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(_client.Init);
    Assert.Equal("{}", _ws.Fs.File.ReadAllText(_ws.Path("tooth.json")));
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Init_CannotAsk_ThrowsAndWritesNothing() {
    // Arrange
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>()))
        .ThrowsAsync(new CannotAskException("cannot ask"));

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(_client.Init);
    Assert.False(_ws.Exists("tooth.json"));
  }

  [Fact]
  public async Task Init_NoAnswer_ThrowsAndWritesNothing() {
    // Arrange: a user interaction that returns no answer cannot ask.
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).Returns(Task.FromResult<string>(null!));

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(_client.Init);
    Assert.False(_ws.Exists("tooth.json"));
  }

  #endregion

  #region Migrate

  [Fact]
  public async Task Migrate_FormatThree_RewritesToothFileInPlace() {
    // Arrange
    _ws.AddFile("tooth.json", """
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "github.com/test/repo",
          "version": "1.2.3",
          "variants": [
            {
              "platform": "win-x64",
              "dependencies": { "github.com/LiteLDev/LeviLamina": ">=1.9.0 <1.10.0" },
              "assets": [ { "type": "self", "placements": [
                { "type": "file", "src": "a.txt", "dest": "a.txt" } ] } ],
              "scripts": { "install": ["echo install"], "post_install": ["echo post"] }
            }
          ]
        }
        """);
    _ws.AddFile("a.txt", "a");

    // Act
    await _client.Migrate();

    // Assert: protocol/cli.md §5.8, indented by two spaces, valid format 4.
    string text = _ws.Fs.File.ReadAllText(_ws.Path("tooth.json"));
    Assert.Contains("\n  \"format_version\": 4,", text.Replace("\r\n", "\n"));
    ToothFile tooth = ToothParser.Parse(_ws.Fs.File.ReadAllBytes(_ws.Path("tooth.json")));
    Assert.Equal(("github.com/test/repo", "1.2.3"), (tooth.Tooth, tooth.Version.ToString()));
    JsonNode document = JsonNode.Parse(text)!;
    JsonArray entries = document["variants"]![""]!.AsArray();
    Assert.Equal(
        [("server", "win-x64"), ("client", "win-x64")],
        entries.Select(e => ((string)e!["platform"]!, (string)e["runtime"]!)));
    JsonNode server = entries[0]!;
    Assert.Equal(
        ">=1.9.0 <1.10.0", (string)server["dependencies"]!["github.com/LiteLDev/LeviLamina"]!);
    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse("""["echo install", "echo post"]"""), server["hooks"]!["post_install"]));
    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse("""[ { "type": "self", "placements": [ { "src": "a.txt", "dest": "a.txt" } ] } ]"""),
        server["assets"]));
    Assert.Equal(["a.txt", "tooth.json"], _ws.Fs.Directory.GetFiles(_ws.Root).Select(
        f => _ws.Fs.Path.GetFileName(f)).Order());
  }

  [Fact]
  public async Task Migrate_FormatTwoPlatforms_WritesEntryPerPlatformAndRuntime() {
    // Arrange
    _ws.AddFile("tooth.json", """
        {
          "format_version": 2,
          "tooth": "github.com/test/repo",
          "version": "1.0.0",
          "info": { "name": "Test", "description": "Test package", "author": "A", "tags": [] },
          "platforms": [
            { "goos": "windows", "goarch": "amd64", "files": { "place": [] } },
            { "goos": "linux", "goarch": "amd64", "files": { "place": [] } }
          ]
        }
        """);

    // Act
    await _client.Migrate();

    // Assert
    JsonNode document = JsonNode.Parse(_ws.Fs.File.ReadAllText(_ws.Path("tooth.json")))!;
    Assert.Equal(4, (int)document["format_version"]!);
    Assert.Equal(
        [("client", "linux-x64"), ("client", "win-x64"), ("server", "linux-x64"), ("server", "win-x64")],
        document["variants"]![""]!.AsArray()
            .Select(e => ((string)e!["platform"]!, (string)e["runtime"]!)).Order());
  }

  [Fact]
  public async Task Migrate_VariantWithoutPlatform_AsksForRuntimes() {
    // Arrange
    _ws.AddFile("tooth.json", WorkspaceHarness.Tooth("github.com/test/repo", "1.0.0", "x.dll"));
    _ws.AddFile("file.dll", "x");
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync("win-x64, linux-arm64");

    // Act
    await _client.Migrate();

    // Assert
    JsonNode document = JsonNode.Parse(_ws.Fs.File.ReadAllText(_ws.Path("tooth.json")))!;
    Assert.Equal(
        ["linux-arm64", "win-x64"],
        document["variants"]![""]!.AsArray()
            .Where(e => (string)e!["platform"]! == "server")
            .Select(e => (string)e!["runtime"]!).Order());
  }

  [Fact]
  public async Task Migrate_CannotAsk_ThrowsAndChangesNothing() {
    // Arrange
    string original = WorkspaceHarness.Tooth("github.com/test/repo", "1.0.0", "x.dll");
    _ws.AddFile("tooth.json", original);
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>()))
        .ThrowsAsync(new CannotAskException("cannot ask"));

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(_client.Migrate);
    Assert.Equal(original, _ws.Fs.File.ReadAllText(_ws.Path("tooth.json")));
  }

  [Fact]
  public async Task Migrate_NoToothFile_ThrowsAndWritesNothing() {
    await Assert.ThrowsAsync<InvalidOperationException>(_client.Migrate);
    Assert.False(_ws.Exists("tooth.json"));
  }

  [Theory]
  [InlineData("""{ "format_version": 4, "tooth": "github.com/test/repo" }""")]
  [InlineData("not json")]
  [InlineData("""{ "format_version": 5 }""")]
  public async Task Migrate_NotOfEarlierFormat_ThrowsAndChangesNothing(string content) {
    // Arrange
    _ws.AddFile("tooth.json", content);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(_client.Migrate);
    Assert.Equal(content, _ws.Fs.File.ReadAllText(_ws.Path("tooth.json")));
  }

  #endregion

  [Fact]
  public async Task List_ReturnsIdentityAtVersionByGroupInInstallationOrder() {
    // Arrange: b, then a needing c, then c#x.
    foreach ((string tooth, string entry) in new[] {
      ("example.com/b", ""),
      ("example.com/c", ""),
      ("example.com/a", """ "dependencies": { "example.com/c": "^2.0.0" } """),
    }) {
      _ws.AddTag(tooth, "2.0.0", new() { ["tooth.json"] = WorkspaceHarness.Tooth4(tooth, "2.0.0", entry) });
    }

    _ws.AddTag("example.com/v", "1.0.0", new() {
      ["tooth.json"] = """
          {
            "format_version": 4,
            "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
            "tooth": "example.com/v",
            "version": "1.0.0",
            "variants": { "x": [ { "platform": "server", "runtime": "win-x64" } ] }
          }
          """,
    });
    OperationOptions options = new(Rid: WorkspaceHarness.Runtime);
    await _ws.Service.Install(["example.com/b"], options);
    await _ws.Service.Install(["example.com/a"], options);
    await _ws.Service.Install(["example.com/v#x"], options);

    // Act
    (IEnumerable<string> explicitPackages, IEnumerable<string> implicitPackages) =
        await _client.List();

    // Assert
    Assert.Equal(
        ["example.com/b@2.0.0", "example.com/a@2.0.0", "example.com/v#x@1.0.0"], explicitPackages);
    Assert.Equal(["example.com/c@2.0.0"], implicitPackages);
  }

  [Fact]
  public async Task List_NoWorkspaceFile_ReturnsEmptyGroups() {
    (IEnumerable<string> explicitPackages, IEnumerable<string> implicitPackages) =
        await _client.List();

    Assert.Empty(explicitPackages);
    Assert.Empty(implicitPackages);
  }
}
