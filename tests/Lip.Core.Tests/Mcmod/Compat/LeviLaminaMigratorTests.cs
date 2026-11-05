using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

public class LeviLaminaMigratorTests {
  [Fact]
  public async Task Migrate_EveryKey_IsKeptInOrderWithFormatFirst() {
    // Arrange
    JsonObject manifest = JsonNode.Parse("""
        {
          "name": "LegacyMoney", "entry": "LegacyMoney.dll", "version": "0.22.0",
          "type": "native", "platform": "server", "passive": true,
          "description": "d", "author": "LiteLDev", "extraInfo": { "x": 1 },
          "dependencies": [ { "name": "LeviLamina", "version": ">=1.0.0" }, { "name": "B" } ],
          "conflicts": [ { "name": "LLMoney", "version": "invalid range" } ],
          "optionalDependencies": [ { "name": "LSE", "version": ">=0.22.0" } ],
          "loadAfter": [ { "name": "LSE" }, { "name": "My Mod" } ],
          "loadBefore": [ { "name": "__before_all_mods__" } ],
          "custom": [1]
        }
        """)!.AsObject();

    // Act
    ToothMigrator.Result result = await LeviLaminaMigrator.Migrate(manifest, new FakeUi());

    // Assert
    JsonNode expected = JsonNode.Parse("""
        {
          "format": "bf45b1",
          "name": "LegacyMoney", "entry": "LegacyMoney.dll", "version": "0.22.0",
          "type": "native", "platform": "server", "passive": true,
          "description": "d", "author": "LiteLDev", "extraInfo": { "x": 1 },
          "dependencies": [ { "name": "LeviLamina", "version": ">=1.0.0" }, { "name": "B" } ],
          "conflicts": [ { "name": "LLMoney" } ],
          "optionalDependencies": [ { "name": "LSE", "version": ">=0.22.0" } ],
          "loadAfter": [ { "name": "LSE" }, { "name": "My Mod" } ],
          "loadBefore": [ { "name": "__before_all_mods__" } ],
          "custom": [1]
        }
        """)!;
    Assert.Equal(expected.ToJsonString(), result.Document.ToJsonString());
    Assert.StartsWith("conflicts[0].version:", Assert.Single(result.Notes), StringComparison.Ordinal);
    PackageDocument document = Assert.IsType<PackageDocument>(Reparse(result));
    Assert.True(document.IsMod);
    Assert.Equal(["LeviLamina", "B"], document.Dependencies.Select(d => d.Id));
    Assert.Null(Assert.Single(document.Conflicts).Version);
    Assert.Empty(result.Targets);
  }

  [Fact]
  public async Task Migrate_InvalidEntriesAndPlatform_AreDroppedWithNotes() {
    // Arrange
    JsonObject manifest = JsonNode.Parse("""
        {
          "name": "m", "version": "1.0.0", "type": "native", "entry": "m.dll",
          "platform": "universal",
          "dependencies": [ { "name": "My Mod" }, { "name": "a", "version": 5 }, "x" ],
          "conflicts": { "b": "*" }
        }
        """)!.AsObject();

    // Act
    ToothMigrator.Result result = await LeviLaminaMigrator.Migrate(manifest, new FakeUi());

    // Assert
    Assert.Equal(
        """{"format":"bf45b1","name":"m","version":"1.0.0","type":"native","entry":"m.dll","dependencies":[{"name":"a"}],"conflicts":[]}""",
        result.Document.ToJsonString());
    Assert.Equal(
        ["platform:", "dependencies[0]:", "dependencies[1].version:", "dependencies[2]:", "conflicts:"],
        result.Notes.Select(n => n[..(n.IndexOf(':') + 1)]));
    Assert.IsType<PackageDocument>(Reparse(result));
  }

  [Fact]
  public async Task Migrate_MissingValues_AreAsked() {
    // Arrange
    JsonObject manifest = JsonNode.Parse("""{ "name": "My Mod" }""")!.AsObject();
    FakeUi ui = new("MyMod", "1.0", "1.0.0", "native", "MyMod.dll");

    // Act
    ToothMigrator.Result result = await LeviLaminaMigrator.Migrate(manifest, ui);

    // Assert
    Assert.Equal(
        """{"format":"bf45b1","name":"MyMod","version":"1.0.0","type":"native","entry":"MyMod.dll"}""",
        result.Document.ToJsonString());
    Assert.Equal(5, ui.Questions.Count);
    Assert.Single(ui.Warnings);
  }

  private static ManifestDocument Reparse(ToothMigrator.Result result) =>
      new DocumentParser().Parse(Encoding.UTF8.GetBytes(result.Document.ToJsonString()));
}
