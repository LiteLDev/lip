using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

/// lip migrate on a package repository (protocol/compatibility.md §5).
public class RepositoryMigratorTests {
  private const string _uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

  private readonly MockFileSystem _fs = new();

  public RepositoryMigratorTests() {
    _fs.AddDirectory(_root);
  }

  /// A format 3 tooth.json with a single variant for win-x64.
  private static readonly string _packageTooth = $$"""
      {
        "format_version": 3,
        "format_uuid": "{{_uuid3}}",
        "tooth": "github.com/test/tool",
        "version": "1.0.0",
        "variants": [
          { "platform": "win-x64", "assets": [ { "type": "self", "placements": [
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
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("There is no tooth.json", ex.Message);
    Assert.Equal(["manifest.json"], Files());
    Assert.Equal("{ \"name\": \"x\" }", Read("manifest.json"));
  }

  [Fact]
  public async Task Migrate_PackageFileExists_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    AddFile("lip.yaml", "mine");

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("lip.yaml already exists", ex.Message);
    Assert.Equal(["lip.yaml", "tooth.json"], Files());
    Assert.Equal("mine", Read("lip.yaml"));
    Assert.Equal(_packageTooth, Read("tooth.json"));
  }

  [Fact]
  public async Task Migrate_ToothJsonOfFormat4_ThrowsAndChangesNothing() {
    // Arrange: format_version 4 was never released, and is not read.
    string tooth = """
        { "format_version": 4, "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
          "tooth": "github.com/test/tool", "version": "1.0.0", "variants": {} }
        """;
    AddFile("tooth.json", tooth);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("Cannot read tooth.json", ex.Message);
    Assert.Equal(tooth, Read("tooth.json"));
    Assert.Equal(["tooth.json"], Files());
  }

  [Theory]
  [InlineData("{ \"format_version\": 9 }")]
  [InlineData("{ \"format_version\": \"3\" }")]
  [InlineData("[]")]
  public async Task Migrate_NoKnownFormatVersion_ThrowsAndChangesNothing(string tooth) {
    // Arrange
    AddFile("tooth.json", tooth);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("Cannot read tooth.json", ex.Message);
    Assert.Equal(tooth, Read("tooth.json"));
    Assert.Equal(["tooth.json"], Files());
  }

  [Theory]
  [InlineData("{ \"format_version\": 3, }")]
  [InlineData("// c\n{ \"format_version\": 3 }")]
  [InlineData("{ \"format_version\": 3, \"format_version\": 3 }")]
  [InlineData("not json")]
  public async Task Migrate_NotStrictJson_ThrowsAndChangesNothing(string tooth) {
    // Arrange
    AddFile("tooth.json", tooth);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("Cannot read tooth.json", ex.Message);
    Assert.Equal(tooth, Read("tooth.json"));
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_InvalidFormat3_ThrowsAndChangesNothing() {
    // Arrange
    string tooth = _packageTooth.Replace("\"1.0.0\"", "\"one\"");
    AddFile("tooth.json", tooth);

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("Cannot read tooth.json", ex.Message);
    Assert.Contains("tooth.json of format 3 does not follow that format", ex.InnerException!.Message);
    Assert.Equal(tooth, Read("tooth.json"));
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_Format3_WritesPackageFileAndDeletesToothJson() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    AddFile("tool.exe", "exe");

    // Act
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Empty(notes);
    Assert.Equal(["lip.yaml", "tool.exe"], Files());
    Assert.Equal("exe", Read("tool.exe"));
    string text = Read("lip.yaml");
    Assert.StartsWith("format: 4\nid: github.com/test/tool\nversion: 1.0.0\n", text);
    Assert.EndsWith("\n", text);
    Assert.DoesNotContain("\r", text);
    Assert.DoesNotContain("\t", text);
    PackageFile packageFile = ReadPackageFile();
    Assert.Equal("github.com/test/tool", packageFile.Id);
    Assert.Equal(
        [(McmodPlatform.Server, "win-x64")],
        packageFile.Variants[""].Select(e => (e.Platform, e.Runtime)));
    Assert.All(
        packageFile.Variants[""],
        e => Assert.Equal(
            [new EntryPlacement("tool.exe", "tool.exe")], Assert.Single(e.Assets).Placements));
  }

  [Fact]
  public async Task Migrate_Placements_AreConvertedWhateverTheFilesOfTheRepository() {
    // Arrange: as protocol/compatibility.md §2 converts them, whether they match files or not.
    AddFile("tooth.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "variants": [
            { "platform": "win-x64", "assets": [ { "type": "self", "placements": [
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
    Assert.Equal(
        [
          new EntryPlacement("tool", "plugins/tool"),
          new EntryPlacement("missing/", "plugins/missing/"),
          new EntryPlacement("tool/", "plugins/tool/"),
        ],
        ReadPackageFile().Variants[""][0].Assets[0].Placements);
    Assert.DoesNotContain(notes, n => n.Contains("placement"));
  }

  [Fact]
  public async Task Migrate_PatternPlacement_BecomesGlobForm() {
    // Arrange
    AddFile("tooth.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "variants": [
            { "platform": "win-x64", "assets": [ { "type": "self", "placements": [
              { "type": "file", "src": "bin/**/*.dll", "dest": "plugins/tool" },
              { "type": "file", "src": "lib/*.dll", "dest": "plugins/lib" } ] } ] }
          ]
        }
        """);
    AddFile("bin/a.dll", "a");
    AddFile("bin/x/b.dll", "b");
    AddFile("lib/c.dll", "c");
    AddFile(".git/objects/d.dll", "d");

    // Act
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Equal(
        [
          new EntryPlacement("bin/**/*.dll", "plugins/tool/"),
          new EntryPlacement("lib/*.dll", "plugins/lib/"),
        ],
        ReadPackageFile().Variants[""][0].Assets[0].Placements);
    Assert.DoesNotContain(notes, n => n.Contains("placement"));
  }

  [Fact]
  public async Task Migrate_OtherFiles_AreNotTouched() {
    // Arrange: only lip.yaml is written and tooth.json deleted (protocol/compatibility.md §5).
    AddFile("tooth.json", _packageTooth);
    AddFile(".tooth.json.tmp", "mine");

    // Act
    await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Equal([".tooth.json.tmp", "lip.yaml"], Files());
    Assert.Equal("mine", Read(".tooth.json.tmp"));
    Assert.StartsWith("format: 4\n", Read("lip.yaml"));
  }

  [Fact]
  public async Task Migrate_RenameFails_ThrowsAndChangesNothing() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    FaultyFileSystem fs = new(_fs) { Fail = (operation, _) => operation == "move" };

    // Act
    await Assert.ThrowsAsync<IOException>(() => RepositoryMigrator.Migrate(fs, _root, new FakeUi()));

    // Assert
    Assert.Equal(["tooth.json"], Files());
    Assert.Equal(_packageTooth, Read("tooth.json"));
  }

  [Fact]
  public async Task Migrate_ReadOnlyToothJson_IsDeleted() {
    // Arrange
    AddFile("tooth.json", _packageTooth);
    _fs.File.SetAttributes(Path("tooth.json"), FileAttributes.ReadOnly);

    // Act
    await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Equal(["lip.yaml"], Files());
  }

  [Fact]
  public async Task Migrate_ToothJsonWithByteOrderMark_IsReadAsLipReadsIt() {
    // Arrange: a byte order mark is ignored, as when lip reads a tooth.json (ToothCompat).
    byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(_packageTooth)];
    _fs.AddFile(Path("tooth.json"), new MockFileData(bytes));

    // Act
    await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.StartsWith("format: 4\n", Read("lip.yaml"));
  }

  [Fact]
  public async Task Migrate_ToothJsonNotUtf8_ThrowsAndChangesNothing() {
    // Arrange
    byte[] bytes = [.. Encoding.UTF8.GetBytes(_packageTooth), 0xFF];
    _fs.AddFile(Path("tooth.json"), new MockFileData(bytes));

    // Act
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));

    // Assert
    Assert.Contains("Cannot read tooth.json", ex.Message);
    Assert.Equal(bytes, _fs.File.ReadAllBytes(Path("tooth.json")));
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_QuestionWithoutTerminal_ThrowsAndChangesNothing() {
    // Arrange: the variant without a platform needs its runtimes asked for.
    string tooth = _packageTooth.Replace("\"platform\": \"win-x64\", ", "");
    AddFile("tooth.json", tooth);

    // Act & Assert
    await Assert.ThrowsAsync<CannotAskException>(
        () => RepositoryMigrator.Migrate(_fs, _root, new FakeUi()));
    Assert.Equal(tooth, Read("tooth.json"));
    Assert.Equal(["tooth.json"], Files());
  }

  [Fact]
  public async Task Migrate_AnsweredQuestions_WritesEntriesForTheRuntimes() {
    // Arrange
    AddFile("tooth.json", _packageTooth.Replace("\"platform\": \"win-x64\", ", ""));
    AddFile("tool.exe", "exe");
    FakeUi ui = new("win-x64, linux-x64");

    // Act
    await RepositoryMigrator.Migrate(_fs, _root, ui);

    // Assert
    Assert.Single(ui.Questions);
    Assert.Equal(
        [(McmodPlatform.Server, "win-x64"), (McmodPlatform.Server, "linux-x64")],
        ReadPackageFile().Variants[""].Select(e => (e.Platform, e.Runtime)));
  }

  [Fact]
  public async Task Migrate_DroppedScriptsAndConvertedPaths_AreNotedAndValid() {
    // Arrange: a script other than the install and uninstall ones is dropped, and a placement
    // src with an empty segment loses it.
    AddFile("tooth.json", $$"""
        {
          "format_version": 3,
          "format_uuid": "{{_uuid3}}",
          "tooth": "github.com/test/tool",
          "version": "1.0.0",
          "variants": [ { "platform": "win-x64", "scripts": { "build": ["make"] },
            "assets": [ { "type": "zip", "urls": ["https://example.com/a.zip"], "placements": [
              { "type": "file", "src": "a//b.dll", "dest": "b.dll" } ] } ] } ]
        }
        """);

    // Act
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());

    // Assert
    Assert.Contains(notes, n => n.StartsWith("variants[0].scripts.build:"));
    Assert.StartsWith("format: 4\n", Read("lip.yaml"));
    Assert.Equal(
        [new EntryPlacement("a/b.dll", "b.dll")],
        ReadPackageFile().Variants[""][0].Assets[0].Placements);
  }

  private PackageFile ReadPackageFile() =>
      PackageFileParser.Parse(_fs.File.ReadAllBytes(Path("lip.yaml")));

  private string Path(string name) => _fs.Path.Join(_root, name);

  private void AddFile(string name, string content) =>
      _fs.AddFile(Path(name), new MockFileData(content));

  private string Read(string name) => _fs.File.ReadAllText(Path(name));

  private string[] Files() =>
      [.. _fs.Directory.GetFiles(_root).Select(f => _fs.Path.GetFileName(f)).Order(StringComparer.Ordinal)];
}
