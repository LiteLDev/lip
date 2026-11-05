using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Migration.PackageManifests;
using Semver;

namespace Lip.Core.Tests.Mcmod.Compat;

/// Reading tooth files of earlier formats (protocol/package.md §5).
public class ToothCompatTests {
  private const string _uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly LipEnvironment _serverWin = new(McmodPlatform.Server, "win-x64");

  private static readonly LipEnvironment _clientWin = new(McmodPlatform.Client, "win-x64");

  private static readonly string _multiPlatformV3 = $$"""
      {
        "format_version": 3,
        "format_uuid": "{{_uuid3}}",
        "tooth": "github.com/user/repo",
        "version": "1.2.3",
        "info": { "name": "Repo", "description": "d", "tags": ["a"], "avatar_url": "" },
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

  private static PackageManifest Manifest(string json) {
    using JsonDocument document = JsonDocument.Parse(json);
    return PackageManifestMigration.Migrate(document);
  }

  private static List<string> Dependencies(ToothEntry entry) =>
      [.. entry.Dependencies.Select(d => d.Source.ToString())];

  [Fact]
  public void Derive_PlatformPattern_MergesMatchingVariantsOfEachLabel() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal("github.com/user/repo", tooth.Tooth);
    Assert.Equal(SemVersion.Parse("1.2.3"), tooth.Version);
    Assert.Equal(["", "client"], tooth.Variants.Keys.Order(StringComparer.Ordinal));
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.True(entry.Legacy);
    Assert.Equal(McmodPlatform.Server, entry.Platform);
    Assert.Equal("win-x64", entry.Runtime);
    Assert.Equal(
        ["github.com/user/common", "github.com/user/windows#client"], Dependencies(entry));
    Assert.All(entry.Dependencies, d => Assert.True(d.ClientCompat));
    Assert.True(entry.Dependencies[1].Range.IsSatisfiedBy(SemVersion.Parse("1.9.0")));
    Assert.False(entry.Dependencies[1].Range.IsSatisfiedBy(SemVersion.Parse("2.0.0")));
    Assert.Equal(["config/*.json"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["echo win"], entry.Hooks.PostInstall);
    EntryAsset asset = Assert.Single(entry.Assets);
    Assert.Equal(AssetType.Self, asset.Type);
    Assert.Equal([new EntryPlacement("a.dll", "plugins/a/a.dll", false)], asset.Placements);
    Assert.Same(entry, tooth.SelectEntry("", _serverWin));
    Assert.Equal(
        ["github.com/user/clientdep"], Dependencies(Assert.Single(tooth.Variants["client"])));
  }

  [Theory]
  [InlineData("win-arm64")]
  [InlineData("win-x86")]
  public void Derive_OtherRuntimeMatchingPattern_UsesThatRuntime(string runtime) {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);
    LipEnvironment environment = new(McmodPlatform.Server, runtime);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, environment);

    // Assert
    Assert.Equal([""], tooth.Variants.Keys);
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(runtime, entry.Runtime);
    Assert.Equal(
        ["github.com/user/common", "github.com/user/windows#client"], Dependencies(entry));
    Assert.Same(entry, tooth.SelectEntry("", environment));
    Assert.Null(tooth.SelectEntry("", _serverWin));
  }

  [Fact]
  public void Derive_ExactPlatform_SelectsOnlyThatRuntime() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.Equal([""], tooth.Variants.Keys);
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(["github.com/user/common", "github.com/user/linux"], Dependencies(entry));
    Assert.Empty(entry.Assets);
    Assert.Empty(entry.Hooks.PostInstall);
  }

  [Fact]
  public void Derive_LabelMatchingOnlyAnyPlatform_HasNoVariant() {
    // Arrange: only the variant without a platform matches osx-arm64; lip v0.37.0 needs no
    // variant with that exact platform, so the label "" still has an entry.
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "osx-arm64"));

    // Assert
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(["github.com/user/common"], Dependencies(entry));
    Assert.False(tooth.Variants.ContainsKey("extra"));
    Assert.False(tooth.Variants.ContainsKey("client"));
  }

  [Fact]
  public void Derive_NoVariantMatchesRuntime_HasNoVariants() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [ { "platform": "win-x64" }, { "label": "lua", "platform": "win-*" } ]
        }
        """);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.Empty(tooth.Variants);
    Assert.Empty(tooth.Json.GetProperty("variants").EnumerateObject());
  }

  [Fact]
  public void Derive_ClientPlatform_VariantEmptyIsTheClientLabel() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, _clientWin);

    // Assert
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(McmodPlatform.Client, entry.Platform);
    Assert.Equal("win-x64", entry.Runtime);
    Assert.Equal(["github.com/user/clientdep"], Dependencies(entry));
    Assert.Equal(["github.com/user/clientdep"],
        Dependencies(Assert.Single(tooth.Variants["client"])));
    Assert.Equal(
        tooth.Json.GetProperty("variants").GetProperty("client").GetRawText(),
        tooth.Json.GetProperty("variants").GetProperty("").GetRawText());
    Assert.Same(entry, tooth.SelectEntry("", _clientWin));
  }

  [Fact]
  public void Derive_ClientPlatformWithoutClientVariantForRuntime_HasNoVariantEmpty() {
    // Arrange: the label "client" exists but has no variant for linux-x64.
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, new(McmodPlatform.Client, "linux-x64"));

    // Assert
    Assert.Empty(tooth.Variants);
  }

  [Fact]
  public void Derive_ClientPlatformWithoutClientLabel_KeepsVariantEmpty() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [ { "label": "", "platform": "win-x64",
                          "dependencies": { "github.com/user/a": "1.0.0" } } ]
        }
        """);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, _clientWin);

    // Assert
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(McmodPlatform.Client, entry.Platform);
    Assert.Equal(["github.com/user/a"], Dependencies(entry));
  }

  [Fact]
  public void Derive_ServerPlatform_KeepsVariantEmptyAndClientLabel() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.NotEqual(
        Dependencies(Assert.Single(tooth.Variants[""])),
        Dependencies(Assert.Single(tooth.Variants["client"])));
    Assert.Equal(McmodPlatform.Server, tooth.Variants["client"][0].Platform);
  }

  [Fact]
  public void Derive_DerivedDocument_IsFormat4WithEntriesForTheEnvironment() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    ToothFile tooth = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    JsonElement json = tooth.Json;
    Assert.Equal(4, json.GetProperty("format_version").GetInt32());
    Assert.Equal(ToothFormat.FormatUuid, json.GetProperty("format_uuid").GetString());
    Assert.Equal("github.com/user/repo", json.GetProperty("tooth").GetString());
    Assert.Equal("1.2.3", json.GetProperty("version").GetString());
    Assert.Equal("Repo", json.GetProperty("info").GetProperty("name").GetString());
    JsonElement entry = Assert.Single(json.GetProperty("variants").GetProperty("").EnumerateArray());
    Assert.Equal(
        ["platform", "runtime", "dependencies", "assets", "preserve", "hooks"],
        entry.EnumerateObject().Select(p => p.Name));
    Assert.Equal("server", entry.GetProperty("platform").GetString());
    Assert.Equal("win-x64", entry.GetProperty("runtime").GetString());
    Assert.Equal(
        """{"post_install":["echo win"]}""", entry.GetProperty("hooks").GetRawText());
    Assert.Equal(entry.GetRawText(), tooth.Variants[""][0].Json.GetRawText());
  }

  [Theory]
  [InlineData("https://example.com/a.png", "https://example.com/a.png")]
  [InlineData("http://example.com/a.png", null)]
  [InlineData("a.png", null)]
  [InlineData("", null)]
  public void Derive_AvatarUrl_IsLogoOnlyWhenHttps(string avatarUrl, string? logo) {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "info": { "name": "Repo", "avatar_url": "{{avatarUrl}}" },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    JsonElement info = ToothCompat.Derive(manifest, _serverWin).Json.GetProperty("info");

    // Assert
    Assert.Equal(
        logo is null ? ["name"] : ["name", "logo"], info.EnumerateObject().Select(p => p.Name));
    Assert.Equal(
        logo, info.TryGetProperty("logo", out JsonElement value) ? value.GetString() : null);
  }

  [Fact]
  public void Derive_NoAvatarUrl_HasNoLogo() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "info": { "name": "Repo", "description": "d", "tags": ["a"] },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    JsonElement info = ToothCompat.Derive(manifest, _serverWin).Json.GetProperty("info");

    // Assert
    Assert.Equal(["name", "description", "tags"], info.EnumerateObject().Select(p => p.Name));
  }

  [Theory]
  [InlineData("https://example.com/a.png", true)]
  [InlineData("https://", true)]
  [InlineData("http://example.com/a.png", false)]
  [InlineData("HTTPS://example.com/a.png", false)]
  [InlineData("a.png", false)]
  [InlineData("", false)]
  public void IsLogo_ChecksHttpsPrefix(string avatarUrl, bool expected) {
    // Act & Assert
    Assert.Equal(expected, ToothCompat.IsLogo(avatarUrl));
  }

  [Fact]
  public void Info_EmptyMembers_AreDropped() {
    // Arrange
    PackageManifestInfo info = new() {
      Name = "",
      Description = "",
      Tags = [],
      AvatarUrl = "a.png",
    };

    // Act
    JsonObject result = ToothCompat.Info(info);

    // Assert
    Assert.Empty(result);
  }

  [Fact]
  public void Info_HttpsAvatarUrl_IsLogo() {
    // Arrange
    PackageManifestInfo info = new() {
      Name = "n",
      Description = "d",
      Tags = ["a"],
      AvatarUrl = "https://example.com/a.png",
    };

    // Act
    JsonObject result = ToothCompat.Info(info);

    // Assert
    Assert.Equal(
        """{"name":"n","description":"d","tags":["a"],"logo":"https://example.com/a.png"}""",
        result.ToJsonString());
  }

  [Fact]
  public void Derive_DerivedEntry_HasPreserveAndPurge() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [ { "platform": "win-x64",
                          "assets": [ { "type": "self", "placements": [
                            { "type": "file", "src": "a.dll", "dest": "plugins/a.dll" } ] } ],
                          "preserve_files": ["config.json"], "remove_files": ["logs"] } ]
        }
        """);

    // Act
    JsonElement entry = Assert.Single(ToothCompat.Derive(manifest, _serverWin).Variants[""]).Json;

    // Assert: the globs use the keys of format 4; the assets keep the placements of format 3.
    Assert.Equal(
        ["platform", "runtime", "assets", "preserve", "purge"],
        entry.EnumerateObject().Select(p => p.Name));
    Assert.Equal("""["config.json"]""", entry.GetProperty("preserve").GetRawText());
    Assert.Equal("""["logs"]""", entry.GetProperty("purge").GetRawText());
    JsonElement asset = Assert.Single(entry.GetProperty("assets").EnumerateArray());
    Assert.False(asset.TryGetProperty("place", out _));
    Assert.Equal(
        "file",
        Assert.Single(asset.GetProperty("placements").EnumerateArray()).GetProperty("type")
            .GetString());
  }

  [Fact]
  public void ParseEntry_LegacyEntryWithOldGlobKeys_IgnoresThem() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse("""
        { "platform": "server", "runtime": "win-x64",
          "preserve_files": ["config.json"], "remove_files": ["logs"] }
        """);

    // Act
    ToothEntry entry = ToothParser.ParseEntry(json.RootElement, legacy: true);

    // Assert
    Assert.Empty(entry.PreserveFiles);
    Assert.Empty(entry.RemoveFiles);
  }

  [Fact]
  public void Derive_DerivedEntry_ReparsesAsLegacyEntry() {
    // Arrange
    ToothFile tooth = ToothCompat.Derive(Manifest(_multiPlatformV3), _serverWin);
    ToothEntry entry = tooth.Variants[""][0];

    // Act
    ToothEntry reparsed = ToothParser.ParseEntry(entry.Json, legacy: true);

    // Assert
    Assert.True(reparsed.Legacy);
    Assert.Equal(entry.Platform, reparsed.Platform);
    Assert.Equal(entry.Runtime, reparsed.Runtime);
    Assert.Equal(Dependencies(entry), Dependencies(reparsed));
    Assert.Equal(entry.Assets[0].Placements, reparsed.Assets[0].Placements);
    Assert.Equal(
        entry.PreserveFiles.Select(g => g.Pattern), reparsed.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(entry.Hooks.PostInstall, reparsed.Hooks.PostInstall);
  }

  [Fact]
  public void Derive_Scripts_BecomeHooks() {
    // Arrange: scripts of one label merge across variants, one variant per script.
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [
            { "scripts": { "pre_install": ["pre"], "install": ["install"],
                           "uninstall": ["un"] } },
            { "platform": "win-x64", "scripts": { "post_install": ["post"],
                           "pre_uninstall": ["preun"], "post_uninstall": ["postun"] } }
          ]
        }
        """);

    // Act
    Hooks hooks = Assert.Single(ToothCompat.Derive(manifest, _serverWin).Variants[""]).Hooks;

    // Assert
    Assert.Equal(["pre"], hooks.PreInstall);
    Assert.Equal(["install", "post"], hooks.PostInstall);
    Assert.Equal(["preun", "un"], hooks.PreUninstall);
    Assert.Equal(["postun"], hooks.PostUninstall);
  }

  [Fact]
  public void Derive_MergedVariants_ConcatenateAssetsAndFiles() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "zip", "urls": ["https://example.com/a.zip"] } ],
              "preserve_files": ["config.json"], "remove_files": ["logs"] },
            { "platform": "win-*",
              "assets": [
                { "type": "uncompressed", "urls": ["https://example.com/b.exe"],
                  "placements": [ { "type": "file", "src": "", "dest": "b.exe" } ] },
                { "type": "tar", "urls": ["https://example.com/c.tar"] },
                { "type": "tgz", "urls": ["https://example.com/d.tgz"],
                  "placements": [ { "type": "dir", "src": "d", "dest": "plugins/d" } ] }
              ],
              "preserve_files": ["plugins\\x\\*.json"], "remove_files": ["tmp/"] }
          ]
        }
        """);

    // Act
    ToothEntry entry = Assert.Single(ToothCompat.Derive(manifest, _serverWin).Variants[""]);

    // Assert
    Assert.Equal(
        [AssetType.Zip, AssetType.Uncompressed, AssetType.Tar, AssetType.Tgz],
        entry.Assets.Select(a => a.Type));
    Assert.Equal(["https://example.com/a.zip"], entry.Assets[0].Urls);
    Assert.Equal([new EntryPlacement("", "b.exe", false)], entry.Assets[1].Placements);
    Assert.Equal([new EntryPlacement("d", "plugins/d", true)], entry.Assets[3].Placements);
    Assert.False(entry.IsMod);
    Assert.Equal(
        ["**/config.json", "plugins/x/*.json"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs", "logs/", "tmp/"], entry.RemoveFiles.Select(g => g.Pattern));
  }

  [Fact]
  public void Derive_SameScriptInTwoMergedVariants_Throws() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [
            { "scripts": { "install": ["a"] } },
            { "platform": "win-x64", "scripts": { "install": ["b"] } }
          ]
        }
        """);

    // Act
    ToothFormatException ex = Assert.Throws<ToothFormatException>(
        () => ToothCompat.Derive(manifest, _serverWin));

    // Assert
    Assert.Contains("labeled ''", ex.Message);
    ToothFile linux = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "linux-x64"));
    Assert.Equal(["a"], linux.Variants[""][0].Hooks.PostInstall);
  }

  [Fact]
  public void Derive_SameDependencyInTwoMergedVariants_Throws() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [
            { "label": "lua", "dependencies": { "github.com/user/a": "1.*" } },
            { "label": "lua", "platform": "win-x64",
              "dependencies": { "github.com/user/a": "2.*" } }
          ]
        }
        """);

    // Act & Assert
    Assert.Contains(
        "labeled 'lua'",
        Assert.Throws<ToothFormatException>(() => ToothCompat.Derive(manifest, _serverWin))
            .Message);
  }

  [Fact]
  public void Derive_V1_IsReadAsFormat3First() {
    // Arrange
    PackageManifest manifest = Manifest("""
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
    ToothFile tooth = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal("github.com/LiteLScript-Dev/HelperLib", tooth.Tooth);
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(["echo install"], entry.Hooks.PostInstall);
    Assert.Equal(["github.com/user/dep"], Dependencies(entry));
  }

  [Fact]
  public void Derive_V2_MapsAssetUrlRemoveFilesAndScripts() {
    // Arrange
    PackageManifest manifest = Manifest("""
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
    ToothFile tooth = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal(SemVersion.Parse("1.1.0"), tooth.Version);
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(["github.com/LiteLDev/bds"], Dependencies(entry));
    Assert.True(entry.Dependencies[0].Range.IsSatisfiedBy(SemVersion.Parse("1.21.60")));
    EntryAsset asset = Assert.Single(entry.Assets);
    Assert.Equal(AssetType.Zip, asset.Type);
    Assert.Equal(["https://example.com/v1.1.0/levilamina.zip"], asset.Urls);
    Assert.Equal(
        ["bedrock_server_mod.exe", "bedrock_server_mod.exe/"],
        entry.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal([".\\PeEditor.exe -mb"], entry.Hooks.PostInstall);
  }

  [Theory]
  [InlineData("github.com/user/repo", "repo")]
  [InlineData("github.com/user/repo/v2", "repo")]
  [InlineData("github.com/user/repo/v", "v")]
  [InlineData("github.com/user/repo/v2x", "v2x")]
  [InlineData("example.com/v3", "example.com")]
  [InlineData("example.com", "example.com")]
  public void GetToothName_StripsMajorVersionSuffix(string tooth, string name) {
    // Act & Assert
    Assert.Equal(name, ToothCompat.GetToothName(tooth));
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
    Hooks converted = ToothCompat.ConvertScripts(scripts);

    // Assert
    Assert.Equal(["pre"], converted.PreInstall);
    Assert.Equal(["install", "post"], converted.PostInstall);
    Assert.Equal(["preun", "un"], converted.PreUninstall);
    Assert.Equal(["postun"], converted.PostUninstall);
  }

  [Theory]
  [InlineData("**/config.json", true)]
  [InlineData("logs/", true)]
  [InlineData("", false)]
  [InlineData("/abs", false)]
  [InlineData("a//b", false)]
  public void TryParseGlob_InvalidPattern_ReturnsNull(string pattern, bool valid) {
    // Act
    PathGlob? glob = ToothCompat.TryParseGlob(pattern);

    // Assert
    Assert.Equal(valid, glob is not null);
  }

  [Fact]
  public void ParseEntry_LegacyEntry_ConvertsPreserveAndPurge() {
    // Arrange: an entry of the workspace file derived from format 3 (protocol/workspace.md §7.1).
    using JsonDocument json = JsonDocument.Parse("""
        {
          "platform": "server", "runtime": "win-x64",
          "dependencies": { "github.com/user/a#client": "^1.0.0" },
          "assets": [
            { "type": "uncompressed", "urls": ["https://example.com/a.exe"],
              "placements": [ { "type": "file", "src": "", "dest": "a.exe" } ] },
            { "type": "self", "placements": [ { "type": "dir", "src": "/x\\", "dest": "../y" } ] }
          ],
          "preserve": ["config.json", "data\\", "/bad", "a//b"],
          "purge": ["logs", "tmp/"],
          "hooks": { "post_install": ["x"] }
        }
        """);

    // Act
    ToothEntry entry = ToothParser.ParseEntry(json.RootElement, legacy: true);

    // Assert
    Assert.True(entry.Legacy);
    ToothDependency dependency = Assert.Single(entry.Dependencies);
    Assert.True(dependency.ClientCompat);
    Assert.Equal("client", dependency.Source.Variant);
    Assert.Equal([AssetType.Uncompressed, AssetType.Self], entry.Assets.Select(a => a.Type));
    Assert.Equal([new EntryPlacement("", "a.exe", false)], entry.Assets[0].Placements);
    Assert.Equal([new EntryPlacement("/x\\", "../y", true)], entry.Assets[1].Placements);
    Assert.Equal(["**/config.json", "data/"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs", "logs/", "tmp/"], entry.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal(["x"], entry.Hooks.PostInstall);
  }

  [Theory]
  [InlineData("""{ "platform": "server", "runtime": "win-x64", "assets": [ { "type": "mcmod", "urls": [] } ] }""")]
  [InlineData("""{ "platform": "server", "runtime": "win-x64", "assets": [ { "type": "self", "placements": [ { "type": "glob", "src": "", "dest": "" } ] } ] }""")]
  [InlineData("""{ "platform": "any", "runtime": "win-x64" }""")]
  [InlineData("""{ "platform": "server" }""")]
  [InlineData("""{ "platform": "server", "runtime": "win-x64", "preserve": [ 1 ] }""")]
  public void ParseEntry_InvalidLegacyEntry_Throws(string entry) {
    // Arrange
    using JsonDocument json = JsonDocument.Parse(entry);

    // Act & Assert
    Assert.Throws<ToothFormatException>(() => ToothParser.ParseEntry(json.RootElement, legacy: true));
  }

  [Fact]
  public void ParseEntry_LegacyRuntime_IsNotValidatedAsRid() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse("""{ "platform": "client", "runtime": "x y" }""");

    // Act
    ToothEntry entry = ToothParser.ParseEntry(json.RootElement, legacy: true);

    // Assert
    Assert.Equal("x y", entry.Runtime);
    Assert.Throws<ToothFormatException>(() => ToothParser.ParseEntry(json.RootElement, legacy: false));
  }

  [Fact]
  public void Derive_DerivedDocumentJson_IsUtf8Json() {
    // Arrange
    ToothFile tooth = ToothCompat.Derive(Manifest(_multiPlatformV3), _serverWin);

    // Act
    byte[] bytes = Encoding.UTF8.GetBytes(tooth.Json.GetRawText());

    // Assert: the derived document is not of format 4 as a file (its entries are legacy), but
    // is strict JSON.
    using JsonDocument document = ToothParser.ParseJson(bytes);
    Assert.Equal(4, ToothParser.FormatVersion(document.RootElement));
  }
}
