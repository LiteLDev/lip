using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

public class LipDirectoriesTests {
  [Fact]
  public void Config_IsLipUnderApplicationData() {
    // Act & Assert
    Assert.Equal(
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip"),
        LipDirectories.Config);
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
  public void Data_IsLipUnderLocalApplicationData() {
    // Act & Assert
    Assert.Equal(
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lip"),
        LipDirectories.Data);
  }

  [Fact]
  public void Staging_IsHiddenDirectoryInCache() {
    // Act & Assert
    Assert.Equal(Path.Join(LipDirectories.Cache, ".staging"), LipDirectories.Staging);
  }

  [Fact]
  public void Directories_NoneUnderSystemTemp() {
    // Arrange
    string temp = Path.GetTempPath();

    // Act & Assert
    Assert.All(
        [LipDirectories.Config, LipDirectories.Data, LipDirectories.Cache, LipDirectories.Staging],
        d => Assert.False(d.StartsWith(temp, StringComparison.OrdinalIgnoreCase), d));
  }
}
