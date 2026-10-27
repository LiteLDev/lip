using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

public class ToothRepositoryMigratorTests {
  private const string Uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

  private readonly MockFileSystem _fs = new();

  public ToothRepositoryMigratorTests() {
    _fs.AddDirectory(_root);
  }

  /// A format 3 tooth.json routing by platform, like that of a LeviLamina mod repository.
  private static readonly string _routedTooth = $$"""
      {
        "format_version": 3,
        "format_uuid": "{{Uuid3}}",
        "tooth": "github.com/LiteLDev/LegacyMoney",
        "version": "0.22.0",
        "variants": [
          { "platform": "win-x64", "assets": [ { "type": "zip", "urls": ["https://example.com/a.zip"] } ] },
          { "label": "client", "platform": "win-x64", "assets": [] }
        ]
      }
      """;

  /// A format 3 tooth.json converted to a concrete file package.
  private static readonly string _concreteTooth = $$"""
      {
        "format_version": 3,
        "format_uuid": "{{Uuid3}}",
        "tooth": "github.com/test/tool",
        "version": "1.0.0",
        "variants": [
          { "assets": [ { "type": "self", "placements": [
            { "type": "file", "src": "tool.exe", "dest": "tool.exe" } ] } ] }
        ]
      }
      """;

  [Fact]
  public async Task Migrate_NoToothJson_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("manifest.json", "{ \"name\": \"x\" }");

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => ToothRepositoryMigrator.Migrate(_fs, _root));

    // Assert
    Assert.Contains("There is no tooth.json", ex.Message);
    Assert.Equal("{ \"name\": \"x\" }", Read("manifest.json"));
    Assert.Equal(["manifest.json"], Files());
  }

  [Fact]
  public async Task Migrate_InvalidToothJson_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", "{ \"format_version\": 9 }");

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => ToothRepositoryMigrator.Migrate(_fs, _root));

    // Assert
    Assert.Contains("is not a valid tooth.json", ex.Message);
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_NoManifest_WritesConvertedDocumentAndDeletesTooth() {
    // Arrange
    AddFile("tooth.json", _concreteTooth);
    AddFile("tool.exe", "exe");

    // Act
    IReadOnlyList<string> notes = await ToothRepositoryMigrator.Migrate(_fs, _root);

    // Assert
    Assert.Equal(["manifest.json", "tool.exe"], Files());
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.True(document.IsMarked);
    Assert.Equal("tool", document.Name);
    Assert.Equal([new Placement("tool.exe", "tool.exe")], document.Placements);
    Assert.DoesNotContain(notes, n => n.Contains("not a valid manifest.json"));
  }

  [Fact]
  public async Task Migrate_FilePlacementOfDirectory_IsLeftOutWithNote() {
    // Arrange
    AddFile("tooth.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "{{Uuid3}}",
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "tool", "dest": "plugins/tool" },
              { "type": "dir", "src": "missing", "dest": "plugins/missing" },
              { "type": "dir", "src": "tool", "dest": "plugins/tool" } ] } ] }
          ]
        }
        """);
    AddFile("tool/a.dll", "dll");

    // Act
    IReadOnlyList<string> notes = await ToothRepositoryMigrator.Migrate(_fs, _root);

    // Assert
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.Equal([new Placement("tool/", "plugins/tool/")], document.Placements);
    Assert.Contains(notes, n => n.Contains("'tool' -> 'plugins/tool' names a directory"));
    Assert.Contains(notes, n => n.Contains("'missing' -> 'plugins/missing' matches no file"));
  }

  [Fact]
  public async Task Migrate_PatternPlacement_IsExpandedIntoFilePlacements() {
    // Arrange
    AddFile("tooth.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "{{Uuid3}}",
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "bin/*.dll", "dest": "plugins/tool" } ] } ] }
          ]
        }
        """);
    AddFile("bin/a.dll", "a");
    AddFile("bin/b.dll", "b");
    AddFile("bin/c.pdb", "c");

    // Act
    IReadOnlyList<string> notes = await ToothRepositoryMigrator.Migrate(_fs, _root);

    // Assert
    ConcreteDocument document = Assert.IsType<ConcreteDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.Equal(
        [new Placement("bin/a.dll", "plugins/tool/a.dll"),
          new Placement("bin/b.dll", "plugins/tool/b.dll")],
        document.Placements);
    Assert.Contains(notes, n => n.Contains("expanded into 2 file placements"));
  }

  [Fact]
  public async Task Migrate_ReadOnlyToothJson_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", _concreteTooth);
    _fs.File.SetAttributes(Path("tooth.json"), FileAttributes.ReadOnly);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => ToothRepositoryMigrator.Migrate(_fs, _root));

    // Assert
    Assert.Contains("read-only", ex.Message);
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_ExistingManifest_AppendsMissingKeysInItsFormat() {
    // Arrange
    AddFile("tooth.json", _routedTooth);
    AddFile("manifest.json", "{\n    \"entry\": \"LegacyMoney.dll\",\n    \"type\": \"native\"\n}\n");

    // Act
    IReadOnlyList<string> notes = await ToothRepositoryMigrator.Migrate(_fs, _root);

    // Assert
    Assert.Equal(["manifest.json"], Files());
    string expected = """
        {
            "entry": "LegacyMoney.dll",
            "type": "native",
            "formatVersion": 4,
            "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
            "name": "LegacyMoney",
            "version": "0.22.0",
            "refs": [
                "github.com/LiteLDev/LegacyMoney"
            ],
            "artifacts": [
                {
                    "platform": "server",
                    "rid": "win-x64",
                    "refs": [
                        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"
                    ]
                },
                {
                    "platform": "client",
                    "rid": "win-x64",
                    "refs": [
                        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"
                    ]
                }
            ]
        }

        """.ReplaceLineEndings("\n");
    Assert.Equal(expected, Read("manifest.json"));
    Assert.IsType<RoutingDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.DoesNotContain(notes, n => n.Contains("already has"));
    Assert.Contains(notes, n => n.StartsWith("variants[0]", StringComparison.Ordinal)
        && n.Contains("zip assets"));
  }

  [Fact]
  public async Task Migrate_ExistingKeysDiffer_KeepsThemAndNotesEachConflict() {
    // Arrange
    AddFile("tooth.json", _routedTooth);
    AddFile("manifest.json", """
        {
          "name": "${modName}",
          "version": "${modVersion}",
          "refs": ["github.com/LiteLDev/LegacyMoney"]
        }
        """);

    // Act
    IReadOnlyList<string> notes = await ToothRepositoryMigrator.Migrate(_fs, _root);

    // Assert
    JsonObject merged = JsonNode.Parse(Read("manifest.json"))!.AsObject();
    Assert.Equal("${modName}", (string?)merged["name"]);
    Assert.Equal("${modVersion}", (string?)merged["version"]);
    Assert.Equal(
        ["name", "version", "refs", "formatVersion", "formatUuid", "artifacts"],
        merged.Select(p => p.Key));
    Assert.Contains(notes, n => n.Contains("already has \"name\": \"${modName}\""));
    Assert.Contains(notes, n => n.Contains("already has \"version\": \"${modVersion}\""));
    Assert.DoesNotContain(notes, n => n.Contains("already has \"refs\""));
    Assert.Contains(notes, n => n.StartsWith("The result is not a valid manifest.json yet",
        StringComparison.Ordinal));
    Assert.False(_fs.File.Exists(Path("tooth.json")));
  }

  [Fact]
  public async Task Migrate_InvalidExistingManifest_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", _concreteTooth);
    AddFile("manifest.json", "{ \"name\": \"x\", // comment\n }");

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => ToothRepositoryMigrator.Migrate(_fs, _root));

    // Assert
    Assert.Contains("cannot be merged into", ex.Message);
    Assert.Equal(["manifest.json", "tooth.json"], Files());
    Assert.Equal("{ \"name\": \"x\", // comment\n }", Read("manifest.json"));
  }

  [Fact]
  public async Task Migrate_LeftoverTempFile_IsReplacedAndRemoved() {
    // Arrange
    AddFile("tooth.json", _concreteTooth);
    AddFile(ToothRepositoryMigrator.TempFileName, "garbage");

    // Act
    await ToothRepositoryMigrator.Migrate(_fs, _root);

    // Assert
    Assert.Equal(["manifest.json"], Files());
  }

  [Fact]
  public void Merge_CrlfTabsAndBom_ArePreserved() {
    // Arrange
    byte[] existing = [
      .. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("{\r\n\t\"type\": \"native\"\r\n}"),
    ];
    JsonObject converted = new() { ["type"] = "native", ["refs"] = new JsonArray("a.com/b") };
    List<string> notes = [];

    // Act
    byte[] merged = ToothRepositoryMigrator.Merge(existing, converted, notes);

    // Assert
    Assert.True(merged.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    Assert.Equal(
        "{\r\n\t\"type\": \"native\",\r\n\t\"refs\": [\r\n\t\t\"a.com/b\"\r\n\t]\r\n}",
        Encoding.UTF8.GetString(merged[Encoding.UTF8.Preamble.Length..]));
    Assert.Empty(notes);
  }

  [Fact]
  public void Merge_EmptyOneLineObject_GetsKeysOnTheirOwnLines() {
    // Arrange
    byte[] existing = Encoding.UTF8.GetBytes("{}");
    JsonObject converted = new() { ["name"] = "a", ["version"] = "1.0.0" };

    // Act
    byte[] merged = ToothRepositoryMigrator.Merge(existing, converted, []);

    // Assert
    Assert.Equal(
        "{\n  \"name\": \"a\",\n  \"version\": \"1.0.0\"\n}",
        Encoding.UTF8.GetString(merged));
  }

  [Fact]
  public void Merge_NestedKeyIndentedFirst_UsesTheRootKeyIndentation() {
    // Arrange
    byte[] existing = Encoding.UTF8.GetBytes("{\n  \"a\": {\n        \"b\": 1\n  }\n}");
    JsonObject converted = new() { ["c"] = 2 };

    // Act
    byte[] merged = ToothRepositoryMigrator.Merge(existing, converted, []);

    // Assert
    Assert.Equal(
        "{\n  \"a\": {\n        \"b\": 1\n  },\n  \"c\": 2\n}",
        Encoding.UTF8.GetString(merged));
  }

  [Fact]
  public void Merge_NothingToAdd_ReturnsTheFileUnchanged() {
    // Arrange
    byte[] existing = Encoding.UTF8.GetBytes("{ \"name\":\"a\" }");
    JsonObject converted = new() { ["name"] = "b" };
    List<string> notes = [];

    // Act
    byte[] merged = ToothRepositoryMigrator.Merge(existing, converted, notes);

    // Assert
    Assert.Same(existing, merged);
    Assert.Equal(
        ["manifest.json already has \"name\": \"a\"; it was kept instead of the converted \"b\"."],
        notes);
  }

  private string Path(string name) => _fs.Path.Join(_root, name);

  private void AddFile(string name, string content) =>
      _fs.AddFile(Path(name), new MockFileData(content));

  private string Read(string name) => _fs.File.ReadAllText(Path(name));

  private string[] Files() =>
      [.. _fs.Directory.GetFiles(_root).Select(f => _fs.Path.GetFileName(f)).Order(StringComparer.Ordinal)];
}
