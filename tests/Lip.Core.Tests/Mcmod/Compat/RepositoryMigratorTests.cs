using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

public class RepositoryMigratorTests {
  private const string Uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

  private readonly MockFileSystem _fs = new();

  public RepositoryMigratorTests() {
    _fs.AddDirectory(_root);
  }

  /// A format 3 tooth.json read as a package document.
  private static readonly string _packageTooth = $$"""
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
  public async Task Migrate_NeitherFile_ThrowsAndChangesNothing() {
    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("There is neither tooth.json nor manifest.json", ex.Message);
    Assert.Empty(Files());
  }

  [Fact]
  public async Task Migrate_BothFiles_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    AddFile("manifest.json", "{ \"name\": \"x\" }");

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("Both tooth.json and manifest.json exist", ex.Message);
    Assert.Equal("{ \"name\": \"x\" }", Read("manifest.json"));
    Assert.Equal(["manifest.json", "tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_InvalidToothJson_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", "{ \"format_version\": 9 }");

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("is not a valid tooth.json", ex.Message);
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_ToothJson_WritesConvertedDocumentAndDeletesTooth() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    AddFile("tool.exe", "exe");

    // Act
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Equal(["manifest.json", "tool.exe"], Files());
    PackageDocument document = Assert.IsType<PackageDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.Equal("tool", document.Id);
    Assert.Equal([new PackageFile(null, "tool.exe", "tool.exe")], document.Files);
    Assert.StartsWith("{\n  \"format\": \"bf45b1\",\n", Read("manifest.json"));
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
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    PackageDocument document = Assert.IsType<PackageDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.Equal([new PackageFile(null, "tool/", "plugins/tool/")], document.Files);
    Assert.Contains(notes, n => n.Contains("'tool' -> 'plugins/tool' names a directory"));
    Assert.Contains(notes, n => n.Contains("'missing' -> 'plugins/missing' matches no file"));
  }

  [Fact]
  public async Task Migrate_PatternPlacementThroughSubdirectories_IsExpandedIntoFiles() {
    // Arrange: tooth.json placed the files by name; a glob form keeps their subdirectories.
    AddFile("tooth.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "{{Uuid3}}",
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "variants": [
            { "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "bin/**/*.dll", "dest": "plugins/tool" },
              { "type": "file", "src": "lib/*.dll", "dest": "plugins/lib" } ] } ] }
          ]
        }
        """);
    AddFile("bin/a.dll", "a");
    AddFile("bin/x/b.dll", "b");
    AddFile("lib/c.dll", "c");

    // Act
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    PackageDocument document = Assert.IsType<PackageDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.Equal(
        [
          new PackageFile(null, "bin/a.dll", "plugins/tool/a.dll"),
          new PackageFile(null, "bin/x/b.dll", "plugins/tool/b.dll"),
          new PackageFile(null, "lib/*.dll", "plugins/lib/"),
        ],
        document.Files);
    Assert.Single(notes, n => n.Contains("expanded into 2 files"));
  }

  [Fact]
  public async Task Migrate_ReadOnlyToothJson_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    _fs.File.SetAttributes(Path("tooth.json"), FileAttributes.ReadOnly);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("read-only", ex.Message);
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_LeftoverTempFile_IsReplacedAndRemoved() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    AddFile(RepositoryMigrator.TempFileName, "garbage");

    // Act
    await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Equal(["manifest.json"], Files());
  }

  [Fact]
  public async Task Migrate_LeviLaminaManifest_IsRewritten() {
    // Arrange
    AddFile("manifest.json", """
        { "name": "MyMod", "entry": "MyMod.dll", "version": "1.0.0", "type": "native" }
        """);

    // Act
    await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    PackageDocument document = Assert.IsType<PackageDocument>(
        new DocumentParser().Parse(_fs.File.ReadAllBytes(Path("manifest.json"))));
    Assert.Equal("MyMod", document.Id);
    Assert.True(document.IsMod);
    Assert.Equal(["manifest.json"], Files());
  }

  [Fact]
  public async Task Migrate_ManifestAlreadyMigrated_ThrowsAndChangesNothing() {
    // Arrange
    const string manifest = """{ "format": "bf45b1", "id": "m", "version": "1.0.0" }""";
    AddFile("manifest.json", manifest);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("nothing to migrate", ex.Message);
    Assert.Equal(manifest, Read("manifest.json"));
  }

  [Fact]
  public async Task Migrate_QuestionWithoutTerminal_ThrowsAndChangesNothing() {
    // Arrange
    const string manifest = """{ "name": "My Mod", "version": "1.0.0", "type": "native" }""";
    AddFile("manifest.json", manifest);

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));
    Assert.Equal(manifest, Read("manifest.json"));
  }

  private string Path(string name) => _fs.Path.Join(_root, name);

  private void AddFile(string name, string content) =>
      _fs.AddFile(Path(name), new MockFileData(content));

  private string Read(string name) => _fs.File.ReadAllText(Path(name));

  private string[] Files() =>
      [.. _fs.Directory.GetFiles(_root).Select(f => _fs.Path.GetFileName(f)).Order(StringComparer.Ordinal)];
}
