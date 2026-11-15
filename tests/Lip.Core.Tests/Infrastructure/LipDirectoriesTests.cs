using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

public class LipDirectoriesTests {
  /// protocol/lip-userspace.md, "Directories": %APPDATA%\lip on Windows, $XDG_CONFIG_HOME/lip
  /// elsewhere, macOS included, where .NET names another folder for application data.
  [Fact]
  public void Config_FollowsPlatformConvention() {
    // Arrange
    string expected;
    if (OperatingSystem.IsWindows()) {
      expected = Path.Join(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip");
    } else {
      string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
      string configHome = !string.IsNullOrEmpty(xdgConfigHome) && Path.IsPathRooted(xdgConfigHome)
          ? xdgConfigHome
          : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
      expected = Path.Join(configHome, "lip");
    }

    // Act & Assert
    Assert.Equal(expected, LipDirectories.Config);
  }

  [Fact]
  public void Cache_FollowsPlatformConvention() {
    // Arrange
    string expected;
    if (OperatingSystem.IsWindows()) {
      expected = Path.Join(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lip", "Cache");
    } else {
      string? xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
      string cacheHome = !string.IsNullOrEmpty(xdgCacheHome) && Path.IsPathRooted(xdgCacheHome)
          ? xdgCacheHome
          : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
      expected = Path.Join(cacheHome, "lip");
    }

    // Act & Assert
    Assert.Equal(expected, LipDirectories.Cache);
  }

  [Fact]
  public void Data_FollowsPlatformConvention() {
    // Arrange
    string expected;
    if (OperatingSystem.IsWindows()) {
      expected = Path.Join(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lip", "Data");
    } else {
      string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
      string dataHome = !string.IsNullOrEmpty(xdgDataHome) && Path.IsPathRooted(xdgDataHome)
          ? xdgDataHome
          : Path.Join(
              Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
      expected = Path.Join(dataHome, "lip");
    }

    // Act & Assert
    Assert.Equal(expected, LipDirectories.Data);
  }

  [Fact]
  public void Directories_NoneUnderSystemTemp() {
    // Arrange
    string temp = Path.GetTempPath();

    // Act & Assert
    Assert.All(
        [LipDirectories.Config, LipDirectories.Cache, LipDirectories.Data],
        d => Assert.False(d.StartsWith(temp, StringComparison.OrdinalIgnoreCase), d));
  }
}
