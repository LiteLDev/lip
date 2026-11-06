using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod.Parsing;

/// Validation of tooth files of format 4 (tooth.schema.json and protocol/package.md §3).
public class ToothParserTests {
  private const string _uuid = "f74a5fda-cb2a-4de8-959f-a1a76bad3102";

  /// A minimal valid tooth file of format 4.
  private static JsonObject Valid() => JsonNode.Parse($$"""
      {
        "format_version": 4,
        "format_uuid": "{{_uuid}}",
        "tooth": "example.com/owner/tool",
        "version": "1.2.3",
        "variants": {
          "": [ { "platform": "server", "runtime": "win-x64" } ]
        }
      }
      """)!.AsObject();

  /// A valid tooth file whose variant "" has this single entry.
  private static JsonObject WithEntry(string entry) {
    JsonObject document = Valid();
    document["variants"] = new JsonObject { [""] = new JsonArray(JsonNode.Parse(entry)) };
    return document;
  }

  /// A valid tooth file whose single entry has these members besides platform and runtime.
  private static JsonObject WithMembers(string members) =>
      WithEntry($$"""{ "platform": "server", "runtime": "win-x64", {{members}} }""");

  private static ToothFile Parse(JsonNode document) =>
      ToothParser.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));

  private static ToothEntry ParseEntry(JsonNode document) =>
      Assert.Single(Parse(document).Variants[""]);

  private static ToothFormatException AssertInvalid(JsonNode document, string path) {
    ToothFormatException ex = Assert.Throws<ToothFormatException>(() => Parse(document));
    Assert.Contains($"at {path}", ex.Message);
    return ex;
  }

  private static ToothFormatException AssertInvalid(string json) =>
      Assert.Throws<ToothFormatException>(() => ToothParser.Parse(Encoding.UTF8.GetBytes(json)));

  [Fact]
  public void Parse_MinimalDocument_ReadsMembers() {
    // Arrange
    JsonObject document = Valid();

    // Act
    ToothFile tooth = Parse(document);

    // Assert
    Assert.Equal("example.com/owner/tool", tooth.Tooth);
    Assert.Equal(SemVersion.Parse("1.2.3"), tooth.Version);
    Assert.Equal([""], tooth.Variants.Keys);
    ToothEntry entry = Assert.Single(tooth.Variants[""]);
    Assert.Equal(McmodPlatform.Server, entry.Platform);
    Assert.Equal("win-x64", entry.Runtime);
    Assert.Empty(entry.Dependencies);
    Assert.Empty(entry.Assets);
    Assert.Empty(entry.PreserveFiles);
    Assert.Empty(entry.RemoveFiles);
    Assert.Empty(entry.Hooks.PreInstall);
    Assert.Empty(entry.Hooks.PostInstall);
    Assert.Empty(entry.Hooks.PreUninstall);
    Assert.Empty(entry.Hooks.PostUninstall);
    Assert.False(entry.Derived);
    Assert.Null(entry.McmodAsset);
    Assert.Null(entry.McmodAsset);
    Assert.Equal("server", entry.Json.GetProperty("platform").GetString());
    Assert.Equal(4, tooth.Json.GetProperty("format_version").GetInt32());
  }

  [Fact]
  public void Parse_FullDocument_ReadsEveryMember() {
    // Arrange
    JsonObject document = WithMembers("""
        "dependencies": { "example.com/a": "^1.0.0", "example.com/b#lua": ">=2.0.0 <3.0.0" },
        "assets": [
          { "type": "self", "place": [ { "src": "bin/", "dest": "tools/x/" } ] },
          { "type": "zip", "urls": [ "https://a.example/x.zip", "https://b.example/x.zip" ],
            "place": [ { "src": "x.dll", "dest": "plugins/x.dll" },
                            { "src": "lib/*.dll", "dest": "" } ] },
          { "type": "mcmod", "urls": [ "https://a.example/x.mcmod" ] }
        ],
        "preserve": [ "config/**", "data/" ],
        "purge": [ "logs/" ],
        "hooks": {
          "pre_install": [ "a" ], "post_install": [ "b", "c" ],
          "pre_uninstall": [ "d" ], "post_uninstall": [ "e" ]
        }
        """);
    document["info"] = JsonNode.Parse("""
        { "name": "Tool", "description": "d", "tags": [ "x" ], "logo": "https://a.example/a.png" }
        """);

    // Act
    ToothEntry entry = ParseEntry(document);

    // Assert
    Assert.Equal(
        [("example.com/a", "", "^1.0.0"), ("example.com/b", "lua", ">=2.0.0 <3.0.0")],
        entry.Dependencies.Select(d => (d.Source.Target, d.Source.Variant, d.Range.Text)));
    Assert.All(entry.Dependencies, d => Assert.False(d.ClientCompat));
    Assert.True(entry.Dependencies[0].Range.IsSatisfiedBy(SemVersion.Parse("1.9.0")));
    Assert.False(entry.Dependencies[0].Range.IsSatisfiedBy(SemVersion.Parse("2.0.0")));

    Assert.Equal(
        [AssetType.Self, AssetType.Zip, AssetType.Mcmod], entry.Assets.Select(a => a.Type));
    Assert.Empty(entry.Assets[0].Urls);
    Assert.Equal([new EntryPlacement("bin/", "tools/x/")], entry.Assets[0].Placements);
    Assert.Equal(["https://a.example/x.zip", "https://b.example/x.zip"], entry.Assets[1].Urls);
    Assert.Equal(
        [new EntryPlacement("x.dll", "plugins/x.dll"), new EntryPlacement("lib/*.dll", "")],
        entry.Assets[1].Placements);
    Assert.Equal(["https://a.example/x.mcmod"], entry.Assets[2].Urls);
    Assert.Empty(entry.Assets[2].Placements);
    Assert.NotNull(entry.McmodAsset);
    Assert.Equal(2, entry.McmodAsset);

    Assert.Equal(["config/**", "data/"], entry.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(["logs/"], entry.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal(["a"], entry.Hooks.PreInstall);
    Assert.Equal(["b", "c"], entry.Hooks.PostInstall);
    Assert.Equal(["d"], entry.Hooks.PreUninstall);
    Assert.Equal(["e"], entry.Hooks.PostUninstall);
  }

  [Theory]
  [InlineData("format_version")]
  [InlineData("format_uuid")]
  [InlineData("tooth")]
  [InlineData("version")]
  [InlineData("variants")]
  public void Parse_MissingRequiredMember_Throws(string key) {
    // Arrange
    JsonObject document = Valid();
    document.Remove(key);

    // Act & Assert
    AssertInvalid(document, $"$.{key}");
  }

  [Theory]
  [InlineData("3")]
  [InlineData("5")]
  [InlineData("4.5")]
  [InlineData("\"4\"")]
  [InlineData("null")]
  public void Parse_OtherFormatVersion_Throws(string value) {
    // Arrange
    JsonObject document = Valid();
    document["format_version"] = JsonNode.Parse(value);

    // Act & Assert
    AssertInvalid(document, "$.format_version");
  }

  [Theory]
  [InlineData("\"289f771f-2c9a-4d73-9f3f-8492495a924d\"")]
  [InlineData("\"F74A5FDA-CB2A-4DE8-959F-A1A76BAD3102\"")]
  [InlineData("\"\"")]
  [InlineData("4")]
  public void Parse_OtherFormatUuid_Throws(string value) {
    // Arrange
    JsonObject document = Valid();
    document["format_uuid"] = JsonNode.Parse(value);

    // Act & Assert
    AssertInvalid(document, "$.format_uuid");
  }

  [Theory]
  [InlineData("github.com/LiteLDev/LeviLamina")]
  [InlineData("example.com/a/v2")]
  [InlineData("example.com/a.b-c_d~e")]
  public void Parse_GoModulePath_IsAccepted(string tooth) {
    // Arrange
    JsonObject document = Valid();
    document["tooth"] = tooth;

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Equal(tooth, file.Tooth);
  }

  [Theory]
  [InlineData("")]
  [InlineData("tool")]
  [InlineData("/example.com/a")]
  [InlineData("example.com/a/")]
  [InlineData("example.com//a")]
  [InlineData("example.com/a b")]
  [InlineData("example.com/../a")]
  [InlineData("https://example.com/a")]
  public void Parse_NotAGoModulePath_Throws(string tooth) {
    // Arrange
    JsonObject document = Valid();
    document["tooth"] = tooth;

    // Act & Assert
    AssertInvalid(document, "$.tooth");
  }

  [Fact]
  public void Parse_ToothNotAString_Throws() {
    // Arrange
    JsonObject document = Valid();
    document["tooth"] = 1;

    // Act & Assert
    AssertInvalid(document, "$.tooth");
  }

  [Theory]
  [InlineData("0.0.0")]
  [InlineData("1.0.0-rc.1")]
  [InlineData("1.0.0-rc.1+build.5")]
  public void Parse_Version_IsAccepted(string version) {
    // Arrange
    JsonObject document = Valid();
    document["version"] = version;

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Equal(SemVersion.Parse(version), file.Version);
  }

  [Theory]
  [InlineData("v1.0.0")]
  [InlineData("1.0")]
  [InlineData("01.0.0")]
  [InlineData("1.0.0-01")]
  [InlineData(" 1.0.0")]
  [InlineData("")]
  public void Parse_NotAVersion_Throws(string version) {
    // Arrange
    JsonObject document = Valid();
    document["version"] = version;

    // Act & Assert
    AssertInvalid(document, "$.version");
  }

  [Theory]
  [InlineData("lua")]
  [InlineData("a_1")]
  [InlineData("0")]
  [InlineData("engine/lua")]
  public void Parse_VariantName_IsAccepted(string variant) {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![variant] = JsonNode.Parse(
        """[ { "platform": "client", "runtime": "win-x64" } ]""");

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Equal(["", variant], file.Variants.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(McmodPlatform.Client, Assert.Single(file.Variants[variant]).Platform);
  }

  [Theory]
  [InlineData("Lua")]
  [InlineData("a-b")]
  [InlineData("a.b")]
  [InlineData("a/b/c")]
  [InlineData("/a")]
  [InlineData("a/")]
  [InlineData(" ")]
  public void Parse_NotAVariantName_Throws(string variant) {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![variant] = JsonNode.Parse(
        """[ { "platform": "server", "runtime": "win-x64" } ]""");

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"{variant}\"]");
  }

  [Fact]
  public void Parse_NoVariants_IsAccepted() {
    // Arrange
    JsonObject document = Valid();
    document["variants"] = new JsonObject();

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Empty(file.Variants);
  }

  [Theory]
  [InlineData("[]")]
  [InlineData("{}")]
  [InlineData("null")]
  public void Parse_VariantNotANonEmptyArray_Throws(string value) {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![""] = JsonNode.Parse(value);

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"]");
  }

  [Theory]
  [InlineData("[]")]
  [InlineData("\"\"")]
  public void Parse_VariantsNotAnObject_Throws(string value) {
    // Arrange
    JsonObject document = Valid();
    document["variants"] = JsonNode.Parse(value);

    // Act & Assert
    AssertInvalid(document, "$.variants");
  }

  [Theory]
  [InlineData("\"\"")]
  [InlineData("\"Server\"")]
  [InlineData("\"any\"")]
  [InlineData("\"win-x64\"")]
  [InlineData("null")]
  public void Parse_NotAPlatform_Throws(string platform) {
    // Arrange
    JsonObject document = WithEntry($$"""{ "platform": {{platform}}, "runtime": "win-x64" }""");

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].platform");
  }

  [Theory]
  [InlineData("platform")]
  [InlineData("runtime")]
  public void Parse_EntryWithoutPlatformOrRuntime_Throws(string key) {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![""]![0]!.AsObject().Remove(key);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].{key}");
  }

  [Theory]
  [InlineData("win-x64")]
  [InlineData("linux-musl-arm64")]
  [InlineData("osx.13-arm64")]
  [InlineData("any")]
  public void Parse_Runtime_IsAccepted(string runtime) {
    // Arrange
    JsonObject document = WithEntry($$"""{ "platform": "server", "runtime": "{{runtime}}" }""");

    // Act
    ToothEntry entry = ParseEntry(document);

    // Assert
    Assert.Equal(runtime, entry.Runtime);
  }

  [Theory]
  [InlineData("\"\"")]
  [InlineData("\"win-*\"")]
  [InlineData("\"win x64\"")]
  [InlineData("\"win--x64\"")]
  [InlineData("\"-win\"")]
  [InlineData("1")]
  public void Parse_NotARuntime_Throws(string runtime) {
    // Arrange
    JsonObject document = WithEntry($$"""{ "platform": "server", "runtime": {{runtime}} }""");

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].runtime");
  }

  [Fact]
  public void Parse_TwoEntriesWithSamePlatformAndRuntime_Throws() {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![""] = JsonNode.Parse("""
        [
          { "platform": "server", "runtime": "win-x64" },
          { "platform": "client", "runtime": "win-x64" },
          { "platform": "server", "runtime": "win-x64", "hooks": {} }
        ]
        """);

    // Act
    ToothFormatException ex = AssertInvalid(document, "$.variants[\"\"][2]");

    // Assert
    Assert.Contains("$.variants[\"\"][0]", ex.Message);
  }

  [Fact]
  public void Parse_EntriesDifferingInPlatformRuntimeOrVariant_AreAccepted() {
    // Arrange
    JsonObject document = Valid();
    document["variants"] = JsonNode.Parse("""
        {
          "": [
            { "platform": "server", "runtime": "win-x64" },
            { "platform": "client", "runtime": "win-x64" },
            { "platform": "server", "runtime": "linux-x64" }
          ],
          "lua": [ { "platform": "server", "runtime": "win-x64" } ]
        }
        """);

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Equal(
        [(McmodPlatform.Server, "win-x64"), (McmodPlatform.Client, "win-x64"),
         (McmodPlatform.Server, "linux-x64")],
        file.Variants[""].Select(e => (e.Platform, e.Runtime)));
    Assert.Same(
        file.Variants[""][1],
        file.SelectEntry("", new LipEnvironment(McmodPlatform.Client, "win-x64")));
    Assert.Null(file.SelectEntry("", new LipEnvironment(McmodPlatform.Client, "linux-x64")));
    Assert.Null(file.SelectEntry("js", new LipEnvironment(McmodPlatform.Server, "win-x64")));
  }

  [Theory]
  [InlineData("example.com/a", "example.com/a", "")]
  [InlineData("example.com/a/v2#lua", "example.com/a/v2", "lua")]
  [InlineData("example.com/a#engine/lua", "example.com/a", "engine/lua")]
  public void Parse_DependencyKey_IsAccepted(string key, string tooth, string variant) {
    // Arrange
    JsonObject document = WithMembers($$""" "dependencies": { "{{key}}": "*" } """);

    // Act
    ToothDependency dependency = Assert.Single(ParseEntry(document).Dependencies);

    // Assert
    Assert.Equal(RefKind.Spec, dependency.Source.Kind);
    Assert.Equal(tooth, dependency.Source.Target);
    Assert.Equal(variant, dependency.Source.Variant);
    Assert.Null(dependency.Source.GitRef);
  }

  [Theory]
  [InlineData("example.com/a#")]
  [InlineData("example.com/a#Lua")]
  [InlineData("example.com/a#a/b/c")]
  [InlineData("example.com/a@1.0.0")]
  [InlineData("example.com/a@branch:main")]
  [InlineData("example.com/a#lua@1.0.0")]
  [InlineData("https://example.com/a.zip")]
  [InlineData("")]
  public void Parse_DependencyKeyNotAToothPath_Throws(string key) {
    // Arrange
    JsonObject document = WithMembers($$""" "dependencies": { "{{key}}": "*" } """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].dependencies[\"{key}\"]");
  }

  [Theory]
  [InlineData("*", "0.0.1", true)]
  [InlineData("1.2.3", "1.2.3", true)]
  [InlineData("1.2.3", "1.2.4", false)]
  [InlineData("^1.2.0", "1.9.9", true)]
  [InlineData("^1.2.0", "2.0.0", false)]
  [InlineData("~1.2.0", "1.2.9", true)]
  [InlineData("~1.2.0", "1.3.0", false)]
  [InlineData("1.*", "1.5.0", true)]
  [InlineData(">=1.0.0 <2.0.0", "1.5.0", true)]
  [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
  [InlineData("1.0.0 || 3.0.0", "3.0.0", true)]
  [InlineData("1.0.0 || 3.0.0", "2.0.0", false)]
  public void Parse_DependencyRange_MatchesAsSemVersionRange(
      string range, string version, bool satisfied) {
    // Arrange
    JsonObject document = WithMembers($$""" "dependencies": { "example.com/a": "{{range}}" } """);

    // Act
    ToothDependency dependency = Assert.Single(ParseEntry(document).Dependencies);

    // Assert
    Assert.Equal(range, dependency.Range.Text);
    Assert.Equal(satisfied, dependency.Range.IsSatisfiedBy(SemVersion.Parse(version)));
    Assert.Equal(
        SemVersionRange.Parse(range).Contains(SemVersion.Parse(version)),
        dependency.Range.IsSatisfiedBy(SemVersion.Parse(version)));
  }

  [Theory]
  [InlineData("\"abc\"")]
  [InlineData("\">=\"")]
  [InlineData("\"1.0.0 -\"")]
  [InlineData("1")]
  [InlineData("[\">=1.0.0\"]")]
  [InlineData("null")]
  public void Parse_DependencyValueNotARange_Throws(string range) {
    // Arrange
    JsonObject document = WithMembers($$""" "dependencies": { "example.com/a": {{range}} } """);

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].dependencies[\"example.com/a\"]");
  }

  [Fact]
  public void Parse_DependenciesNotAnObject_Throws() {
    // Arrange
    JsonObject document = WithMembers(""" "dependencies": [ "example.com/a" ] """);

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].dependencies");
  }

  [Theory]
  [InlineData("""{ "type": "tar", "urls": [ "https://a.example/x.tar" ] }""", ".type")]
  [InlineData("""{ "type": "tgz", "urls": [ "https://a.example/x.tgz" ] }""", ".type")]
  [InlineData("""{ "type": "uncompressed", "urls": [ "https://a.example/x" ] }""", ".type")]
  [InlineData("""{ "type": "Zip", "urls": [ "https://a.example/x.zip" ] }""", ".type")]
  [InlineData("""{ "urls": [ "https://a.example/x.zip" ] }""", ".type")]
  [InlineData("""{ "type": "zip" }""", ".urls")]
  [InlineData("""{ "type": "zip", "urls": [] }""", ".urls")]
  [InlineData("""{ "type": "zip", "urls": "https://a.example/x.zip" }""", ".urls")]
  [InlineData("""{ "type": "zip", "urls": [ 1 ] }""", ".urls[0]")]
  [InlineData("""{ "type": "mcmod" }""", ".urls")]
  [InlineData("""{ "type": "mcmod", "urls": [] }""", ".urls")]
  [InlineData(
      """{ "type": "mcmod", "urls": [ "https://a.example/x.mcmod" ], "place": [] }""",
      ".place")]
  [InlineData("""{ "type": "self", "place": {} }""", ".place")]
  [InlineData("\"self\"", "")]
  public void Parse_InvalidAsset_Throws(string asset, string member) {
    // Arrange
    JsonObject document = WithMembers($$""" "assets": [ {{asset}} ] """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].assets[0]{member}");
  }

  [Fact]
  public void Parse_SelfAssetWithUrls_IgnoresThem() {
    // Arrange
    JsonObject document = WithMembers("""
        "assets": [ { "type": "self", "urls": [ "https://a.example/x.zip" ] } ]
        """);

    // Act
    EntryAsset asset = Assert.Single(ParseEntry(document).Assets);

    // Assert
    Assert.Equal(AssetType.Self, asset.Type);
    Assert.Empty(asset.Urls);
    Assert.Empty(asset.Placements);
  }

  [Fact]
  public void Parse_McmodAssetWithPlace_Throws() {
    // Arrange
    JsonObject document = WithMembers("""
        "assets": [ { "type": "mcmod", "urls": [ "https://a.example/x.mcmod" ],
                      "place": [ { "src": "a.dll", "dest": "a.dll" } ] } ]
        """);

    // Act
    ToothFormatException ex = AssertInvalid(document, "$.variants[\"\"][0].assets[0].place");

    // Assert
    Assert.Contains("mcmod", ex.Message);
  }

  [Fact]
  public void Parse_McmodAssetWithPlacements_IgnoresThem() {
    // Arrange
    JsonObject document = WithMembers("""
        "assets": [ { "type": "mcmod", "urls": [ "https://a.example/x.mcmod" ],
                      "placements": [ { "src": "a.dll", "dest": "a.dll" } ] } ]
        """);

    // Act
    EntryAsset asset = Assert.Single(ParseEntry(document).Assets);

    // Assert
    Assert.Equal(AssetType.Mcmod, asset.Type);
    Assert.Empty(asset.Placements);
  }

  [Theory]
  [InlineData("""{ "type": "self", "placements": [ { "src": "a.dll", "dest": "b.dll" } ] }""")]
  [InlineData("""{ "type": "self", "placements": [{ "type": "file", "src": "", "dest": "" }] }""")]
  [InlineData("""{ "type": "self", "placements": {} }""")]
  [InlineData("""{ "type": "zip", "urls": [ "https://a.example/x.zip" ], "placements": 1 }""")]
  public void Parse_AssetWithPlacements_IgnoresThem(string asset) {
    // Arrange
    JsonObject document = WithMembers($$""" "assets": [ {{asset}} ] """);

    // Act
    EntryAsset parsed = Assert.Single(ParseEntry(document).Assets);

    // Assert
    Assert.Empty(parsed.Placements);
  }

  [Fact]
  public void Parse_AssetWithPlaceAndPlacements_ReadsPlace() {
    // Arrange
    JsonObject document = WithMembers("""
        "assets": [ { "type": "self",
                      "place": [ { "src": "a.dll", "dest": "plugins/a.dll" } ],
                      "placements": [ { "src": "b.dll", "dest": "plugins/b.dll" } ] } ]
        """);

    // Act
    EntryAsset asset = Assert.Single(ParseEntry(document).Assets);

    // Assert
    Assert.Equal([new EntryPlacement("a.dll", "plugins/a.dll")], asset.Placements);
  }

  [Fact]
  public void Parse_OldGlobKeys_AreIgnored() {
    // Arrange
    JsonObject document = WithMembers("""
        "preserve_files": [ "/not a glob" ], "remove_files": [ 1 ]
        """);

    // Act
    ToothEntry entry = ParseEntry(document);

    // Assert
    Assert.Empty(entry.PreserveFiles);
    Assert.Empty(entry.RemoveFiles);
  }

  [Fact]
  public void Parse_TwoMcmodAssets_Throws() {
    // Arrange
    JsonObject document = WithMembers("""
        "assets": [
          { "type": "mcmod", "urls": [ "https://a.example/a.mcmod" ] },
          { "type": "zip", "urls": [ "https://a.example/b.zip" ] },
          { "type": "mcmod", "urls": [ "https://a.example/c.mcmod" ] }
        ]
        """);

    // Act
    ToothFormatException ex = AssertInvalid(document, "$.variants[\"\"][0].assets");

    // Assert
    Assert.Contains("mcmod", ex.Message);
  }

  [Fact]
  public void Parse_McmodAssetsInDifferentEntries_AreAccepted() {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![""] = JsonNode.Parse("""
        [
          { "platform": "server", "runtime": "win-x64",
            "assets": [ { "type": "mcmod", "urls": [ "https://a.example/s.mcmod" ] } ] },
          { "platform": "client", "runtime": "win-x64",
            "assets": [ { "type": "mcmod", "urls": [ "https://a.example/c.mcmod" ] } ] }
        ]
        """);

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.All(file.Variants[""], e => Assert.Equal(0, e.McmodAsset));
  }

  [Theory]
  [InlineData("a.dll", "plugins/a.dll", PlacementForm.File)]
  [InlineData("bin/a.dll", "a.dll", PlacementForm.File)]
  [InlineData("", "", PlacementForm.Directory)]
  [InlineData("", "tools/x/", PlacementForm.Directory)]
  [InlineData("bin/", "", PlacementForm.Directory)]
  [InlineData("bin/", "tools/x/", PlacementForm.Directory)]
  [InlineData("*.dll", "", PlacementForm.Glob)]
  [InlineData("bin/**/*.dll", "plugins/", PlacementForm.Glob)]
  [InlineData("bin/a?.dll", "plugins/", PlacementForm.Glob)]
  [InlineData("bin/[ab].dll", "plugins/", PlacementForm.Glob)]
  [InlineData("bin/*/", "plugins/", PlacementForm.Glob)]
  [InlineData("a.dll", "C/a.dll", PlacementForm.File)]
  [InlineData("...", "a..b", PlacementForm.File)]
  public void Parse_Placement_IsAcceptedWithItsForm(string src, string dest, PlacementForm form) {
    // Arrange
    JsonObject document = WithMembers($$"""
        "assets": [ { "type": "self", "place": [ { "src": "{{src}}", "dest": "{{dest}}" } ] } ]
        """);

    // Act
    EntryPlacement placement = Assert.Single(Assert.Single(ParseEntry(document).Assets).Placements);

    // Assert
    Assert.Equal(new EntryPlacement(src, dest), placement);
    Assert.Equal(form, placement.Form);
  }

  [Theory]
  [InlineData("a.dll", "plugins/", "dest")]
  [InlineData("a.dll", "", "dest")]
  [InlineData("bin/", "tools/x", "dest")]
  [InlineData("", "tools", "dest")]
  [InlineData("*.dll", "plugins/a.dll", "dest")]
  [InlineData("bin/[^a]", "plugins/", "src")]
  [InlineData("/a.dll", "a.dll", "src")]
  [InlineData("C:/a.dll", "a.dll", "src")]
  [InlineData("c:a.dll", "a.dll", "src")]
  [InlineData("bin//a.dll", "a.dll", "src")]
  [InlineData("bin\\\\a.dll", "a.dll", "src")]
  [InlineData("../a.dll", "a.dll", "src")]
  [InlineData("bin/../a.dll", "a.dll", "src")]
  [InlineData("..", "a/", "src")]
  [InlineData("a.dll", "/a.dll", "dest")]
  [InlineData("a.dll", "D:/a.dll", "dest")]
  [InlineData("a.dll", "plugins//a.dll", "dest")]
  [InlineData("a.dll", "plugins\\\\a.dll", "dest")]
  [InlineData("a.dll", "../a.dll", "dest")]
  [InlineData("a.dll", "plugins/../../a.dll", "dest")]
  public void Parse_InvalidPlacement_Throws(string src, string dest, string member) {
    // Arrange
    JsonObject document = WithMembers($$"""
        "assets": [ { "type": "zip", "urls": [ "https://a.example/x.zip" ],
                      "place": [ { "src": "{{src}}", "dest": "{{dest}}" } ] } ]
        """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].assets[0].place[0].{member}");
  }

  [Theory]
  [InlineData("""{ "dest": "a" }""", ".src")]
  [InlineData("""{ "src": "a" }""", ".dest")]
  [InlineData("""{ "src": 1, "dest": "a" }""", ".src")]
  [InlineData("""[ "a", "a" ]""", "")]
  public void Parse_PlacementWithoutStringSrcAndDest_Throws(string placement, string member) {
    // Arrange
    JsonObject document = WithMembers($$"""
        "assets": [ { "type": "self", "place": [ {{placement}} ] } ]
        """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].assets[0].place[0]{member}");
  }

  [Theory]
  [InlineData("preserve")]
  [InlineData("purge")]
  public void Parse_Globs_AreAccepted(string key) {
    // Arrange
    JsonObject document = WithMembers($$"""
        "{{key}}": [ "config.json", "**/*.json", "data/", "plugins/[a-z]?/*", ".hidden" ]
        """);

    // Act
    ToothEntry entry = ParseEntry(document);

    // Assert
    IReadOnlyList<PathGlob> globs = key == "preserve" ? entry.PreserveFiles : entry.RemoveFiles;
    Assert.Equal(
        ["config.json", "**/*.json", "data/", "plugins/[a-z]?/*", ".hidden"],
        globs.Select(g => g.Pattern));
    Assert.True(globs[1].IsMatch("a/b/c.json"));
    Assert.True(globs[2].MatchesDirectories);
  }

  [Theory]
  [InlineData("preserve", "\"\"")]
  [InlineData("preserve", "\"/config.json\"")]
  [InlineData("preserve", "\"config//a.json\"")]
  [InlineData("preserve", "\"config\\\\a.json\"")]
  [InlineData("preserve", "1")]
  [InlineData("purge", "\"\"")]
  [InlineData("purge", "\"/logs/\"")]
  [InlineData("purge", "\"logs//\"")]
  [InlineData("purge", "\"logs\\\\\"")]
  public void Parse_NotAGlob_Throws(string key, string glob) {
    // Arrange
    JsonObject document = WithMembers($$""" "{{key}}": [ {{glob}} ] """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].{key}[0]");
  }

  [Theory]
  [InlineData("""{ "pre_install": "a" }""", ".pre_install")]
  [InlineData("""{ "post_install": [ 1 ] }""", ".post_install[0]")]
  [InlineData("""{ "pre_uninstall": {} }""", ".pre_uninstall")]
  [InlineData("""{ "post_uninstall": [ null ] }""", ".post_uninstall[0]")]
  [InlineData("""[ "a" ]""", "")]
  public void Parse_InvalidHooks_Throws(string hooks, string member) {
    // Arrange
    JsonObject document = WithMembers($$""" "hooks": {{hooks}} """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].hooks{member}");
  }

  [Theory]
  [InlineData("""{ "name": 1 }""", ".name")]
  [InlineData("""{ "description": [] }""", ".description")]
  [InlineData("""{ "tags": "a" }""", ".tags")]
  [InlineData("""{ "tags": [ 1 ] }""", ".tags[0]")]
  [InlineData("""{ "logo": null }""", ".logo")]
  [InlineData("\"Tool\"", "")]
  public void Parse_InvalidInfo_Throws(string info, string member) {
    // Arrange
    JsonObject document = Valid();
    document["info"] = JsonNode.Parse(info);

    // Act & Assert
    AssertInvalid(document, $"$.info{member}");
  }

  [Theory]
  [InlineData("https://a.example/logo.png")]
  [InlineData("https://")]
  public void Parse_HttpsLogo_IsAccepted(string logo) {
    // Arrange
    JsonObject document = Valid();
    document["info"] = new JsonObject { ["logo"] = logo };

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Equal(logo, file.Json.GetProperty("info").GetProperty("logo").GetString());
  }

  [Theory]
  [InlineData("\"http://a.example/logo.png\"")]
  [InlineData("\"logo.png\"")]
  [InlineData("\"HTTPS://a.example/logo.png\"")]
  [InlineData("\"\"")]
  [InlineData("1")]
  [InlineData("[ \"https://a.example/logo.png\" ]")]
  public void Parse_LogoNotAnHttpsUrl_Throws(string logo) {
    // Arrange
    JsonObject document = Valid();
    document["info"] = JsonNode.Parse($$"""{ "logo": {{logo}} }""");

    // Act & Assert
    AssertInvalid(document, "$.info.logo");
  }

  [Fact]
  public void Parse_AvatarUrl_IsIgnored() {
    // Arrange
    JsonObject document = Valid();
    document["info"] = JsonNode.Parse("""{ "avatar_url": "logo.png" }""");

    // Act
    ToothFile file = Parse(document);

    // Assert
    Assert.Equal("example.com/owner/tool", file.Tooth);
  }

  [Fact]
  public void Parse_UnknownKeys_AreIgnored() {
    // Arrange
    JsonObject document = WithMembers("""
        "x-entry": { "anything": [] },
        "assets": [
          { "type": "self", "x": 1,
            "place": [ { "src": "a.dll", "dest": "b.dll", "type": "dir" } ] },
          { "type": "mcmod", "urls": [ "https://a.example/a.mcmod" ], "sha256": "?" }
        ],
        "hooks": { "pre_pack": [ "x" ], "install": 5 }
        """);
    document["info"] = JsonNode.Parse("""{ "name": "n", "author": 5, "tags": [] }""");
    document["x-root"] = JsonNode.Parse("[ 1, 2 ]");
    document["dependencies"] = JsonNode.Parse("""{ "not a key": 5 }""");

    // Act
    ToothEntry entry = ParseEntry(document);

    // Assert
    Assert.Equal(
        [new EntryPlacement("a.dll", "b.dll")], entry.Assets[0].Placements);
    Assert.Equal(PlacementForm.File, entry.Assets[0].Placements[0].Form);
    Assert.NotNull(entry.McmodAsset);
    Assert.Empty(entry.Hooks.PreInstall);
    Assert.Empty(entry.Hooks.PostInstall);
    Assert.True(entry.Json.TryGetProperty("x-entry", out _));
  }

  [Theory]
  [InlineData("[]")]
  [InlineData("\"tooth\"")]
  [InlineData("null")]
  public void Parse_RootNotAnObject_Throws(string json) {
    // Act & Assert
    Assert.Contains("at $:", AssertInvalid(json).Message);
  }

  [Fact]
  public void Parse_Comment_Throws() {
    // Arrange
    string json = Valid().ToJsonString().Insert(1, "// comment\n");

    // Act & Assert
    Assert.Contains("not strict JSON", AssertInvalid(json).Message);
  }

  [Fact]
  public void Parse_TrailingComma_Throws() {
    // Arrange
    string json = Valid().ToJsonString()[..^1] + ",}";

    // Act & Assert
    Assert.Contains("not strict JSON", AssertInvalid(json).Message);
  }

  [Fact]
  public void Parse_DuplicateMember_Throws() {
    // Arrange
    string json = Valid().ToJsonString()[..^1] + ",\"version\":\"1.2.3\"}";

    // Act & Assert
    Assert.Contains("not strict JSON", AssertInvalid(json).Message);
  }

  [Fact]
  public void Parse_DuplicateMemberInEntry_Throws() {
    // Arrange
    string json = WithMembers(""" "hooks": {} """).ToJsonString()
        .Replace("\"hooks\":{}", "\"hooks\":{},\"hooks\":{}");

    // Act & Assert
    Assert.Contains("not strict JSON", AssertInvalid(json).Message);
  }

  [Fact]
  public void Parse_InvalidUtf8_Throws() {
    // Arrange
    byte[] bytes = [.. Encoding.UTF8.GetBytes(Valid().ToJsonString()[..^1] + ",\"x\":\""),
                    0xC3, 0x28, .. "\"}"u8];

    // Act
    ToothFormatException ex = Assert.Throws<ToothFormatException>(() => ToothParser.Parse(bytes));

    // Assert
    Assert.Contains("UTF-8", ex.Message);
  }

  [Fact]
  public void Parse_LoneSurrogateEscape_Throws() {
    // Arrange
    string json = Valid().ToJsonString().Replace("\"1.2.3\"", "\"\\uD800\"");

    // Act & Assert
    Assert.Contains("$.version", AssertInvalid(json).Message);
  }

  [Fact]
  public void Parse_ByteOrderMark_IsIgnored() {
    // Arrange
    byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(Valid().ToJsonString())];

    // Act
    ToothFile file = ToothParser.Parse(bytes);

    // Assert
    Assert.Equal("example.com/owner/tool", file.Tooth);
  }

  [Theory]
  [InlineData("""{ "format_version": 4 }""", 4)]
  [InlineData("""{ "format_version": 3.0 }""", 3)]
  [InlineData("""{ "format_version": 1 }""", 1)]
  [InlineData("""{ "format_version": 2.5 }""", null)]
  [InlineData("""{ "format_version": "3" }""", null)]
  [InlineData("""{ }""", null)]
  [InlineData("""[ 4 ]""", null)]
  public void FormatVersion_ReadsIntegerFormatVersion(string json, int? expected) {
    // Arrange
    using JsonDocument document = JsonDocument.Parse(json);

    // Act
    int? version = ToothParser.FormatVersion(document.RootElement);

    // Assert
    Assert.Equal(expected, version);
  }

  [Fact]
  public void ParseEntry_Format4Entry_IsValidated() {
    // Arrange
    using JsonDocument valid = JsonDocument.Parse("""
        { "platform": "client", "runtime": "linux-x64",
          "assets": [ { "type": "zip", "urls": [ "https://a.example/x.zip" ] } ] }
        """);
    using JsonDocument invalid = JsonDocument.Parse("""
        { "platform": "client", "runtime": "linux-x64",
          "assets": [ { "type": "tar", "urls": [ "https://a.example/x.tar" ] } ] }
        """);

    // Act
    ToothEntry entry = ToothParser.ParseEntry(valid.RootElement);

    // Assert
    Assert.Equal(McmodPlatform.Client, entry.Platform);
    Assert.Equal("linux-x64", entry.Runtime);
    Assert.False(entry.Derived);
    Assert.Equal(AssetType.Zip, Assert.Single(entry.Assets).Type);
    Assert.Throws<ToothFormatException>(
        () => ToothParser.ParseEntry(invalid.RootElement));
  }

  [Theory]
  [InlineData("win-x64", true)]
  [InlineData("linux-musl-arm64", true)]
  [InlineData("osx.13-arm64", true)]
  [InlineData("win_x64", true)]
  [InlineData("", false)]
  [InlineData("win-*", false)]
  [InlineData("win-", false)]
  [InlineData("win x64", false)]
  [InlineData("win-x64\n", false)]
  public void IsRuntime_ChecksRuntimeIdentifier(string value, bool expected) {
    // Act & Assert
    Assert.Equal(expected, ToothParser.IsRuntime(value));
  }

  [Theory]
  [InlineData("LeviLamina", true)]
  [InlineData("a", true)]
  [InlineData("0a._-b", true)]
  [InlineData("", false)]
  [InlineData("-a", false)]
  [InlineData(".a", false)]
  [InlineData("a b", false)]
  [InlineData("a/b", false)]
  [InlineData("a\n", false)]
  public void PackageNamesIsValid_ChecksId(string value, bool expected) {
    // Act & Assert
    Assert.Equal(expected, PackageNames.IsValid(value));
  }
}
