using System.Text.Json;
using System.Text.Json.Serialization;
using Lip.Core.Entities;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Json;

public class PackageIdToSemVersionRangeDictionary : JsonConverter<Dictionary<PackageId, SemVersionRange>> {
  public override Dictionary<PackageId, SemVersionRange>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
    if (reader.TokenType != JsonTokenType.StartObject) {
      throw new JsonException("Expected the dependencies as a JSON object that maps each package"
          + $" to a version range, but found {JsonTokenNames.Of(reader.TokenType)}.");
    }

    Dictionary<PackageId, SemVersionRange> dictionary = [];

    while (reader.Read()) {
      if (reader.TokenType == JsonTokenType.EndObject) {
        return dictionary;
      }

      if (reader.TokenType != JsonTokenType.PropertyName) {
        throw new JsonException("Expected the name of a dependency, but found"
            + $" {JsonTokenNames.Of(reader.TokenType)}.");
      }

      string? propertyName = reader.GetString()
          ?? throw new JsonException("Expected the name of a dependency, but found none.");
      PackageId key;
      try {
        key = PackageId.Parse(propertyName);
      }
      catch (FormatException ex) {
        // The inner exception says what is wrong with it.
        throw new JsonException($"The dependency '{propertyName}' is not a valid package.", ex);
      }

      reader.Read();
      if (reader.TokenType != JsonTokenType.String) {
        throw new JsonException($"The version range of the dependency '{propertyName}' must be a"
            + $" string, but it is {JsonTokenNames.Of(reader.TokenType)}.");
      }

      string? value = reader.GetString()
          ?? throw new JsonException(
              $"The version range of the dependency '{propertyName}' is missing.");
      SemVersionRange range;
      try {
        range = SemVersionRange.Parse(value);
      }
      catch (Exception ex) when (ex is FormatException or OverflowException) {
        throw new JsonException($"The version range '{value}' of the dependency"
            + $" '{propertyName}' is not valid. {VersionRange.Syntax}", ex);
      }

      dictionary.Add(key, range);
    }

    throw new JsonException("The dependencies object ends unexpectedly.");
  }

  public override void Write(Utf8JsonWriter writer, Dictionary<PackageId, SemVersionRange> value, JsonSerializerOptions options) {
    writer.WriteStartObject();

    foreach (KeyValuePair<PackageId, SemVersionRange> kvp in value) {
      writer.WritePropertyName(kvp.Key.ToString());
      writer.WriteStringValue(kvp.Value.ToString());
    }

    writer.WriteEndObject();
  }
}
