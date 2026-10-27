using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
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

  [Fact]
  public async Task Init_WritesMarkedConcreteManifest() {
    // Act
    await _client.Init();

    // Assert
    byte[] bytes = _ws.Fs.File.ReadAllBytes(_ws.Path("manifest.json"));
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(new DocumentParser().Parse(bytes));
    Assert.True(document.IsMarked);
    Assert.Equal("ws", document.Name);
    Assert.Equal("native", document.Type);
    Assert.False(_ws.Exists("tooth.json"));
  }

  [Fact]
  public async Task Init_ManifestExists_Throws() {
    _ws.AddFile("manifest.json", "{}");

    await Assert.ThrowsAsync<InvalidOperationException>(_client.Init);
  }

  [Fact]
  public async Task Migrate_SingleSelfVariant_WritesConcreteFilePackage() {
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
                { "type": "dir", "src": "bin", "dest": "plugins/repo" },
                { "type": "file", "src": "a.txt", "dest": "a.txt" } ] } ],
              "scripts": { "install": ["echo install"], "post_install": ["echo post"] }
            }
          ]
        }
        """);

    // Act
    await _client.Migrate("tooth.json", "manifest.json");

    // Assert
    byte[] bytes = _ws.Fs.File.ReadAllBytes(_ws.Path("manifest.json"));
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(new DocumentParser().Parse(bytes));
    Assert.True(document.IsMarked);
    Assert.Equal("repo", document.Name);
    Assert.Equal("files", document.Type);
    Assert.Equal("win-x64", document.Rid);
    Assert.Equal(
        [new Placement("bin/", "plugins/repo/"), new Placement("a.txt", "a.txt")],
        document.Placements);
    InstallDep dependency = Assert.Single(document.InstallDependencies);
    Assert.Equal("github.com/LiteLDev/LeviLamina", Assert.Single(dependency.Refs).ToString());
    Assert.True(dependency.Version!.IsSatisfiedBy(Semver.SemVersion.Parse("1.9.5")));
    Assert.False(dependency.Version.IsSatisfiedBy(Semver.SemVersion.Parse("1.10.0")));
    Assert.Equal(["echo install", "echo post"], document.Scripts.PostInstall);
  }

  [Fact]
  public async Task Migrate_SeveralVariants_WritesRoutingDocumentAndWarns() {
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
    await _client.Migrate("tooth.json", "out/manifest.json");

    // Assert
    byte[] bytes = _ws.Fs.File.ReadAllBytes(_ws.Path("out/manifest.json"));
    RoutingDocument document = Assert.IsType<RoutingDocument>(new DocumentParser().Parse(bytes));
    Assert.Equal(2, document.Artifacts.Count);
    Assert.NotEmpty(_ws.Warnings);
  }

  [Fact]
  public async Task List_ReturnsExplicitAndImplicitPackages() {
    // Arrange
    _ws.AddFile("lip_lock.json", JsonSerializer.Serialize(new LockFile {
      Packages = [Locked("A", "1.0.0", true), Locked("B", "2.0.0", false)],
    }));

    // Act
    (IEnumerable<string> explicitPackages, IEnumerable<string> implicitPackages) =
        await _client.List();

    // Assert
    Assert.Equal(["A@1.0.0"], explicitPackages);
    Assert.Equal(["B@2.0.0"], implicitPackages);
  }

  private static LockedPackage Locked(string name, string version, bool isExplicit) => new() {
    Identity = name,
    Version = version,
    Kind = "mcmod",
    Explicit = isExplicit,
    Chain = [],
    ArchiveKey = name,
    Manifest = JsonDocument.Parse("{}").RootElement,
  };
}
