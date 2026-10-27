using System.Text;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Compat;

public class ToothCompatTests {
  private const string _uuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private static readonly LipEnvironment _clientWin = new(McmodPlatform.Client, "win-x64");

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

  private static List<string> EdgeRefs(IPackage package) =>
      [.. package.InstallEdges.Select(e => Assert.Single(e.Refs).ToString())];

  [Fact]
  public async Task Convert_MultiPlatformV3_MergesVariantsForRid() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothConversion? conversion = new ToothCompat().Convert(document, "", _serverWin, null);

    // Assert
    Assert.NotNull(conversion);
    ConcreteDocument converted = conversion.Package.Document;
    Assert.Equal(
        ["github.com/user/common", "github.com/user/windows#client"],
        EdgeRefs(conversion.Package));
    Assert.Equal(["config/*.json"], converted.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["echo win"], converted.Scripts.PostInstall);
    Assert.Equal(
        [new AssetPlacement("0", false, "a.dll", "plugins/a/a.dll")],
        converted.AssetPlacements);
    Assert.Equal(
        [new ToothAsset("0", PackageManifestAsset.AssetType.Self, [])],
        conversion.Assets,
        (a, b) => a.Mount == b.Mount && a.Type == b.Type && a.Urls.SequenceEqual(b.Urls));
  }

  [Fact]
  public async Task Convert_V3_BuildsFilePackageWithIdentityEdgesAndManifest() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    McmodPackage package = ToothCompat.Convert(document.Manifest, "", _serverWin)!.Package;

    // Assert
    Assert.Equal("tooth", package.Kind);
    Assert.Equal("github.com/user/repo", package.Identity);
    Assert.Equal(SemVersion.Parse("1.2.3"), package.Version);
    Assert.True(package.Document.IsMarked);
    Assert.True(package.Document.IsFilePackage);
    Assert.Equal(PackagePlatform.Universal, package.Document.Platform);
    Assert.Null(package.Document.Rid);
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
  public async Task Convert_OtherRid_UsesThatRidNotTheRuntime() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothConversion? conversion = ToothCompat.Convert(
        document.Manifest, "", new LipEnvironment(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.NotNull(conversion);
    Assert.Equal(
        ["github.com/user/common", "github.com/user/linux"], EdgeRefs(conversion.Package));
    Assert.Empty(conversion.Assets);
  }

  [Fact]
  public async Task Convert_ClientPlatformEmptyVariant_UsesClientLabel() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothConversion? conversion = new ToothCompat().Convert(document, "", _clientWin, null);

    // Assert
    Assert.NotNull(conversion);
    Assert.Equal("client", ToothCompat.GetLabel(document.Manifest, "", _clientWin));
    Assert.Equal(["github.com/user/clientdep"], EdgeRefs(conversion.Package));
  }

  [Fact]
  public async Task GetLabel_ServerPlatform_KeepsEmptyLabel() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    string label = ToothCompat.GetLabel(document.Manifest, "", _serverWin);

    // Assert
    Assert.Equal("", label);
  }

  [Fact]
  public async Task GetLabel_ClientPlatformWithoutClientVariant_KeepsEmptyLabel() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [{ "label": "", "platform": "win-x64" }]
        }
        """);

    // Act
    string label = ToothCompat.GetLabel(document.Manifest, "", _clientWin);

    // Assert
    Assert.Equal("", label);
    Assert.NotNull(ToothCompat.Convert(document.Manifest, "", _clientWin));
  }

  [Fact]
  public async Task Convert_ExplicitClientVariantOnServer_KeepsVariant() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothConversion? conversion = ToothCompat.Convert(document.Manifest, "client", _serverWin);

    // Assert
    Assert.NotNull(conversion);
    Assert.Equal("client", ToothCompat.GetLabel(document.Manifest, "client", _serverWin));
    Assert.Equal(["github.com/user/clientdep"], EdgeRefs(conversion.Package));
  }

  [Theory]
  [InlineData("extra")]
  [InlineData("missing")]
  public async Task Convert_NoVariantForRid_ReturnsNull(string variant) {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    ToothConversion? conversion = new ToothCompat().Convert(document, variant, _serverWin, null);

    // Assert
    Assert.Null(conversion);
    Assert.Throws<KeyNotFoundException>(() => document.Manifest.GetVariant(variant, "win-x64"));
  }

  [Fact]
  public async Task Convert_V1_MigratesThenConverts() {
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
    ToothConversion? conversion = ToothCompat.Convert(document.Manifest, "", _serverWin);

    // Assert
    Assert.NotNull(conversion);
    Assert.Equal("github.com/LiteLScript-Dev/HelperLib", conversion.Package.Identity);
    Assert.Equal(3, conversion.Package.Manifest.GetProperty("format_version").GetInt32());
    Assert.Equal(["echo install"], conversion.Package.Document.Scripts.PostInstall);
    Assert.Equal(["github.com/user/dep"], EdgeRefs(conversion.Package));
  }

  [Fact]
  public async Task Convert_V2_MapsAssetUrlRemoveFilesAndScripts() {
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
    ToothConversion? conversion = ToothCompat.Convert(document.Manifest, "", _serverWin);

    // Assert
    Assert.NotNull(conversion);
    McmodPackage package = conversion.Package;
    Assert.Equal(SemVersion.Parse("1.1.0"), package.Version);
    Assert.Equal(["github.com/LiteLDev/bds"], EdgeRefs(package));
    Assert.True(package.InstallEdges[0].Version!.IsSatisfiedBy(SemVersion.Parse("1.21.60")));
    ToothAsset asset = Assert.Single(conversion.Assets);
    Assert.Equal(PackageManifestAsset.AssetType.Zip, asset.Type);
    Assert.Equal(["https://example.com/v1.1.0/levilamina.zip"], asset.Urls);
    Assert.Equal(
        ["bedrock_server_mod.exe", "bedrock_server_mod.exe/"],
        package.Document.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal([".\\PeEditor.exe -mb"], package.Document.Scripts.PostInstall);
  }

  [Fact]
  public async Task Convert_Version_ReplacesVersionInDocumentAndManifest() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument(_multiPlatformV3);

    // Act
    McmodPackage package = ToothCompat.Convert(
        document.Manifest, "", _serverWin, SemVersion.Parse("1.2.4"))!.Package;

    // Assert
    Assert.Equal(SemVersion.Parse("1.2.4"), package.Version);
    Assert.Equal("1.2.4", package.Manifest.GetProperty("version").GetString());
  }

  [Fact]
  public void PreserveFiles_PatternWithoutSlash_MatchesFileNameEverywhere() {
    // Arrange & Act
    List<string> patterns = [
      .. ToothCompat.PreserveFiles(["config.json", "plugins\\x\\*.json", "mods/x/data/"]),
    ];

    // Assert
    Assert.Equal(["**/config.json", "plugins/x/*.json", "mods/x/data/"], patterns);
    PathGlob glob = new(patterns[0]);
    Assert.True(glob.IsMatch("config.json"));
    Assert.True(glob.IsMatch("mods/x/config.json"));
  }

  [Fact]
  public void RemoveFiles_PatternWithoutTrailingSlash_MatchesFilesAndDirectories() {
    // Arrange & Act
    List<string> patterns = [.. ToothCompat.RemoveFiles(["plugins/GMLIB", "logs/", "a\\b"])];

    // Assert
    Assert.Equal(["plugins/GMLIB", "plugins/GMLIB/", "logs/", "a/b", "a/b/"], patterns);
  }

  [Fact]
  public void ConvertScripts_V3Hooks_MapsInstallAndUninstall() {
    // Arrange
    PackageManifestScripts scripts = new() {
      PreInstall = ["pre"],
      Install = ["install"],
      PostInstall = ["post"],
      PreUninstall = ["preun"],
      Uninstall = ["un"],
      PostUninstall = ["postun"],
    };

    // Act
    Scripts converted = ToothCompat.ConvertScripts(scripts);

    // Assert
    Assert.Equal(["pre"], converted.PreInstall);
    Assert.Equal(["install", "post"], converted.PostInstall);
    Assert.Equal(["preun", "un"], converted.PreUninstall);
    Assert.Equal(["postun"], converted.PostUninstall);
  }

  [Fact]
  public async Task OpenArchive_Assets_MountsSelfAndDownloadsUrlAssetsOnFirstUse() {
    // Arrange
    ToothArchiveDocument document = await ParseDocument($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [{
            "assets": [
              { "type": "self" },
              { "type": "zip", "urls": ["https://example.com/a.zip"] },
              { "type": "uncompressed", "urls": ["https://example.com/b.bin"] }
            ]
          }]
        }
        """);
    ToothConversion conversion = ToothCompat.Convert(document.Manifest, "", _serverWin)!;
    ArchiveHandle self = new(
        "github.com/user/repo@1.0.0",
        new InMemorySource(new() { ["tooth.json"] = "{}" }),
        new GitArchiveIdentity("https://github.com/user/repo.git", "abc", ""));
    Mock<IArchiveProvider> provider = new();
    provider.Setup(p => p.OpenAsset(
        It.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { "https://example.com/a.zip" })), true))
        .ReturnsAsync(new InMemorySource(new() { ["dir/x.txt"] = "x" }));
    provider.Setup(p => p.OpenAsset(
        It.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { "https://example.com/b.bin" })), false))
        .ReturnsAsync(new InMemorySource(new() { [""] = "b" }));

    // Act
    ArchiveHandle archive = conversion.OpenArchive(self, provider.Object);

    // Assert
    Assert.Equal(self.Key, archive.Key);
    Assert.Equal(self.Identity, archive.Identity);
    provider.VerifyNoOtherCalls();
    Assert.Equal(["0/tooth.json", "1/dir/x.txt", "2"], archive.Source.Keys);
    using StreamReader reader = new(await archive.Source.OpenRead("2"));
    Assert.Equal("b", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task FromLocked_LockEntry_ConvertsRecordedToothJsonForChainVariant() {
    // Arrange
    PackageManifest manifest = (await ParseDocument(_multiPlatformV3)).Manifest;
    LockedPackage locked = new() {
      Identity = "github.com/user/repo",
      Version = "1.2.5",
      Kind = "tooth",
      Explicit = true,
      Chain = [new LockedHop("github.com/user/repo#client", null)],
      ArchiveKey = "github.com/user/repo@1.2.5",
      Manifest = JsonSerializer.SerializeToElement(manifest),
    };

    // Act
    McmodPackage? package = (await ToothCompat.FromLocked(locked, _serverWin))?.Package;

    // Assert
    Assert.NotNull(package);
    Assert.Equal("tooth", package.Kind);
    Assert.Equal(SemVersion.Parse("1.2.5"), package.Version);
    Assert.Equal(["github.com/user/clientdep"], EdgeRefs(package));
  }

  [Fact]
  public async Task FromLocked_VariantForOtherRid_ReturnsNull() {
    // Arrange
    PackageManifest manifest = (await ParseDocument(_multiPlatformV3)).Manifest;
    LockedPackage locked = new() {
      Identity = "github.com/user/repo",
      Version = "1.2.3",
      Kind = "tooth",
      Explicit = true,
      Chain = [new LockedHop("github.com/user/repo#extra", null)],
      ArchiveKey = "github.com/user/repo@1.2.3",
      Manifest = JsonSerializer.SerializeToElement(manifest),
    };

    // Act
    McmodPackage? package = (await ToothCompat.FromLocked(locked, _serverWin))?.Package;

    // Assert
    Assert.Null(package);
  }

  private sealed class InMemorySource(Dictionary<string, string> files) : ISource {
    public IEnumerable<string> Keys => files.Keys;

    public Task<Stream> OpenRead(string key) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(files[key])));
  }
}
