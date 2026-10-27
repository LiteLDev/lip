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
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lip", "cache");
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
  public void Temp_IsLipUnderSystemTemp() {
    // Act & Assert
    Assert.Equal(Path.Join(Path.GetTempPath(), "lip"), LipDirectories.Temp);
  }
}
