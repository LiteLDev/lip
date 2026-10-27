using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

public class ConfigServiceTests {
  private static string GetConfigPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "config.yaml");

  private static string GetLegacyConfigPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "liprc.json");

  [Fact]
  public async Task List_FileNotExists_ReturnsDefaultWithoutSaving() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(Path.GetDirectoryName(configPath)!, "placeholder.txt"), new MockFileData("") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    var result = await service.List();

    // Assert
    Assert.Equal("", result["github_proxy"]);
    Assert.False(mockFileSystem.File.Exists(configPath));
  }

  [Fact]
  public async Task Get_ValidFile_ReturnsConfig() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData("github_proxy: https://proxy.com\n") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
  }

  [Fact]
  public async Task Get_EmptyFile_ReturnsDefault() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetConfigPath(), new MockFileData("") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("", result);
    mockUserInteraction.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Get_InvalidYaml_PrintsErrorAndReturnsDefault() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetConfigPath(), new MockFileData("github_proxy: [unclosed\n") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("", result);
    mockUserInteraction.Verify(u => u.PrintError(It.IsAny<string>()), Times.Once);
  }

  [Fact]
  public async Task Set_CreatesYamlFileWithConfig() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { Path.Combine(Path.GetDirectoryName(configPath)!, "placeholder.txt"), new MockFileData("") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.Equal("github_proxy: https://new.com", mockFileSystem.File.ReadAllText(configPath).Trim());
    Assert.Equal("https://new.com", await service.Get("github_proxy"));
  }

  [Fact]
  public async Task Delete_SetKey_RemovesItFromFile() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData("github_proxy: https://proxy.com\n") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    await service.Delete("github_proxy");

    // Assert
    Assert.DoesNotContain("github_proxy", mockFileSystem.File.ReadAllText(configPath));
    Assert.Equal("", await service.Get("github_proxy"));
  }

  private const string _legacyConfigJson = """
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "github_proxy": "https://proxy.com",
        "go_module_proxy": "https://goproxy.cn"
      }
      """;

  [Fact]
  public async Task List_LegacyLiprcJson_ConvertsToYamlAndDeletesIt() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetLegacyConfigPath(), new MockFileData(_legacyConfigJson) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("https://proxy.com", result["github_proxy"]);
    Assert.False(result.ContainsKey("go_module_proxy"));
    Assert.False(mockFileSystem.File.Exists(GetLegacyConfigPath()));
    Assert.Equal(
        "github_proxy: https://proxy.com",
        mockFileSystem.File.ReadAllText(GetConfigPath()).Trim());
    mockUserInteraction.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task List_CorruptLegacyLiprcJson_KeepsItAndReturnsDefault() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetLegacyConfigPath(), new MockFileData("not json") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("", result["github_proxy"]);
    Assert.True(mockFileSystem.File.Exists(GetLegacyConfigPath()));
    Assert.False(mockFileSystem.File.Exists(GetConfigPath()));
  }

  [Fact]
  public async Task List_BothFilesExist_UsesYamlAndKeepsLegacy() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetConfigPath(), new MockFileData("github_proxy: https://yaml.com\n") },
            { GetLegacyConfigPath(), new MockFileData(_legacyConfigJson) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("https://yaml.com", result["github_proxy"]);
    Assert.True(mockFileSystem.File.Exists(GetLegacyConfigPath()));
  }

  [Fact]
  public async Task Get_LegacyGoModuleProxyKey_ThrowsKeyNotFoundException() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetLegacyConfigPath(), new MockFileData(_legacyConfigJson) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Get("go_module_proxy"));
  }

  [Fact]
  public async Task Get_UnknownYamlKey_IsIgnored() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetConfigPath(), new MockFileData("go_module_proxy: https://goproxy.cn\ngithub_proxy: https://proxy.com\n") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
    mockUserInteraction.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }
}
