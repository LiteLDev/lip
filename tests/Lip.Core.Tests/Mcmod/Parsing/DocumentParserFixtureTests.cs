using System.Text;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod.Parsing;

/// Parses the spec appendix examples and real LeviLamina manifests (Fixtures/), and checks that
/// every invalid-document rule is enforced.
public class DocumentParserFixtureTests {
  private const string Format = "\"format\": \"bf45b1\"";

  private const string Package = $"{Format}, \"id\": \"m\", \"version\": \"1.0.0\"";

  private const string Mod = "\"mod\": { \"loader\": \"native\", \"entry\": \"m.dll\" }";

  private readonly DocumentParser _parser = new(FakeVersionRange.Parse);

  private ManifestDocument ParseFixture(string fileName) => _parser.Parse(File.ReadAllBytes(
      Path.Combine(AppContext.BaseDirectory, "Mcmod", "Parsing", "Fixtures", fileName)));

  [Fact]
  public void Parse_SpecLegacyMoneyMeta_ReadsDocument() {
    // Act
    MetaDocument doc = Assert.IsType<MetaDocument>(ParseFixture("spec-legacymoney-meta.json"));

    // Assert
    Assert.Equal("LegacyMoney", doc.Id);
    Assert.Equal("0.22.0", doc.Version!.ToString());
    MetaTarget target = Assert.Single(doc.Targets);
    Assert.Equal("", target.Variant);
    Assert.Equal([McmodPlatform.Server, McmodPlatform.Client], target.Platforms);
    Assert.Equal(["win-x64"], target.Runtimes);
    Assert.Equal(
        ["https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/"
            + "{{id}}-{{platform}}-{{runtime}}.mcmod"],
        target.Source);
    Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-client-win-x64.mcmod",
        RefTemplate.Expand(target.Source[0], target, new(McmodPlatform.Client, "win-x64")).Target);
  }

  [Fact]
  public void Parse_SpecLegacyMoneyPackage_ReadsDocument() {
    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(
        ParseFixture("spec-legacymoney-package.json"));

    // Assert
    Assert.Equal("LegacyMoney", doc.Id);
    Assert.True(doc.IsMod);
    Assert.Equal("0.22.0", doc.Version.ToString());
    Assert.Equal(McmodPlatform.Server, doc.Platform);
    Assert.Equal("win-x64", doc.Runtime);
    Assert.Equal(["LeviLamina", "iListenAttentively"], doc.Dependencies.Select(d => d.Id));
    Assert.Equal(
        ["github.com/LiteLDev/LeviLamina"],
        doc.Dependencies[0].Refs!.Select(r => r.ToString()));
    Assert.Equal(
        ["config/", "data/"],
        doc.Preserve.Select(g => g.Pattern));
    Assert.Equal(2, new McmodPackage(doc, default).InstallEdges.Count);
  }

  [Fact]
  public void Parse_SpecBdsFiles_ReadsDocument() {
    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(ParseFixture("spec-bds-files.json"));

    // Assert
    Assert.True(doc.IsFilePackage);
    PackageArchive archive = Assert.Single(doc.Archives);
    Assert.Equal("bds", archive.Name);
    Assert.Equal(PackageArchiveFormat.Detect, archive.Format);
    Assert.Equal(
        [
          new PackageFile("bds", "", ""),
          new PackageFile(null, "defaults/*.json", ""),
        ],
        doc.Files);
    Assert.Equal(PackageFileForm.Directory, doc.Files[0].Form);
    Assert.Equal(PackageFileForm.Glob, doc.Files[1].Form);
  }

  [Fact]
  public void Parse_SpecVariantMeta_ReadsDocument() {
    // Act
    MetaDocument doc = Assert.IsType<MetaDocument>(ParseFixture("spec-variant-meta.json"));

    // Assert
    Assert.Null(doc.Id);
    Assert.Equal(["quickjs", "lua"], doc.Targets.Select(t => t.Variant));
    Assert.Equal(["./quickjs/"], doc.Targets[0].Source);
  }

  [Theory]
  [InlineData("levilamina-legacymoney-0.22.0.json", "LegacyMoney", "0.22.0", "server")]
  [InlineData(
      "levilamina-legacy-script-engine-quickjs-0.22.3.json",
      "legacy-script-engine-quickjs", "0.22.3", "server")]
  [InlineData("levilamina-levilamina-26.51.8.json", "LeviLamina", "26.51.8", null)]
  [InlineData("levilamina-mimalloc.json", "mimalloc", "0.0.0", null)]
  public void Parse_RealLeviLaminaManifest_ReadsModWithoutInstallEdges(
      string fileName,
      string id,
      string version,
      string? platform) {
    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(ParseFixture(fileName));

    // Assert
    Assert.Equal(id, doc.Id);
    Assert.Equal(version, doc.Version.ToString());
    Assert.True(doc.IsMod);
    Assert.Equal(platform is null ? null : McmodPlatform.Server, doc.Platform);
    Assert.Empty(new McmodPackage(doc, default).InstallEdges);
  }

  public static TheoryData<string, string> InvalidDocuments => new() {
    // §1: strict JSON, top-level object.
    { "", "not strict JSON" },
    { "[]", "$: the top level must be an object" },
    { "\"x\"", "$: the top level must be an object" },
    { $$"""{ {{Package}} /* c */ }""", "not strict JSON" },
    { $$"""{ {{Package}} // c""" + "\n}", "not strict JSON" },
    { $$"""{ {{Package}}, }""", "not strict JSON" },
    { $$"""{ {{Package}}, "id": "n" }""", "not strict JSON" },
    { $$"""{ {{Package}} } {}""", "not strict JSON" },
    { $$"""{ {{Package}}, "x": 'a' }""", "not strict JSON" },
    // §4: the format marker.
    { """{ "format": "bf45b2", "id": "m", "version": "1.0.0" }""", "$.format: must be \"bf45b1\"" },
    { """{ "format": 1, "id": "m", "version": "1.0.0" }""", "$.format: must be \"bf45b1\"" },
    // §4.1: package documents.
    { $$"""{ {{Format}}, "version": "1.0.0" }""", "$.id: is required" },
    { $$"""{ {{Format}}, "id": "m" }""", "$.version: is required" },
    { $$"""{ {{Format}}, "id": "-m", "version": "1.0.0" }""", "$.id: '-m' does not match" },
    { $$"""{ {{Format}}, "id": "m", "version": "1.0" }""", "$.version: '1.0' is not a SemVer" },
    { $$"""{ {{Package}}, "platform": "universal" }""", "$.platform: must be \"server\" or \"client\"" },
    { $$"""{ {{Package}}, "runtime": 1 }""", "$.runtime: must be a string" },
    { $$"""{ {{Package}}, "info": { "authors": "a" } }""", "$.info.authors: must be an array" },
    { $$"""{ {{Package}}, "info": { "icon": "http://x" } }""", "$.info.icon: 'http://x' is not an https URL" },
    { $$"""{ {{Package}}, "dependencies": [] }""", "$.dependencies: must be an object" },
    { $$"""{ {{Package}}, "dependencies": { "-a": "*" } }""", "$.dependencies[\"-a\"]: '-a' does not match" },
    { $$"""{ {{Package}}, "dependencies": { "a": 1 } }""", "$.dependencies[\"a\"]: must be a range or an object" },
    { $$"""{ {{Package}}, "dependencies": { "a": "invalid" } }""", "$.dependencies[\"a\"]: invalid version range" },
    { $$"""{ {{Package}}, "dependencies": { "a": { "source": [] } } }""", "$.dependencies[\"a\"].source: must not be empty" },
    { $$"""{ {{Package}}, "dependencies": { "a": { "source": "http://x/a.mcmod" } } }""", "$.dependencies[\"a\"].source: invalid ref" },
    { $$"""{ {{Package}}, "dependencies": { "a": { "mod": false } } }""", "$.dependencies[\"a\"].mod: false is only allowed together with \"source\"" },
    { $$"""{ {{Package}}, "dependencies": { "a": { "mod": 0 } } }""", "$.dependencies[\"a\"].mod: must be a boolean" },
    { $$"""{ {{Package}}, "conflicts": { "a": {} } }""", "$.conflicts[\"a\"]: must be a string" },
    { $$"""{ {{Package}}, "mod": {} }""", "$.mod.loader: is required" },
    { $$"""{ {{Package}}, "mod": { "loader": "", "entry": "m.dll" } }""", "$.mod.loader: must not be empty" },
    { $$"""{ {{Package}}, "mod": { "loader": "native", "entry": "m.dll", "loadAfter": ["_x"] } }""", "$.mod.loadAfter[0]: '_x' is neither an id nor \"*\"" },
    { $$"""{ {{Package}}, "mod": { "loader": "native", "entry": "m.dll", "passive": "yes" } }""", "$.mod.passive: must be a boolean" },
    { $$"""{ {{Package}}, "mod": { "loader": "native", "entry": "m.dll", "extra": [] } }""", "$.mod.extra: must be an object" },
    { $$"""{ {{Package}}, {{Mod}}, "files": [] }""", "$.files: is not allowed together with \"mod\"" },
    { $$"""{ {{Package}}, {{Mod}}, "archives": {} }""", "$.archives: is not allowed together with \"mod\"" },
    { $$"""{ {{Package}}, "files": [ { "src": "a" } ] }""", "$.files[0].dest: is required" },
    { $$"""{ {{Package}}, "files": [ { "src": "/a", "dest": "a" } ] }""", "$.files[0].src: '/a' must not start with '/'" },
    { $$"""{ {{Package}}, "files": [ { "src": "a\\b", "dest": "a" } ] }""", "must not contain '\\'" },
    { $$"""{ {{Package}}, "files": [ { "src": "a", "dest": "C:a" } ] }""", "must not start with a drive letter" },
    { $$"""{ {{Package}}, "files": [ { "src": "a/../b", "dest": "a" } ] }""", "must not contain a '..' segment" },
    { $$"""{ {{Package}}, "files": [ { "src": "a//b", "dest": "a" } ] }""", "must not contain an empty segment" },
    { $$"""{ {{Package}}, "files": [ { "src": "a", "dest": "d/" } ] }""", "$.files[0].dest: 'd/' must name a file" },
    { $$"""{ {{Package}}, "files": [ { "src": "d/", "dest": "d" } ] }""", "$.files[0].dest: 'd' must be \"\" or end with '/' for a directory src" },
    { $$"""{ {{Package}}, "files": [ { "src": "*.txt", "dest": "d" } ] }""", "$.files[0].dest: 'd' must be \"\" or end with '/' for a glob src" },
    { $$"""{ {{Package}}, "files": [ { "src": "[a", "dest": "d/" } ] }""", "$.files[0].src: invalid glob" },
    { $$"""{ {{Package}}, "files": [ { "from": "x", "src": "a", "dest": "a" } ] }""", "$.files[0].from: 'x' is not a key of \"archives\"" },
    { $$"""{ {{Package}}, "archives": { "a": {} } }""", "$.archives[\"a\"].url: is required" },
    { $$"""{ {{Package}}, "archives": { "a": { "url": "http://x" } } }""", "$.archives[\"a\"].url: 'http://x' is not an https URL" },
    { $$"""{ {{Package}}, "archives": { "a": { "url": "https://x", "sha256": "ab" } } }""", "$.archives[\"a\"].sha256: must be 64 hexadecimal digits" },
    { $$"""{ {{Package}}, "archives": { "a": { "url": "https://x", "format": "rar" } } }""", "$.archives[\"a\"].format: must be" },
    { $$"""{ {{Package}}, "preserve": ["/a"] }""", "$.preserve[0]: invalid glob '/a'" },
    { $$"""{ {{Package}}, "remove": ["[^a]"] }""", "$.remove[0]: invalid glob '[^a]'" },
    { $$"""{ {{Package}}, "hooks": { "preInstall": "x" } }""", "$.hooks.preInstall: must be an array" },
    // §4.2: meta documents.
    { $$"""{ {{Format}}, "targets": [] }""", "$.targets: must not be empty" },
    { $$"""{ {{Format}}, "targets": [ {} ] }""", "$.targets[0].source: is required" },
    { $$"""{ {{Format}}, "targets": [ { "source": [] } ] }""", "$.targets[0].source: must not be empty" },
    { $$"""{ {{Format}}, "targets": [ { "variant": "A", "source": "./a/" } ] }""", "$.targets[0].variant: 'A' is not a valid variant" },
    { $$"""{ {{Format}}, "targets": [ { "platform": "universal", "source": "./a/" } ] }""", "$.targets[0].platform: must be" },
    { $$"""{ {{Format}}, "targets": [ { "runtime": [], "source": "./a/" } ] }""", "$.targets[0].runtime: must not be empty" },
    { """{ "format": "bf45b1", "targets": [ { "source": "./{{name}}/" } ] }""", "unknown placeholder {{name}}" },
    { $$"""{ {{Format}}, "targets": [ { "source": "http://x/a.mcmod" } ] }""", "$.targets[0].source: invalid ref" },
    { $$"""{ {{Format}}, "id": "-x", "targets": [ { "source": "./a/" } ] }""", "$.id: '-x' does not match" },
    { $$"""{ {{Format}}, "targets": [ { "id": "-x", "source": "./a/" } ] }""", "$.targets[0].id: '-x' does not match" },
    { $$"""{ {{Format}}, "targets": [ { "source": "./a/" }, { "source": "./b/" } ] }""", "$.targets[1]: ambiguous with $.targets[0]" },
    { $$"""{ {{Format}}, "targets": [ { "platform": "server", "source": "./a/" }, { "platform": ["client", "server"], "source": "./b/" } ] }""", "$.targets[1]: ambiguous" },
    // §10.1: LeviLamina manifests.
    { """{ "version": "1.0.0" }""", "$.name: is required" },
    { """{ "name": "My Mod", "version": "1.0.0" }""", "$.name: 'My Mod' does not match" },
    { """{ "name": "m" }""", "$.version: is required" },
    { """{ "name": "m", "version": "1.0" }""", "$.version: '1.0' is not a SemVer" },
    { """{ "name": "m", "version": "1.0.0", "conflicts": [ {} ] }""", "$.conflicts[0].name: is required" },
    { """{ "name": "m", "version": "1.0.0", "conflicts": [ { "name": "a", "version": "invalid" } ] }""", "invalid version range" },
  };

  [Theory]
  [MemberData(nameof(InvalidDocuments))]
  public void Parse_InvalidDocument_ThrowsManifestFormatException(string json, string message) {
    // Act
    ManifestFormatException ex = Assert.Throws<ManifestFormatException>(
        () => _parser.Parse(Encoding.UTF8.GetBytes(json)));

    // Assert
    Assert.Contains(message, ex.Message);
  }
}
