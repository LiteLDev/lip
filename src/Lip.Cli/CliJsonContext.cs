using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lip.Cli.Commands;

namespace Lip.Cli;

/// The types the commands write as JSON, with metadata generated at build time instead of read
/// by reflection, so that the trimmed lip keeps what writing them needs.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(IDictionary<string, string>))]
[JsonSerializable(typeof(ListCommand.Installed))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class CliJsonContext : JsonSerializerContext {
  /// How a result is written as JSON over several lines: only what JSON requires is escaped,
  /// so that ">", "<", "&", "+" and text in any language read as they are. It is valid JSON
  /// all the same.
  public static JsonSerializerOptions Indented => _indented ??= Relaxed(indented: true);

  /// The same on one line.
  public static JsonSerializerOptions Compact => _compact ??= Relaxed(indented: false);

  // Made when first asked for: Default, which the generated part of the class initializes, is
  // not set yet while the fields of this part are.
  private static JsonSerializerOptions? _indented;
  private static JsonSerializerOptions? _compact;

  /// The metadata of a type of this context for writing it with options.
  public static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions options) =>
      (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

  private static JsonSerializerOptions Relaxed(bool indented) =>
      new() {
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = Default,
      };
}
