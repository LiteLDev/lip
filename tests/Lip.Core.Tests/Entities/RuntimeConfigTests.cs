using System.Text.Json;
using Flurl;
using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Tests.Entities;

public class RuntimeConfigTests {
  [Fact]
  public void Constructor_DefaultValues_SetsCorrectDefaults() {
    RuntimeConfig config = new();

    Assert.Equal(3, config.FormatVersion);
    Assert.Equal("289f771f-2c9a-4d73-9f3f-8492495a924d", config.FormatUuid);
    Assert.Null(config.GithubProxy);
  }

  [Fact]
  public void FormatVersion_InvalidVersion_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => new RuntimeConfig { FormatVersion = 999 });
  }

  [Fact]
  public void FormatUuid_InvalidUuid_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => new RuntimeConfig { FormatUuid = "invalid-uuid" });
  }

  [Fact]
  public void AsDictionary_ReturnsOnlyEditableProperties() {
    RuntimeConfig config = new() {
      GithubProxy = new Url("https://proxy.example.com")
    };

    IDictionary<string, dynamic?> dict = config.AsDictionary();

    Assert.False(dict.ContainsKey("format_version"));
    Assert.False(dict.ContainsKey("format_uuid"));
    Assert.True(dict.ContainsKey("github_proxy"));
    Assert.False(dict.ContainsKey("go_module_proxy"));
  }

  [Fact]
  public void With_ValidKey_ReturnsNewConfigWithUpdatedValue() {
    RuntimeConfig config = new();
    Url newProxy = new("https://new.proxy.io");

    RuntimeConfig newConfig = config.With("github_proxy", newProxy);

    Assert.NotSame(config, newConfig);
    Assert.Equal(newProxy, newConfig.GithubProxy);
  }

  [Fact]
  public void With_RemovedGoModuleProxyKey_ThrowsKeyNotFoundException() {
    RuntimeConfig config = new();

    Assert.Throws<KeyNotFoundException>(() => config.With("go_module_proxy", "https://proxy.golang.org"));
  }

  [Fact]
  public void Deserialize_LegacyGoModuleProxyKey_IsIgnored() {
    string json = """
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "github_proxy": "https://proxy.example.com",
          "go_module_proxy": "https://goproxy.cn"
        }
        """;

    RuntimeConfig config = JsonSerializer.Deserialize<RuntimeConfig>(json)!;

    Assert.Equal("https://proxy.example.com", config.GithubProxy?.ToString());
    Assert.DoesNotContain("go_module_proxy", JsonSerializer.Serialize(config));
  }

  [Fact]
  public void With_InvalidKey_ThrowsKeyNotFoundException() {
    RuntimeConfig config = new();

    Assert.Throws<KeyNotFoundException>(() => config.With("invalid_key", "value"));
  }

  [Theory]
  [InlineData("format_version")]
  [InlineData("format_uuid")]
  public void With_InternalProperties_ThrowsKeyNotFoundException(string key) {
    RuntimeConfig config = new();
    Assert.Throws<KeyNotFoundException>(() => config.With(key, "value"));
  }
}

public class WorkspaceStateTests {
  [Fact]
  public void Constructor_DefaultValues_SetsCorrectDefaults() {
    WorkspaceState state = new();

    Assert.Equal(3, state.FormatVersion);
    Assert.Equal("289f771f-2c9a-4d73-9f3f-8492495a924d", state.FormatUuid);
    Assert.Empty(state.Packages);
  }

  [Fact]
  public void FormatVersion_InvalidVersion_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => new WorkspaceState { FormatVersion = 999 });
  }

  [Fact]
  public void FormatUuid_InvalidUuid_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => new WorkspaceState { FormatUuid = "invalid-uuid" });
  }
}

public class WorkspaceStatePackageTests {
  [Fact]
  public void GetPackageSpec_ReturnsCorrectSpec() {
    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0)
    };
    WorkspaceStatePackage pkg = new() {
      Files = [],
      IsExplicit = true,
      Manifest = manifest,
      Variant = "win_x64"
    };

    PackageSpec spec = pkg.GetPackageSpec();

    Assert.Equal("github.com/test/pkg", spec.Id.Path);
    Assert.Equal("win_x64", spec.Id.Variant);
    Assert.Equal(new SemVersion(1, 0, 0), spec.Version);
  }

  [Fact]
  public void Variant_Invalid_ThrowsArgumentException() {
    PackageManifest manifest = new() {
      Path = "github.com/test/pkg",
      Version = new SemVersion(1, 0, 0)
    };

    Assert.Throws<ArgumentException>(() => new WorkspaceStatePackage {
      Files = [],
      IsExplicit = true,
      Manifest = manifest,
      Variant = "INVALID-VARIANT!"
    });
  }
}
