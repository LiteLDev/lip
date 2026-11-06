using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Lip.Core.Json;

/// Thrown when a YAML file is not a YAML stream of one document that represents a JSON value
/// (protocol/README.md, "YAML file").
public class YamlFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// YAML files (protocol/README.md, "YAML file"): a YAML stream of one document, read with the
/// YAML 1.2 core schema as the JSON value it represents, so that the code reading lip.yaml,
/// .lip/workspace.yaml and config.yaml works on JSON values.
///
/// Reading: a quoted or block scalar, or one tagged "!" or "!!str", is a string. A plain scalar
/// is resolved by the core schema: null, a boolean, an integer (decimal, 0o octal, 0x
/// hexadecimal) or a float, and otherwise a string. Aliases are expanded. A mapping key must be a
/// string, and keys must be unique. A value JSON cannot represent (.inf, .nan, a tag other than
/// those of the core schema) is not valid.
///
/// Writing: block style, indented by two spaces, empty collections in flow style; a string that
/// would read back as another value ("", "1.0", "true", "null", ...) is double-quoted, so the
/// result reads back as the same JSON value.
public static partial class YamlJson {
  private const string _coreTagPrefix = "tag:yaml.org,2002:";

  /// The nesting depth of collections, as JsonDocument allows by default.
  private const int _maxDepth = 64;

  /// The number of nodes a document may expand to through aliases.
  private const int _maxNodes = 1_000_000;

  private static readonly UTF8Encoding _strictUtf8 = new(
      encoderShouldEmitUTF8Identifier: false,
      throwOnInvalidBytes: true);

  /// Reads a YAML file as the JSON value it represents. The caller disposes the result. Throws
  /// YamlFormatException.
  public static JsonDocument Parse(byte[] yaml) {
    string text;
    try {
      using StreamReader reader = new(
          new MemoryStream(yaml), _strictUtf8, detectEncodingFromByteOrderMarks: true);
      text = reader.ReadToEnd();
    }
    catch (DecoderFallbackException ex) {
      throw new YamlFormatException("not valid UTF-8.", ex);
    }

    return Parse(text);
  }

  /// Reads a YAML file as the JSON value it represents. The caller disposes the result. Throws
  /// YamlFormatException.
  public static JsonDocument Parse(string yaml) {
    JsonNode? value;
    try {
      Reader reader = new(new Parser(new StringReader(yaml)));
      value = reader.ReadStream();
    }
    catch (YamlException ex) {
      throw new YamlFormatException($"not valid YAML ({ex.Message})", ex);
    }

    // Written and read again, so the value is a JsonDocument like the one JSON gives.
    try {
      using MemoryStream memory = new();
      using (Utf8JsonWriter writer = new(memory)) {
        if (value is null) {
          writer.WriteNullValue();
        } else {
          value.WriteTo(writer);
        }
      }

      return JsonDocument.Parse(memory.ToArray());
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
        or JsonException) {
      // E.g. a string with a lone surrogate, which is not Unicode text.
      throw new YamlFormatException($"not a JSON value ({ex.Message})", ex);
    }
  }

  /// Writes a JSON value as a YAML file that reads back as the same value, ending with a line
  /// break.
  public static string Serialize(JsonElement value) {
    using StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
    Emitter emitter = new(writer, 2, int.MaxValue, false);
    emitter.Emit(new StreamStart());
    emitter.Emit(new DocumentStart(null, null, true));
    Emit(emitter, value);
    emitter.Emit(new DocumentEnd(true));
    emitter.Emit(new StreamEnd());
    return writer.ToString().ReplaceLineEndings("\n");
  }

  /// Serialize, as UTF-8 without a byte order mark.
  public static byte[] SerializeToUtf8Bytes(JsonElement value) =>
      _strictUtf8.GetBytes(Serialize(value));

  /// Serialize, for a JSON node.
  public static string Serialize(JsonNode? value) {
    using JsonDocument document = JsonDocument.Parse(value?.ToJsonString() ?? "null");
    return Serialize(document.RootElement);
  }

  private static void Emit(IEmitter emitter, JsonElement value) {
    switch (value.ValueKind) {
      case JsonValueKind.Object: {
          bool empty = !value.EnumerateObject().Any();
          emitter.Emit(new MappingStart(
              null, null, true, empty ? MappingStyle.Flow : MappingStyle.Block));
          foreach (JsonProperty property in value.EnumerateObject()) {
            EmitString(emitter, property.Name);
            Emit(emitter, property.Value);
          }

          emitter.Emit(new MappingEnd());
          break;
        }
      case JsonValueKind.Array: {
          bool empty = value.GetArrayLength() == 0;
          emitter.Emit(new SequenceStart(
              null, null, true, empty ? SequenceStyle.Flow : SequenceStyle.Block));
          foreach (JsonElement item in value.EnumerateArray()) {
            Emit(emitter, item);
          }

          emitter.Emit(new SequenceEnd());
          break;
        }
      case JsonValueKind.String:
        EmitString(emitter, value.GetString()!);
        break;
      case JsonValueKind.Number:
        // A JSON number is a plain scalar the core schema reads as an integer or a float.
        EmitPlain(emitter, value.GetRawText());
        break;
      case JsonValueKind.True:
        EmitPlain(emitter, "true");
        break;
      case JsonValueKind.False:
        EmitPlain(emitter, "false");
        break;
      default:
        EmitPlain(emitter, "null");
        break;
    }
  }

  private static void EmitPlain(IEmitter emitter, string text) =>
      emitter.Emit(new Scalar(null, null, text, ScalarStyle.Plain, true, false));

  /// A string that a plain scalar would not read back as is double-quoted, and so is one with a
  /// line break or another control character, which the other styles fold or cannot hold; any
  /// other is written in the style the emitter chooses, which keeps it a string. The YAML 1.1
  /// booleans (yes, no, on, off, ...) are quoted too, for readers that still resolve them.
  private static void EmitString(IEmitter emitter, string value) {
    ScalarStyle style = ReadsBackAsString(value) && !Yaml11BoolRegex().IsMatch(value)
        && !value.Any(char.IsControl)
        ? ScalarStyle.Any
        : ScalarStyle.DoubleQuoted;
    emitter.Emit(new Scalar(null, null, value, style, true, true));
  }

  private static bool ReadsBackAsString(string value) {
    try {
      return ResolvePlain(value) is JsonValue resolved
          && resolved.GetValueKind() == JsonValueKind.String;
    }
    catch (YamlFormatException) {
      return false;
    }
  }

  /// The value of a plain scalar by the YAML 1.2 core schema: null, a boolean, an integer, a
  /// float, or else a string. Throws YamlFormatException for .inf and .nan.
  private static JsonNode? ResolvePlain(string text) {
    if (NullRegex().IsMatch(text)) {
      return null;
    }

    if (BoolRegex().IsMatch(text)) {
      return JsonValue.Create(text[0] is 't' or 'T');
    }

    if (DecimalRegex().IsMatch(text)) {
      return Integer(BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    if (OctalRegex().IsMatch(text)) {
      BigInteger value = BigInteger.Zero;
      foreach (char c in text[2..]) {
        value = (value * 8) + (c - '0');
      }

      return Integer(value);
    }

    if (HexadecimalRegex().IsMatch(text)) {
      // A leading 0 keeps the value positive.
      return Integer(BigInteger.Parse(
          "0" + text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    if (FloatRegex().IsMatch(text)) {
      double value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
      return double.IsFinite(value)
          ? JsonValue.Create(value)
          : throw new YamlFormatException($"the float '{text}' is out of range.");
    }

    if (InfinityRegex().IsMatch(text) || NotANumberRegex().IsMatch(text)) {
      throw new YamlFormatException($"the float '{text}' cannot be represented in JSON.");
    }

    return JsonValue.Create(text);
  }

  private static JsonNode Integer(BigInteger value) =>
      value >= long.MinValue && value <= long.MaxValue
          ? JsonValue.Create((long)value)
          : JsonNode.Parse(value.ToString(CultureInfo.InvariantCulture))!;

  /// Reads the events of a YAML stream into a JSON value.
  private sealed class Reader(IParser parser) {
    private readonly Dictionary<string, (JsonNode? Node, int Size)> _anchors = [];
    private int _nodes;

    public JsonNode? ReadStream() {
      parser.Consume<StreamStart>();
      if (!parser.TryConsume<DocumentStart>(out _)) {
        throw new YamlFormatException("it holds no YAML document.");
      }

      JsonNode? value = Read(0, out _);
      parser.Consume<DocumentEnd>();
      if (!parser.TryConsume<StreamEnd>(out _)) {
        throw new YamlFormatException("it holds more than one YAML document.");
      }

      return value;
    }

    /// Reads a node and returns it, with the number of nodes it holds.
    private JsonNode? Read(int depth, out int size) {
      if (depth > _maxDepth) {
        throw new YamlFormatException($"it nests deeper than {_maxDepth} levels.");
      }

      if (parser.Current is AnchorAlias alias) {
        parser.MoveNext();
        if (!_anchors.TryGetValue(alias.Value.Value, out (JsonNode? Node, int Size) anchored)) {
          throw new YamlFormatException($"the alias *{alias.Value} names no earlier anchor.");
        }

        size = anchored.Size;
        Count(size);
        return anchored.Node?.DeepClone();
      }

      NodeEvent start = parser.Current as NodeEvent
          ?? throw new YamlFormatException($"unexpected {parser.Current}.");
      JsonNode? node;
      switch (start) {
        case Scalar scalar:
          parser.MoveNext();
          node = ReadScalar(scalar);
          size = 1;
          Count(1);
          break;
        case SequenceStart sequenceStart:
          parser.MoveNext();
          CheckCollectionTag(sequenceStart.Tag, "seq");
          JsonArray array = [];
          size = 1;
          Count(1);
          while (!parser.TryConsume<SequenceEnd>(out _)) {
            array.Add(Read(depth + 1, out int itemSize));
            size += itemSize;
          }

          node = array;
          break;
        case MappingStart mappingStart:
          parser.MoveNext();
          CheckCollectionTag(mappingStart.Tag, "map");
          JsonObject mapping = [];
          size = 1;
          Count(1);
          while (!parser.TryConsume<MappingEnd>(out _)) {
            JsonNode? key = Read(depth + 1, out _);
            if (key is not JsonValue keyValue
                || keyValue.GetValueKind() != JsonValueKind.String) {
              throw new YamlFormatException(
                  $"the mapping key {key?.ToJsonString() ?? "null"} is not a string.");
            }

            string name = keyValue.GetValue<string>();
            if (mapping.ContainsKey(name)) {
              throw new YamlFormatException($"the mapping key '{name}' appears twice.");
            }

            mapping[name] = Read(depth + 1, out int valueSize);
            size += valueSize;
          }

          node = mapping;
          break;
        default:
          throw new YamlFormatException($"unexpected {start}.");
      }

      if (!start.Anchor.IsEmpty) {
        _anchors[start.Anchor.Value] = (node, size);
      }

      return node;
    }

    private void Count(int nodes) {
      _nodes += nodes;
      if (_nodes > _maxNodes) {
        throw new YamlFormatException($"it expands to more than {_maxNodes} nodes.");
      }
    }

    private static JsonNode? ReadScalar(Scalar scalar) {
      string value = scalar.Value;
      if (scalar.Tag.IsEmpty) {
        return scalar.Style == ScalarStyle.Plain ? ResolvePlain(value) : JsonValue.Create(value);
      }

      if (scalar.Tag.IsNonSpecific) {
        return JsonValue.Create(value);
      }

      string tag = scalar.Tag.Value;
      JsonNode? resolved = ResolvePlain(value);
      JsonValueKind kind = resolved?.GetValueKind() ?? JsonValueKind.Null;
      return tag switch {
        _coreTagPrefix + "str" => JsonValue.Create(value),
        _coreTagPrefix + "null" when kind == JsonValueKind.Null => null,
        _coreTagPrefix + "bool" when kind is JsonValueKind.True or JsonValueKind.False => resolved,
        _coreTagPrefix + "int" when DecimalRegex().IsMatch(value) || OctalRegex().IsMatch(value)
            || HexadecimalRegex().IsMatch(value) => resolved,
        _coreTagPrefix + "float" when kind == JsonValueKind.Number => resolved,
        _ => throw new YamlFormatException($"the scalar '{value}' tagged {tag} is not valid."),
      };
    }

    private static void CheckCollectionTag(TagName tag, string kind) {
      if (!tag.IsEmpty && !tag.IsNonSpecific && tag.Value != _coreTagPrefix + kind) {
        throw new YamlFormatException($"the tag {tag.Value} is not valid here.");
      }
    }
  }

  [GeneratedRegex(@"^(?:null|Null|NULL|~)?\z")]
  private static partial Regex NullRegex();

  [GeneratedRegex(@"^(?:true|True|TRUE|false|False|FALSE)\z")]
  private static partial Regex BoolRegex();

  [GeneratedRegex(@"^(?:y|Y|yes|Yes|YES|n|N|no|No|NO|on|On|ON|off|Off|OFF)\z")]
  private static partial Regex Yaml11BoolRegex();

  [GeneratedRegex(@"^[-+]?[0-9]+\z")]
  private static partial Regex DecimalRegex();

  [GeneratedRegex(@"^0o[0-7]+\z")]
  private static partial Regex OctalRegex();

  [GeneratedRegex(@"^0x[0-9a-fA-F]+\z")]
  private static partial Regex HexadecimalRegex();

  [GeneratedRegex(@"^[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?\z")]
  private static partial Regex FloatRegex();

  [GeneratedRegex(@"^[-+]?(?:\.inf|\.Inf|\.INF)\z")]
  private static partial Regex InfinityRegex();

  [GeneratedRegex(@"^(?:\.nan|\.NaN|\.NAN)\z")]
  private static partial Regex NotANumberRegex();
}
