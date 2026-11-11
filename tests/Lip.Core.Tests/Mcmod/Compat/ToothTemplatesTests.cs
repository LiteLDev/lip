using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;

namespace Lip.Core.Tests.Mcmod.Compat;

/// lip migrate keeps the templates of tooth.json (protocol/lip-compatibility.md, "lip migrate"),
/// and the lip.toml it writes reads back as the package file migrated.
public class ToothTemplatesTests {
  private const string _uuid3 = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

  private readonly MockFileSystem _fs = new();

  public ToothTemplatesTests() {
    _fs.AddDirectory(_root);
  }

  /// A format 3 tooth.json of version 1.2.3 with one variant for win-x64 that has these members
  /// besides label and platform.
  private static string Tooth(string members) => $$"""
      {
        "format_version": 3,
        "format_uuid": "{{_uuid3}}",
        "tooth": "github.com/test/tool",
        "version": "1.2.3",
        "info": { "name": "Tool", "description": "", "tags": [] },
        "variants": [ { "label": "", "platform": "win-x64", {{members}} } ]
      }
      """;

  private async Task<(string Text, PackageEntry Entry)> Migrate(string tooth) {
    _fs.AddFile(_fs.Path.Join(_root, "tooth.json"), new MockFileData(tooth));
    _ = await RepositoryMigrator.Migrate(_fs, _root, new FakeUi());
    string path = _fs.Path.Join(_root, "lip.toml");
    PackageFile file = PackageFileParser.Parse(_fs.File.ReadAllBytes(path));
    return (_fs.File.ReadAllText(path), file.Variants[""][0]);
  }

  [Fact]
  public async Task Migrate_VersionInUrl_KeepsTheTemplate() {
    // Act
    (string text, PackageEntry entry) = await Migrate(Tooth("""
        "assets": [ { "type": "zip",
          "urls": [ "https://example.com/v{{version}}/tool-{{ version }}.zip" ] } ]
        """));

    // Assert
    Assert.Contains("\"https://example.com/v{{version}}/tool-{{ version }}.zip\"", text);
    Assert.Equal("https://example.com/v1.2.3/tool-1.2.3.zip", Assert.Single(entry.Assets[0].Urls));
  }

  [Fact]
  public async Task Migrate_ToothVariable_BecomesId() {
    // Act
    (string text, PackageEntry entry) = await Migrate(Tooth("""
        "scripts": { "install": [ "echo {{tooth}} {{info.name}}" ] }
        """));

    // Assert
    Assert.Contains("\"echo {{id}} {{info.name}}\"", text);
    Assert.Equal("echo github.com/test/tool Tool", Assert.Single(entry.Hooks.PostInstall));
  }

  [Fact]
  public async Task Migrate_TemplatedPathsAndGlobs_KeepTheTemplateAfterConversion() {
    // Act
    (string text, PackageEntry entry) = await Migrate(Tooth("""
        "assets": [ { "type": "self", "placements": [
          { "type": "dir", "src": "out\\{{version}}", "dest": "./plugins/{{info.name}}" } ] } ],
        "preserve_files": [ "{{info.name}}.json" ]
        """));

    // Assert
    Assert.Contains("\"out/{{version}}/\"", text);
    Assert.Contains("\"plugins/{{info.name}}/\"", text);
    Assert.Contains("\"**/{{info.name}}.json\"", text);
    Assert.Equal(
        [new EntryPlacement("out/1.2.3/", "plugins/Tool/")], Assert.Single(entry.Assets).Placements);
    Assert.Equal("**/Tool.json", Assert.Single(entry.PreserveFiles).ToString());
  }

  [Fact]
  public async Task Migrate_TemplateRenderingOtherwiseInLipToml_IsWrittenAsItsValue() {
    // Act: format_version is 3 in tooth.json, and lip.toml has no such variable.
    (string text, PackageEntry entry) = await Migrate(Tooth("""
        "scripts": { "install": [ "echo {{format_version}}" ] }
        """));

    // Assert
    Assert.Contains("\"echo 3\"", text);
    Assert.Equal("echo 3", Assert.Single(entry.Hooks.PostInstall));
  }

  [Fact]
  public async Task Migrate_RenderedBraces_AreEscaped() {
    // Act
    (string text, PackageEntry entry) = await Migrate(Tooth("""
        "scripts": { "install": [ "echo {{ '{{' }}x}}" ] }
        """));

    // Assert
    Assert.DoesNotContain("\"echo {{x}}\"", text);
    Assert.Equal("echo {{x}}", Assert.Single(entry.Hooks.PostInstall));
  }

  [Fact]
  public async Task Migrate_Format2Variables_BecomeTemplates() {
    // Act
    (string text, PackageEntry entry) = await Migrate("""
        {
          "format_version": 2,
          "tooth": "github.com/test/tool",
          "version": "1.2.3",
          "info": { "name": "Tool", "description": "", "author": "", "tags": [] },
          "asset_url": "https://example.com/v$(version)/tool.zip",
          "files": { "place": [ { "src": "bin/*", "dest": "tools/$(version)/" } ] },
          "platforms": [ { "goos": "windows", "goarch": "amd64",
            "commands": { "post_install": [ "echo $(path)" ] } } ]
        }
        """);

    // Assert
    Assert.Contains("\"https://example.com/v{{version}}/tool.zip\"", text);
    Assert.Contains("\"tools/{{version}}/\"", text);
    Assert.Contains("\"echo {{id}}\"", text);
    Assert.Equal("https://example.com/v1.2.3/tool.zip", Assert.Single(entry.Assets[0].Urls));
    Assert.Equal("echo github.com/test/tool", Assert.Single(entry.Hooks.PostInstall));
  }
}
