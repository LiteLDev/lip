using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lip.Core.Config;

/// A JSON configuration file (protocol/lip-tui.md, "Configuration"): read with comments and
/// trailing commas, written as a JSON file whose objects keep the order of their members.
public static class JsonConfigFile {
  private static readonly JsonDocumentOptions _readOptions = new() {
    CommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    AllowDuplicateProperties = false,
  };

  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
    IndentSize = 4,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  /// Reads the text of a JSON configuration file. Throws JsonException.
  public static JsonNode Parse(string text) {
    JsonNode? node;
    try {
      node = JsonNode.Parse(text.TrimStart('﻿'), documentOptions: _readOptions);
    }
    catch (ArgumentException ex) {
      // A JsonObject with two members of the same name.
      throw new JsonException(ex.Message, ex);
    }

    return node ?? throw new JsonException("The file holds null.");
  }

  /// A value that the user writes as JSON: null, a scalar, an object or an array; text that is
  /// not JSON is a string.
  public static JsonNode? ParseValue(string text) {
    try {
      return JsonNode.Parse(text.Trim(), documentOptions: _readOptions);
    }
    catch (Exception ex) when (ex is JsonException or ArgumentException) {
      return JsonValue.Create(text);
    }
  }

  /// The text of the file that holds the value, indented by 4 spaces, ending with a line break.
  public static string Serialize(JsonNode node) => node.ToJsonString(_writeOptions) + "\n";

  /// Whether the member of an object is the top-level integer "version" that a mod keeps to
  /// itself, which the user does not edit.
  public static bool IsReadOnly(JsonNode? container, string? member) =>
      container is JsonObject root && root.Parent is null && member == "version"
      && root[member] is JsonValue value && value.GetValueKind() == JsonValueKind.Number
      && long.TryParse(
          value.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
          out _);

  /// The text in which the user edits a scalar: a string as it is, anything else as JSON.
  public static string EditText(JsonNode? value) =>
      value is JsonValue v && v.GetValueKind() == JsonValueKind.String
          ? v.GetValue<string>()
          : value?.ToJsonString() ?? "null";

  /// The value that text gives to a scalar that was current: a boolean, a number or null stays
  /// one and must be written as JSON; a string takes the text as it is. Null when the text is not
  /// such a value, with why in error.
  public static bool TryEdit(
      JsonNode? current, string text, out JsonNode? value, out string? error) {
    JsonValueKind kind = current?.GetValueKind() ?? JsonValueKind.Null;
    error = null;
    value = null;
    if (kind == JsonValueKind.String) {
      value = JsonValue.Create(text);
      return true;
    }

    JsonNode? parsed;
    try {
      parsed = JsonNode.Parse(text.Trim(), documentOptions: _readOptions);
    }
    catch (JsonException) {
      parsed = null;
      if (kind == JsonValueKind.Null) {
        // A value that was null takes any text.
        value = JsonValue.Create(text);
        return true;
      }
    }

    JsonValueKind parsedKind = parsed?.GetValueKind() ?? JsonValueKind.Null;
    bool fits = kind switch {
      JsonValueKind.True or JsonValueKind.False =>
          parsedKind is JsonValueKind.True or JsonValueKind.False,
      JsonValueKind.Number => parsedKind == JsonValueKind.Number,
      _ => parsedKind is not (JsonValueKind.Object or JsonValueKind.Array),
    };
    if (!fits || (parsed is null && text.Trim() != "null")) {
      error = kind switch {
        JsonValueKind.True or JsonValueKind.False => "The value is true or false.",
        JsonValueKind.Number => "The value is a number.",
        _ => "The value is a string, a number, true, false or null.",
      };
      return false;
    }

    value = parsed;
    return true;
  }
}
