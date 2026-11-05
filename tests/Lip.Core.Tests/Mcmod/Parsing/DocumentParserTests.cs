using System.Text;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod.Parsing;

public class DocumentParserTests {
  private const string Format = "\"format\": \"bf45b1\"";

  private readonly DocumentParser _parser = new(FakeVersionRange.Parse);

  private ManifestDocument Parse(string json) => _parser.Parse(Encoding.UTF8.GetBytes(json));

  [Fact]
  public void Parse_LeviLaminaManifest_ReadsModWithConflictsOnly() {
    // Arrange
    string json = """
        {
          "name": "my-mod", "entry": "my-mod.dll", "version": "1.0.0", "type": "native",
          "description": "d", "author": "a", "extraInfo": { "x": 1 }, "platform": "x",
          "dependencies": [ { "name": "LeviLamina", "version": ">=1.0.0", "refs": 5 } ],
          "conflicts": [ { "name": "Other", "version": "<2.0.0" }, { "name": "Any" } ],
          "artifacts": "ignored", "formatVersion": 4
        }
        """;

    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(Parse(json));

    // Assert
    Assert.Equal("my-mod", doc.Id);
    Assert.Equal("1.0.0", doc.Version.ToString());
    Assert.True(doc.IsMod);
    Assert.Null(doc.Platform);
    Assert.Empty(doc.Dependencies);
    Assert.Equal(["Other", "Any"], doc.Conflicts.Select(c => c.Id));
    Assert.Equal(new FakeVersionRange("<2.0.0"), doc.Conflicts[0].Version);
    Assert.Null(doc.Conflicts[1].Version);
    Assert.Empty(new McmodPackage(doc, default).InstallEdges);
  }

  [Fact]
  public void Parse_PackageDocument_ReadsAllKeys() {
    // Arrange
    string json = $$"""
        {
          {{Format}},
          "name": "Pkg.Name_1-x", "version": "1.2.3-rc.1+build.5",
          "platform": "client", "runtime": "win-x64",
          "info": { "authors": "not validated", "icon": "http://x" },
          "dependencies": [
            { "name": "d", "version": "^1.0.0", "source": "./d/#v1" },
            { "name": "e", "version": ">=2.0.0" },
            { "name": "f", "source": ["https://example.com/f.mcmod", "github.com/o/f"], "mod": 5 }
          ],
          "conflicts": [ { "name": "c", "version": "<2.0.0" }, { "name": "g" } ],
          "archives": {
            "bin": { "url": ["https://example.com/a.zip", "https://example.com/b.zip"],
                     "sha256": "{{new string('A', 64)}}", "format": "zip" },
            "tool": { "url": "https://example.com/tool.exe", "format": "file" }
          },
          "files": [
            { "src": "", "dest": "" },
            { "from": "bin", "src": "bin/", "dest": "tools/bin/" },
            { "src": "a.exe", "dest": "b/a.exe" },
            { "src": "cfg/**/*.json", "dest": "config/" }
          ],
          "preserve": ["config/"],
          "remove": ["logs/**/*.log"],
          "hooks": {
            "preInstall": ["a"], "postInstall": ["b", "c"],
            "preUninstall": [], "postUninstall": ["d"], "install": 5
          }
        }
        """;

    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(Parse(json));

    // Assert
    Assert.True(doc.IsFilePackage);
    Assert.Equal("1.2.3-rc.1+build.5", doc.Version.ToString());
    Assert.Equal(McmodPlatform.Client, doc.Platform);
    Assert.Equal("win-x64", doc.Runtime);
    Assert.Equal(["d", "e", "f"], doc.Dependencies.Select(d => d.Id));
    Ref depRef = Assert.Single(doc.Dependencies[0].Refs!);
    Assert.Equal(RefKind.Relative, depRef.Kind);
    Assert.Equal("v1", depRef.Variant);
    Assert.Equal(new FakeVersionRange(">=2.0.0"), doc.Dependencies[1].Version);
    Assert.Null(doc.Dependencies[1].Refs);
    Assert.Equal(2, doc.Dependencies[2].Refs!.Count);
    Assert.Null(doc.Dependencies[2].Version);
    Assert.Equal(["c", "g"], doc.Conflicts.Select(c => c.Id));
    Assert.Equal(new FakeVersionRange("<2.0.0"), doc.Conflicts[0].Version);
    Assert.Null(doc.Conflicts[1].Version);
    Assert.All(doc.Conflicts, c => Assert.Null(c.Refs));
    Assert.Equal(
        [
          new PackageArchive(
              "bin", ["https://example.com/a.zip", "https://example.com/b.zip"],
              new string('a', 64), PackageArchiveFormat.Zip),
          new PackageArchive(
              "tool", ["https://example.com/tool.exe"], null, PackageArchiveFormat.File),
        ],
        doc.Archives,
        new PackageArchiveComparer());
    Assert.Equal(
        [
          PackageFileForm.Directory, PackageFileForm.Directory, PackageFileForm.File,
          PackageFileForm.Glob,
        ],
        doc.Files.Select(f => f.Form));
    Assert.Equal("bin", doc.Files[1].From);
    Assert.Equal("config/", Assert.Single(doc.Preserve).Pattern);
    Assert.Equal("logs/**/*.log", Assert.Single(doc.Remove).Pattern);
    Assert.Equal(["a"], doc.Hooks.PreInstall);
    Assert.Equal(["b", "c"], doc.Hooks.PostInstall);
    Assert.Empty(doc.Hooks.PreUninstall);
    Assert.Equal(["d"], doc.Hooks.PostUninstall);
    Assert.Equal(
        ["d", "f"],
        new McmodPackage(doc, default).InstallEdges.Select(e => e.Name));
  }

  [Fact]
  public void Parse_Mod_ReadsModAndIgnoresUnknownKeys() {
    // Arrange
    string json = $$"""
        {
          {{Format}}, "name": "m", "version": "1.0.0",
          "entry": "m.dll", "type": "preload-native", "passive": "yes",
          "loadAfter": ["_x"], "loadBefore": 5, "extra": [], "mod": { "loader": "" }
        }
        """;

    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(Parse(json));

    // Assert
    Assert.True(doc.IsMod);
    Assert.Empty(doc.Files);
  }

  [Fact]
  public void Parse_Meta_ReadsTargetsAndIgnoresOtherKeys() {
    // Arrange
    string json = $$$"""
        {
          {{{Format}}},
          "id": "-n", "name": 5, "version": "1.0.0", "dependencies": 5, "entry": "m.dll",
          "targets": [
            { "platform": "server", "runtime": ["win-x64", "linux-x64"], "source": "./s/{{runtime}}/" },
            { "platform": "client", "source": ["./c.mcmod", "https://example.com/c.mcmod#x"],
              "id": "-n-client", "version": "2.0" },
            { "variant": "", "source": "github.com/a/b#lua", "id": null, "version": null },
            { "variant": "lua/x", "source": "./lua/", "files": "anything" }
          ]
        }
        """;

    // Act
    MetaDocument doc = Assert.IsType<MetaDocument>(Parse(json));

    // Assert
    Assert.Equal("1.0.0", doc.Version!.ToString());
    Assert.Equal(4, doc.Targets.Count);
    Assert.Equal([McmodPlatform.Server], doc.Targets[0].Platforms);
    Assert.Equal(["win-x64", "linux-x64"], doc.Targets[0].Runtimes);
    Assert.Equal(["./s/{{runtime}}/"], doc.Targets[0].Source);
    Assert.Null(doc.Targets[1].Runtimes);
    Assert.Equal(2, doc.Targets[1].Source.Count);
    Assert.Null(doc.Targets[2].Platforms);
    Assert.Equal(["github.com/a/b#lua"], doc.Targets[2].Source);
    Assert.Equal("lua/x", doc.Targets[3].Variant);
  }

  [Theory]
  [InlineData("""{ "platform": "server", "source": "./a/" }, { "source": "./b/" }""")]
  [InlineData("""{ "platform": "server", "source": "./a/" }, { "runtime": "x", "source": "./b/" }""")]
  [InlineData(
      """{ "platform": "server", "source": "./a/" }, { "platform": "client", "source": "./b/" }""")]
  [InlineData("""{ "runtime": ["a", "b"], "source": "./a/" }, { "runtime": "c", "source": "./b/" }""")]
  [InlineData("""{ "source": "./a/" }, { "variant": "x", "source": "./b/" }""")]
  [InlineData(
      """{ "platform": "server", "runtime": "a", "source": "./a/" }, """
      + """{ "platform": ["server", "client"], "runtime": "b", "source": "./b/" }""")]
  public void Parse_UnambiguousTargets_Succeeds(string targets) {
    // Arrange
    string json = $$"""{ {{Format}}, "targets": [ {{targets}} ] }""";

    // Act & Assert
    Assert.IsType<MetaDocument>(Parse(json));
  }

  [Fact]
  public void Parse_IdWithTrailingNewline_ThrowsManifestFormatException() {
    // Arrange: an id must match ^[A-Za-z0-9][A-Za-z0-9._-]*$ in full (specs/package_format.md
    // §4.1); .NET's '$' alone would accept a trailing line break.
    string json = $$"""{ {{Format}}, "name": "my-mod\n", "version": "1.0.0" }""";

    // Act & Assert
    Assert.Throws<ManifestFormatException>(() => Parse(json));
  }

  [Fact]
  public void Parse_ByteOrderMark_IsIgnored() {
    // Arrange
    byte[] bytes = [
      .. Encoding.UTF8.Preamble,
      .. Encoding.UTF8.GetBytes($$"""{ {{Format}}, "name": "m", "version": "1.0.0" }"""),
    ];

    // Act & Assert
    Assert.IsType<PackageDocument>(_parser.Parse(bytes));
  }

  [Fact]
  public void Parse_InvalidUtf8_ThrowsManifestFormatException() {
    // Arrange
    byte[] bytes = [
      .. Encoding.UTF8.GetBytes($$"""{ {{Format}}, "name": "m", "version": "1.0.0", "x": " """),
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
    string json = $$"""
        { {{Format}}, "name": "m", "version": "1.0.0",
          "conflicts": [ { "name": "a", "version": "*" }, { "name": "b", "version": "invalid" } ] }
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
        { {{Format}}, "name": "m", "version": "1.0.0",
          "dependencies": [ { "name": "a", "version": ">=1.0.0", "source": "github.com/o/a#lua@branch:main" } ] }
        """;

    // Act
    PackageDocument doc = Assert.IsType<PackageDocument>(Parse(json));

    // Assert
    Ref reference = Assert.Single(doc.Dependencies[0].Refs!);
    Assert.Equal("github.com/o/a", reference.Target);
    Assert.Equal("lua", reference.Variant);
    Assert.Equal(GitRefKind.Branch, reference.GitRef?.Kind);
    Assert.NotNull(doc.Dependencies[0].Version);
  }

  [Theory]
  [InlineData("./a#b#c")]
  [InlineData("https://example.com/a b.mcmod")]
  [InlineData("github.com/o/a@v1.0.0")]
  [InlineData("file:///C:/a.mcmod")]
  public void Parse_InvalidRefInDependency_ThrowsManifestFormatException(string reference) {
    // Arrange
    string json = $$"""
        { {{Format}}, "name": "m", "version": "1.0.0",
          "dependencies": [ { "name": "a", "source": "{{reference}}" } ] }
        """;

    // Act & Assert
    Assert.Throws<ManifestFormatException>(() => Parse(json));
  }

  [Fact]
  public void Parse_EscapedLoneSurrogate_ThrowsManifestFormatException() {
    // Arrange
    string json = $$"""{ {{Format}}, "name": "\uD800", "version": "1.0.0" }""";

    // Act
    ManifestFormatException ex = Assert.Throws<ManifestFormatException>(() => Parse(json));

    // Assert
    Assert.Contains("$.name: is not a valid Unicode string", ex.Message);
  }

  private sealed class PackageArchiveComparer : IEqualityComparer<PackageArchive> {
    public bool Equals(PackageArchive? x, PackageArchive? y) =>
        x is not null && y is not null && x.Name == y.Name && x.Urls.SequenceEqual(y.Urls)
        && x.Sha256 == y.Sha256 && x.Format == y.Format;

    public int GetHashCode(PackageArchive obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
  }
}
