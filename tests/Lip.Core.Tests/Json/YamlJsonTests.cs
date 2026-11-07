using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Json;

namespace Lip.Core.Tests.Json;

/// YAML files (protocol/README.md): a YAML stream of one document read with the YAML 1.2 core
/// schema as the JSON value it represents, and JSON values written so that they read back as the
/// same value.
public class YamlJsonTests {
  private static string Read(string yaml) {
    using JsonDocument document = YamlJson.Parse(yaml);
    return document.RootElement.GetRawText();
  }

  private static string Compact(string json) =>
      JsonNode.Parse(json)!.ToJsonString();

  [Theory]
  [InlineData("a: null", """{"a":null}""")]
  [InlineData("a: Null", """{"a":null}""")]
  [InlineData("a: NULL", """{"a":null}""")]
  [InlineData("a: ~", """{"a":null}""")]
  [InlineData("a:", """{"a":null}""")]
  [InlineData("a: true", """{"a":true}""")]
  [InlineData("a: True", """{"a":true}""")]
  [InlineData("a: FALSE", """{"a":false}""")]
  [InlineData("a: 4", """{"a":4}""")]
  [InlineData("a: -12", """{"a":-12}""")]
  [InlineData("a: +12", """{"a":12}""")]
  [InlineData("a: 007", """{"a":7}""")]
  [InlineData("a: 0o17", """{"a":15}""")]
  [InlineData("a: 0x1F", """{"a":31}""")]
  [InlineData("a: 123456789012345678901234567890", """{"a":123456789012345678901234567890}""")]
  [InlineData("a: 1.5", """{"a":1.5}""")]
  [InlineData("a: .5", """{"a":0.5}""")]
  [InlineData("a: 1.", """{"a":1}""")]
  [InlineData("a: -1e3", """{"a":-1000}""")]
  public void Parse_PlainScalar_ResolvesByCoreSchema(string yaml, string json) {
    // Act & Assert
    Assert.Equal(json, Read(yaml));
  }

  [Theory]
  [InlineData("a: yes", "yes")]
  [InlineData("a: on", "on")]
  [InlineData("a: nULL", "nULL")]
  [InlineData("a: tRUE", "tRUE")]
  [InlineData("a: 1.0.0", "1.0.0")]
  [InlineData("a: 0x", "0x")]
  [InlineData("a: 0b101", "0b101")]
  [InlineData("a: 1_000", "1_000")]
  [InlineData("a: nan", "nan")]
  [InlineData("a: ^1.0.0", "^1.0.0")]
  [InlineData("a: https://example.com/a.zip", "https://example.com/a.zip")]
  [InlineData("a: '1'", "1")]
  [InlineData("a: \"true\"", "true")]
  [InlineData("a: ''", "")]
  [InlineData("a: \"null\"", "null")]
  [InlineData("a: !!str 5", "5")]
  [InlineData("a: !!str .inf", ".inf")]
  [InlineData("a: !!str -.Inf", "-.Inf")]
  [InlineData("a: !!str .nan", ".nan")]
  [InlineData("a: !!str 1e999", "1e999")]
  [InlineData("a: !!str \".nan\"", ".nan")]
  [InlineData("a: !!str null", "null")]
  [InlineData("a: ! 5", "5")]
  [InlineData("a: |\n  x\n  y\n", "x\ny\n")]
  [InlineData("a: >-\n  4\n", "4")]
  public void Parse_String_IsString(string yaml, string value) {
    // Act
    using JsonDocument document = YamlJson.Parse(yaml);

    // Assert
    JsonElement a = document.RootElement.GetProperty("a");
    Assert.Equal(JsonValueKind.String, a.ValueKind);
    Assert.Equal(value, a.GetString());
  }

  [Theory]
  [InlineData("a: !!int 5", """{"a":5}""")]
  [InlineData("a: !!int 0x10", """{"a":16}""")]
  [InlineData("a: !!float 5", """{"a":5}""")]
  [InlineData("a: !!bool true", """{"a":true}""")]
  [InlineData("a: !!null ''", """{"a":null}""")]
  [InlineData("a: !!seq [1]", """{"a":[1]}""")]
  [InlineData("a: !!map {b: 1}", """{"a":{"b":1}}""")]
  public void Parse_CoreSchemaTag_IsResolved(string yaml, string json) {
    // Act & Assert
    Assert.Equal(json, Read(yaml));
  }

  [Fact]
  public void Parse_BlockAndFlowCollections_ReadAsJson() {
    // Arrange
    const string yaml = """
        # comment
        format: 4
        id: example.com/a
        variants:
          "":
            - platform: server
              runtime: win-x64
              assets: [ { type: self, place: [] } ]
          lua: []
        info: {}
        """;

    // Act & Assert
    Assert.Equal(
        Compact("""
            { "format": 4, "id": "example.com/a",
              "variants": {
                "": [ { "platform": "server", "runtime": "win-x64",
                        "assets": [ { "type": "self", "place": [] } ] } ],
                "lua": [] },
              "info": {} }
            """),
        Read(yaml));
  }

  [Fact]
  public void Parse_Json_ReadsAsTheSameValue() {
    // Arrange: JSON is YAML.
    const string json = """
        { "a": [1, -2.5, true, false, null, "x\tyé\\/"], "": {}, "b": "😀" }
        """;

    // Act & Assert
    Assert.Equal(Compact(json), Compact(Read(json)));
  }

  [Fact]
  public void Parse_Alias_IsExpanded() {
    // Act & Assert
    Assert.Equal("""{"a":[1,2],"b":[1,2]}""", Read("a: &x [1, 2]\nb: *x\n"));
  }

  [Theory]
  [InlineData("", "no YAML document")]
  [InlineData("# only a comment\n", "no YAML document")]
  [InlineData("a: 1\n---\nb: 2\n", "more than one YAML document")]
  [InlineData("a: [1\n", "not valid YAML")]
  [InlineData("a: 1\na: 2\n", "appears twice")]
  [InlineData("1: a\n", "is not a string")]
  [InlineData("null: a\n", "is not a string")]
  [InlineData("true: a\n", "is not a string")]
  [InlineData("? [a]\n: b\n", "is not a string")]
  [InlineData("a: *x\n", "names no earlier anchor")]
  [InlineData("a: .inf\n", "cannot be represented")]
  [InlineData("a: -.Inf\n", "cannot be represented")]
  [InlineData("a: .NaN\n", "cannot be represented")]
  [InlineData("a: 1e999\n", "out of range")]
  [InlineData("a: !!int abc\n", "is not valid")]
  [InlineData("a: !!bool 1\n", "is not valid")]
  [InlineData("a: !!null x\n", "is not valid")]
  [InlineData("a: !!float .inf\n", "cannot be represented")]
  [InlineData("a: !!float 1e999\n", "out of range")]
  [InlineData("a: !custom x\n", "is not valid")]
  [InlineData("a: !!map [1]\n", "is not valid here")]
  public void Parse_NotAJsonValue_Throws(string yaml, string reason) {
    // Act & Assert
    YamlFormatException ex = Assert.Throws<YamlFormatException>(() => YamlJson.Parse(yaml));
    Assert.Contains(reason, ex.Message);
  }

  [Fact]
  public void Parse_AliasExpansionBomb_Throws() {
    // Arrange: every level doubles the nodes of the one before.
    StringBuilder yaml = new("l0: &l0 [a, a, a, a, a, a, a, a, a, a]\n");
    for (int i = 1; i < 12; i++) {
      yaml.Append($"l{i}: &l{i} [*l{i - 1}, *l{i - 1}, *l{i - 1}, *l{i - 1}, *l{i - 1}]\n");
    }

    // Act & Assert
    Assert.Contains(
        "expands to more than",
        Assert.Throws<YamlFormatException>(() => YamlJson.Parse(yaml.ToString())).Message);
  }

  [Fact]
  public void Parse_DeepNesting_Throws() {
    // Arrange
    string yaml = new string('[', 100) + new string(']', 100);

    // Act & Assert
    Assert.Throws<YamlFormatException>(() => YamlJson.Parse(yaml));
  }

  [Fact]
  public void Parse_ByteOrderMarks_AreHonored() {
    // Arrange
    byte[] utf8 = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("a: é")];
    byte[] utf16 = [.. Encoding.Unicode.Preamble, .. Encoding.Unicode.GetBytes("a: é")];

    // Act & Assert
    Assert.Equal(Compact("""{"a":"é"}"""), Compact(ReadBytes(utf8)));
    Assert.Equal(Compact("""{"a":"é"}"""), Compact(ReadBytes(utf16)));
  }

  [Fact]
  public void Parse_InvalidUtf8_Throws() {
    // Arrange
    byte[] bytes = [.. "a: "u8, 0xC3, 0x28];

    // Act & Assert
    Assert.Contains(
        "UTF-8", Assert.Throws<YamlFormatException>(() => YamlJson.Parse(bytes)).Message);
  }

  [Fact]
  public void Serialize_Document_WritesBlockYaml() {
    // Arrange
    using JsonDocument document = JsonDocument.Parse("""
        {
          "format": 4,
          "id": "example.com/a",
          "version": "1.0",
          "variants": { "": [ { "platform": "server", "deps": {}, "purge": [] } ] },
          "n": [ 1, -2.5, true, null, [ "x" ] ]
        }
        """);

    // Act
    string yaml = YamlJson.Serialize(document.RootElement);

    // Assert
    Assert.Equal("""
        format: 4
        id: example.com/a
        version: "1.0"
        variants:
          "":
          - platform: server
            deps: {}
            purge: []
        "n":
        - 1
        - -2.5
        - true
        - null
        - - x

        """.ReplaceLineEndings("\n"), yaml);
  }

  public static TheoryData<string> Strings() => [
    "",
    " ",
    "1.0",
    "1",
    "-1",
    "0x10",
    "0o7",
    "1e3",
    ".5",
    ".inf",
    "-.Inf",
    ".nan",
    "true",
    "False",
    "null",
    "~",
    "NULL",
    "yes",
    "a: b",
    "a:b",
    "- a",
    "-",
    "? a",
    "#a",
    "a #b",
    "*a",
    "&a",
    "!a",
    "%a",
    "@a",
    "`a",
    "|",
    ">",
    "{a}",
    "[a]",
    "a, b",
    "'",
    "\"",
    "\\",
    " lead",
    "trail ",
    "multi\nline",
    "multi\nline\n",
    "\n",
    "tab\there",
    "\u0001\u007f",
    "\u0085",
    "a\u0085b",
    "\u2028",
    "a\u2028b",
    "\u2029",
    "a\u2029b",
    "a\u2028\nb\u2029",
    "中文 é",
    "😀",
    "---",
    "...",
    "https://example.com/a.zip#x",
    new string('x', 500),
    "^1.0.0 || >=2.0.0",
  ];

  [Theory]
  [MemberData(nameof(Strings))]
  public void Serialize_String_ReadsBackAsTheSameString(string value) {
    // Arrange
    JsonObject document = new() { [value] = value, ["list"] = new JsonArray(value) };

    // Act
    string yaml = YamlJson.Serialize(document);

    // Assert
    using JsonDocument read = YamlJson.Parse(yaml);
    Assert.Equal(value, read.RootElement.GetProperty(value).GetString());
    Assert.Equal(value, read.RootElement.GetProperty("list")[0].GetString());
  }

  [Theory]
  [InlineData("null")]
  [InlineData("true")]
  [InlineData("4")]
  [InlineData("-0.5")]
  [InlineData("1E+20")]
  [InlineData("123456789012345678901234567890")]
  [InlineData("\"x\"")]
  [InlineData("[]")]
  [InlineData("{}")]
  [InlineData("[[], {}, [[1]], {\"a\": {\"b\": []}}]")]
  public void Serialize_Value_ReadsBackAsTheSameValue(string json) {
    // Arrange
    using JsonDocument document = JsonDocument.Parse(json);

    // Act
    string yaml = YamlJson.Serialize(document.RootElement);

    // Assert
    using JsonDocument read = YamlJson.Parse(yaml);
    Assert.True(JsonElement.DeepEquals(document.RootElement, read.RootElement), yaml);
    Assert.EndsWith("\n", yaml);
  }

  [Theory]
  [InlineData("yes")]
  [InlineData("Off")]
  [InlineData("n")]
  public void Serialize_Yaml11Boolean_IsQuoted(string value) {
    // Arrange
    JsonObject document = new() { ["a"] = value };

    // Act
    string yaml = YamlJson.Serialize(document);

    // Assert
    Assert.Equal($"a: \"{value}\"\n", yaml);
  }

  [Fact]
  public void Serialize_NonAscii_IsWrittenUnescaped() {
    // Arrange
    JsonObject document = new() { ["name"] = "中文 é" };

    // Act
    string yaml = YamlJson.Serialize(document);

    // Assert
    Assert.Equal("name: 中文 é\n", yaml);
  }

  [Fact]
  public void SerializeToUtf8Bytes_WritesUtf8WithoutByteOrderMark() {
    // Arrange
    using JsonDocument document = JsonDocument.Parse("""{ "a": "é" }""");

    // Act
    byte[] bytes = YamlJson.SerializeToUtf8Bytes(document.RootElement);

    // Assert
    Assert.Equal("a: é\n"u8.ToArray(), bytes);
  }

  private static string ReadBytes(byte[] yaml) {
    using JsonDocument document = YamlJson.Parse(yaml);
    return document.RootElement.GetRawText();
  }
}
