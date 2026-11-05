using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Migration.PackageManifests;

namespace Lip.Core.Tests.Mcmod.Compat;

public class ToothMigratorTests {
  private const string Uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  [Fact]
  public async Task Migrate_ServerAndClientVariants_TargetsByPlatform() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "github.com/LiteLDev/LegacyMoney", "version": "0.22.0",
          "info": { "name": "Legacy Money", "description": "d", "tags": ["economy"],
                    "avatar_url": "https://example.com/a.png" },
          "variants": [
            { "platform": "win-x64", "assets": [] },
            { "label": "client", "platform": "win-x64", "assets": [] }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    MetaDocument document = Assert.IsType<MetaDocument>(Reparse(result));
    Assert.Equal(
        ["format", "version", "targets"],
        result.Document.Select(p => p.Key));
    Assert.Equal("0.22.0", document.Version!.ToString());
    Assert.Equal(
        [
          ("", McmodPlatform.Server),
          ("client", (McmodPlatform?)null),
          ("", McmodPlatform.Client),
        ],
        document.Targets.Select(t => (t.Variant, t.Platforms?.Single())));
    const string prefix = "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/";
    Assert.Equal(
        [
          prefix + "LegacyMoney-{{platform}}-{{runtime}}.mcmod",
          prefix + "LegacyMoney-client-{{runtime}}.mcmod",
          prefix + "LegacyMoney-client-{{platform}}-{{runtime}}.mcmod",
        ],
        document.Targets.Select(t => t.Source.Single()));
    Assert.All(
        result.Document["targets"]!.AsArray(),
        t => Assert.Empty(t!.AsObject().Select(p => p.Key)
            .Except(["variant", "platform", "runtime", "source"])));
    Assert.Equal(
        [
          """{"format":"bf45b1","name":"LegacyMoney","version":"0.22.0","platform":"server","runtime":"win-x64"}""",
          """{"format":"bf45b1","name":"LegacyMoney-client","version":"0.22.0","runtime":"win-x64"}""",
          """{"format":"bf45b1","name":"LegacyMoney-client","version":"0.22.0","platform":"client","runtime":"win-x64"}""",
        ],
        result.Targets.Select(t => t.ToJsonString()));
    Assert.All(result.Targets, t => Assert.IsType<PackageDocument>(Reparse(t)));
    Assert.Contains("info: manifest.json has no package information; it was dropped.", result.Notes);
    Assert.DoesNotContain(result.Notes, n => n.Contains("not a valid manifest.json"));
  }

  [Fact]
  public async Task Migrate_LabeledVariantsOutsideGitHub_AsksForThePrefix() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "example.com/engine", "version": "1.0.0",
          "variants": [ { "label": "lua", "assets": [] }, { "label": "quickjs", "assets": [] } ]
        }
        """);
    FakeUi ui = new("http://bad", "https://example.com/releases/v{{version}}");

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    MetaDocument document = Assert.IsType<MetaDocument>(Reparse(result));
    Assert.Equal(
        [
          ("lua", "https://example.com/releases/v{{version}}/engine-lua.mcmod"),
          ("quickjs", "https://example.com/releases/v{{version}}/engine-quickjs.mcmod"),
        ],
        document.Targets.Select(t => (t.Variant, t.Source.Single())));
    Assert.Equal(
        ["engine-lua", "engine-quickjs"],
        result.Targets.Select(t => (string?)t["name"]));
    Assert.Equal(2, ui.Questions.Count);
    Assert.Single(ui.Warnings);
  }

  [Fact]
  public async Task Migrate_PrefixWithoutTerminal_ThrowsCannotAsk() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "example.com/engine", "version": "1.0.0",
          "variants": [ { "label": "lua", "assets": [] } ]
        }
        """);

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(() => ToothMigrator.Migrate(tooth, new FakeUi()));
  }

  [Fact]
  public async Task Migrate_PatternPlatforms_AskRuntimesAndMergePerRuntime() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "github.com/o/tool", "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "zip", "urls": ["https://example.com/common.zip"],
                "placements": [ { "type": "dir", "src": "", "dest": "common" } ] } ] },
            { "platform": "win-*", "assets": [ { "type": "uncompressed",
                "urls": ["https://example.com/win.exe"],
                "placements": [ { "type": "file", "src": "", "dest": "tool.exe" } ] } ] },
            { "platform": "linux-x64", "dependencies": { "github.com/o/libx": "^1.0.0" } }
          ]
        }
        """);
    FakeUi ui = new("win-x64, win-arm64");

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    MetaDocument document = Assert.IsType<MetaDocument>(Reparse(result));
    Assert.Equal(
        [["win-x64", "win-arm64"], ["linux-x64"], null],
        document.Targets.Select(t => t.Runtimes));
    Assert.Equal(
        """{"runtime":["win-x64","win-arm64"],"source":"https://github.com/o/tool/releases/download/v{{version}}/tool-{{runtime}}.mcmod"}""",
        result.Document["targets"]![0]!.ToJsonString());
    IReadOnlyList<JsonObject> targets = result.Targets;
    Assert.Equal(3, targets.Count);
    Assert.All(targets, t => Assert.IsType<PackageDocument>(Reparse(t)));
    Assert.Equal(
        [null, "linux-x64", null],
        targets.Select(t => (string?)t["runtime"]));
    JsonObject windows = targets[0];
    Assert.Equal(["asset0", "asset1"], windows["archives"]!.AsObject().Select(p => p.Key));
    Assert.Equal("file", (string?)windows["archives"]!["asset1"]!["format"]);
    Assert.Equal(
        """[{"from":"asset0","src":"","dest":"common/"},{"from":"asset1","src":"asset1","dest":"tool.exe"}]""",
        windows["files"]!.ToJsonString());
    JsonObject linux = targets[1];
    Assert.Equal(
        """[{"name":"libx","version":"1.*","source":"github.com/o/libx"}]""",
        linux["dependencies"]!.ToJsonString());
    Assert.False(targets[2].ContainsKey("dependencies"));
  }

  [Fact]
  public async Task Migrate_SingleSelfVariant_WritesAPackageDocument() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [
            { "platform": "win-x64",
              "assets": [ { "type": "self", "placements": [
                { "type": "file", "src": "bin/*.dll", "dest": "plugins\\tool" } ] } ],
              "preserve_files": ["config.json", "data/"], "remove_files": ["logs"],
              "scripts": { "install": ["i"], "post_install": ["p"] } }
          ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    PackageDocument document = Assert.IsType<PackageDocument>(Reparse(result));
    Assert.Equal(
        ["format", "name", "version", "runtime", "files", "preserve", "remove", "hooks"],
        result.Document.Select(p => p.Key));
    Assert.Equal("tool", document.Id);
    Assert.Equal("win-x64", document.Runtime);
    Assert.Empty(result.Targets);
    Assert.Equal([new PackageFile(null, "bin/*.dll", "plugins/tool/")], document.Files);
    Assert.Equal(["**/config.json", "data/"], document.Preserve.Select(g => g.Pattern));
    Assert.Equal(["logs", "logs/"], document.Remove.Select(g => g.Pattern));
    Assert.Equal(["i", "p"], document.Hooks.PostInstall);
    string note = Assert.Single(result.Notes, n => n.Contains("config.json"));
    Assert.StartsWith("variants[0].preserve_files:", note);
  }

  [Fact]
  public async Task Migrate_InvalidToothName_AsksForTheId() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "example.com/_tool", "version": "1.0.0",
          "variants": [ { "assets": [] } ]
        }
        """);
    FakeUi ui = new("tool");

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, ui);

    // Assert
    Assert.Equal("tool", Assert.IsType<PackageDocument>(Reparse(result)).Id);
    Assert.Contains("'_tool' is not a valid id", Assert.Single(ui.Questions));
  }

  [Fact]
  public async Task Migrate_ConvertibleRange_HasNoNote() {
    // Arrange
    PackageManifest tooth = Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
          "tooth": "example.com/tool", "version": "1.0.0",
          "variants": [ { "assets": [], "dependencies": {
            "example.com/a": ">=1.2.3 <2.0.0", "example.com/b/v2#lua": "*" } } ]
        }
        """);

    // Act
    ToothMigrator.Result result = await ToothMigrator.Migrate(tooth, new FakeUi());

    // Assert
    PackageDocument document = Assert.IsType<PackageDocument>(Reparse(result));
    Assert.Equal(["a", "b-lua"], document.Dependencies.Select(d => d.Id));
    Assert.Equal(
        ["example.com/a", "example.com/b/v2#lua"],
        document.Dependencies.Select(d => d.Refs!.Single().ToString()));
    Assert.Null(document.Dependencies[1].Version);
    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse("""
            [
              { "name": "a", "version": ">=1.2.3 <2.0.0", "source": "example.com/a" },
              { "name": "b-lua", "source": "example.com/b/v2#lua" }
            ]
            """),
        result.Document["dependencies"]));
    Assert.Empty(result.Notes);
  }

  [Fact]
  public void DroppedFields_ExtraV3Scripts_AreNoted() {
    // Arrange
    using JsonDocument json = JsonDocument.Parse($$"""
        {
          "format_version": 3, "format_uuid": "{{Uuid3}}",
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

  private static PackageManifest Parse(string json) {
    using JsonDocument document = JsonDocument.Parse(json);
    return PackageManifestMigration.Migrate(document);
  }

  private static ManifestDocument Reparse(ToothMigrator.Result result) => Reparse(result.Document);

  private static ManifestDocument Reparse(JsonObject document) =>
      new DocumentParser().Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
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
