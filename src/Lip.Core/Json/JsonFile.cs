using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lip.Core.Json;

/// JSON files (protocol/README.md, "JSON file"): a JSON text in UTF-8, with no two members of the
/// same name in an object, as .lip/state.json is.
public static class JsonFile {
  private static readonly JsonDocumentOptions _readOptions = new() {
    AllowDuplicateProperties = false,
  };

  private static readonly JsonWriterOptions _writeOptions = new() {
    Indented = true,
    IndentSize = 2,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  private static readonly UTF8Encoding _strictUtf8 = new(
      encoderShouldEmitUTF8Identifier: false,
      throwOnInvalidBytes: true);

  /// Reads a JSON file. The caller disposes the result. Throws JsonException.
  public static JsonDocument Parse(byte[] json) {
    string text;
    try {
      using StreamReader reader = new(
          new MemoryStream(json), _strictUtf8, detectEncodingFromByteOrderMarks: true);
      text = reader.ReadToEnd();
    }
    catch (DecoderFallbackException ex) {
      throw new JsonException("The file is not valid UTF-8 text.", ex);
    }

    return JsonDocument.Parse(text, _readOptions);
  }

  /// Writes a JSON value as a JSON file, indented by two spaces, ending with a line break, as
  /// UTF-8 without a byte order mark.
  public static byte[] SerializeToUtf8Bytes(JsonElement value) {
    using MemoryStream memory = new();
    using (Utf8JsonWriter writer = new(memory, _writeOptions)) {
      value.WriteTo(writer);
    }

    memory.WriteByte((byte)'\n');
    return memory.ToArray();
  }
}
