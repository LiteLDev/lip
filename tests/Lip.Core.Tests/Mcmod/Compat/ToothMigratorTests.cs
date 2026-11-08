using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Migration.PackageManifests;
using Semver;

namespace Lip.Core.Tests.Mcmod.Compat;

/// Converting tooth.json of format 1–3 to a package file (lip migrate, protocol/compatibility.md
/// §5), agreeing with protocol/compatibility.md §2.
public class ToothMigratorTests {
  private const string _uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  [Fact]
  public async Task Migrate_Labels_TakeThePlatformFromLeviLamina() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/LiteLDev/LegacyMoney", "version": "0.22.0",
          "info": { "name": "Legacy Money", "description": "d", "tags": ["economy"],
                    "avatar_url": "https://example.com/a.png" },
          "variants": [
            { "platform": "win-x64",
              "dependencies": { "github.com/LiteLDev/LeviLamina": "1.0.0" } },
            { "label": "client", "platform": "win-x64",
              "dependencies": { "github.com/LiteLDev/LeviLamina#client": "1.0.0" } },
            { "label": "tool", "platform": "win-x64",
              "dependencies": { "github.com/a/tool": "1.0.0" } }
          ]
        }
        """);
    FakeUi ui = new();

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    Assert.Empty(ui.Questions);
    Assert.Empty(result.Notes);
    PackageFile file = Reparse(result);
    Assert.Equal("github.com/LiteLDev/LegacyMoney", file.Id);
    Assert.Equal(SemVersion.Parse("0.22.0"), file.Version);
    Assert.Equal(
        [
          ("", McmodPlatform.Server, "win-x64", "github.com/LiteLDev/LeviLamina"),
          ("client", McmodPlatform.Client, "win-x64", "github.com/LiteLDev/LeviLamina#client"),
          ("tool", McmodPlatform.Server, "win-x64", "github.com/a/tool"),
          ("tool", McmodPlatform.Client, "win-x64", "github.com/a/tool"),
        ],
        Entries(file).Select(e => (e.Variant, e.Entry.Platform, e.Entry.Runtime,
            Assert.Single(e.Entry.Dependencies).Source.ToString()))
            .OrderBy(e => e.Item1, StringComparer.Ordinal).ThenBy(e => e.Item2));
    JsonObject info = result.Document["info"]!.AsObject();
    Assert.Equal(
        """{"name":"Legacy Money","description":"d","tags":["economy"],"logo":"https://example.com/a.png"}""",
        info.ToJsonString());
    Assert.Equal(
        ["format", "id", "version", "info", "variants"],
        result.Document.Select(p => p.Key));
  }

  [Fact]
  public async Task Migrate_EverySelectedEntry_AgreesWithDerive() {
    // Arrange: for every environment the migrated file has an entry for, it holds what
    // protocol/compatibility.md §2 derives.
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "dependencies": { "github.com/o/common": ">=1.0.0" } },
            { "platform": "win-*", "dependencies": { "github.com/o/win": "1.*" } },
            { "label": "client", "platform": "win-x64",
              "dependencies": { "github.com/o/c": "^2.0.0",
                                "github.com/LiteLDev/LeviLamina#client": "1.0.0" } }
          ]
        }
        """);
    FakeUi ui = new("win-x64, win-arm64", "win-x64, linux-x64");

    // Act
    PackageFile file = Reparse(await ToothMigrator.Migrate(tooth, ui));

    // Assert
    foreach ((string variant, PackageEntry entry) in Entries(file)) {
      LipEnvironment environment = new(entry.Platform, entry.Runtime);
      PackageEntry derived = ToothCompat.Derive(tooth, environment).Variants[variant].Single();
      Assert.Equal(
          derived.Dependencies.Select(d => (d.Source.ToString(), d.Range.Text)),
          entry.Dependencies.Select(d => (d.Source.ToString(), d.Range.Text)));
    }

    // Without a dependency on LeviLamina, the variant "" has entries for both platforms; the
    // variant "client", which depends on LeviLamina#client, for the client only.
    Assert.Equal(
        [
          (McmodPlatform.Server, "win-x64"),
          (McmodPlatform.Client, "win-x64"),
          (McmodPlatform.Server, "linux-x64"),
          (McmodPlatform.Client, "linux-x64"),
          (McmodPlatform.Server, "win-arm64"),
          (McmodPlatform.Client, "win-arm64"),
        ],
        file.Variants[""].Select(e => (e.Platform, e.Runtime)));
    Assert.Equal(
        [(McmodPlatform.Client, "win-x64")],
        file.Variants["client"].Select(e => (e.Platform, e.Runtime)));
  }

  [Fact]
  public async Task Migrate_VariantsWithoutPlatform_AskForRuntimes() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/engine", "version": "1.0.0",
          "variants": [ { "label": "lua" }, { "label": "js" } ]
        }
        """);
    FakeUi ui = new("", "win x64", "win-*", " win-x64 , linux-x64,win-x64 ");

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    Assert.Equal(4, ui.Questions.Count);
    Assert.Contains("without a platform", ui.Questions[0]);
    Assert.Equal(3, ui.Warnings.Count);
    PackageFile file = Reparse(result);
    Assert.Equal(["js", "lua"], file.Variants.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(
        [
          (McmodPlatform.Server, "win-x64"), (McmodPlatform.Client, "win-x64"),
          (McmodPlatform.Server, "linux-x64"), (McmodPlatform.Client, "linux-x64"),
        ],
        file.Variants["lua"].Select(e => (e.Platform, e.Runtime)));
  }

  [Fact]
  public async Task Migrate_PlatformPattern_AsksForMatchingRuntimesOnce() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-*", "dependencies": { "github.com/o/a": "1.0.0" } },
            { "label": "lua", "platform": "win-*" },
            { "platform": "linux-x64", "dependencies": { "github.com/o/b": "1.0.0" } }
          ]
        }
        """);
    FakeUi ui = new("linux-x64", "win-x64,win-arm64");

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    Assert.Equal(2, ui.Questions.Count);
    Assert.All(ui.Questions, q => Assert.Contains("'win-*'", q));
    Assert.Contains("'linux-x64' does not match 'win-*'", Assert.Single(ui.Warnings));
    PackageFile file = Reparse(result);
    Assert.Equal(
        [
          (McmodPlatform.Server, "win-x64", "github.com/o/a"),
          (McmodPlatform.Client, "win-x64", "github.com/o/a"),
          (McmodPlatform.Server, "win-arm64", "github.com/o/a"),
          (McmodPlatform.Client, "win-arm64", "github.com/o/a"),
          (McmodPlatform.Server, "linux-x64", "github.com/o/b"),
          (McmodPlatform.Client, "linux-x64", "github.com/o/b"),
        ],
        file.Variants[""].Select(e => (e.Platform, e.Runtime,
            Assert.Single(e.Dependencies).Source.ToString())));
    Assert.Equal(
        ["win-x64", "win-x64", "win-arm64", "win-arm64"],
        file.Variants["lua"].Select(e => e.Runtime));
  }

  [Fact]
  public async Task Migrate_PlatformNotARuntime_AsksForRuntimesOnce() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "platform": "windows/amd64", "dependencies": { "github.com/o/a": "1.0.0" } },
            { "label": "lua", "platform": "windows/amd64" },
            { "platform": "linux-x64", "dependencies": { "github.com/o/b": "1.0.0" } }
          ]
        }
        """);
    FakeUi ui = new("windows/amd64", "win-x64,win-arm64");

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    Assert.Equal(2, ui.Questions.Count);
    Assert.All(ui.Questions, q => Assert.Contains("'windows/amd64'", q));
    Assert.Contains("'windows/amd64' is not a runtime identifier", Assert.Single(ui.Warnings));
    PackageFile file = Reparse(result);
    Assert.Equal(
        [
          (McmodPlatform.Server, "win-x64", "github.com/o/a"),
          (McmodPlatform.Client, "win-x64", "github.com/o/a"),
          (McmodPlatform.Server, "win-arm64", "github.com/o/a"),
          (McmodPlatform.Client, "win-arm64", "github.com/o/a"),
          (McmodPlatform.Server, "linux-x64", "github.com/o/b"),
          (McmodPlatform.Client, "linux-x64", "github.com/o/b"),
        ],
        file.Variants[""].Select(e => (e.Platform, e.Runtime,
            Assert.Single(e.Dependencies).Source.ToString())));
    Assert.Equal(
        ["win-x64", "win-x64", "win-arm64", "win-arm64"],
        file.Variants["lua"].Select(e => e.Runtime));
  }

  [Fact]
  public async Task Migrate_PlatformNotARuntimeWithoutTerminal_ThrowsNamingThePlatform() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [ { "platform": "windows/amd64" } ]
        }
        """);

    // Act & Assert
    CannotAskException ex = await Assert.ThrowsAsync<CannotAskException>(
        () => ToothMigrator.Migrate(tooth, new FakeUi()));
    Assert.Contains("'windows/amd64'", ex.Message);
  }

  [Fact]
  public async Task Migrate_QuestionWithoutTerminal_ThrowsCannotAsk() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/engine", "version": "1.0.0",
          "variants": [ { "label": "lua" } ]
        }
        """);

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(() => ToothMigrator.Migrate(tooth, new FakeUi()));
  }

  [Fact]
  public async Task Migrate_Placements_AreConvertedToFormat4Forms() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64", "assets": [
              { "type": "zip", "urls": ["https://example.com/common.zip"], "placements": [
                { "type": "dir", "src": "", "dest": "common" },
                { "type": "dir", "src": "/lib\\", "dest": "plugins\\lib\\" },
                { "type": "file", "src": "a.dll", "dest": "plugins/a/a.dll" },
                { "type": "file", "src": "bin/*.dll", "dest": "plugins\\tool" },
                { "type": "dir", "src": "x", "dest": "." }
              ] },
              { "type": "self" }
            ] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    Assert.Empty(result.Notes);
    PackageEntry entry = Reparse(result).Variants[""][0];
    Assert.Equal(
        [
          new EntryPlacement("", "common/"),
          new EntryPlacement("lib/", "plugins/lib/"),
          new EntryPlacement("a.dll", "plugins/a/a.dll"),
          new EntryPlacement("bin/*.dll", "plugins/tool/"),
          new EntryPlacement("x/", ""),
        ],
        entry.Assets[0].Placements);
    Assert.Equal(["https://example.com/common.zip"], entry.Assets[0].Urls);
    Assert.Equal(AssetType.Self, entry.Assets[1].Type);
    Assert.Empty(entry.Assets[1].Placements);
    Assert.False(result.Document["variants"]![""]![0]!["assets"]![1]!.AsObject()
        .ContainsKey("place"));
  }

  [Fact]
  public async Task Migrate_PlacementsInvalidInFormat4_AreLeftOutWithNote() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64", "assets": [
              { "type": "zip", "urls": ["https://example.com/a.zip"], "placements": [
                { "type": "dir", "src": "a*", "dest": "x" },
                { "type": "file", "src": "bin/**/*.dll", "dest": "y" },
                { "type": "file", "src": "bin/[^a]", "dest": "z" },
                { "type": "file", "src": "ok.dll", "dest": "ok.dll" }
              ] }
            ] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    string note = Assert.Single(result.Notes);
    Assert.StartsWith("variants (label '', win-x64).assets[0]:", note);
    Assert.Contains("'bin/[^a]' -> 'z' becomes 'bin/[^a]' -> 'z/'", note);
    Assert.Equal(
        [
          new EntryPlacement("a*/", "x/"),
          new EntryPlacement("bin/**/*.dll", "y/"),
          new EntryPlacement("ok.dll", "ok.dll"),
        ],
        Reparse(result).Variants[""][0].Assets[0].Placements);
  }

  [Fact]
  public async Task Migrate_UnsupportedAssetType_LeavesOutTheEntryWithNote() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64", "assets": [
              { "type": "tar", "urls": ["https://example.com/a.tar"] },
              { "type": "tgz", "urls": ["https://example.com/a.tgz"] },
              { "type": "uncompressed", "urls": ["https://example.com/a.exe"],
                "placements": [ { "type": "file", "src": "", "dest": "a.exe" } ] },
              { "type": "zip", "urls": ["https://example.com/a.zip"] }
            ] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    // Assert: as protocol/compatibility.md §2 reads it, the variant has no entry for win-x64.
    Assert.StartsWith(
        "variants (label '', win-x64).assets[0]: lip.toml has no asset type \"tar\"",
        Assert.Single(result.Notes));
    Assert.Empty(result.Document["variants"]!.AsObject());
  }

  [Theory]
  [InlineData("tar")]
  [InlineData("tgz")]
  [InlineData("uncompressed")]
  public async Task Migrate_UnsupportedAssetTypeForOneRuntime_LeavesOutOnlyItsEntries(string type) {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64",
              "assets": [ { "type": "{{type}}", "urls": ["https://example.com/a"] } ] },
            { "platform": "linux-x64",
              "assets": [ { "type": "zip", "urls": ["https://example.com/a.zip"] } ] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    Assert.Equal(
        [(McmodPlatform.Server, "linux-x64"), (McmodPlatform.Client, "linux-x64")],
        Reparse(result).Variants[""].Select(e => (e.Platform, e.Runtime)));
    Assert.Contains($"\"{type}\"", Assert.Single(result.Notes));
  }

  [Fact]
  public async Task Migrate_Dependencies_KeepTheirRangeText() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [ { "platform": "win-x64", "dependencies": {
            "example.com/a": ">=1.2.3 <2.0.0", "example.com/b/v2#lua": "*",
            "example.com/c": "^1.0.0", "example.com/d": "1.*" } } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    Assert.Empty(result.Notes);
    PackageEntry entry = Reparse(result).Variants[""][0];
    Assert.Equal(
        ["example.com/a", "example.com/b/v2#lua", "example.com/c", "example.com/d"],
        entry.Dependencies.Select(d => d.Source.ToString()));
    foreach ((PackageDependency dependency, SemVersionRange original) in entry.Dependencies.Zip(
        tooth.Variants[0].Dependencies.Values)) {
      Assert.Equal(original.ToString(), dependency.Range.Text);
      Assert.Equal(SemVersionRange.Parse(dependency.Range.Text), dependency.Range.Range);
    }
  }

  [Fact]
  public async Task Migrate_ClientVariantDependency_IsKept() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [ { "platform": "win-x64",
                          "dependencies": { "example.com/a#client": "1.0.0" } } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    PackageEntry entry = Reparse(result).Variants[""][0];
    Assert.Equal("example.com/a#client", Assert.Single(entry.Dependencies).Source.ToString());
    Assert.Empty(result.Notes);
  }

  [Fact]
  public async Task Migrate_FilesAndScripts_BecomeGlobsAndHooks() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64",
              "preserve_files": ["config.json", "data/", "plugins\\x\\*.json"],
              "remove_files": ["logs", "tmp/", "{a,b}"],
              "scripts": { "pre_install": ["a"], "install": ["i"], "post_install": ["p"],
                           "pre_uninstall": ["pu"], "uninstall": ["u"],
                           "post_uninstall": ["z"] } }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    PackageEntry entry = Reparse(result).Variants[""][0];
    Assert.Equal(
        ["**/config.json", "data/", "plugins/x/*.json"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs", "logs/", "tmp/", "{a,b}", "{a,b}/"], entry.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal(["a"], entry.Hooks.PreInstall);
    Assert.Equal(["i", "p"], entry.Hooks.PostInstall);
    Assert.Equal(["pu", "u"], entry.Hooks.PreUninstall);
    Assert.Equal(["z"], entry.Hooks.PostUninstall);
    Assert.Contains(
        result.Notes,
        n => n.StartsWith("variants (label '', win-x64).preserve_files: 'config.json' has no '/'"));

  }

  [Fact]
  public async Task Migrate_Entry_WritesKeysOfFormat4() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64",
              "assets": [ { "type": "self", "placements": [
                { "type": "file", "src": "a.dll", "dest": "plugins/a.dll" } ] } ],
              "preserve_files": ["config/"], "remove_files": ["logs/"] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    JsonObject entry = result.Document["variants"]![""]![0]!.AsObject();
    Assert.Equal(
        ["platform", "runtime", "assets", "preserve", "purge"], entry.Select(p => p.Key));
    Assert.Equal("""["config/"]""", entry["preserve"]!.ToJsonString());
    Assert.Equal("""["logs/"]""", entry["purge"]!.ToJsonString());
    JsonObject asset = entry["assets"]![0]!.AsObject();
    Assert.Equal(["type", "place"], asset.Select(p => p.Key));
    Assert.Equal(
        """[{"src":"a.dll","dest":"plugins/a.dll"}]""", asset["place"]!.ToJsonString());
    PackageEntry reparsed = Reparse(result).Variants[""][0];
    Assert.Equal(["config/"], reparsed.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs/"], reparsed.RemoveFiles.Select(g => g.Pattern));
  }

  [Theory]
  [InlineData("http://example.com/a.png")]
  [InlineData("a.png")]
  public async Task Migrate_AvatarUrlNotHttps_IsDroppedWithNote(string avatarUrl) {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "info": { "name": "Tool", "avatar_url": "{{avatarUrl}}" },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    string note = Assert.Single(result.Notes);
    Assert.StartsWith("info.avatar_url:", note);
    Assert.Contains($"'{avatarUrl}'", note);
    Assert.Equal("""{"name":"Tool"}""", result.Document["info"]!.ToJsonString());
    Reparse(result);
  }

  [Fact]
  public async Task Migrate_OnlyAvatarUrlNotHttps_WritesEmptyInfo() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "info": { "avatar_url": "a.png" },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    // The info of T without its avatar_url (protocol/compatibility.md §2).
    Assert.StartsWith("info.avatar_url:", Assert.Single(result.Notes));
    Assert.Equal("{}", result.Document["info"]!.ToJsonString());
  }

  [Fact]
  public async Task Migrate_EmptyAvatarUrl_WritesNoLogoAndNoNote() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "info": { "name": "Tool", "avatar_url": "" },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    Assert.Empty(result.Notes);
    Assert.Equal("""{"name":"Tool"}""", result.Document["info"]!.ToJsonString());
  }

  [Fact]
  public async Task Migrate_EmptyInfoMembers_AreKept() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "info": { "name": "", "description": "", "tags": [] },
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    Assert.Equal(
        """{"name":"","description":"","tags":[]}""", result.Document["info"]!.ToJsonString());
    Reparse(result);
  }

  [Fact]
  public async Task Migrate_UnmergeableVariants_Throws() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64", "scripts": { "install": ["a"] } },
            { "platform": "win-*", "scripts": { "install": ["b"] } }
          ]
        }
        """);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => ToothMigrator.Migrate(tooth, new FakeUi("win-x64")));

    // Assert
    Assert.Contains("labeled '' cannot be merged for win-x64", ex.Message);
  }

  [Fact]
  public async Task Migrate_NoInfo_IsLeftOut() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [ { "platform": "win-x64" } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    Assert.False(result.Document.ContainsKey("info"));
    Assert.Equal(
        """{"platform":"server","runtime":"win-x64"}""",
        result.Document["variants"]![""]![0]!.ToJsonString());
  }

  [Fact]
  public async Task Migrate_V1_IsMigratedThroughFormat3() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 1,
          "tooth": "github.com/LiteLScript-Dev/HelperLib",
          "version": "2.14.1",
          "dependencies": { "github.com/user/dep": [[">=1.0.0"]] },
          "commands": [
            { "type": "install", "commands": ["echo install"],
              "GOOS": "windows", "GOARCH": "amd64" }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    PackageFile file = Reparse(result);
    PackageEntry entry = file.Variants[""][0];
    Assert.Equal("win-x64", entry.Runtime);
    Assert.Equal(["echo install"], entry.Hooks.PostInstall);
    Assert.Equal(["github.com/user/dep"], entry.Dependencies.Select(d => d.Source.ToString()));
  }

  [Fact]
  public void DroppedFields_ExtraV3Scripts_AreNoted() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{_uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [ { "scripts": { "post_install": ["a"], "pre_pack": [], "build": ["b"] } } ]
        }
        """);

    // Act
    IReadOnlyList<string> notes = ToothMigrator.DroppedFields(
        json.RootElement, PackageManifestMigration.Migrate(json));

    // Assert
    Assert.StartsWith("variants[0].scripts.build:", Assert.Single(notes));
  }

  [Fact]
  public void DroppedFields_UnreadableV1RangeAndV2Prerequisites_AreNoted() {
    // Arrange
    using JsonDocument v1 = JsonDocument.Parse("""
        {
          "format_version": 1,
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "dependencies": { "example.com/a": [["2.16.x"]], "example.com/b": [[">=1.0.0"]] }
        }
        """);
    using JsonDocument v2 = JsonDocument.Parse("""
        {
          "format_version": 2,
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "info": { "name": "", "description": "", "author": "", "tags": [] },
          "prerequisites": { "example.com/a": "1.x" }
        }
        """);

    // Act
    IReadOnlyList<string> v1Notes = ToothMigrator.DroppedFields(
        v1.RootElement, PackageManifestMigration.Migrate(v1));
    IReadOnlyList<string> v2Notes = ToothMigrator.DroppedFields(
        v2.RootElement, PackageManifestMigration.Migrate(v2));

    // Assert
    Assert.StartsWith("dependencies[\"example.com/a\"]:", Assert.Single(v1Notes));
    Assert.StartsWith("prerequisites:", Assert.Single(v2Notes));
  }

  [Fact]
  public void Serialize_WritesTomlWithFinalLineBreak() {
    // Arrange
    JsonObject document = new() {
      ["a"] = new JsonObject { ["b"] = "<é>", ["c"] = "1.0" },
    };

    // Act
    string text = Encoding.UTF8.GetString(ToothMigrator.Serialize(document));

    // Assert
    Assert.Equal("[a]\nb = \"<é>\"\nc = \"1.0\"\n", text);
  }

  [Fact]
  public void Serialize_Null_ThrowsPackageFormatException() {
    // Arrange: TOML has no null.
    JsonObject document = new() { ["info"] = new JsonObject { ["tags"] = new JsonArray((JsonNode?)null) } };

    // Act & Assert
    Assert.Throws<PackageFormatException>(() => ToothMigrator.Serialize(document));
  }

  [Fact]
  public async Task AskValid_InvalidAnswers_AreWarnedAndAskedAgain() {
    // Arrange
    FakeUi ui = new("  ", " ok ");

    // Act
    string answer = await ui.AskValid("q", a => a == "" ? "empty" : null);

    // Assert
    Assert.Equal("ok", answer);
    Assert.Equal(["q", "q"], ui.Questions);
    Assert.Equal(["Invalid answer: empty"], ui.Warnings);
  }

  private static PackageManifest Parse(string json) {
    using JsonDocument document = JsonDocument.Parse(json);
    return PackageManifestMigration.Migrate(document);
  }

  /// The migrated document read back as a package file; it must be valid.
  private static PackageFile Reparse(ToothMigrator.Result result) {
    return PackageFileParser.Parse(ToothMigrator.Serialize(result.Document));
  }

  private static IEnumerable<(string Variant, PackageEntry Entry)> Entries(PackageFile file) =>
      file.Variants.SelectMany(v => v.Value.Select(e => (v.Key, e)));
}

/// Answers questions from a queue; with no answer left it cannot ask, like a redirected
/// terminal.
internal sealed class FakeUi(params string[] answers) : IUserInteraction {
  private readonly Queue<string> _answers = new(answers);

  public List<string> Questions { get; } = [];

  public List<string> Warnings { get; } = [];

  public Task<string> Ask(string question) {
    Questions.Add(question);
    return _answers.TryDequeue(out string? answer)
        ? Task.FromResult(answer)
        : throw new CannotAskException($"lip needs {question}, but cannot ask for it here.");
  }

  public Task PrintInfo(string message) => Task.CompletedTask;

  public Task PrintSuccess(string message) => Task.CompletedTask;

  public Task PrintWarning(string message) {
    Warnings.Add(message);
    return Task.CompletedTask;
  }

  public Task PrintError(string message) => Task.CompletedTask;

  public Task RunWithProgress(string message, Func<IProgress<double>, Task> action) =>
      action(new Progress<double>());
}
