using System.Text;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod.Parsing;

/// Parses the spec appendix examples and real LeviLamina manifests (Fixtures/), and checks that
/// every invalid-document rule is enforced.
public class DocumentParserFixtureTests {
  private const string Uuid = ManifestFormat.Uuid;

  private const string Markers =
      "\"formatVersion\": 4, \"formatUuid\": \"0c6b977a-64ad-441f-9a29-c7f4b196882f\"";

  private const string Concrete = "\"name\": \"m\", \"version\": \"1.0.0\", \"type\": \"native\"";

  private const string Files = "\"name\": \"m\", \"version\": \"1.0.0\", \"type\": \"files\"";

  private readonly DocumentParser _parser = new(FakeVersionRange.Parse);

  private ManifestDocument ParseFixture(string fileName) => _parser.Parse(File.ReadAllBytes(
      Path.Combine(AppContext.BaseDirectory, "Mcmod", "Parsing", "Fixtures", fileName)));

  [Fact]
  public void Parse_SpecLegacyMoneyRouting_ReadsDocument() {
    // Act
    RoutingDocument doc = Assert.IsType<RoutingDocument>(
        ParseFixture("spec-legacymoney-routing.json"));

    // Assert
    Assert.True(doc.IsMarked);
    Assert.Equal("LegacyMoney", doc.Name);
    Assert.Equal("0.22.0", doc.Version!.ToString());
    Artifact artifact = Assert.Single(doc.Artifacts);
    Assert.Equal("", artifact.Variant);
    Assert.Equal([McmodPlatform.Server, McmodPlatform.Client], artifact.Platforms);
    Assert.Equal(["win-x64"], artifact.Rids);
    Assert.Equal(
        ["https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/"
            + "{{name}}-{{platform}}-{{rid}}.mcmod"],
        artifact.Refs);
    Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-client-win-x64.mcmod",
        RefTemplate.Expand(artifact.Refs[0], doc, new(McmodPlatform.Client, "win-x64")).Target);
  }

  [Fact]
  public void Parse_SpecLegacyMoneyConcrete_ReadsDocument() {
    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(
        ParseFixture("spec-legacymoney-concrete.json"));

    // Assert
    Assert.True(doc.IsMarked);
    Assert.Equal("LegacyMoney", doc.Name);
    Assert.Equal("native", doc.Type);
    Assert.False(doc.IsFilePackage);
    Assert.Equal("0.22.0", doc.Version.ToString());
    Assert.Equal(PackagePlatform.Server, doc.Platform);
    Assert.Equal("win-x64", doc.Rid);
    Assert.Equal(RefKind.Spec, Assert.Single(doc.Refs).Kind);

    Dep dep = Assert.Single(doc.Dependencies);
    Assert.Equal("iListenAttentively", dep.Name);
    Assert.Equal(new FakeVersionRange("^0.17.0"), dep.Version);
    Assert.Equal("github.com/MiracleForest/iListenAttentively", Assert.Single(dep.Refs!).Target);

    InstallDep install = Assert.Single(doc.InstallDependencies);
    Assert.Null(install.Name);
    Assert.Equal(new FakeVersionRange(">=26.51.0 <26.52.0"), install.Version);
    Assert.Equal("github.com/LiteLDev/LeviLamina", Assert.Single(install.Refs).Target);
  }

  [Fact]
  public void Parse_SpecPeEditorFiles_ReadsDocument() {
    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(
        ParseFixture("spec-peeditor-files.json"));

    // Assert
    Assert.True(doc.IsFilePackage);
    Assert.Equal("PeEditor", doc.Name);
    Assert.Equal("3.9.0", doc.Version.ToString());
    Assert.Equal(PackagePlatform.Universal, doc.Platform);
    Assert.Equal("win-x64", doc.Rid);
    Placement placement = Assert.Single(doc.Placements);
    Assert.Equal(new Placement("PeEditor.exe", "PeEditor.exe"), placement);
    Assert.False(placement.IsDirectory);
  }

  [Fact]
  public void Parse_SpecVariantRouting_ReadsDocument() {
    // Act
    RoutingDocument doc = Assert.IsType<RoutingDocument>(
        ParseFixture("spec-variant-routing.json"));

    // Assert
    Assert.Null(doc.Name);
    Assert.Null(doc.Version);
    Assert.Equal(["quickjs", "lua"], doc.Artifacts.Select(a => a.Variant));
    Assert.All(doc.Artifacts, a => Assert.Equal(0, a.SelectorCount));
    Assert.Equal(["./quickjs/"], doc.Artifacts[0].Refs);
    Assert.Equal(["./lua/"], doc.Artifacts[1].Refs);
  }

  [Theory]
  [InlineData("levilamina-legacymoney-0.22.0.json", "LegacyMoney", "0.22.0", "native", "server")]
  [InlineData(
      "levilamina-legacyparticleapi-0.14.0.json", "LegacyParticleAPI", "0.14.0", "native", null)]
  [InlineData(
      "levilamina-legacy-script-engine-quickjs-0.22.3.json",
      "legacy-script-engine-quickjs", "0.22.3", "native", "server")]
  [InlineData(
      "levilamina-levilamina-26.51.8.json", "LeviLamina", "26.51.8", "preload-native", null)]
  [InlineData("levilamina-mimalloc.json", "mimalloc", "0.0.0", "preload-native", null)]
  public void Parse_RealLeviLaminaManifest_ReadsUnmarkedConcreteDocument(
      string fileName,
      string name,
      string version,
      string type,
      string? platform) {
    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(ParseFixture(fileName));

    // Assert
    Assert.False(doc.IsMarked);
    Assert.Equal(name, doc.Name);
    Assert.Equal(version, doc.Version.ToString());
    Assert.Equal(type, doc.Type);
    Assert.Equal(
        platform == "server" ? PackagePlatform.Server : PackagePlatform.Universal,
        doc.Platform);
    Assert.Empty(new McmodPackage(doc, default).InstallEdges);
  }

  [Fact]
  public void Parse_RealLeviLaminaManifestWithDependencies_ReadsDependencyNames() {
    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(
        ParseFixture("levilamina-legacy-script-engine-quickjs-0.22.3.json"));

    // Assert
    Assert.Equal(
        ["LegacyMoney", "LegacyRemoteCall", "iListenAttentively"],
        doc.Dependencies.Select(d => d.Name));
    Assert.All(doc.Dependencies, d => Assert.Null(d.Version));
  }

  public static TheoryData<string, string> InvalidDocuments => new() {
    // §1: strict JSON, top-level object.
    { "", "not strict JSON" },
    { "[]", "$: the top level must be an object" },
    { "\"x\"", "$: the top level must be an object" },
    { $$"""{ {{Concrete}} /* c */ }""", "not strict JSON" },
    { $$"""{ {{Concrete}} // c""" + "\n}", "not strict JSON" },
    { $$"""{ {{Concrete}}, }""", "not strict JSON" },
    { $$"""{ {{Concrete}}, "name": "n" }""", "not strict JSON" },
    { $$"""{ {{Concrete}} } {}""", "not strict JSON" },
    { $$"""{ {{Concrete}}, "x": 'a' }""", "not strict JSON" },
    // Markers.
    { $$"""{ "formatVersion": 3, "formatUuid": "{{Uuid}}", {{Concrete}} }""",
      "$.formatVersion: must be 4" },
    { $$"""{ "formatVersion": "4", "formatUuid": "{{Uuid}}", {{Concrete}} }""",
      "$.formatVersion: must be 4" },
    { $$"""{ "formatVersion": 4.5, "formatUuid": "{{Uuid}}", {{Concrete}} }""",
      "$.formatVersion: must be 4" },
    { $$"""{ "formatVersion": 4, "formatUuid": "289f771f-2c9a-4d73-9f3f-8492495a924d", """
        + Concrete + " }",
      "$.formatUuid: must be" },
    { $$"""{ "formatVersion": 4, "formatUuid": 4, {{Concrete}} }""", "$.formatUuid: must be" },
    { $$"""{ "formatVersion": 4, {{Concrete}} }""", "$.formatUuid: is required" },
    { $$"""{ "formatUuid": "{{Uuid}}", {{Concrete}} }""",
      "$.formatVersion: is required" },
    { $$"""{ "artifacts": [ { "refs": ["./a/"] } ] }""", "$.artifacts: a routing document must" },
    // §4.1 routing documents.
    { $$"""{ {{Markers}}, "artifacts": [] }""", "$.artifacts: must not be empty" },
    { $$"""{ {{Markers}}, "artifacts": {} }""", "$.artifacts: must be an array" },
    { $$"""{ {{Markers}}, "artifacts": [ "./a/" ] }""", "$.artifacts[0]: must be an object" },
    { $$"""{ {{Markers}}, "artifacts": [ { "variant": "Lua", "refs": ["./a/"] } ] }""",
      "$.artifacts[0].variant: 'Lua' is not a valid variant" },
    { $$"""{ {{Markers}}, "artifacts": [ { "variant": "a/b/c", "refs": ["./a/"] } ] }""",
      "$.artifacts[0].variant" },
    { $$"""{ {{Markers}}, "artifacts": [ { "variant": 1, "refs": ["./a/"] } ] }""",
      "$.artifacts[0].variant: must be a string" },
    { $$"""{ {{Markers}}, "artifacts": [ { "platform": "universal", "refs": ["./a/"] } ] }""",
      "$.artifacts[0].platform: must be" },
    { $$"""{ {{Markers}}, "artifacts": [ { "platform": [], "refs": ["./a/"] } ] }""",
      "$.artifacts[0].platform: must not be empty" },
    { $$"""{ {{Markers}}, "artifacts": [ { "platform": ["server", "x"], "refs": ["./a/"] } ] }""",
      "$.artifacts[0].platform[1]: must be" },
    { $$"""{ {{Markers}}, "artifacts": [ { "platform": null, "refs": ["./a/"] } ] }""",
      "$.artifacts[0].platform: must be" },
    { $$"""{ {{Markers}}, "artifacts": [ { "rid": 5, "refs": ["./a/"] } ] }""",
      "$.artifacts[0].rid: must be a string or a non-empty array" },
    { $$"""{ {{Markers}}, "artifacts": [ { "rid": [], "refs": ["./a/"] } ] }""",
      "$.artifacts[0].rid: must not be empty" },
    { $$"""{ {{Markers}}, "artifacts": [ { "rid": ["a", 1], "refs": ["./a/"] } ] }""",
      "$.artifacts[0].rid[1]: must be a string" },
    { $$"""{ {{Markers}}, "artifacts": [ { "platform": "server" } ] }""",
      "$.artifacts[0].refs: is required" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": [] } ] }""",
      "$.artifacts[0].refs: must not be empty" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": "./a/" } ] }""",
      "$.artifacts[0].refs: must be an array" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": [5] } ] }""",
      "$.artifacts[0].refs[0]: must be a string" },
    { $$$"""{ {{{Markers}}}, "artifacts": [ { "refs": ["./{{os}}/"] } ] }""",
      "$.artifacts[0].refs[0]: unknown placeholder {{os}}" },
    { $$$"""{ {{{Markers}}}, "artifacts": [ { "refs": ["github.com/a/b#{{rid}"] } ] }""",
      "$.artifacts[0].refs[0]: invalid ref" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": ["http://example.com/a.zip"] } ] }""",
      "$.artifacts[0].refs[0]: invalid ref" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": ["./a/../b/"] } ] }""",
      "$.artifacts[0].refs[0]: invalid ref" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": ["./a/#"] } ] }""",
      "$.artifacts[0].refs[0]: invalid ref" },
    { $$"""{ {{Markers}}, "name": 5, "artifacts": [ { "refs": ["./a/"] } ] }""",
      "$.name: must be a string" },
    { $$"""{ {{Markers}}, "version": "1.0", "artifacts": [ { "refs": ["./a/"] } ] }""",
      "$.version: '1.0' is not a SemVer 2.0.0 version" },
    { $$"""{ {{Markers}}, "refs": ["file:///x"], "artifacts": [ { "refs": ["./a/"] } ] }""",
      "$.refs[0]: invalid ref" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": ["./a/"] }, { "refs": ["./b/"] } ] }""",
      "$.artifacts[1]: ambiguous with $.artifacts[0]" },
    { $$"""{ {{Markers}}, "artifacts": [ """
        + """{ "variant": "x", "platform": ["server", "client"], "refs": ["./a/"] }, """
        + """{ "variant": "x", "platform": "client", "refs": ["./b/"] } ] }""",
      "$.artifacts[1]: ambiguous with $.artifacts[0]" },
    { $$"""{ {{Markers}}, "artifacts": [ { "refs": ["./a/"] }, """
        + """{ "rid": "r", "refs": ["./b/"] }, { "rid": ["q", "r"], "refs": ["./c/"] } ] }""",
      "$.artifacts[2]: ambiguous with $.artifacts[1]" },
    { $$"""{ {{Markers}}, "artifacts": [ """
        + """{ "platform": "server", "rid": ["a", "b"], "refs": ["./a/"] }, """
        + """{ "platform": ["client", "server"], "rid": "b", "refs": ["./b/"] } ] }""",
      "$.artifacts[1]: ambiguous with $.artifacts[0]" },
    // §4.2 concrete documents.
    { """{ "version": "1.0.0", "type": "native" }""", "$.name: is required" },
    { """{ "name": 1, "version": "1.0.0", "type": "native" }""", "$.name: must be a string" },
    { """{ "name": "-m", "version": "1.0.0", "type": "native" }""", "$.name: '-m' does not match" },
    { """{ "name": "a b", "version": "1.0.0", "type": "native" }""", "$.name: 'a b'" },
    { """{ "name": "", "version": "1.0.0", "type": "native" }""", "$.name: '' does not match" },
    { """{ "name": "m", "type": "native" }""", "$.version: is required" },
    { """{ "name": "m", "version": "1.0", "type": "native" }""", "$.version: '1.0' is not" },
    { """{ "name": "m", "version": "v1.0.0", "type": "native" }""", "$.version: 'v1.0.0'" },
    { """{ "name": "m", "version": "01.0.0", "type": "native" }""", "$.version: '01.0.0'" },
    { """{ "name": "m", "version": " 1.0.0", "type": "native" }""", "$.version" },
    { """{ "name": "m", "version": 1, "type": "native" }""", "$.version: must be a string" },
    { """{ "name": "m", "version": "1.0.0" }""", "$.type: is required" },
    { """{ "name": "m", "version": "1.0.0", "type": null }""",
      "$.type: must be a string, not null" },
    { $$"""{ {{Concrete}}, "platform": "both" }""", "$.platform: must be" },
    { $$"""{ {{Concrete}}, "platform": ["server"] }""", "$.platform: must be" },
    { $$"""{ {{Concrete}}, "rid": ["win-x64"] }""", "$.rid: must be a string" },
    { $$"""{ {{Concrete}}, "refs": "github.com/a/b" }""", "$.refs: must be an array" },
    { $$"""{ {{Concrete}}, "refs": ["http://x.com/a.zip"] }""", "$.refs[0]: invalid ref" },
    { $$"""{ {{Concrete}}, "dependencies": {} }""", "$.dependencies: must be an array" },
    { $$"""{ {{Concrete}}, "dependencies": ["x"] }""", "$.dependencies[0]: must be an object" },
    { $$"""{ {{Concrete}}, "dependencies": [ { "version": "^1.0.0" } ] }""",
      "$.dependencies[0].name: is required" },
    { $$"""{ {{Concrete}}, "dependencies": [ { "name": "a", "version": 1 } ] }""",
      "$.dependencies[0].version: must be a string" },
    { $$"""{ {{Concrete}}, "dependencies": [ { "name": "a", "version": "invalid" } ] }""",
      "$.dependencies[0].version: invalid version range 'invalid'" },
    { $$"""{ {{Markers}}, {{Concrete}}, "dependencies": [ { "name": "a", "refs": [] } ] }""",
      "$.dependencies[0].refs: must not be empty" },
    { $$"""{ {{Markers}}, {{Concrete}}, "dependencies": [ { "name": "a", "refs": ["./../"] } ] }""",
      "$.dependencies[0].refs[0]: invalid ref" },
    { $$"""{ {{Concrete}}, "optionalDependencies": [ { "name": 1 } ] }""",
      "$.optionalDependencies[0].name: must be a string" },
    { $$"""{ {{Concrete}}, "conflicts": [ {} ] }""", "$.conflicts[0].name: is required" },
    { $$"""{ {{Markers}}, {{Concrete}}, "installDependencies": [ { "name": "a" } ] }""",
      "$.installDependencies[0].refs: is required" },
    { $$"""{ {{Markers}}, {{Concrete}}, "installDependencies": [ { "refs": [] } ] }""",
      "$.installDependencies[0].refs: must not be empty" },
    { $$"""{ {{Markers}}, {{Concrete}}, "installDependencies": {} }""",
      "$.installDependencies: must be an array" },
    { $$"""{ {{Concrete}}, "placements": [] }""", "$.placements: only allowed when" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a" } ] }""",
      "$.placements[0].dest: is required" },
    { $$"""{ {{Files}}, "placements": [ { "dest": "a" } ] }""",
      "$.placements[0].src: is required" },
    { $$"""{ {{Files}}, "placements": [ { "src": "/a", "dest": "a" } ] }""",
      "$.placements[0].src: '/a' must not start with '/'" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a", "dest": "/a" } ] }""",
      "$.placements[0].dest: '/a' must not start with '/'" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a/../b", "dest": "b" } ] }""",
      "$.placements[0].src: 'a/../b' must not contain a '..' segment" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a", "dest": "../b" } ] }""",
      "$.placements[0].dest: '../b' must not contain a '..' segment" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a/", "dest": ".." } ] }""",
      "$.placements[0].dest: '..' must not contain a '..' segment" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a\\b", "dest": "b" } ] }""",
      "$.placements[0].src: 'a\\b' must not contain '\\'" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a", "dest": "C:/b" } ] }""",
      "$.placements[0].dest: 'C:/b' must not start with a drive letter" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a", "dest": "c:" } ] }""",
      "$.placements[0].dest: 'c:' must not start with a drive letter" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a/", "dest": "b" } ] }""",
      "$.placements[0]: src 'a/' and dest 'b' must both be directories" },
    { $$"""{ {{Files}}, "placements": [ { "src": "", "dest": "b" } ] }""",
      "$.placements[0]: src '' and dest 'b'" },
    { $$"""{ {{Files}}, "placements": [ { "src": "a", "dest": "" } ] }""",
      "$.placements[0]: src 'a' and dest ''" },
    { $$"""{ {{Files}}, "placements": [ { "src": 1, "dest": "a" } ] }""",
      "$.placements[0].src: must be a string" },
    { $$"""{ {{Concrete}}, "preserveFiles": ["/abs"] }""", "$.preserveFiles[0]: invalid glob" },
    { $$"""{ {{Concrete}}, "preserveFiles": "a" }""", "$.preserveFiles: must be an array" },
    { $$"""{ {{Concrete}}, "removeFiles": [5] }""", "$.removeFiles[0]: must be a string" },
    { $$"""{ {{Concrete}}, "removeFiles": ["a//b"] }""", "$.removeFiles[0]: invalid glob" },
    { $$"""{ {{Concrete}}, "scripts": [] }""", "$.scripts: must be an object" },
    { $$"""{ {{Concrete}}, "scripts": { "preInstall": "cmd" } }""",
      "$.scripts.preInstall: must be an array" },
    { $$"""{ {{Concrete}}, "scripts": { "postUninstall": [1] } }""",
      "$.scripts.postUninstall[0]: must be a string" },
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
