using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl;

namespace Lip.Core.Json;

public class UrlListJsonConverter : JsonConverter<List<Url>> {
  public override List<Url>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
    if (reader.TokenType != JsonTokenType.StartArray) {
      throw new JsonException("Expected a list of URLs (a JSON array of strings), but found"
          + $" {JsonTokenNames.Of(reader.TokenType)}.");
    }

    var list = new List<Url>();

    while (reader.Read()) {
      if (reader.TokenType == JsonTokenType.EndArray) {
        return list;
      }

      if (reader.TokenType == JsonTokenType.String) {
        string? s = reader.GetString();
        if (!string.IsNullOrEmpty(s)) {
          try {
            list.Add(Url.Parse(s));
          }
          catch (Exception ex) when (ex is not JsonException) {
            throw new JsonException($"'{s}' is not a valid URL.", ex);
          }
        }
      } else {
        throw new JsonException("Each member of a list of URLs must be a string, but found"
            + $" {JsonTokenNames.Of(reader.TokenType)}.");
      }
    }

    throw new JsonException("A list of URLs ends unexpectedly.");
  }

  public override void Write(Utf8JsonWriter writer, List<Url> value, JsonSerializerOptions options) {
    writer.WriteStartArray();
    foreach (var url in value) {
      writer.WriteStringValue(url.ToString());
    }
    writer.WriteEndArray();
  }
}
