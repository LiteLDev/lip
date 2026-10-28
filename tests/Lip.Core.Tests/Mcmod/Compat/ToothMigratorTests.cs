using System.Text;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Migration.PackageManifests;

namespace Lip.Core.Tests.Mcmod.Compat;

public class ToothMigratorTests {
  [Fact]
  public void Migrate_ServerAndClientVariants_RoutesByPlatform() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "github.com/LiteLDev/LegacyMoney",
          "version": "0.22.0",
          "variants": [
            { "platform": "win-x64", "assets": [] },
            { "label": "client", "platform": "win-x64", "assets": [] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    RoutingDocument document = Assert.IsType<RoutingDocument>(Reparse(result));
    Assert.Equal("LegacyMoney", document.Name);
    Assert.Equal(
        [("", McmodPlatform.Server), ("", McmodPlatform.Client)],
        document.Artifacts.Select(a => (a.Variant, Assert.Single(a.Platforms!))));
    Assert.All(document.Artifacts, a => Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/"
            + "{{name}}-{{platform}}-{{rid}}.mcmod",
        Assert.Single(a.Refs)));
    Assert.DoesNotContain(result.Notes, n => n.Contains("not a valid manifest.json"));
  }

  [Fact]
  public void Migrate_LabeledVariants_RoutesByVariant() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/engine",
          "version": "1.0.0",
          "variants": [
            { "label": "lua", "assets": [] },
            { "label": "quickjs", "assets": [] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    RoutingDocument document = Assert.IsType<RoutingDocument>(Reparse(result));
    Assert.Equal(
        [("lua", "./lua/"), ("quickjs", "./quickjs/")],
        document.Artifacts.Select(a => (a.Variant, Assert.Single(a.Refs))));
  }

  [Fact]
  public void Migrate_GlobFilePlacement_IsLeftOutWithNote() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "*.dll", "dest": "plugins/tool" } ] } ] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(Reparse(result));
    Assert.Empty(document.Placements);
    Assert.Contains(result.Notes, n => n.Contains("'*.dll'"));
  }

  [Fact]
  public void Migrate_NameOnlyPreservePattern_IsPrefixedWithNote() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "self" } ], "preserve_files": ["config.json", "data/"] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(Reparse(result));
    Assert.Equal(["**/config.json", "data/"], document.PreserveFiles.Select(g => g.Pattern));
    string note = Assert.Single(result.Notes, n => n.Contains("config.json"));
    Assert.StartsWith("variants[0].preserve_files:", note);
  }

  [Fact]
  public void Migrate_RoutedVariantWithZipAssets_NamesTheAssetKind() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "zip", "urls": ["https://example.com/a.zip"] } ],
              "remove_files": ["old/"], "scripts": { "post_install": ["echo"] } }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    Assert.IsType<RoutingDocument>(Reparse(result));
    string note = Assert.Single(result.Notes, n => n.StartsWith("variants[0]:", StringComparison.Ordinal));
    Assert.Contains("zip assets", note);
    Assert.Contains("removeFiles", note);
    Assert.Contains("scripts", note);
  }

  [Fact]
  public void Migrate_LabeledVariants_NotesThatTheyNeedDistinctNames() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/engine",
          "version": "1.0.0",
          "variants": [
            { "label": "", "dependencies": { "example.com/engine#lua": "1.0.0" } },
            { "label": "lua", "assets": [ { "type": "self" } ] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    Assert.IsType<RoutingDocument>(Reparse(result));
    string note = Assert.Single(result.Notes, n => n.StartsWith("variants:", StringComparison.Ordinal));
    Assert.Contains("\"example.com/engine#<label>\"", note);
    Assert.Contains("'engine-lua'", note);
  }

  [Fact]
  public void Migrate_ConvertibleRange_HasNoNote() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "variants": [ { "assets": [], "dependencies": {
            "example.com/a": ">=26.51.2 <26.60.0", "example.com/b": "26.51.*" } } ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(Reparse(result));
    Assert.Equal(2, document.InstallDependencies.Count);
    Assert.DoesNotContain(result.Notes, n => n.Contains("dependencies"));
  }

  [Fact]
  public void Migrate_RangeWithDifferentSemantics_KeepsItWithNote() {
    // Arrange
    PackageManifest tooth = Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "variants": [ { "assets": [], "dependencies": {
            "example.com/a": "^1.2.3 || ^2.0.0-0" } } ]
        }
        """);

    // Act
    ToothMigrator.Result result = ToothMigrator.Migrate(tooth);

    // Assert
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(Reparse(result));
    Assert.Equal("^1.2.3||2.*", Assert.Single(document.InstallDependencies).Version!.ToString());
    string note = Assert.Single(result.Notes);
    Assert.StartsWith("variants[0].dependencies[\"example.com/a\"]:", note);
    Assert.Contains("rejects 2.0.0-0", note);
  }

  [Fact]
  public void DroppedFields_ExtraV3Scripts_AreNoted() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse("""
        {
          "format_version": 3,
          "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
          "tooth": "example.com/tool",
          "version": "1.0.0",
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
  public void DroppedFields_V2RootDependenciesOverriddenByEveryPlatform_AreNotNoted() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse("""
        {
          "format_version": 2,
          "tooth": "example.com/tool",
          "version": "1.0.0",
          "info": { "name": "", "description": "", "author": "", "tags": [] },
          "dependencies": { "example.com/a": "1.x" },
          "platforms": [
            { "goos": "windows", "goarch": "amd64", "dependencies": { "example.com/b": "2.x" } }
          ]
        }
        """);

    // Act
    IReadOnlyList<string> notes = ToothMigrator.DroppedFields(
        json.RootElement, PackageManifestMigration.Migrate(json));

    // Assert
    Assert.Empty(notes);
  }

  private static PackageManifest Parse(string json) =>
      PackageManifestMigration.Migrate(JsonDocument.Parse(json));

  private static ManifestDocument Reparse(ToothMigrator.Result result) =>
      new DocumentParser().Parse(Encoding.UTF8.GetBytes(result.Document.ToJsonString()));
}
