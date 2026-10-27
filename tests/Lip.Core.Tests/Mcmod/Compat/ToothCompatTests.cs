using System.Text;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Semver;

namespace Lip.Core.Tests.Mcmod.Compat;

public class ToothCompatTests {
  private const string _uuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private static readonly LipEnvironment _clientWin = new(McmodPlatform.Client, "win-x64");

  private static readonly LipEnvironment _unknownWin = new(null, "win-x64");

  private static readonly string _multiPlatformV3 = $$"""
      {
        "format_version": 3,
        "format_uuid": "{{_uuid}}",
        "tooth": "github.com/user/repo",
        "version": "1.2.3",
        "variants": [
          {
            "platform": "",
            "dependencies": { "github.com/user/common": ">=1.0.0" },
            "preserve_files": ["config/*.json"]
          },
          {
            "platform": "win-*",
            "dependencies": { "github.com/user/windows#client": "1.*" },
            "assets": [{ "type": "self", "placements": [
              { "type": "file", "src": "a.dll", "dest": "plugins/a/a.dll" }
            ] }],
            "scripts": { "post_install": ["echo win"] }
          },
          {
            "platform": "linux-x64",
            "dependencies": { "github.com/user/linux": "1.*" }
          },
          {
            "label": "client",
            "platform": "win-x64",
            "dependencies": { "github.com/user/clientdep": "^2.0.0" }
          },
          {
            "label": "extra",
            "platform": "linux-arm64"
          }
        ]
      }
      """;

  private static async Task<ToothArchiveDocument> ParseDocument(string json) {
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    PackageManifest manifest = await PackageManifest.FromStream(new MemoryStream(bytes));
    return new ToothArchiveDocument(manifest, JsonDocument.Parse(bytes).RootElement.Clone());
  }

  private static void AssertSameVariant(
      PackageManifestVariant expected,
      PackageManifestVariant actual) {
    Assert.Equal(
        JsonSerializer.Serialize(expected),
        JsonSerializer.Serialize(actual));
  }

  [Fact]
  public async Task Select_MultiPlatformV3_MatchesGetVariantForRid() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage? package = (ToothPackage?)new ToothCompat().Select(document, "", _serverWin);

    // Assert
    Assert.NotNull(package);
    Assert.Equal("", package.Label);
    AssertSameVariant(document.Manifest.GetVariant("", "win-x64"), package.Variant);
    Assert.Equal(
        ["github.com/user/common", "github.com/user/windows#client"],
        package.InstallEdges.Select(e => e.Refs.Single().ToString()));
    Assert.Equal(["echo win"], package.Variant.Scripts.PostInstall);
  }

  [Fact]
  public async Task Select_OtherRid_UsesThatRidNotTheRuntime() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage? package = ToothCompat.Select(
        document.Manifest, "", new LipEnvironment(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.NotNull(package);
    Assert.Equal(
        ["github.com/user/common", "github.com/user/linux"],
        package.InstallEdges.Select(e => e.Refs.Single().ToString()));
  }

  [Fact]
  public async Task Select_ClientPlatformEmptyVariant_UsesClientLabel() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage? package = (ToothPackage?)new ToothCompat().Select(document, "", _clientWin);

    // Assert
    Assert.NotNull(package);
    Assert.Equal("client", package.Label);
    AssertSameVariant(document.Manifest.GetVariant("client", "win-x64"), package.Variant);
    Assert.Equal(
        ["github.com/user/clientdep"],
        package.InstallEdges.Select(e => e.Refs.Single().ToString()));
  }

  [Theory]
  [InlineData(McmodPlatform.Server)]
  [InlineData(null)]
  public async Task Select_NotClientPlatform_KeepsEmptyLabel(McmodPlatform? platform) {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage? package = ToothCompat.Select(
        document.Manifest, "", new LipEnvironment(platform, "win-x64"));

    // Assert
    Assert.NotNull(package);
    Assert.Equal("", package.Label);
  }

  [Fact]
  public async Task Select_ClientPlatformWithoutClientVariant_KeepsEmptyLabel() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [{ "label": "", "platform": "win-x64" }]
        }
        """);

    // Act
    ToothPackage? package = ToothCompat.Select(document.Manifest, "", _clientWin);

    // Assert
    Assert.NotNull(package);
    Assert.Equal("", package.Label);
  }

  [Fact]
  public async Task Select_ClientPlatformExplicitVariant_KeepsVariant() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage? package = ToothCompat.Select(
        document.Manifest, "client", _serverWin);

    // Assert
    Assert.NotNull(package);
    Assert.Equal("client", package.Label);
  }

  [Theory]
  [InlineData("extra")]
  [InlineData("missing")]
  public async Task Select_NoVariantForRid_ReturnsNull(string variant) {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    IPackage? package = new ToothCompat().Select(document, variant, _serverWin);

    // Assert
    Assert.Null(package);
    Assert.Throws<KeyNotFoundException>(() => document.Manifest.GetVariant(variant, "win-x64"));
  }

  [Fact]
  public async Task Select_UnknownPlatformAndNoClientVariant_DoesNotNeedPlatform() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage? package = ToothCompat.Select(document.Manifest, "client", _unknownWin);

    // Assert
    Assert.NotNull(package);
  }

  [Fact]
  public async Task Select_V3_BuildsIdentityEdgesAndManifest() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothPackage package = ToothCompat.Select(document.Manifest, "", _serverWin)!;

    // Assert
    Assert.Equal("tooth", package.Kind);
    Assert.Equal("github.com/user/repo", package.Identity);
    Assert.Equal(SemVersion.Parse("1.2.3"), package.Version);
    Assert.Empty(package.Conflicts);

    InstallEdge edge = package.InstallEdges[1];
    Assert.Null(edge.Name);
    Assert.True(edge.ToothClientCompat);
    Ref reference = Assert.Single(edge.Refs);
    Assert.Equal(RefKind.Spec, reference.Kind);
    Assert.Equal("github.com/user/windows", reference.Target);
    Assert.Equal("client", reference.Variant);
    SemVersionRangeConstraint range = Assert.IsType<SemVersionRangeConstraint>(edge.Version);
    Assert.True(range.IsSatisfiedBy(SemVersion.Parse("1.9.0")));
    Assert.False(range.IsSatisfiedBy(SemVersion.Parse("2.0.0")));

    Assert.Equal(3, package.Manifest.GetProperty("format_version").GetInt32());
    PackageManifest roundTripped = await PackageManifest.FromStream(
        new MemoryStream(Encoding.UTF8.GetBytes(package.Manifest.GetRawText())));
    Assert.Equal(
        JsonSerializer.Serialize(document.Manifest),
        JsonSerializer.Serialize(roundTripped));
  }

  [Fact]
  public async Task Select_V1_StoresMigratedV3Manifest() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument("""
        {
          "format_version": 1,
          "tooth": "github.com/LiteLScript-Dev/HelperLib",
          "version": "2.14.1",
          "dependencies": { "github.com/user/dep": [[">=1.0.0"]] },
          "placement": [{ "source": "src/*", "destination": "declaration/llse/*" }],
          "commands": [
            { "type": "install", "commands": ["echo install"],
              "GOOS": "windows", "GOARCH": "amd64" }
          ]
        }
        """);

    // Act
    ToothPackage? package = ToothCompat.Select(document.Manifest, "", _serverWin);

    // Assert
    Assert.NotNull(package);
    Assert.Equal("github.com/LiteLScript-Dev/HelperLib", package.Identity);
    Assert.Equal(3, package.Manifest.GetProperty("format_version").GetInt32());
    AssertSameVariant(document.Manifest.GetVariant("", "win-x64"), package.Variant);
    Assert.Equal(["echo install"], package.Variant.Scripts.PostInstall);
    Assert.Equal(
        ["github.com/user/dep"],
        package.InstallEdges.Select(e => e.Refs.Single().ToString()));
  }

  [Fact]
  public async Task Select_V2_MatchesGetVariant() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument("""
        {
          "format_version": 2,
          "tooth": "github.com/LiteLDev/LeviLamina",
          "version": "1.1.0",
          "info": { "name": "LeviLamina", "description": "", "author": "levimc", "tags": [] },
          "asset_url": "https://example.com/v$(version)/levilamina.zip",
          "dependencies": { "github.com/LiteLDev/bds": "1.21.60" },
          "files": {
            "place": [{ "src": "LeviLamina/*", "dest": "plugins/LeviLamina/" }],
            "remove": ["bedrock_server_mod.exe"]
          },
          "platforms": [
            { "goos": "windows", "goarch": "amd64",
              "commands": { "post_install": [".\\PeEditor.exe -mb"] } }
          ]
        }
        """);

    // Act
    ToothPackage? package = ToothCompat.Select(document.Manifest, "", _serverWin);

    // Assert
    Assert.NotNull(package);
    Assert.Equal(SemVersion.Parse("1.1.0"), package.Version);
    AssertSameVariant(document.Manifest.GetVariant("", "win-x64"), package.Variant);
    Assert.Equal(
        ["github.com/LiteLDev/bds"],
        package.InstallEdges.Select(e => e.Refs[0].ToString()));
    Assert.True(package.InstallEdges[0].Version!.IsSatisfiedBy(SemVersion.Parse("1.21.60")));
  }

  [Fact]
  public async Task WithVersion_ReplacesVersionInManifest() {
    // Arrange
    ToothPackage package = ToothCompat.Select(
        (await ParseDocument(_multiPlatformV3)).Manifest, "", _serverWin)!;

    // Act
    ToothPackage overridden = package.WithVersion(SemVersion.Parse("1.2.4"));

    // Assert
    Assert.Equal(SemVersion.Parse("1.2.4"), overridden.Version);
    Assert.Equal("1.2.4", overridden.Manifest.GetProperty("version").GetString());
    Assert.Equal(package.Label, overridden.Label);
  }
}
