using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

public class ConfigServiceTests {
  private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

  private static string GetConfigPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "liprc.json");

  [Fact]
  public async Task List_FileNotExists_ReturnsDefaultAndSaves() {
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
    Assert.NotNull(result);
    Assert.False(mockFileSystem.File.Exists(configPath));
  }

  [Fact]
  public async Task Get_ValidFile_ReturnsConfig() {
    // Arrange
    string configPath = GetConfigPath();
    RuntimeConfig config = new() { GithubProxy = new Url("https://proxy.com") };
    string json = JsonSerializer.Serialize(config, _jsonSerializerOptions);

    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData(json) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
  }

  [Fact]
  public async Task Set_CreatesFileWithConfig() {
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
    Assert.True(mockFileSystem.File.Exists(configPath));
    string content = mockFileSystem.File.ReadAllText(configPath);
    Assert.Contains("https://new.com", content);
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
  public async Task List_LegacyGoModuleProxyKey_LoadsWithoutErrorAndIsHidden() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData(_legacyConfigJson) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    IDictionary<string, string> result = await service.List();

    // Assert
    Assert.Equal("https://proxy.com", result["github_proxy"]);
    Assert.False(result.ContainsKey("go_module_proxy"));
    mockUserInteraction.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Get_LegacyGoModuleProxyKey_ThrowsKeyNotFoundException() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData(_legacyConfigJson) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Get("go_module_proxy"));
  }

  [Fact]
  public async Task Set_LegacyGoModuleProxyKey_DropsKeyOnSave() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData(_legacyConfigJson) }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    string content = mockFileSystem.File.ReadAllText(configPath);
    Assert.Contains("https://new.com", content);
    Assert.DoesNotContain("go_module_proxy", content);
  }
}
