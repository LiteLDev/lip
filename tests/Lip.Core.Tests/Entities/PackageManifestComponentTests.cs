using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Tests.Entities;

public class PackageManifestAssetPlacementTests {
  [Fact]
  public void Constructor_ValidRelativeDst_Succeeds() {
    PackageManifestAssetPlacement placement = new() {
      Type = PackageManifestAssetPlacement.PlacementType.File,
      Src = "src/*",
      Dst = "plugins/test/"
    };

    Assert.Equal("plugins/test/", placement.Dst);
  }

  public static TheoryData<string> InvalidDstData =>
      new(
          Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/", "absolute", "path"),
          "../parent/path",
          "path/../escape"
      );

  [Theory]
  [MemberData(nameof(InvalidDstData))]
  public void Constructor_InvalidDst_ThrowsArgumentException(string dst) {
    Assert.Throws<ArgumentException>(() => new PackageManifestAssetPlacement {
      Type = PackageManifestAssetPlacement.PlacementType.File,
      Src = "src/*",
      Dst = dst
    });
  }

  [Fact]
  public void IsValidDst_RelativePath_ReturnsTrue() {
    Assert.True(PackageManifestAssetPlacement.IsValidDst("plugins/test/"));
    Assert.True(PackageManifestAssetPlacement.IsValidDst("file.txt"));
  }

  [Fact]
  public void IsValidDst_AbsoluteOrParentPath_ReturnsFalse() {
    Assert.False(PackageManifestAssetPlacement.IsValidDst(Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/", "path")));
    Assert.False(PackageManifestAssetPlacement.IsValidDst("../"));
  }
}

public class PackageManifestInfoTests {
  [Fact]
  public void Constructor_DefaultValues_SetsDefaults() {
    PackageManifestInfo info = new();

    Assert.Equal("", info.Name);
    Assert.Equal("", info.Description);
    Assert.Empty(info.Tags);
    Assert.Equal("", info.AvatarUrl);
  }

  [Fact]
  public void Constructor_WithValues_SetsValues() {
    PackageManifestInfo info = new() {
      Name = "Test Package",
      Description = "A test package",
      Tags = ["test", "example"]
    };

    Assert.Equal("Test Package", info.Name);
    Assert.Equal("A test package", info.Description);
    Assert.Equal(2, info.Tags.Count);
  }
}

public class PackageManifestVariantTests {
  [Fact]
  public void Constructor_DefaultValues_SetsDefaults() {
    PackageManifestVariant variant = new();

    Assert.Equal("", variant.Label);
    Assert.Equal("", variant.Platform);
    Assert.Empty(variant.Dependencies);
    Assert.Empty(variant.Assets);
    Assert.Empty(variant.PreserveFiles);
    Assert.Empty(variant.RemoveFiles);
    Assert.NotNull(variant.Scripts);
  }
}

public class PackageManifestAssetTests {
  [Fact]
  public void Constructor_WithValues_SetsValues() {
    PackageManifestAsset asset = new() {
      Type = PackageManifestAsset.AssetType.Zip,
      Urls = [new Flurl.Url("https://example.com/file.zip")]
    };

    Assert.Equal(PackageManifestAsset.AssetType.Zip, asset.Type);
    Assert.Single(asset.Urls);
  }
}
