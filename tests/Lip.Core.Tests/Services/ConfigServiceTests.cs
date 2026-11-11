using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

/// The configuration (protocol/config.toml.md, config.schema.toml) and the migration of
/// liprc.json (protocol/lip-compatibility.md, "liprc.json").
public class ConfigServiceTests {
  private static string GetConfigPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "config.toml");

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
      [GetConfigPath()] = new("format = 4\ngithub_proxy = \"https://proxy.com\"\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
  }

  /// protocol/config.toml.md: a config.toml that is not valid against config.schema.toml, which
  /// requires "format": 4, means every key has its default.
  [Theory]
  [InlineData("")]
  [InlineData("github_proxy = \"https://proxy.com\"\n")]
  [InlineData("format = 3\ngithub_proxy = \"https://proxy.com\"\n")]
  [InlineData("format = \"4\"\ngithub_proxy = \"https://proxy.com\"\n")]
  [InlineData("format = 4\ngithub_proxy = 5\n")]
  [InlineData("format = 4\ngithub_proxy = [\"unclosed\"\n")]
  [InlineData("format = 4\nformat = 4\n")]
  [InlineData("[[format]]\n")]
  // config.schema.toml: github_proxy has the format "uri".
  [InlineData("format = 4\ngithub_proxy = \"\"\n")]
  [InlineData("format = 4\ngithub_proxy = \"example.com/mirror\"\n")]
  [InlineData("format = 4\ngithub_proxy = \"https://example.com/a b\"\n")]
  public async Task Get_InvalidFile_PrintsErrorAndReturnsDefault(string toml) {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new(toml),
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
      [GetConfigPath()] = new("format = 4\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("", result);
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Set_CreatesTomlFileWithFormatAndConfig() {
    // Arrange
    string configPath = GetConfigPath();
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [Path.Combine(Path.GetDirectoryName(configPath)!, "placeholder.txt")] = new(""),
    });

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.Equal("format = 4\ngithub_proxy = \"https://new.com\"\n", fs.File.ReadAllText(configPath));
    Assert.Equal("https://new.com", await service.Get("github_proxy"));
  }

  /// config.schema.toml: github_proxy has the format "uri", so a value that is not a URI would
  /// make config.toml invalid.
  [Theory]
  [InlineData("")]
  [InlineData("true")]
  [InlineData("1.0")]
  [InlineData("example.com/mirror")]
  [InlineData("/home/user/mirror")]
  [InlineData(@"C:\mirror")]
  [InlineData("https://example.com/a b")]
  [InlineData("https://example.com/\"x\"")]
  public async Task Set_NotAUri_FailsAndChangesNothing(string value) {
    // Arrange
    string configPath = GetConfigPath();
    const string original = "format = 4\ngithub_proxy = \"https://proxy.com\"\n";
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [configPath] = new(original),
    });

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => service.Set("github_proxy", value));
    Assert.Equal(original, fs.File.ReadAllText(configPath));
  }

  [Theory]
  [InlineData("https://example.com/")]
  [InlineData("http://127.0.0.1:8080/github/")]
  [InlineData("https://user:t0k%20en@example.com/a?b=c#d")]
  public async Task Set_Uri_ReadsBackAsTheString(string value) {
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
      [configPath] = new("format = 4\ngithub_proxy = \"https://proxy.com\"\n"),
    });

    // Act
    await service.Delete("github_proxy");

    // Assert
    Assert.Equal("format = 4\n", fs.File.ReadAllText(configPath));
    Assert.Equal("", await service.Get("github_proxy"));
  }

  /// protocol/config.toml.md: the file is written atomically, through a .tmp file in the config
  /// directory that is renamed over it.
  [Fact]
  public async Task Set_ExistingFile_ReplacesItLeavingNoOtherFile() {
    // Arrange
    string configPath = GetConfigPath();
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [configPath] = new("format = 4\ngithub_proxy = \"https://old.com\"\n"),
    });

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.Equal("format = 4\ngithub_proxy = \"https://new.com\"\n", fs.File.ReadAllText(configPath));
    Assert.Equal([configPath], fs.Directory.GetFiles(Path.GetDirectoryName(configPath)!));
  }

  [Fact]
  public async Task Get_GoModuleProxyKey_ThrowsNamingTheValidKey() {
    // Arrange
    (_, _, ConfigService service) = Create(new() {
      [GetConfigPath()] = new("format = 4\ngo_module_proxy = \"https://goproxy.cn\"\n"),
    });

    // Act & Assert
    ArgumentException ex =
        await Assert.ThrowsAsync<ArgumentException>(() => service.Get("go_module_proxy"));
    Assert.Contains("'go_module_proxy' is not a configuration key", ex.Message);
    Assert.Contains("github_proxy", ex.Message);
  }

  [Fact]
  public async Task Get_UnknownTomlKey_IsIgnored() {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new(
          "format = 4\ngo_module_proxy = \"https://goproxy.cn\"\ngithub_proxy = \"https://proxy.com\"\n"),
    });

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  /// protocol/config.toml.md: keys that are not configuration keys are ignored, whatever their
  /// value.
  [Theory]
  [InlineData("foo = { a = 1 }\n")]
  [InlineData("foo = [1, 2]\n")]
  public async Task Get_UnknownTomlKeyWithNonScalarValue_IsIgnored(string unknown) {
    // Arrange
    (_, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetConfigPath()] = new("format = 4\n" + unknown + "github_proxy = \"https://proxy.com\"\n"),
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
    await Assert.ThrowsAsync<ArgumentException>(() => service.Set("go_module_proxy", "x"));
    Assert.False(fs.File.Exists(GetConfigPath()));
  }

  // liprc.json (protocol/lip-compatibility.md, "liprc.json")

  [Fact]
  public async Task List_LiprcJson_WritesConfigTomlAndDeletesIt() {
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
        "format = 4\ngithub_proxy = \"https://proxy.com\"\n", fs.File.ReadAllText(GetConfigPath()));
    Assert.Equal([GetConfigPath()], fs.Directory.GetFiles(Path.GetDirectoryName(GetConfigPath())!));
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task List_LiprcJsonWithoutFormatVersion_WritesConfigTomlAndDeletesIt() {
    // Arrange: lip before February 2026 wrote liprc.json without format_version and format_uuid.
    (MockFileSystem fs, Mock<IUserInteraction> ui, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new("""
          {
            "cache": "C:/lip-cache",
            "github_proxy": "https://proxy.com",
            "go_module_proxy": "https://goproxy.io"
          }
          """),
    });

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("https://proxy.com", result["github_proxy"]);
    Assert.False(fs.File.Exists(GetLiprcPath()));
    Assert.Equal(
        "format = 4\ngithub_proxy = \"https://proxy.com\"\n", fs.File.ReadAllText(GetConfigPath()));
    ui.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Theory]
  [InlineData("""{ "format_version": 3 }""")]
  [InlineData("""{ "format_version": 3, "github_proxy": 5 }""")]
  [InlineData("""{ "format_version": 3, "github_proxy": null }""")]
  [InlineData("""{}""")]
  [InlineData("""{ "cache": "C:/cache", "go_module_proxy": "https://goproxy.io" }""")]
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
    Assert.Equal("format = 4\n", fs.File.ReadAllText(GetConfigPath()));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("""{ "format_version": 2, "github_proxy": "https://proxy.com" }""")]
  [InlineData("""{ "format_version": "3", "github_proxy": "https://proxy.com" }""")]
  [InlineData("""{ "format_version": null, "github_proxy": "https://proxy.com" }""")]
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
  [InlineData("format = 4\ngithub_proxy = \"https://toml.com\"\n", "https://toml.com")]
  [InlineData("github_proxy = \"https://toml.com\"\n", "")]
  public async Task List_ConfigTomlAndLiprcJson_ReadsConfigTomlAndKeepsLiprc(
      string toml, string expected) {
    // Arrange: liprc.json is read only when there is no config.toml, even an invalid one.
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetConfigPath()] = new(toml),
      [GetLiprcPath()] = new(_liprcJson),
    });

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal(expected, result["github_proxy"]);
    Assert.Equal(_liprcJson, fs.File.ReadAllText(GetLiprcPath()));
    Assert.Equal(toml, fs.File.ReadAllText(GetConfigPath()));
  }

  /// A dry run reads liprc.json the same way, but writes no config.toml and keeps it.
  [Theory]
  [InlineData(_liprcJson, "https://proxy.com")]
  [InlineData("""{ "github_proxy": "https://proxy.com" }""", "https://proxy.com")]
  [InlineData("""{ "format_version": 3 }""", null)]
  [InlineData("""{ "format_version": 2, "github_proxy": "https://proxy.com" }""", null)]
  public async Task GetGithubProxy_DryRunWithLiprcJson_ReadsItAndChangesNothing(
      string json, string? expected) {
    // Arrange
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new(json),
    });

    // Act
    string? result = await service.GetGithubProxy(dryRun: true);

    // Assert
    Assert.Equal(expected, result);
    Assert.Equal(json, fs.File.ReadAllText(GetLiprcPath()));
    Assert.Equal([GetLiprcPath()], fs.Directory.GetFiles(Path.GetDirectoryName(GetLiprcPath())!));
  }

  [Fact]
  public async Task GetGithubProxy_DryRunThenNot_MigratesOnlyTheSecondTime() {
    // Arrange
    (MockFileSystem fs, _, ConfigService service) = Create(new() {
      [GetLiprcPath()] = new(_liprcJson),
    });

    // Act
    string? dryRun = await service.GetGithubProxy(dryRun: true);
    bool keptByDryRun = fs.File.Exists(GetLiprcPath());
    string? result = await service.GetGithubProxy();

    // Assert
    Assert.Equal("https://proxy.com", dryRun);
    Assert.True(keptByDryRun);
    Assert.Equal("https://proxy.com", result);
    Assert.False(fs.File.Exists(GetLiprcPath()));
    Assert.Equal(
        "format = 4\ngithub_proxy = \"https://proxy.com\"\n", fs.File.ReadAllText(GetConfigPath()));
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
    Assert.Equal("format = 4\ngithub_proxy = \"https://new.com\"\n", fs.File.ReadAllText(GetConfigPath()));
  }
}
