using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Services;

public class ConfigServiceTests {
  private static string GetConfigPath() =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip", "config.yaml");

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

  /// protocol/userspace.md §2: the file is written atomically, through a file in the config
  /// directory that is renamed over it.
  [Fact]
  public async Task Set_ExistingFile_ReplacesItLeavingNoOtherFile() {
    // Arrange
    string configPath = GetConfigPath();
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { configPath, new MockFileData("github_proxy: https://old.com\n") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    await service.Set("github_proxy", "https://new.com");

    // Assert
    Assert.Equal("github_proxy: https://new.com", mockFileSystem.File.ReadAllText(configPath).Trim());
    Assert.Equal([configPath], mockFileSystem.Directory.GetFiles(Path.GetDirectoryName(configPath)!));
  }

  [Fact]
  public async Task Get_GoModuleProxyKey_ThrowsKeyNotFoundException() {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetConfigPath(), new MockFileData("go_module_proxy: https://goproxy.cn\n") }
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

  /// protocol/userspace.md §2: keys that are not configuration keys are ignored, whatever their
  /// value.
  [Theory]
  [InlineData("foo:\n  a: 1\n")]
  [InlineData("foo: [1, 2]\n")]
  public async Task Get_UnknownYamlKeyWithNonScalarValue_IsIgnored(string unknown) {
    // Arrange
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { GetConfigPath(), new MockFileData(unknown + "github_proxy: https://proxy.com\n") }
        });
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act
    string result = await service.Get("github_proxy");

    // Assert
    Assert.Equal("https://proxy.com", result);
    Assert.Equal("https://proxy.com", await service.GetGithubProxy());
    mockUserInteraction.Verify(u => u.PrintError(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task GetGithubProxy_FileNotExists_ReturnsNull() {
    // Arrange
    MockFileSystem mockFileSystem = new();
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act & Assert
    Assert.Null(await service.GetGithubProxy());
  }

  [Fact]
  public async Task Set_UnknownKey_ThrowsAndWritesNothing() {
    // Arrange
    MockFileSystem mockFileSystem = new();
    Mock<IUserInteraction> mockUserInteraction = new();
    ConfigService service = new(mockFileSystem, mockUserInteraction.Object);

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Set("go_module_proxy", "x"));
    Assert.False(mockFileSystem.File.Exists(GetConfigPath()));
  }
}
