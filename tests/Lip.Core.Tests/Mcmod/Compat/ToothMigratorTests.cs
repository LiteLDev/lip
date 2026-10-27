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

  private static PackageManifest Parse(string json) =>
      PackageManifestMigration.Migrate(JsonDocument.Parse(json));

  private static ManifestDocument Reparse(ToothMigrator.Result result) =>
      new DocumentParser().Parse(Encoding.UTF8.GetBytes(result.Document.ToJsonString()));
}
