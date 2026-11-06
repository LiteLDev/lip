using System.Text.Json;
using System.Text.Json.Serialization;
using Semver;

namespace Lip.Core.Json;

public class SemVersionJsonConverter : JsonConverter<SemVersion> {
  public override SemVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
    if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null)) {
      throw new JsonException("Expected a version (a string such as \"1.2.3\"), but found"
          + $" {JsonTokenNames.Of(reader.TokenType)}.");
    }

    string? s = reader.GetString();
    if (s is null) {
      return null;
    }

    try {
      return SemVersion.Parse(s, SemVersionStyles.Any);
    }
    catch (Exception ex) when (ex is FormatException or OverflowException) {
      throw new JsonException(
          $"'{s}' is not a valid version: write a SemVer version such as 1.2.3.", ex);
    }
  }

  public override void Write(Utf8JsonWriter writer, SemVersion value, JsonSerializerOptions options) {
    writer.WriteStringValue(value.ToString());
  }
}
