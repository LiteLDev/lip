using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lip.Core.Json;

/// System.Text.Json by reflection, for the types of lip whose properties are init-only with
/// default values: the metadata LipJsonContext generates sets each such property when reading,
/// to null or zero when the JSON leaves it out, where reflection keeps its default.
///
/// The trimmed lip keeps every member of the types of Lip.Core (TrimmerRootAssembly in
/// Lip.Cli.csproj), which is what reflection reads of them.
internal static class ReflectionJson {
  private const string _justification =
      "The trimmed lip keeps every member of the types of Lip.Core (TrimmerRootAssembly).";

  [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = _justification)]
  public static T? Deserialize<T>(JsonElement element, JsonSerializerOptions? options = null) =>
      element.Deserialize<T>(options);

  [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = _justification)]
  public static T? Deserialize<T>(JsonDocument document, JsonSerializerOptions? options = null) =>
      document.Deserialize<T>(options);

  [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = _justification)]
  public static T? Deserialize<T>(JsonNode? node, JsonSerializerOptions? options = null) =>
      node.Deserialize<T>(options);

  [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = _justification)]
  public static JsonElement SerializeToElement<T>(T value, JsonSerializerOptions? options = null) =>
      JsonSerializer.SerializeToElement(value, options);
}
