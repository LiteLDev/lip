using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod.Parsing;

/// Validation of package files (lip.schema.toml and protocol/package.md §3), which are TOML files
/// (protocol/README.md); the tests build them as JSON values, written as TOML. A value TOML cannot
/// hold (null, a root that is not a table) is validated as a JSON value.
public class PackageFileParserTests {
  /// A minimal valid package file.
  private static JsonObject Valid() => JsonNode.Parse("""
      {
        "format": 4,
        "id": "example.com/owner/tool",
        "version": "1.2.3",
        "variants": {
          "": [ { "side": "server", "platform": "win-x64" } ]
        }
      }
      """)!.AsObject();

  /// A valid package file whose variant "" has this single entry.
  private static JsonObject WithEntry(string entry) {
    JsonObject document = Valid();
    document["variants"] = new JsonObject { [""] = new JsonArray(JsonNode.Parse(entry)) };
    return document;
  }

  /// A valid package file whose single entry has these members besides side and platform.
  private static JsonObject WithMembers(string members) =>
      WithEntry($$"""{ "side": "server", "platform": "win-x64", {{members}} }""");

  private static PackageFile Parse(JsonNode? document) {
    if (document is JsonObject && !HasNull(document)) {
      return PackageFileParser.Parse(TomlJson.SerializeToUtf8Bytes(Element(document)));
    }

    return PackageFileParser.Parse(Element(document));
  }

  private static JsonElement Element(JsonNode? node) {
    using JsonDocument json = JsonDocument.Parse(node?.ToJsonString() ?? "null");
    return json.RootElement.Clone();
  }

  private static bool HasNull(JsonNode? node) => node switch {
    null => true,
    JsonObject o => o.Any(m => HasNull(m.Value)),
    JsonArray a => a.Any(HasNull),
    _ => false,
  };

  private static PackageEntry ParseEntry(JsonNode document) =>
      Assert.Single(Parse(document).Variants[""]);

  private static PackageFormatException AssertInvalid(JsonNode document, string path) {
    PackageFormatException ex = Assert.Throws<PackageFormatException>(() => Parse(document));
    Assert.Contains($"at {path}", ex.Message);
    return ex;
  }

  private static PackageFormatException AssertInvalid(string toml) =>
      Assert.Throws<PackageFormatException>(() => PackageFileParser.Parse(Encoding.UTF8.GetBytes(toml)));

  private static PackageFormatException AssertInvalidJson(string json) =>
      Assert.Throws<PackageFormatException>(() => Parse(JsonNode.Parse(json)));

  [Fact]
  public void Parse_MinimalDocument_ReadsMembers() {
    // Arrange
    JsonObject document = Valid();

    // Act
    PackageFile file = Parse(document);

    // Assert
    Assert.Equal("example.com/owner/tool", file.Id);
    Assert.Equal(SemVersion.Parse("1.2.3"), file.Version);
    Assert.Equal([""], file.Variants.Keys);
    PackageEntry entry = Assert.Single(file.Variants[""]);
    Assert.Equal(McmodSide.Server, entry.Side);
    Assert.Equal("win-x64", entry.Platform);
    Assert.Empty(entry.Dependencies);
    Assert.Empty(entry.Assets);
    Assert.Empty(entry.PreserveFiles);
    Assert.Empty(entry.RemoveFiles);
    Assert.Empty(entry.Hooks.PreInstall);
    Assert.Empty(entry.Hooks.PostInstall);
    Assert.Empty(entry.Hooks.PreUninstall);
    Assert.Empty(entry.Hooks.PostUninstall);
    Assert.Null(entry.McmodAsset);
    Assert.Null(entry.McmodAsset);
    Assert.Equal("server", entry.Json.GetProperty("side").GetString());
    Assert.Equal(4, file.Json.GetProperty("format").GetInt32());
  }

  [Fact]
  public void Parse_HandWrittenToml_ReadsMembers() {
    // Arrange
    byte[] toml = Encoding.UTF8.GetBytes("""
        # lip.toml
        format = 4
        id = "example.com/owner/tool"
        version = "1.0.0"

        [info]
        name = 'true'
        tags = ["null", "~"]

        [[variants.""]]
        side = "server"
        platform = "win-x64"
        hooks.post_install = ["echo yes"]

        [variants."".deps]
        "example.com/a" = "^1.0.0"
        """);

    // Act
    PackageFile file = PackageFileParser.Parse(toml);

    // Assert
    Assert.Equal(SemVersion.Parse("1.0.0"), file.Version);
    Assert.Equal("true", file.Json.GetProperty("info").GetProperty("name").GetString());
    Assert.Equal(
        ["null", "~"],
        file.Json.GetProperty("info").GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    PackageEntry entry = Assert.Single(file.Variants[""]);
    Assert.Equal("example.com/a", Assert.Single(entry.Dependencies).Source.Target);
    Assert.Equal(["echo yes"], entry.Hooks.PostInstall);
  }

  [Theory]
  [InlineData("version = 1.0", "$.version")]
  [InlineData("version = true", "$.version")]
  [InlineData("version = []", "$.version")]
  [InlineData("format = \"4\"", "$.format")]
  [InlineData("format = 4.5", "$.format")]
  public void Parse_ValueOfOtherType_Throws(string line, string path) {
    // Arrange
    string key = line[..line.IndexOf(' ')];
    string toml = """
        format = 4
        id = "example.com/owner/tool"
        version = "1.2.3"
        variants = { "" = [ { side = "server", platform = "win-x64" } ] }
        """;
    toml = string.Join('\n', toml.Split('\n').Select(l => l.StartsWith($"{key} =") ? line : l));

    // Act & Assert
    Assert.Contains(path, AssertInvalid(toml).Message);
  }

  [Fact]
  public void Parse_FullDocument_ReadsEveryMember() {
    // Arrange
    JsonObject document = WithMembers("""
        "deps": { "example.com/a": "^1.0.0", "example.com/b#lua": ">=2.0.0 <3.0.0" },
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
    PackageEntry entry = ParseEntry(document);

    // Assert
    Assert.Equal(
        [("example.com/a", "", "^1.0.0"), ("example.com/b", "lua", ">=2.0.0 <3.0.0")],
        entry.Dependencies.Select(d => (d.Source.Target, d.Source.Variant, d.Range.Text)));
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
  [InlineData("format")]
  [InlineData("id")]
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
  [InlineData("4.5")]
  [InlineData("\"4\"")]
  [InlineData("null")]
  public void Parse_OtherFormat_Throws(string value) {
    // Arrange
    JsonObject document = Valid();
    document["format"] = JsonNode.Parse(value);

    // Act & Assert
    AssertInvalid(document, "$.format");
  }

  [Fact]
  public void Parse_NewerFormat_ThrowsAskingToUpdateLip() {
    // Arrange
    JsonObject document = Valid();
    document["format"] = 5;

    // Act
    NewerPackageFormatException ex =
        Assert.Throws<NewerPackageFormatException>(() => Parse(document));

    // Assert
    Assert.Contains("update lip", ex.Message);
  }

  [Fact]
  public void Parse_ToothJsonOfFormatVersion4_Throws() {
    // Arrange: the keys of the unreleased tooth.json of format_version 4 are not read.
    JsonObject document = JsonNode.Parse("""
        {
          "format_version": 4,
          "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
          "tooth": "example.com/owner/tool",
          "version": "1.2.3",
          "variants": { "": [ { "side": "server", "platform": "win-x64" } ] }
        }
        """)!.AsObject();

    // Act & Assert
    AssertInvalid(document, "$.format");
  }

  [Theory]
  [InlineData("github.com/LiteLDev/LeviLamina")]
  [InlineData("example.com/a/v2")]
  [InlineData("example.com/a.b-c_d~e")]
  public void Parse_GoModulePath_IsAccepted(string id) {
    // Arrange
    JsonObject document = Valid();
    document["id"] = id;

    // Act
    PackageFile file = Parse(document);

    // Assert
    Assert.Equal(id, file.Id);
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
  public void Parse_NotAGoModulePath_Throws(string id) {
    // Arrange
    JsonObject document = Valid();
    document["id"] = id;

    // Act & Assert
    AssertInvalid(document, "$.id");
  }

  [Fact]
  public void Parse_IdNotAString_Throws() {
    // Arrange
    JsonObject document = Valid();
    document["id"] = 1;

    // Act & Assert
    AssertInvalid(document, "$.id");
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
    PackageFile file = Parse(document);

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
        """[ { "side": "client", "platform": "win-x64" } ]""");

    // Act
    PackageFile file = Parse(document);

    // Assert
    Assert.Equal(["", variant], file.Variants.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(McmodSide.Client, Assert.Single(file.Variants[variant]).Side);
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
        """[ { "side": "server", "platform": "win-x64" } ]""");

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"{variant}\"]");
  }

  [Fact]
  public void Parse_NoVariants_IsAccepted() {
    // Arrange
    JsonObject document = Valid();
    document["variants"] = new JsonObject();

    // Act
    PackageFile file = Parse(document);

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
  public void Parse_NotASide_Throws(string side) {
    // Arrange
    JsonObject document = WithEntry($$"""{ "side": {{side}}, "platform": "win-x64" }""");

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].side");
  }

  [Theory]
  [InlineData("side")]
  [InlineData("platform")]
  public void Parse_EntryWithoutSideOrPlatform_Throws(string key) {
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
  public void Parse_Platform_IsAccepted(string platform) {
    // Arrange
    JsonObject document = WithEntry($$"""{ "side": "server", "platform": "{{platform}}" }""");

    // Act
    PackageEntry entry = ParseEntry(document);

    // Assert
    Assert.Equal(platform, entry.Platform);
  }

  [Theory]
  [InlineData("\"\"")]
  [InlineData("\"win-*\"")]
  [InlineData("\"win x64\"")]
  [InlineData("\"win--x64\"")]
  [InlineData("\"-win\"")]
  [InlineData("1")]
  public void Parse_NotAPlatform_Throws(string platform) {
    // Arrange
    JsonObject document = WithEntry($$"""{ "side": "server", "platform": {{platform}} }""");

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].platform");
  }

  /// An entry of lip before v0.40 has "platform" for the side and "runtime" for the platform;
  /// the error names the rename instead of a missing "side".
  [Theory]
  [InlineData("""{ "platform": "server", "runtime": "win-x64" }""")]
  [InlineData("""{ "platform": "client", "runtime": "win-x64" }""")]
  [InlineData("""{ "platform": "server" }""")]
  [InlineData("""{ "runtime": "win-x64" }""")]
  [InlineData("""{ "side": "server", "platform": "win-x64", "runtime": "win-x64" }""")]
  public void Parse_EntryWithOldEnvironmentKeys_ThrowsRenameMessage(string entry) {
    // Arrange
    JsonObject document = WithEntry(entry);

    // Act
    PackageFormatException ex = Assert.Throws<PackageFormatException>(() => Parse(document));

    // Assert
    Assert.Equal(
        "Invalid package file at $.variants[\"\"][0]: uses the keys \"platform\" and"
        + " \"runtime\" of lip before v0.40; rename \"platform\" to \"side\" and"
        + " \"runtime\" to \"platform\".",
        ex.Message);
  }

  [Fact]
  public void Parse_HandWrittenTomlWithOldEnvironmentKeys_ThrowsRenameMessage() {
    // Arrange: a lip.toml written for lip v0.39.
    byte[] toml = Encoding.UTF8.GetBytes("""
        format = 4
        id = "example.com/owner/tool"
        version = "1.0.0"

        [[variants.client]]
        platform = "client"
        runtime = "win-x64"
        """);

    // Act
    PackageFormatException ex =
        Assert.Throws<PackageFormatException>(() => PackageFileParser.Parse(toml));

    // Assert
    Assert.StartsWith(
        "Invalid package file at $.variants[\"client\"][0]: uses the keys", ex.Message);
    Assert.Contains(PackageFileParser.OldEnvironmentKeysReason, ex.Message);
  }

  [Fact]
  public void Parse_TwoEntriesWithSameSideAndPlatform_Throws() {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![""] = JsonNode.Parse("""
        [
          { "side": "server", "platform": "win-x64" },
          { "side": "client", "platform": "win-x64" },
          { "side": "server", "platform": "win-x64", "hooks": {} }
        ]
        """);

    // Act
    PackageFormatException ex = AssertInvalid(document, "$.variants[\"\"][2]");

    // Assert
    Assert.Contains("$.variants[\"\"][0]", ex.Message);
  }

  [Fact]
  public void Parse_EntriesDifferingInSidePlatformOrVariant_AreAccepted() {
    // Arrange
    JsonObject document = Valid();
    document["variants"] = JsonNode.Parse("""
        {
          "": [
            { "side": "server", "platform": "win-x64" },
            { "side": "client", "platform": "win-x64" },
            { "side": "server", "platform": "linux-x64" }
          ],
          "lua": [ { "side": "server", "platform": "win-x64" } ]
        }
        """);

    // Act
    PackageFile file = Parse(document);

    // Assert
    Assert.Equal(
        [(McmodSide.Server, "win-x64"), (McmodSide.Client, "win-x64"),
         (McmodSide.Server, "linux-x64")],
        file.Variants[""].Select(e => (e.Side, e.Platform)));
    Assert.Same(
        file.Variants[""][1],
        file.SelectEntry("", new LipEnvironment(McmodSide.Client, "win-x64")));
    Assert.Null(file.SelectEntry("", new LipEnvironment(McmodSide.Client, "linux-x64")));
    Assert.Null(file.SelectEntry("js", new LipEnvironment(McmodSide.Server, "win-x64")));
  }

  [Theory]
  [InlineData("example.com/a", "example.com/a", "")]
  [InlineData("example.com/a/v2#lua", "example.com/a/v2", "lua")]
  [InlineData("example.com/a#engine/lua", "example.com/a", "engine/lua")]
  public void Parse_DependencyKey_IsAccepted(string key, string id, string variant) {
    // Arrange
    JsonObject document = WithMembers($$""" "deps": { "{{key}}": "*" } """);

    // Act
    PackageDependency dependency = Assert.Single(ParseEntry(document).Dependencies);

    // Assert
    Assert.Equal(RefKind.Spec, dependency.Source.Kind);
    Assert.Equal(id, dependency.Source.Target);
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
  public void Parse_DependencyKeyNotAPackageId_Throws(string key) {
    // Arrange
    JsonObject document = WithMembers($$""" "deps": { "{{key}}": "*" } """);

    // Act & Assert
    AssertInvalid(document, $"$.variants[\"\"][0].deps[\"{key}\"]");
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
    JsonObject document = WithMembers($$""" "deps": { "example.com/a": "{{range}}" } """);

    // Act
    PackageDependency dependency = Assert.Single(ParseEntry(document).Dependencies);

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
    JsonObject document = WithMembers($$""" "deps": { "example.com/a": {{range}} } """);

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].deps[\"example.com/a\"]");
  }

  [Fact]
  public void Parse_DependenciesNotAnObject_Throws() {
    // Arrange
    JsonObject document = WithMembers(""" "deps": [ "example.com/a" ] """);

    // Act & Assert
    AssertInvalid(document, "$.variants[\"\"][0].deps");
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
    PackageFormatException ex = AssertInvalid(document, "$.variants[\"\"][0].assets[0].place");

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
    PackageEntry entry = ParseEntry(document);

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
    PackageFormatException ex = AssertInvalid(document, "$.variants[\"\"][0].assets");

    // Assert
    Assert.Contains("mcmod", ex.Message);
  }

  [Fact]
  public void Parse_McmodAssetsInDifferentEntries_AreAccepted() {
    // Arrange
    JsonObject document = Valid();
    document["variants"]![""] = JsonNode.Parse("""
        [
          { "side": "server", "platform": "win-x64",
            "assets": [ { "type": "mcmod", "urls": [ "https://a.example/s.mcmod" ] } ] },
          { "side": "client", "platform": "win-x64",
            "assets": [ { "type": "mcmod", "urls": [ "https://a.example/c.mcmod" ] } ] }
        ]
        """);

    // Act
    PackageFile file = Parse(document);

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
    PackageEntry entry = ParseEntry(document);

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
    PackageFile file = Parse(document);

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
    PackageFile file = Parse(document);

    // Assert
    Assert.Equal("example.com/owner/tool", file.Id);
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
    document["deps"] = JsonNode.Parse("""{ "not a key": 5 }""");
    document["format_uuid"] = 5;
    document["tooth"] = 5;

    // Act
    PackageEntry entry = ParseEntry(document);

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
  [InlineData("\"lip\"")]
  [InlineData("null")]
  public void Parse_RootNotAnObject_Throws(string json) {
    // Act & Assert
    Assert.Contains("at $:", AssertInvalidJson(json).Message);
  }

  [Fact]
  public void Parse_Comment_IsIgnored() {
    // Arrange
    string toml = "# comment\n" + TomlJson.Serialize(Valid());

    // Act
    PackageFile file = PackageFileParser.Parse(Encoding.UTF8.GetBytes(toml));

    // Assert
    Assert.Equal("example.com/owner/tool", file.Id);
  }

  [Theory]
  [InlineData("{ \"format\": 4 }")]
  [InlineData("format: 4")]
  [InlineData("format = ")]
  public void Parse_NotToml_Throws(string text) {
    // Act & Assert
    Assert.Contains("not valid TOML", AssertInvalid(text).InnerException!.Message);
  }

  [Fact]
  public void Parse_DuplicateKey_Throws() {
    // Arrange
    string toml = "version = \"1.2.3\"\n" + TomlJson.Serialize(Valid());

    // Act & Assert
    Assert.Contains("not valid TOML", AssertInvalid(toml).InnerException!.Message);
  }

  [Theory]
  [InlineData("released = 1979-05-27T07:32:00Z")]
  [InlineData("released = 1979-05-27")]
  [InlineData("released = 07:32:00")]
  [InlineData("size = inf")]
  [InlineData("size = nan")]
  public void Parse_ValueJsonCannotHold_Throws(string line) {
    // Arrange
    string toml = line + "\n" + TomlJson.Serialize(Valid());

    // Act & Assert
    AssertInvalid(toml);
  }

  [Fact]
  public void Parse_InvalidUtf8_Throws() {
    // Arrange
    byte[] bytes = [.. Encoding.UTF8.GetBytes(TomlJson.Serialize(Valid()) + "x = \""),
                    0xC3, 0x28, .. "\"\n"u8];

    // Act
    PackageFormatException ex = Assert.Throws<PackageFormatException>(() => PackageFileParser.Parse(bytes));

    // Assert
    Assert.Contains("UTF-8", ex.InnerException!.Message);
  }

  [Fact]
  public void Parse_SurrogateEscape_Throws() {
    // Arrange
    string toml = TomlJson.Serialize(Valid()).Replace("\"1.2.3\"", "\"\\uD800\"");

    // Act & Assert
    AssertInvalid(toml);
  }

  [Fact]
  public void Parse_ByteOrderMark_IsIgnored() {
    // Arrange
    byte[] bytes = [.. Encoding.UTF8.Preamble, .. TomlJson.SerializeToUtf8Bytes(Element(Valid()))];

    // Act
    PackageFile file = PackageFileParser.Parse(bytes);

    // Assert
    Assert.Equal("example.com/owner/tool", file.Id);
  }

  [Theory]
  [InlineData("""{ "format": 4 }""", 4)]
  [InlineData("""{ "format": 3.0 }""", 3)]
  [InlineData("""{ "format": 1 }""", 1)]
  [InlineData("""{ "format": 2.5 }""", null)]
  [InlineData("""{ "format": "3" }""", null)]
  [InlineData("""{ "format_version": 4 }""", null)]
  [InlineData("""{ }""", null)]
  [InlineData("""[ 4 ]""", null)]
  public void Format_ReadsIntegerFormat(string json, int? expected) {
    // Arrange
    using JsonDocument document = JsonDocument.Parse(json);

    // Act
    int? format = PackageFileParser.Format(document.RootElement);

    // Assert
    Assert.Equal(expected, format);
  }

  [Fact]
  public void ParseEntry_Entry_IsValidated() {
    // Arrange
    using JsonDocument valid = JsonDocument.Parse("""
        { "side": "client", "platform": "linux-x64",
          "assets": [ { "type": "zip", "urls": [ "https://a.example/x.zip" ] } ] }
        """);
    using JsonDocument invalid = JsonDocument.Parse("""
        { "side": "client", "platform": "linux-x64",
          "assets": [ { "type": "tar", "urls": [ "https://a.example/x.tar" ] } ] }
        """);

    // Act
    PackageEntry entry = PackageFileParser.ParseEntry(valid.RootElement);

    // Assert
    Assert.Equal(McmodSide.Client, entry.Side);
    Assert.Equal("linux-x64", entry.Platform);
    Assert.Equal(AssetType.Zip, Assert.Single(entry.Assets).Type);
    Assert.Throws<PackageFormatException>(
        () => PackageFileParser.ParseEntry(invalid.RootElement));
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
  public void IsPlatform_ChecksRuntimeIdentifier(string value, bool expected) {
    // Act & Assert
    Assert.Equal(expected, PackageFileParser.IsPlatform(value));
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
  public void PackageNamesIsValid_ChecksModName(string value, bool expected) {
    // Act & Assert
    Assert.Equal(expected, PackageNames.IsValid(value));
  }
}
