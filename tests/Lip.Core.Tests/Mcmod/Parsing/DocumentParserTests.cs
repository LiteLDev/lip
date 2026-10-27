using System.Text;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod.Parsing;

public class DocumentParserTests {
  private const string Markers =
      "\"formatVersion\": 4, \"formatUuid\": \"0c6b977a-64ad-441f-9a29-c7f4b196882f\"";

  private readonly DocumentParser _parser = new(FakeVersionRange.Parse);

  private ManifestDocument Parse(string json) => _parser.Parse(Encoding.UTF8.GetBytes(json));

  [Fact]
  public void Parse_UnmarkedLeviLaminaManifest_ReturnsUnmarkedConcreteDocument() {
    // Arrange
    string json = """
        {
          "name": "my-mod", "entry": "my-mod.dll", "version": "1.0.0", "type": "native",
          "description": "d", "author": "a", "extraInfo": { "x": 1 },
          "dependencies": [ { "name": "LeviLamina", "version": ">=1.0.0" } ]
        }
        """;

    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(Parse(json));

    // Assert
    Assert.False(doc.IsMarked);
    Assert.Equal("my-mod", doc.Name);
    Assert.Equal("1.0.0", doc.Version.ToString());
    Assert.Equal("native", doc.Type);
    Assert.Equal(PackagePlatform.Universal, doc.Platform);
    Dep dep = Assert.Single(doc.Dependencies);
    Assert.Equal("LeviLamina", dep.Name);
    Assert.Equal(new FakeVersionRange(">=1.0.0"), dep.Version);
    Assert.Null(dep.Refs);
  }

  [Fact]
  public void Parse_UnmarkedWithInstallKeys_IgnoresDependencyRefsAndInstallDependencies() {
    // Arrange
    string json = """
        {
          "name": "m", "version": "1.0.0", "type": "native",
          "dependencies": [ { "name": "x", "refs": 5 } ],
          "installDependencies": "anything"
        }
        """;

    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(Parse(json));

    // Assert
    Assert.Null(Assert.Single(doc.Dependencies).Refs);
    Assert.Empty(doc.InstallDependencies);
  }

  [Fact]
  public void Parse_MarkedConcrete_ReadsAllKeys() {
    // Arrange
    string json = $$"""
        {
          {{Markers}},
          "name": "Pkg.Name_1-x", "version": "1.2.3-rc.1+build.5", "type": "files",
          "platform": "client", "rid": "win-x64",
          "refs": ["github.com/a/b"],
          "dependencies": [ { "name": "d", "version": "^1.0.0", "refs": ["./d/#v1"] } ],
          "optionalDependencies": [ { "name": "o" } ],
          "conflicts": [ { "name": "c", "version": "<2.0.0" } ],
          "installDependencies": [ { "name": "i", "refs": ["https://example.com/i.mcmod"] } ],
          "placements": [
            { "src": "", "dest": "" },
            { "src": "bin/", "dest": "tools/bin/" },
            { "src": "a.exe", "dest": "b/a.exe" }
          ],
          "preserveFiles": ["config/"],
          "removeFiles": ["logs/**/*.log"],
          "scripts": {
            "preInstall": ["a"], "postInstall": ["b", "c"],
            "preUninstall": [], "postUninstall": ["d"], "install": 5
          }
        }
        """;

    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(Parse(json));

    // Assert
    Assert.True(doc.IsMarked);
    Assert.True(doc.IsFilePackage);
    Assert.Equal("1.2.3-rc.1+build.5", doc.Version.ToString());
    Assert.Equal(PackagePlatform.Client, doc.Platform);
    Assert.Equal("win-x64", doc.Rid);
    Assert.Equal("github.com/a/b", Assert.Single(doc.Refs).Target);
    Ref depRef = Assert.Single(Assert.Single(doc.Dependencies).Refs!);
    Assert.Equal(RefKind.Relative, depRef.Kind);
    Assert.Equal("v1", depRef.Variant);
    Assert.Null(Assert.Single(doc.OptionalDependencies).Version);
    Assert.Equal(new FakeVersionRange("<2.0.0"), Assert.Single(doc.Conflicts).Version);
    InstallDep install = Assert.Single(doc.InstallDependencies);
    Assert.Equal("i", install.Name);
    Assert.Equal(RefKind.Url, Assert.Single(install.Refs).Kind);
    Assert.Equal(
        [new Placement("", ""), new Placement("bin/", "tools/bin/"), new("a.exe", "b/a.exe")],
        doc.Placements);
    Assert.Equal("config/", Assert.Single(doc.PreserveFiles).Pattern);
    Assert.Equal("logs/**/*.log", Assert.Single(doc.RemoveFiles).Pattern);
    Assert.Equal(["a"], doc.Scripts.PreInstall);
    Assert.Equal(["b", "c"], doc.Scripts.PostInstall);
    Assert.Empty(doc.Scripts.PreUninstall);
    Assert.Equal(["d"], doc.Scripts.PostUninstall);
  }

  [Fact]
  public void Parse_Routing_ReadsArtifacts() {
    // Arrange
    string json = $$$"""
        {
          {{{Markers}}},
          "name": "n", "version": "1.0.0", "refs": ["github.com/a/b"], "dependencies": 5,
          "artifacts": [
            { "platform": "server", "rid": ["win-x64", "linux-x64"], "refs": ["./s/{{rid}}/"] },
            { "platform": "client", "refs": ["./c.mcmod", "https://example.com/c.mcmod#x"] },
            { "variant": "", "refs": ["github.com/a/b#lua"] },
            { "variant": "lua/x", "refs": ["./lua/"] }
          ]
        }
        """;

    // Act
    RoutingDocument doc = Assert.IsType<RoutingDocument>(Parse(json));

    // Assert
    Assert.True(doc.IsMarked);
    Assert.Equal("n", doc.Name);
    Assert.Equal("1.0.0", doc.Version!.ToString());
    Assert.Single(doc.Refs);
    Assert.Equal(4, doc.Artifacts.Count);
    Assert.Equal([McmodPlatform.Server], doc.Artifacts[0].Platforms);
    Assert.Equal(["win-x64", "linux-x64"], doc.Artifacts[0].Rids);
    Assert.Equal(["./s/{{rid}}/"], doc.Artifacts[0].Refs);
    Assert.Null(doc.Artifacts[1].Rids);
    Assert.Equal(2, doc.Artifacts[1].Refs.Count);
    Assert.Null(doc.Artifacts[2].Platforms);
    Assert.Equal("", doc.Artifacts[2].Variant);
    Assert.Equal("lua/x", doc.Artifacts[3].Variant);
  }

  [Theory]
  [InlineData("""{ "platform": "server", "refs": ["./a/"] }, { "refs": ["./b/"] }""")]
  [InlineData("""{ "platform": "server", "refs": ["./a/"] }, { "rid": "x", "refs": ["./b/"] }""")]
  [InlineData(
      """{ "platform": "server", "refs": ["./a/"] }, { "platform": "client", "refs": ["./b/"] }""")]
  [InlineData("""{ "rid": ["a", "b"], "refs": ["./a/"] }, { "rid": "c", "refs": ["./b/"] }""")]
  [InlineData("""{ "refs": ["./a/"] }, { "variant": "x", "refs": ["./b/"] }""")]
  [InlineData(
      """{ "platform": "server", "rid": "a", "refs": ["./a/"] }, """
      + """{ "platform": ["server", "client"], "rid": "b", "refs": ["./b/"] }""")]
  public void Parse_UnambiguousArtifacts_Succeeds(string artifacts) {
    // Arrange
    string json = $$"""{ {{Markers}}, "artifacts": [ {{artifacts}} ] }""";

    // Act & Assert
    Assert.IsType<RoutingDocument>(Parse(json));
  }

  [Fact]
  public void Parse_ByteOrderMark_IsIgnored() {
    // Arrange
    byte[] bytes = [
      .. Encoding.UTF8.Preamble,
      .. Encoding.UTF8.GetBytes("""{ "name": "m", "version": "1.0.0", "type": "native" }"""),
    ];

    // Act & Assert
    Assert.IsType<ConcreteDocument>(_parser.Parse(bytes));
  }

  [Fact]
  public void Parse_InvalidUtf8_ThrowsManifestFormatException() {
    // Arrange
    byte[] bytes = [
      .. Encoding.UTF8.GetBytes("""{ "name": "m", "version": "1.0.0", "type": "native", "x": " """),
      0xC3, 0x28,
      .. Encoding.UTF8.GetBytes("\" }"),
    ];

    // Act
    ManifestFormatException ex = Assert.Throws<ManifestFormatException>(() => _parser.Parse(bytes));

    // Assert
    Assert.Contains("UTF-8", ex.Message);
  }

  [Fact]
  public void Parse_InvalidRange_ReportsPath() {
    // Arrange
    string json = """
        { "name": "m", "version": "1.0.0", "type": "native",
          "conflicts": [ { "name": "a" }, { "name": "b", "version": "invalid" } ] }
        """;

    // Act
    ManifestFormatException ex = Assert.Throws<ManifestFormatException>(() => Parse(json));

    // Assert
    Assert.Contains("$.conflicts[1].version", ex.Message);
    Assert.Contains("fake parse error", ex.Message);
  }

  [Fact]
  public void Parse_DependencyWithGitRef_KeepsGitRefAndRange() {
    // Arrange
    string json = $$"""
        { {{Markers}}, "name": "m", "version": "1.0.0", "type": "native",
          "dependencies": [
            { "name": "a", "version": ">=1.0.0", "refs": ["github.com/o/a#lua@branch:main"] }
          ] }
        """;

    // Act
    ConcreteDocument doc = Assert.IsType<ConcreteDocument>(Parse(json));

    // Assert
    Ref reference = Assert.Single(doc.Dependencies[0].Refs!);
    Assert.Equal("github.com/o/a", reference.Target);
    Assert.Equal("lua", reference.Variant);
    Assert.Equal(GitRefKind.Branch, reference.GitRef?.Kind);
    Assert.NotNull(doc.Dependencies[0].Version);
  }

  [Theory]
  [InlineData("./a/@branch:main")]
  [InlineData("https://example.com/a.mcmod@tag:v1")]
  [InlineData("github.com/o/a@1.0.0")]
  [InlineData("file:///C:/a.mcmod")]
  public void Parse_InvalidRefInInstallDependency_ThrowsManifestFormatException(string reference) {
    // Arrange
    string json = $$"""
        { {{Markers}}, "name": "m", "version": "1.0.0", "type": "native",
          "installDependencies": [ { "refs": ["{{reference}}"] } ] }
        """;

    // Act & Assert
    Assert.Throws<ManifestFormatException>(() => Parse(json));
  }

  [Fact]
  public void Parse_EscapedLoneSurrogate_ThrowsManifestFormatException() {
    // Arrange
    string json = """{ "name": "\uD800", "version": "1.0.0", "type": "native" }""";

    // Act
    ManifestFormatException ex = Assert.Throws<ManifestFormatException>(() => Parse(json));

    // Assert
    Assert.Contains("$.name: is not a valid Unicode string", ex.Message);
  }
}
