using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Tests.Entities;

public class PackageManifestTests {
  [Fact]
  public void FormatVersion_InvalidVersion_ThrowsException() {
    // The FormatVersion setter throws ArgumentException but the Path validation
    // may throw FormatException first depending on initialization order.
    // Path is required, so we must have a valid path. FormatVersion and FormatUuid
    // are validated on init. Let's see which one throw.

    // As required props are initialized, the FormatVersion setter will run at some point.
    // But first we need to pass a valid Path to avoid FormatException from Path.

    Exception ex = Assert.ThrowsAny<Exception>(() => new PackageManifest {
      FormatVersion = 999,
      Path = "github.com/test/valid",
      Version = new SemVersion(1, 0, 0),
      FormatUuid = "289f771f-2c9a-4d73-9f3f-8492495a924d"
    });

    Assert.True(ex is ArgumentException, $"Expected ArgumentException but got {ex.GetType().Name}");
  }

  [Fact]
  public void FormatUuid_InvalidUuid_ThrowsException() {
    Exception ex = Assert.ThrowsAny<Exception>(() => new PackageManifest {
      FormatVersion = 3,
      Path = "github.com/test/valid",
      Version = new SemVersion(1, 0, 0),
      FormatUuid = "invalid-uuid"
    });

    Assert.True(ex is ArgumentException, $"Expected ArgumentException but got {ex.GetType().Name}");
  }

  [Fact]
  public void Path_InvalidPath_ThrowsFormatException() {
    Assert.Throws<FormatException>(() => new PackageManifest {
      Path = "invalid path with spaces",
      Version = new SemVersion(1, 0, 0)
    });
  }

  [Fact]
  public void GetVariant_MatchesLabel_ReturnsVariant() {
    // Arrange
    PackageManifestVariant variant = new() { Label = "default", Platform = "" };
    PackageManifest manifest = new() {
      Path = "github.com/test/valid",
      Version = new SemVersion(1, 0, 0),
      Variants = [variant]
    };

    // Act
    PackageManifestVariant result = manifest.GetVariant("default", "win-x64");

    // Assert
    Assert.Equal("default", result.Label);
  }

  [Fact]
  public void GetVariant_NoMatch_ThrowsKeyNotFoundException() {
    // Arrange
    PackageManifest manifest = new() {
      Path = "github.com/test/valid",
      Version = new SemVersion(1, 0, 0),
      Variants = []
    };

    // Act & Assert
    Assert.Throws<KeyNotFoundException>(() => manifest.GetVariant("nonexistent", "win-x64"));
  }
}
