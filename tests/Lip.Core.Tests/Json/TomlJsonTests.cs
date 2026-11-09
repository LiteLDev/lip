using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Json;

namespace Lip.Core.Tests.Json;

/// TOML files (protocol/README.md): a TOML document read as the JSON value it represents, and
/// JSON objects written so that they read back as the same value.
public class TomlJsonTests {
  private static string Read(string toml) {
    using JsonDocument document = TomlJson.Parse(toml);
    return document.RootElement.GetRawText();
  }

  private static void AssertRoundTrips(string json) {
    JsonNode expected = JsonNode.Parse(json)!;
    string toml = TomlJson.Serialize(expected);
    using JsonDocument document = TomlJson.Parse(toml);
    Assert.True(
        JsonNode.DeepEquals(expected, JsonNode.Parse(document.RootElement.GetRawText())),
        toml);
  }

  [Theory]
  [InlineData("a = true", """{"a":true}""")]
  [InlineData("a = false", """{"a":false}""")]
  [InlineData("a = 4", """{"a":4}""")]
  [InlineData("a = -12", """{"a":-12}""")]
  [InlineData("a = 0x1F", """{"a":31}""")]
  [InlineData("a = 1_000", """{"a":1000}""")]
  [InlineData("a = 1.5", """{"a":1.5}""")]
  [InlineData("a = \"1.0.0\"", """{"a":"1.0.0"}""")]
  [InlineData("a = 'C:\\x'", """{"a":"C:\\x"}""")]
  [InlineData("a = []", """{"a":[]}""")]
  [InlineData("a = {}", """{"a":{}}""")]
  [InlineData("a.b = 1", """{"a":{"b":1}}""")]
  [InlineData("\"\" = 1", """{"":1}""")]
  [InlineData("[[a]]\nb = 1\n[[a]]\nb = 2", """{"a":[{"b":1},{"b":2}]}""")]
  [InlineData("a = [1, \"x\", { b = 2 }]", """{"a":[1,"x",{"b":2}]}""")]
  public void Parse_Values_ReadsJsonValue(string toml, string json) {
    // Act & Assert
    Assert.Equal(json, Read(toml));
  }

  [Fact]
  public void Parse_Empty_IsEmptyObject() {
    // Act & Assert
    Assert.Equal("{}", Read(""));
  }

  [Theory]
  [InlineData("a = 1979-05-27T07:32:00Z")]
  [InlineData("a = 1979-05-27")]
  [InlineData("a = 07:32:00")]
  [InlineData("a = inf")]
  [InlineData("a = nan")]
  [InlineData("a = 1\na = 2")]
  [InlineData("a: 1")]
  [InlineData("{ \"a\": 1 }")]
  [InlineData("a = ")]
  public void Parse_NotAJsonValueOrNotToml_Throws(string toml) {
    // Act & Assert
    Assert.Throws<TomlFormatException>(() => TomlJson.Parse(toml));
  }

  [Fact]
  public void Parse_InvalidUtf8_Throws() {
    // Act & Assert
    TomlFormatException ex =
        Assert.Throws<TomlFormatException>(() => TomlJson.Parse([0x61, 0x3D, 0x22, 0xFF, 0x22]));
    Assert.Contains("UTF-8", ex.Message);
  }

  [Fact]
  public void Parse_ByteOrderMark_IsSkipped() {
    // Act
    using JsonDocument document = TomlJson.Parse([0xEF, 0xBB, 0xBF, .. "a = 1"u8.ToArray()]);

    // Assert
    Assert.Equal("""{"a":1}""", document.RootElement.GetRawText());
  }

  [Theory]
  [InlineData("""{"format":4,"id":"github.com/a/b","version":"1.0.0"}""")]
  [InlineData("""{"a":"","b":"true","c":"1","d":"x\"y\\z\n\t\u0001\u007f","e":"日本"}""")]
  [InlineData("""{"a b":1,"":2,"a.b":3,"ü":4,"#":5}""")]
  [InlineData("""{"a":[],"b":{},"c":[[1,2],["x"]],"d":[1,"x",true,{"e":1}]}""")]
  [InlineData("""{"a":1.5,"b":-0.25,"c":1e300,"d":-9223372036854775808}""")]
  [InlineData("""{"a":{"b":{"c":1}},"d":2}""")]
  [InlineData("""{"a":[{"b":1},{"c":[{"d":{"e":1}}]}]}""")]
  [InlineData("""{"a":[{"b":{}},{"c":[]}]}""")]
  [InlineData("""{"a":{"b":[{"c":{"d":1}}],"e":{"f":2}}}""")]
  [InlineData("""
      {
        "format": 4, "id": "github.com/user/repo", "version": "1.0.0",
        "info": { "name": "x", "tags": ["a", "b"] },
        "variants": {
          "": [
            {
              "side": "server", "platform": "win-x64",
              "deps": { "github.com/a/b#c/d": ">=1.0.0" },
              "assets": [
                { "type": "self", "place": [ { "src": "a/", "dest": "b/" } ] },
                { "type": "mcmod", "urls": [ "https://example.com/a.zip" ] }
              ],
              "preserve": [ "**/config.json" ],
              "hooks": { "post_install": [ "echo hi" ] }
            },
            { "side": "server", "platform": "linux-x64" }
          ],
          "x/y": [ { "side": "client", "platform": "win-x64" } ]
        }
      }
      """)]
  public void Serialize_ReadsBackAsSameValue(string json) {
    // Act & Assert
    AssertRoundTrips(json);
  }

  [Fact]
  public void Serialize_PackageFile_WritesTablesAndArraysOfTables() {
    // Arrange
    JsonNode value = JsonNode.Parse("""
        {
          "format": 4, "id": "github.com/user/repo", "version": "0.1.0",
          "variants": { "": [ { "side": "server", "platform": "win-x64",
            "assets": [ { "type": "self", "place": [ { "src": "a", "dest": "b" } ] } ] } ] }
        }
        """)!;

    // Act
    string toml = TomlJson.Serialize(value);

    // Assert
    Assert.Equal(
        """
        format = 4
        id = "github.com/user/repo"
        version = "0.1.0"

        [[variants.""]]
        side = "server"
        platform = "win-x64"

        [[variants."".assets]]
        type = "self"
        place = [{ src = "a", dest = "b" }]

        """.ReplaceLineEndings("\n"),
        toml);
  }

  [Fact]
  public void Serialize_Null_Throws() {
    // Act & Assert
    Assert.Throws<ArgumentException>(() => TomlJson.Serialize(JsonNode.Parse("""{"a":null}""")));
  }

  [Fact]
  public void Serialize_NotAnObject_Throws() {
    // Act & Assert
    Assert.Throws<ArgumentException>(() => TomlJson.Serialize(JsonNode.Parse("[1]")));
  }

  [Fact]
  public void SerializeToUtf8Bytes_HasNoByteOrderMark() {
    // Act
    using JsonDocument document = JsonDocument.Parse("""{"a":"日本"}""");
    byte[] bytes = TomlJson.SerializeToUtf8Bytes(document.RootElement);

    // Assert
    Assert.Equal("a = \"日本\"\n", Encoding.UTF8.GetString(bytes));
    Assert.NotEqual(0xEF, bytes[0]);
  }
}
