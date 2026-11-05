using System.Text.Json.Nodes;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

public class LeviLaminaMigratorTests {
  [Fact]
  public async Task Migrate_EveryKey_IsMappedByTheTable() {
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
          "format": "bf45b1", "id": "LegacyMoney", "version": "0.22.0", "platform": "server",
          "info": { "description": "d", "authors": ["LiteLDev"] },
          "dependencies": { "LeviLamina": ">=1.0.0", "B": "*" },
          "conflicts": { "LLMoney": "*" },
          "mod": {
            "loader": "native", "entry": "LegacyMoney.dll", "passive": true,
            "loadAfter": ["LSE"], "loadBefore": ["*"], "extra": { "x": 1, "custom": [1] }
          }
        }
        """)!;
    Assert.Equal(expected.ToJsonString(), result.Document.ToJsonString());
    Assert.Contains(result.Notes, n => n.StartsWith("conflicts[0].version:", StringComparison.Ordinal));
    Assert.Contains(result.Notes, n => n.StartsWith("loadAfter[1]:", StringComparison.Ordinal));
    Assert.Contains(result.Notes, n => n.StartsWith("optionalDependencies[0].version:", StringComparison.Ordinal));
    Assert.Contains(result.Notes, n => n == "custom: moved into mod.extra.");
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
        """{"format":"bf45b1","id":"MyMod","version":"1.0.0","mod":{"loader":"native","entry":"MyMod.dll"}}""",
        result.Document.ToJsonString());
    Assert.Equal(5, ui.Questions.Count);
    Assert.Single(ui.Warnings);
  }
}
