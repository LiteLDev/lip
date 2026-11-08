using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Lip.Core.Json;

/// Thrown when a TOML file is not a TOML document that represents a JSON value
/// (protocol/README.md, "TOML file"). The message says what is wrong with the file, and where
/// when that is known; the caller names the file.
public class TomlFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// TOML files (protocol/README.md, "TOML file"): a TOML document in UTF-8, read as the JSON value
/// it represents, so that the code reading lip.toml and config.toml works on JSON values.
///
/// Reading: a table is an object, an array an array, a string a string, an integer or a float a
/// number, a boolean a boolean. A date-time, a date or a time, and the floats inf and nan, are
/// not valid.
///
/// Writing: the value must be an object without null. Members that are scalars, arrays without
/// tables, or empty objects come first as "key = value"; then each object member as a [table];
/// then each array of objects, at least one of which holds an object or an array of objects, as
/// an [[array of tables]]. Any other object is written as an inline table. The result reads back
/// as the same JSON value.
public static partial class TomlJson {
  /// The nesting depth of collections, as JsonDocument allows by default.
  private const int _maxDepth = 64;

  private static readonly UTF8Encoding _strictUtf8 = new(
      encoderShouldEmitUTF8Identifier: false,
      throwOnInvalidBytes: true);

  /// Reads a TOML file as the JSON value it represents. The caller disposes the result. Throws
  /// TomlFormatException.
  public static JsonDocument Parse(byte[] toml) {
    string text;
    try {
      using StreamReader reader = new(
          new MemoryStream(toml), _strictUtf8, detectEncodingFromByteOrderMarks: true);
      text = reader.ReadToEnd();
    }
    catch (DecoderFallbackException ex) {
      throw new TomlFormatException(
          "The file is not valid UTF-8 text. Save it with the UTF-8 encoding.", ex);
    }

    return Parse(text);
  }

  /// Reads a TOML file as the JSON value it represents. The caller disposes the result. Throws
  /// TomlFormatException.
  public static JsonDocument Parse(string toml) {
    TomlTable table;
    try {
      // Validated first: deserializing alone lets a later key replace an earlier one.
      _ = SyntaxParser.ParseStrict(toml, "", validate: true);
      table = TomlSerializer.Deserialize<TomlTable>(toml)
          ?? throw new TomlFormatException("The file is not a valid TOML document.");
    }
    catch (TomlException ex) {
      throw new TomlFormatException($"The file is not valid TOML: {ex.Message}", ex);
    }

    JsonNode value = Convert(table, 0, "")!;

    // Written and read again, so the value is a JsonDocument like the one JSON gives.
    try {
      using MemoryStream memory = new();
      using (Utf8JsonWriter writer = new(memory)) {
        value.WriteTo(writer);
      }

      return JsonDocument.Parse(memory.ToArray());
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
        or JsonException) {
      // E.g. a string with a lone surrogate, which is not Unicode text.
      throw new TomlFormatException(
          "The file holds a value that is not a JSON value, such as a string that is not"
          + " Unicode text.",
          ex);
    }
  }

  private static JsonNode? Convert(object? value, int depth, string key) {
    if (depth > _maxDepth) {
      throw new TomlFormatException(
          $"The file nests tables or arrays deeper than {_maxDepth} levels at {Name(key)}.");
    }

    switch (value) {
      case TomlTable table: {
          JsonObject result = [];
          foreach (KeyValuePair<string, object> member in table) {
            result[member.Key] = Convert(
                member.Value, depth + 1, key.Length == 0 ? member.Key : $"{key}.{member.Key}");
          }

          return result;
        }
      case TomlTableArray tables: {
          JsonArray result = [];
          int i = 0;
          foreach (TomlTable item in tables) {
            result.Add(Convert(item, depth + 1, $"{key}[{i++}]"));
          }

          return result;
        }
      case TomlArray array: {
          JsonArray result = [];
          int i = 0;
          foreach (object? item in array) {
            result.Add(Convert(item, depth + 1, $"{key}[{i++}]"));
          }

          return result;
        }
      case string text:
        return JsonValue.Create(text);
      case bool boolean:
        return JsonValue.Create(boolean);
      case long integer:
        return JsonValue.Create(integer);
      case double number when double.IsFinite(number):
        return JsonValue.Create(number);
      case double:
        throw new TomlFormatException(
            $"The value of {Name(key)} is inf or nan, which JSON cannot represent. Quote it if it"
            + " is meant as text.");
      case TomlDateTime or DateTime or DateTimeOffset or DateOnly or TimeOnly:
        throw new TomlFormatException(
            $"The value of {Name(key)} is a date or a time, which lip does not read. Quote it if"
            + " it is meant as text.");
      default:
        throw new TomlFormatException(
            $"The value of {Name(key)} is of a kind lip does not read ({value?.GetType().Name}).");
    }
  }

  private static string Name(string key) => key.Length == 0 ? "the document" : $"'{key}'";

  /// Writes a JSON object as a TOML file that reads back as the same value, ending with a line
  /// break. Throws ArgumentException when the value is not an object, or holds null.
  public static string Serialize(JsonElement value) {
    if (value.ValueKind != JsonValueKind.Object) {
      throw new ArgumentException("A TOML document is a table.", nameof(value));
    }

    StringBuilder builder = new();
    WriteTable(builder, value, []);
    return builder.ToString();
  }

  /// Serialize, as UTF-8 without a byte order mark.
  public static byte[] SerializeToUtf8Bytes(JsonElement value) =>
      _strictUtf8.GetBytes(Serialize(value));

  /// Serialize, for a JSON node.
  public static string Serialize(JsonNode? value) {
    using JsonDocument document = JsonDocument.Parse(value?.ToJsonString() ?? "null");
    return Serialize(document.RootElement);
  }

  /// Writes the members of a table whose header, if any, was written already.
  private static void WriteTable(StringBuilder builder, JsonElement table, List<string> path) {
    List<JsonProperty> tables = [];
    List<JsonProperty> tableArrays = [];
    foreach (JsonProperty member in table.EnumerateObject()) {
      if (IsTable(member.Value)) {
        tables.Add(member);
      } else if (IsTableArray(member.Value)) {
        tableArrays.Add(member);
      } else {
        builder.Append(Key(member.Name)).Append(" = ");
        WriteInline(builder, member.Value);
        builder.Append('\n');
      }
    }

    foreach (JsonProperty member in tables) {
      List<string> subPath = [.. path, member.Name];
      // A table of only tables needs no header of its own: theirs define it.
      if (member.Value.EnumerateObject().Any(m => !IsHeaded(m.Value))) {
        Separate(builder);
        builder.Append('[').Append(string.Join('.', subPath.Select(Key))).Append("]\n");
      }

      WriteTable(builder, member.Value, subPath);
    }

    foreach (JsonProperty member in tableArrays) {
      List<string> subPath = [.. path, member.Name];
      foreach (JsonElement item in member.Value.EnumerateArray()) {
        Separate(builder);
        builder.Append("[[").Append(string.Join('.', subPath.Select(Key))).Append("]]\n");
        WriteTable(builder, item, subPath);
      }
    }
  }

  /// An object written as a [table]: a non-empty one.
  private static bool IsTable(JsonElement value) =>
      value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any();

  /// A value written under a header of its own, as a [table] or an [[array of tables]].
  private static bool IsHeaded(JsonElement value) => IsTable(value) || IsTableArray(value);

  /// An array written as an array of tables: one of objects only, at least one of which holds a
  /// non-empty object or an array with an object. Arrays of flat objects are written inline.
  private static bool IsTableArray(JsonElement value) =>
      value.ValueKind == JsonValueKind.Array
      && value.GetArrayLength() > 0
      && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object)
      && value.EnumerateArray().Any(item => item.EnumerateObject().Any(member =>
          IsTable(member.Value)
          || (member.Value.ValueKind == JsonValueKind.Array
              && member.Value.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object))));

  /// A blank line before a header, except at the start of the file.
  private static void Separate(StringBuilder builder) {
    if (builder.Length > 0) {
      builder.Append('\n');
    }
  }

  private static void WriteInline(StringBuilder builder, JsonElement value) {
    switch (value.ValueKind) {
      case JsonValueKind.Object: {
          if (!value.EnumerateObject().Any()) {
            builder.Append("{}");
            break;
          }

          builder.Append("{ ");
          bool first = true;
          foreach (JsonProperty member in value.EnumerateObject()) {
            if (!first) {
              builder.Append(", ");
            }

            first = false;
            builder.Append(Key(member.Name)).Append(" = ");
            WriteInline(builder, member.Value);
          }

          builder.Append(" }");
          break;
        }
      case JsonValueKind.Array: {
          builder.Append('[');
          bool first = true;
          foreach (JsonElement item in value.EnumerateArray()) {
            if (!first) {
              builder.Append(", ");
            }

            first = false;
            WriteInline(builder, item);
          }

          builder.Append(']');
          break;
        }
      case JsonValueKind.String:
        WriteString(builder, value.GetString()!);
        break;
      case JsonValueKind.Number:
        builder.Append(Number(value));
        break;
      case JsonValueKind.True:
        builder.Append("true");
        break;
      case JsonValueKind.False:
        builder.Append("false");
        break;
      default:
        throw new ArgumentException("TOML has no null.", nameof(value));
    }
  }

  /// A JSON number as a TOML integer or float of the same value. JSON numbers are TOML floats
  /// or integers as written, except integers beyond 64 bits, written as floats.
  private static string Number(JsonElement value) {
    string text = value.GetRawText();
    if (value.TryGetInt64(out long integer)) {
      return integer.ToString(CultureInfo.InvariantCulture);
    }

    if (text.Contains('.') || text.Contains('e') || text.Contains('E')) {
      return text;
    }

    string number = value.GetDouble().ToString("R", CultureInfo.InvariantCulture);
    return number.Contains('E') || number.Contains('.') ? number : number + ".0";
  }

  private static string Key(string key) =>
      BareKeyRegex().IsMatch(key) ? key : Quote(key);

  private static void WriteString(StringBuilder builder, string value) =>
      builder.Append(Quote(value));

  /// A TOML basic string.
  private static string Quote(string value) {
    StringBuilder builder = new(value.Length + 2);
    builder.Append('"');
    foreach (char c in value) {
      switch (c) {
        case '"':
          builder.Append("\\\"");
          break;
        case '\\':
          builder.Append("\\\\");
          break;
        case '\b':
          builder.Append("\\b");
          break;
        case '\t':
          builder.Append("\\t");
          break;
        case '\n':
          builder.Append("\\n");
          break;
        case '\f':
          builder.Append("\\f");
          break;
        case '\r':
          builder.Append("\\r");
          break;
        default:
          if (char.IsControl(c)) {
            builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
          } else {
            builder.Append(c);
          }

          break;
      }
    }

    return builder.Append('"').ToString();
  }

  [GeneratedRegex(@"^[A-Za-z0-9_-]+\z")]
  private static partial Regex BareKeyRegex();
}
