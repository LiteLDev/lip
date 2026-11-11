using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Lip.Core.Tests.Mcmod;
using Moq;

namespace Lip.Core.Tests.PublicApi;

public class LipClientTests {
  private readonly WorkspaceHarness _ws = new();
  private readonly Mock<ICacheService> _cacheService = new();
  private readonly LipClient _client;

  public LipClientTests() {
    _ws.Fs.Directory.SetCurrentDirectory(_ws.Root);
    _client = new LipClient(
        _ws.Fs,
        _ws.Ui.Object,
        _cacheService.Object,
        new ConfigService(_ws.Fs, _ws.Ui.Object),
        _ws.Service);
  }

  [Fact]
  public async Task CacheClean_CallsService() {
    await _client.CacheClean();
    _cacheService.Verify(s => s.Clean(), Times.Once);
  }

  #region Init

  /// protocol/lip-init.md: asks until the answer is a Go module path, then writes exactly the
  /// members of the document it gives.
  [Fact]
  public async Task Init_AsksPackageIdUntilValid_WritesPackageFile() {
    // Arrange
    _ws.Ui.SetupSequence(u => u.Ask(It.IsAny<string>()))
        .ReturnsAsync("not a module path")
        .ReturnsAsync("  github.com/owner/repo  ");

    // Act
    await _client.Init();

    // Assert
    JsonNode expected = JsonNode.Parse("""
        {
          "format": 4,
          "id": "github.com/owner/repo",
          "version": "0.1.0",
          "variants": { "": [ { "side": "server", "platform": "win-x64" } ] }
        }
        """)!;
    byte[] bytes = _ws.Fs.File.ReadAllBytes(_ws.Path("lip.toml"));
    Assert.True(JsonNode.DeepEquals(expected, Toml(bytes)));
    Assert.Equal("github.com/owner/repo", PackageFileParser.Parse(bytes).Id);
    Assert.False(_ws.Exists("tooth.json"));
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Exactly(2));
    Assert.Contains(_ws.Warnings, w => w.Contains("not a module path"));
  }

  [Fact]
  public async Task Init_PackageFileExists_ThrowsWithoutAskingOrWriting() {
    // Arrange
    _ws.AddFile("lip.toml", "{}");

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(_client.Init);
    Assert.Contains("lip.toml already exists", ex.Message);
    Assert.Contains("delete lip.toml", ex.Message);
    Assert.Equal("{}", _ws.Fs.File.ReadAllText(_ws.Path("lip.toml")));
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Init_OnlyToothJsonExists_WritesPackageFile() {
    // Arrange: only lip.toml makes lip init fail (protocol/lip-init.md).
    _ws.AddFile("tooth.json", "{}");
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync("github.com/owner/repo");

    // Act
    await _client.Init();

    // Assert
    Assert.True(_ws.Exists("lip.toml"));
    Assert.Equal("{}", _ws.Fs.File.ReadAllText(_ws.Path("tooth.json")));
  }

  [Fact]
  public async Task Init_CannotAsk_ThrowsAndWritesNothing() {
    // Arrange
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>()))
        .ThrowsAsync(new CannotAskException("cannot ask"));

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(_client.Init);
    Assert.False(_ws.Exists("lip.toml"));
  }

  [Fact]
  public async Task Init_NoAnswer_ThrowsAndWritesNothing() {
    // Arrange: a user interaction that returns no answer cannot ask.
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).Returns(Task.FromResult<string>(null!));

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(_client.Init);
    Assert.False(_ws.Exists("lip.toml"));
  }

  #endregion

  #region Migrate

  [Fact]
  public async Task Migrate_FormatThree_WritesPackageFileAndDeletesToothJson() {
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

    // Assert: protocol/lip-compatibility.md, "lip migrate", a valid package file.
    byte[] bytes = _ws.Fs.File.ReadAllBytes(_ws.Path("lip.toml"));
    Assert.StartsWith("format = 4\n", System.Text.Encoding.UTF8.GetString(bytes));
    PackageFile packageFile = PackageFileParser.Parse(bytes);
    Assert.Equal(
        ("github.com/test/repo", "1.2.3"), (packageFile.Id, packageFile.Version.ToString()));
    JsonNode document = Toml(bytes)!;
    JsonArray entries = document["variants"]![""]!.AsArray();
    Assert.Equal(
        [("server", "win-x64")],
        entries.Select(e => ((string)e!["side"]!, (string)e["platform"]!)));
    JsonNode server = entries[0]!;
    Assert.Equal(
        ">=1.9.0 <1.10.0", (string)server["deps"]!["github.com/LiteLDev/LeviLamina"]!);
    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse("""["echo install", "echo post"]"""), server["hooks"]!["post_install"]));
    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse("""[ { "type": "self", "place": [ { "src": "a.txt", "dest": "a.txt" } ] } ]"""),
        server["assets"]));
    Assert.Equal(["a.txt", "lip.toml"], _ws.Fs.Directory.GetFiles(_ws.Root).Select(
        f => _ws.Fs.Path.GetFileName(f)).Order());
  }

  [Fact]
  public async Task Migrate_FormatTwoPlatforms_WritesEntryPerSideAndPlatform() {
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
    JsonNode document = Toml(_ws.Fs.File.ReadAllBytes(_ws.Path("lip.toml")))!;
    Assert.Equal(4, (int)document["format"]!);
    Assert.Equal(
        [("client", "linux-x64"), ("client", "win-x64"), ("server", "linux-x64"), ("server", "win-x64")],
        document["variants"]![""]!.AsArray()
            .Select(e => ((string)e!["side"]!, (string)e["platform"]!)).Order());
  }

  [Fact]
  public async Task Migrate_VariantWithoutPlatform_AsksForPlatforms() {
    // Arrange
    _ws.AddFile("tooth.json", WorkspaceHarness.Tooth("github.com/test/repo", "1.0.0", "x.dll"));
    _ws.AddFile("file.dll", "x");
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync("win-x64, linux-arm64");

    // Act
    await _client.Migrate();

    // Assert
    JsonNode document = Toml(_ws.Fs.File.ReadAllBytes(_ws.Path("lip.toml")))!;
    Assert.Equal(
        ["linux-arm64", "win-x64"],
        document["variants"]![""]!.AsArray()
            .Where(e => (string)e!["side"]! == "server")
            .Select(e => (string)e!["platform"]!).Order());
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
    Assert.False(_ws.Exists("lip.toml"));
  }

  [Fact]
  public async Task Migrate_NoToothJson_ThrowsAndWritesNothing() {
    await Assert.ThrowsAsync<InvalidOperationException>(_client.Migrate);
    Assert.False(_ws.Exists("lip.toml"));
  }

  [Fact]
  public async Task Migrate_PackageFileExists_ThrowsAndChangesNothing() {
    // Arrange
    string original = WorkspaceHarness.Tooth("github.com/test/repo", "1.0.0", "x.dll");
    _ws.AddFile("tooth.json", original);
    _ws.AddFile("lip.toml", "mine");

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(_client.Migrate);
    Assert.Equal(original, _ws.Fs.File.ReadAllText(_ws.Path("tooth.json")));
    Assert.Equal("mine", _ws.Fs.File.ReadAllText(_ws.Path("lip.toml")));
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
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
    Assert.False(_ws.Exists("lip.toml"));
  }

  #endregion

  #region Configuration

  private static readonly string _configPath = Path.Join(LipDirectories.Config, "config.toml");

  private static readonly string _liprcPath = Path.Join(LipDirectories.Config, "liprc.json");

  /// The configuration is read, migrating liprc.json (protocol/lip-compatibility.md, "liprc.json"),
  /// only by a command that needs it, not when the client is created.
  [Fact]
  public async Task Create_CommandWithoutConfiguration_KeepsLiprc() {
    // Arrange
    _ws.Fs.AddFile(_liprcPath, new("""{ "github_proxy": "https://proxy.com" }"""));
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync("github.com/owner/repo");

    // Act
    LipClient client = LipClient.Create(_ws.Fs, _ws.Ui.Object);
    await client.Init();

    // Assert
    Assert.True(_ws.Exists("lip.toml"));
    Assert.True(_ws.Fs.File.Exists(_liprcPath));
    Assert.False(_ws.Fs.File.Exists(_configPath));
  }

  [Fact]
  public async Task Create_CommandReadingConfiguration_MigratesLiprc() {
    // Arrange
    _ws.Fs.AddFile(_liprcPath, new("""{ "github_proxy": "https://proxy.com" }"""));

    // Act
    LipClient client = LipClient.Create(_ws.Fs, _ws.Ui.Object);
    await client.List();

    // Assert
    Assert.False(_ws.Fs.File.Exists(_liprcPath));
    Assert.Equal("https://proxy.com", await client.ConfigGet("github_proxy"));
  }

  /// When config.toml cannot be written, the configuration cannot be read
  /// (protocol/lip-compatibility.md, "liprc.json"): a command reading it fails, and the others
  /// still work.
  [Fact]
  public async Task Create_MigrationWriteFails_FailsOnlyCommandsReadingConfiguration() {
    // Arrange
    _ws.Fs.AddFile(_liprcPath, new("""{ "github_proxy": "https://proxy.com" }"""));
    _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync("github.com/owner/repo");
    FaultyFileSystem fs = new(_ws.Fs) {
      Fail = (operation, path) =>
          operation == "move" && path.StartsWith(LipDirectories.Config, StringComparison.Ordinal),
    };

    // Act
    LipClient client = LipClient.Create(fs, _ws.Ui.Object);
    await client.Init();
    IOException ex = await Assert.ThrowsAsync<IOException>(client.List);

    // Assert
    Assert.Contains("config.toml", ex.Message);
    Assert.True(_ws.Exists("lip.toml"));
    Assert.True(_ws.Fs.File.Exists(_liprcPath));
    Assert.False(_ws.Fs.File.Exists(_configPath));
  }

  #endregion

  [Fact]
  public async Task List_ReturnsIdentityAtVersionByGroupInInstallationOrder() {
    // Arrange: b, then a needing c, then c#x.
    foreach ((string id, string entry) in new[] {
      ("example.com/b", ""),
      ("example.com/c", ""),
      ("example.com/a", """ "deps": { "example.com/c": "^2.0.0" } """),
    }) {
      _ws.AddTag(id, "2.0.0", new() { ["lip.toml"] = WorkspaceHarness.LipToml(id, "2.0.0", entry) });
    }

    _ws.AddTag("example.com/v", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.Toml("""
          {
            "format": 4,
            "id": "example.com/v",
            "version": "1.0.0",
            "variants": { "x": [ { "side": "server", "platform": "win-x64" } ] }
          }
          """),
    });
    OperationOptions options = new(Platform: WorkspaceHarness.Platform);
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

  /// A TOML file as the JSON value it represents.
  private static JsonNode? Toml(byte[] toml) {
    using JsonDocument document = TomlJson.Parse(toml);
    return JsonNode.Parse(document.RootElement.GetRawText());
  }
}
