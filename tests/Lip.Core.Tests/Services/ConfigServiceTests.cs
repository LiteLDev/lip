using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

/// The configuration (protocol/userspace.md §2, config.schema.yaml) and the migration of
/// liprc.json (protocol/compatibility.md §4).
public class ConfigServiceTests {
  private static string GetConfigPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "config.yaml");

  private static string GetLiprcPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "liprc.json");

  private const string _liprcJson = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "github_proxy": "https://proxy.com",
        "go_module_proxy": "https://goproxy.cn"
      }
      """;

  private static (MockFileSystem Fs, Mock<IUserInteraction> Ui, ConfigService Service) Create(
      Dictionary<string, MockFileData> files) {
    MockFileSystem fs = new(files);
    Mock<IUserInteraction> ui = new();
    return (fs, ui, new ConfigService(fs, ui.Object));
  }

  [Fact]
  public async Task List_FileNotExists_ReturnsDefaultWithoutSaving() {
    // Arrange
    string configPath = GetConfigPath();
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [Path.Combine(Path.GetDirectoryName(configPath)!, "placeholder.txt")] = new(""),
    });

    // Act
    var result = await service.List();

    // Assert
    Assert.Equal("", result["github_proxy"]);
    Assert.False(fs.File.Exists(configPath));
  }

  [Fact]
  public async Task Get_ValidFile_ReturnsConfig() {
    // Arrange
    (_, _, ConfigService service) = Create(new() {
      [GetConfigPath()] = new("format: 4\ngithub_proxy: https://proxy.com\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
  }

  /// protocol/userspace.md §2: a config.yaml that is not valid against config.schema.yaml, which
  /// requires "format": 4, means every key has its default.
  [Theory]
  [InlineData("")]
  [InlineData("github_proxy: https://proxy.com\n")]
  [InlineData("format: 3\ngithub_proxy: https://proxy.com\n")]
  [InlineData("format: \"4\"\ngithub_proxy: https://proxy.com\n")]
  [InlineData("format: 4\ngithub_proxy: 5\n")]
  [InlineData("format: 4\ngithub_proxy: [unclosed\n")]
  [InlineData("- format: 4\n")]
  public async Task Get_InvalidFile_PrintsErrorAndReturnsDefault(string yaml) {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new(yaml),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("", result);
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Once);
  }

  [Fact]
  public async Task Get_FormatWithoutKeys_ReturnsDefault() {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new("format: 4\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("", result);
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Set_CreatesYamlFileWithFormatAndConfig() {
    // Arrange
    string configPath = GetConfigPath();
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [Path.Combine(Path.GetDirectoryName(configPath)!, "placeholder.txt")] = new(""),
    });

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.Equal("format: 4\ngithub_proxy: https://new.com\n", fs.File.ReadAllText(configPath));
    Assert.Equal("https://new.com", await service.Get("github_proxy"));
  }

  [Theory]
  [InlineData("")]
  [InlineData("true")]
  [InlineData("1.0")]
  [InlineData("null")]
  public async Task Set_ValueThatLooksLikeAnotherType_ReadsBackAsTheString(string value) {
    // Arrange
    (_, _, ConfigService service) = Create([]);

    // Act
    await service.Set("github_proxy", value);

    // Assert
    Assert.Equal(value, await service.GetGithubProxy());
  }

  [Fact]
  public async Task Delete_SetKey_RemovesItFromFileAndKeepsFormat() {
    // Arrange
    string configPath = GetConfigPath();
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [configPath] = new("format: 4\ngithub_proxy: https://proxy.com\n"),
    });

    // Act
    await service.Delete("github_proxy");

    // Assert
    Assert.Equal("format: 4\n", fs.File.ReadAllText(configPath));
    Assert.Equal("", await service.Get("github_proxy"));
  }

  /// protocol/userspace.md §2: the file is written atomically, through a .tmp file in the config
  /// directory that is renamed over it.
  [Fact]
  public async Task Set_ExistingFile_ReplacesItLeavingNoOtherFile() {
    // Arrange
    string configPath = GetConfigPath();
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [configPath] = new("format: 4\ngithub_proxy: https://old.com\n"),
    });

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.Equal("format: 4\ngithub_proxy: https://new.com\n", fs.File.ReadAllText(configPath));
    Assert.Equal([configPath], fs.Directory.GetFiles(Path.GetDirectoryName(configPath)!));
  }

  [Fact]
  public async Task Get_GoModuleProxyKey_ThrowsKeyNotFoundException() {
    // Arrange
    (_, _, ConfigService service) = Create(new() {
      [GetConfigPath()] = new("format: 4\ngo_module_proxy: https://goproxy.cn\n"),
    });

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Get("go_module_proxy"));
  }

  [Fact]
  public async Task Get_UnknownYamlKey_IsIgnored() {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new(
          "format: 4\ngo_module_proxy: https://goproxy.cn\ngithub_proxy: https://proxy.com\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  /// protocol/userspace.md §2: keys that are not configuration keys are ignored, whatever their
  /// value.
  [Theory]
  [InlineData("foo:\n  a: 1\n")]
  [InlineData("foo: [1, 2]\n")]
  public async Task Get_UnknownYamlKeyWithNonScalarValue_IsIgnored(string unknown) {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new("format: 4\n" + unknown + "github_proxy: https://proxy.com\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
    Assert.Equal("https://proxy.com", await service.GetGithubProxy());
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task GetGithubProxy_FileNotExists_ReturnsNull() {
    // Arrange
    (_, _, ConfigService service) = Create([]);

    // Act & Assert
    Assert.Null(await service.GetGithubProxy());
  }

  [Fact]
  public async Task Set_UnknownKey_ThrowsAndWritesNothing() {
    // Arrange
    (MockFileSystem fs, _, ConfigService service) = Create([]);

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Set("go_module_proxy", "x"));
    Assert.False(fs.File.Exists(GetConfigPath()));
  }

  // liprc.json (protocol/compatibility.md §4)

  [Fact]
  public async Task List_LiprcJson_WritesConfigYamlAndDeletesIt() {
    // Arrange
    (MockFileSystem fs, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new(_liprcJson),
    });

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("https://proxy.com", result["github_proxy"]);
    Assert.False(result.ContainsKey("go_module_proxy"));
    Assert.False(fs.File.Exists(GetLiprcPath()));
    Assert.Equal(
        "format: 4\ngithub_proxy: https://proxy.com\n", fs.File.ReadAllText(GetConfigPath()));
    Assert.Equal([GetConfigPath()], fs.Directory.GetFiles(Path.GetDirectoryName(GetConfigPath())!));
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Theory]
  [InlineData("""{ "format_version": 3 }""")]
  [InlineData("""{ "format_version": 3, "github_proxy": 5 }""")]
  [InlineData("""{ "format_version": 3, "github_proxy": null }""")]
  public async Task List_LiprcJsonWithoutStringGithubProxy_WritesFormatOnly(string json) {
    // Arrange
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new(json),
    });

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("", result["github_proxy"]);
    Assert.False(fs.File.Exists(GetLiprcPath()));
    Assert.Equal("format: 4\n", fs.File.ReadAllText(GetConfigPath()));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("""{ "github_proxy": "https://proxy.com" }""")]
  [InlineData("""{ "format_version": 2, "github_proxy": "https://proxy.com" }""")]
  [InlineData("""{ "format_version": "3", "github_proxy": "https://proxy.com" }""")]
  public async Task List_LiprcJsonNotOfFormat3_KeepsItAndReturnsDefault(string json) {
    // Arrange
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new(json),
    });

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("", result["github_proxy"]);
    Assert.Equal(json, fs.File.ReadAllText(GetLiprcPath()));
    Assert.False(fs.File.Exists(GetConfigPath()));
  }

  [Theory]
  [InlineData("format: 4\ngithub_proxy: https://yaml.com\n", "https://yaml.com")]
  [InlineData("github_proxy: https://yaml.com\n", "")]
  public async Task List_ConfigYamlAndLiprcJson_ReadsConfigYamlAndKeepsLiprc(
      string yaml, string expected) {
    // Arrange: liprc.json is read only when there is no config.yaml, even an invalid one.
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetConfigPath()] = new(yaml),
      [GetLiprcPath()] = new(_liprcJson),
    });

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal(expected, result["github_proxy"]);
    Assert.Equal(_liprcJson, fs.File.ReadAllText(GetLiprcPath()));
    Assert.Equal(yaml, fs.File.ReadAllText(GetConfigPath()));
  }

  [Fact]
  public async Task Set_LiprcJson_MigratesItThenSets() {
    // Arrange
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new(_liprcJson),
    });

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.False(fs.File.Exists(GetLiprcPath()));
    Assert.Equal("format: 4\ngithub_proxy: https://new.com\n", fs.File.ReadAllText(GetConfigPath()));
  }
}
