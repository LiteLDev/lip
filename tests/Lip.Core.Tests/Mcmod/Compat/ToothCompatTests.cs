using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Migration.PackageManifests;
using Semver;

namespace Lip.Core.Tests.Mcmod.Compat;

/// Reading a tooth.json as a package file (protocol/compatibility.md §2).
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

  private static List<string> Dependencies(PackageEntry entry) =>
      [.. entry.Dependencies.Select(d => d.Source.ToString())];

  [Fact]
  public void Derive_PlatformPattern_MergesMatchingVariantsOfEachLabel() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal("github.com/user/repo", packageFile.Id);
    Assert.Equal(SemVersion.Parse("1.2.3"), packageFile.Version);
    Assert.Equal(["", "client"], packageFile.Variants.Keys.Order(StringComparer.Ordinal));
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.True(entry.Derived);
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
    Assert.Equal([new EntryPlacement("a.dll", "plugins/a/a.dll")], asset.Placements);
    Assert.Same(entry, packageFile.SelectEntry("", _serverWin));
    Assert.Equal(
        ["github.com/user/clientdep"], Dependencies(Assert.Single(packageFile.Variants["client"])));
  }

  [Theory]
  [InlineData("win-arm64")]
  [InlineData("win-x86")]
  public void Derive_OtherRuntimeMatchingPattern_UsesThatRuntime(string runtime) {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);
    LipEnvironment environment = new(McmodPlatform.Server, runtime);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, environment);

    // Assert
    Assert.Equal([""], packageFile.Variants.Keys);
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.Equal(runtime, entry.Runtime);
    Assert.Equal(
        ["github.com/user/common", "github.com/user/windows#client"], Dependencies(entry));
    Assert.Same(entry, packageFile.SelectEntry("", environment));
    Assert.Null(packageFile.SelectEntry("", _serverWin));
  }

  [Fact]
  public void Derive_ExactPlatform_SelectsOnlyThatRuntime() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.Equal([""], packageFile.Variants.Keys);
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
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
    PackageFile packageFile = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "osx-arm64"));

    // Assert
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.Equal(["github.com/user/common"], Dependencies(entry));
    Assert.False(packageFile.Variants.ContainsKey("extra"));
    Assert.False(packageFile.Variants.ContainsKey("client"));
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
    PackageFile packageFile = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "linux-x64"));

    // Assert
    Assert.Empty(packageFile.Variants);
    Assert.Empty(packageFile.Json.GetProperty("variants").EnumerateObject());
  }

  [Fact]
  public void Derive_ClientPlatform_VariantEmptyIsTheClientLabel() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, _clientWin);

    // Assert
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.Equal(McmodPlatform.Client, entry.Platform);
    Assert.Equal("win-x64", entry.Runtime);
    Assert.Equal(["github.com/user/clientdep"], Dependencies(entry));
    Assert.Equal(["github.com/user/clientdep"],
        Dependencies(Assert.Single(packageFile.Variants["client"])));
    Assert.Equal(
        packageFile.Json.GetProperty("variants").GetProperty("client").GetRawText(),
        packageFile.Json.GetProperty("variants").GetProperty("").GetRawText());
    Assert.Same(entry, packageFile.SelectEntry("", _clientWin));
  }

  [Fact]
  public void Derive_ClientPlatformWithoutClientVariantForRuntime_HasNoVariantEmpty() {
    // Arrange: the label "client" exists but has no variant for linux-x64.
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, new(McmodPlatform.Client, "linux-x64"));

    // Assert
    Assert.Empty(packageFile.Variants);
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
    PackageFile packageFile = ToothCompat.Derive(manifest, _clientWin);

    // Assert
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.Equal(McmodPlatform.Client, entry.Platform);
    Assert.Equal(["github.com/user/a"], Dependencies(entry));
  }

  [Fact]
  public void Derive_ServerPlatform_KeepsVariantEmptyAndClientLabel() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.NotEqual(
        Dependencies(Assert.Single(packageFile.Variants[""])),
        Dependencies(Assert.Single(packageFile.Variants["client"])));
    Assert.Equal(McmodPlatform.Server, packageFile.Variants["client"][0].Platform);
  }

  [Fact]
  public void Derive_DerivedDocument_IsFormat4WithEntriesForTheEnvironment() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    JsonElement json = packageFile.Json;
    Assert.Equal(4, json.GetProperty("format").GetInt32());
    Assert.False(json.TryGetProperty("format_uuid", out _));
    Assert.Equal("github.com/user/repo", json.GetProperty("id").GetString());
    Assert.Equal("1.2.3", json.GetProperty("version").GetString());
    Assert.Equal("Repo", json.GetProperty("info").GetProperty("name").GetString());
    JsonElement entry = Assert.Single(json.GetProperty("variants").GetProperty("").EnumerateArray());
    Assert.Equal(
        ["platform", "runtime", "deps", "assets", "preserve", "hooks"],
        entry.EnumerateObject().Select(p => p.Name));
    Assert.Equal("server", entry.GetProperty("platform").GetString());
    Assert.Equal("win-x64", entry.GetProperty("runtime").GetString());
    Assert.Equal(
        """{"post_install":["echo win"]}""", entry.GetProperty("hooks").GetRawText());
    Assert.Equal(entry.GetRawText(), packageFile.Variants[""][0].Json.GetRawText());
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
  public void Info_EmptyMembers_AreKept() {
    // Arrange: the info of T, as it is (protocol/compatibility.md §2).
    PackageManifestInfo info = new() {
      Name = "",
      Description = "",
      Tags = [],
      AvatarUrl = "a.png",
    };

    // Act
    JsonObject result = ToothCompat.Info(info);

    // Assert
    Assert.Equal("""{"name":"","description":"","tags":[]}""", result.ToJsonString());
  }

  [Fact]
  public void Info_AbsentMembers_AreAbsent() {
    // Act
    JsonObject result = ToothCompat.Info(new PackageManifestInfo { AvatarUrl = "a.png" });

    // Assert
    Assert.Empty(result);
  }

  [Fact]
  public void Derive_NoInfo_HasNoInfo() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    JsonElement json = ToothCompat.Derive(manifest, _serverWin).Json;

    // Assert
    Assert.False(json.TryGetProperty("info", out _));
  }

  [Fact]
  public void Derive_EmptyInfoMembers_AreKept() {
    // Arrange
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "info": { "name": "", "tags": [], "avatar_url": "" },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    JsonElement info = ToothCompat.Derive(manifest, _serverWin).Json.GetProperty("info");

    // Assert
    Assert.Equal("""{"name":"","tags":[]}""", info.GetRawText());
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

    // Assert: everything is converted to a package file.
    Assert.Equal(
        ["platform", "runtime", "assets", "preserve", "purge"],
        entry.EnumerateObject().Select(p => p.Name));
    Assert.Equal("""["**/config.json"]""", entry.GetProperty("preserve").GetRawText());
    Assert.Equal("""["logs","logs/"]""", entry.GetProperty("purge").GetRawText());
    Assert.Equal(
        """{"type":"self","place":[{"src":"a.dll","dest":"plugins/a.dll"}]}""",
        Assert.Single(entry.GetProperty("assets").EnumerateArray()).GetRawText());
  }

  [Fact]
  public void ParseEntry_EntryWithGlobKeysOfFormat3_IgnoresThem() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse("""
        { "platform": "server", "runtime": "win-x64",
          "preserve_files": ["config.json"], "remove_files": ["logs"] }
        """);

    // Act
    PackageEntry entry = PackageFileParser.ParseEntry(json.RootElement);

    // Assert
    Assert.Empty(entry.PreserveFiles);
    Assert.Empty(entry.RemoveFiles);
  }

  [Fact]
  public void Derive_DerivedEntry_ReparsesAsTheSameEntryOfFormat4() {
    // Arrange
    PackageFile packageFile = ToothCompat.Derive(Manifest(_multiPlatformV3), _serverWin);
    PackageEntry entry = packageFile.Variants[""][0];

    // Act
    PackageEntry reparsed = PackageFileParser.ParseEntry(entry.Json);

    // Assert
    Assert.False(reparsed.Derived);
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
                { "type": "self",
                  "placements": [ { "type": "dir", "src": "d", "dest": "plugins/d" } ] }
              ],
              "preserve_files": ["plugins\\x\\*.json"], "remove_files": ["tmp/"] }
          ]
        }
        """);

    // Act
    PackageEntry entry = Assert.Single(ToothCompat.Derive(manifest, _serverWin).Variants[""]);

    // Assert
    Assert.Equal([AssetType.Zip, AssetType.Self], entry.Assets.Select(a => a.Type));
    Assert.Equal(["https://example.com/a.zip"], entry.Assets[0].Urls);
    Assert.Empty(entry.Assets[0].Placements);
    Assert.Empty(entry.Assets[1].Urls);
    Assert.Equal([new EntryPlacement("d/", "plugins/d/")], entry.Assets[1].Placements);
    Assert.Null(entry.McmodAsset);
    Assert.Equal(
        ["**/config.json", "plugins/x/*.json"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs", "logs/", "tmp/"], entry.RemoveFiles.Select(g => g.Pattern));
  }

  [Theory]
  [InlineData("tar")]
  [InlineData("tgz")]
  [InlineData("uncompressed")]
  public void Derive_AssetOfTypeWithoutFormat4Equivalent_MakesTheVariantAbsent(string type) {
    // Arrange: the asset is in a variant of the label "client" for windows only.
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "zip", "urls": ["https://example.com/a.zip"] } ] },
            { "label": "client",
              "assets": [ { "type": "zip", "urls": ["https://example.com/c.zip"] } ] },
            { "label": "client", "platform": "win-*",
              "assets": [ { "type": "{{type}}", "urls": ["https://example.com/b"],
                "placements": [ { "type": "file", "src": "", "dest": "b.exe" } ] } ] }
          ]
        }
        """);

    // Act
    PackageFile serverWin = ToothCompat.Derive(manifest, _serverWin);
    PackageFile clientWin = ToothCompat.Derive(manifest, _clientWin);
    PackageFile clientLinux = ToothCompat.Derive(manifest, new(McmodPlatform.Client, "linux-x64"));

    // Assert: on windows, the variant "client" is absent, and so is the variant "" derived from
    // it on the client; elsewhere, it is there.
    Assert.Equal([""], serverWin.Variants.Keys);
    Assert.False(serverWin.Json.GetProperty("variants").TryGetProperty("client", out _));
    Assert.Empty(clientWin.Variants);
    Assert.Empty(clientWin.Json.GetProperty("variants").EnumerateObject());
    Assert.Equal(["", "client"], clientLinux.Variants.Keys.Order(StringComparer.Ordinal));
  }

  [Theory]
  [InlineData("file", "a.dll", "plugins/a.dll", "a.dll", "plugins/a.dll")]
  [InlineData("file", ".\\bin\\a.dll", "plugins\\\\a.dll", "bin/a.dll", "plugins/a.dll")]
  [InlineData("file", "bin/*.dll", "plugins/a", "bin/*.dll", "plugins/a/")]
  [InlineData("file", "bin/a?.dll", "plugins/", "bin/a?.dll", "plugins/")]
  [InlineData("file", "bin/[ab].dll", "", "bin/[ab].dll", "")]
  [InlineData("dir", "bin", "plugins/a", "bin/", "plugins/a/")]
  [InlineData("dir", "./bin/", "plugins//a/", "bin/", "plugins/a/")]
  [InlineData("dir", "", "plugins/a", "", "plugins/a/")]
  [InlineData("dir", ".", ".", "", "")]
  public void ConvertPlacement_Format3Placement_BecomesMemberOfPlace(
      string type, string src, string dest, string expectedSrc, string expectedDest) {
    // Arrange
    PackageManifestAssetPlacement placement = new() {
      Type = type == "dir"
          ? PackageManifestAssetPlacement.PlacementType.Directory
          : PackageManifestAssetPlacement.PlacementType.File,
      Src = src,
      Dst = dest,
    };

    // Act
    (string actualSrc, string actualDest) = ToothCompat.ConvertPlacement(placement);

    // Assert
    Assert.Equal((expectedSrc, expectedDest), (actualSrc, actualDest));
    Assert.True(PackageFileParser.IsValidPlacement(actualSrc, actualDest));
  }

  [Fact]
  public void Derive_PlacementsAndGlobsInvalidInFormat4_AreLeftOut() {
    // Arrange: a file placement of the whole archive ("" is the directory form in a package file), a
    // file placed at "" and globs that are no globs of a package file.
    PackageManifest manifest = Manifest($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/user/repo", "version": "1.0.0",
          "variants": [ { "platform": "win-x64",
            "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "", "dest": "b.exe" },
              { "type": "file", "src": "a.dll", "dest": "." },
              { "type": "file", "src": "c.dll", "dest": "plugins/c.dll" } ] } ],
            "preserve_files": ["/abs", "a//b", "ok.json"],
            "remove_files": ["/abs", "a//b", "logs/"] } ]
        }
        """);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.Equal(
        [new EntryPlacement("c.dll", "plugins/c.dll")], Assert.Single(entry.Assets).Placements);
    Assert.Equal(["**/ok.json"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs/"], entry.RemoveFiles.Select(g => g.Pattern));
    _ = PackageFileParser.Parse(Encoding.UTF8.GetBytes(packageFile.Json.GetRawText()));
  }

  [Fact]
  public void Derive_Version_IsTheVersionOfThePackage() {
    // Arrange
    PackageManifest manifest = Manifest(_multiPlatformV3);

    // Act
    PackageFile tagged = ToothCompat.Derive(manifest, _serverWin, SemVersion.Parse("2.0.0"));
    PackageFile untagged = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal(SemVersion.Parse("2.0.0"), tagged.Version);
    Assert.Equal("2.0.0", tagged.Json.GetProperty("version").GetString());
    Assert.Equal(SemVersion.Parse("1.2.3"), untagged.Version);
    Assert.Equal("1.2.3", untagged.Json.GetProperty("version").GetString());
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
    PackageFormatException ex = Assert.Throws<PackageFormatException>(
        () => ToothCompat.Derive(manifest, _serverWin));

    // Assert
    Assert.Contains("labeled ''", ex.Message);
    PackageFile linux = ToothCompat.Derive(manifest, new(McmodPlatform.Server, "linux-x64"));
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
        Assert.Throws<PackageFormatException>(() => ToothCompat.Derive(manifest, _serverWin))
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
    PackageFile packageFile = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal("github.com/LiteLScript-Dev/HelperLib", packageFile.Id);
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
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
    PackageFile packageFile = ToothCompat.Derive(manifest, _serverWin);

    // Assert
    Assert.Equal(SemVersion.Parse("1.1.0"), packageFile.Version);
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
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
  [InlineData("win-x64", true)]
  [InlineData("linux-x64", false)]
  [InlineData("osx-arm64", false)]
  public void Derive_V2PlatformPattern_HasVariantForEveryRuntime(string runtime, bool windows) {
    // Arrange: lip v0.37.0, running on the runtime, added an empty variant for it when a
    // platform is a pattern; the result does not depend on the machine lip runs on.
    PackageManifest manifest = Manifest("""
        {
          "format_version": 2,
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "info": { "name": "", "description": "", "author": "", "tags": [] },
          "platforms": [ { "goos": "windows", "commands": { "post_install": ["echo"] } } ]
        }
        """);

    // Act
    PackageFile packageFile = ToothCompat.Derive(manifest, new LipEnvironment(McmodPlatform.Server, runtime));

    // Assert
    PackageEntry entry = Assert.Single(packageFile.Variants[""]);
    Assert.Equal(runtime, entry.Runtime);
    Assert.Equal(windows ? new[] { "echo" } : [], entry.Hooks.PostInstall);
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
  public void Derive_DerivedDocument_IsAValidToothFileOfFormat4() {
    // Arrange
    PackageFile packageFile = ToothCompat.Derive(Manifest(_multiPlatformV3), _serverWin);

    // Act
    byte[] bytes = Encoding.UTF8.GetBytes(packageFile.Json.GetRawText());

    // Assert: the derived document is a valid package file.
    PackageFile reparsed = PackageFileParser.Parse(bytes);
    Assert.Equal(packageFile.Id, reparsed.Id);
    Assert.Equal(packageFile.Variants.Keys, reparsed.Variants.Keys);
  }
}
