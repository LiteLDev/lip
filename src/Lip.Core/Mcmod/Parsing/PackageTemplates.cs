using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Lip.Core.Mcmod;

/// The templates of lip.toml (protocol/package.md §3): each string other than a key is Scriban
/// text, rendered with the members of the top-level object as written as its variables.
public static class PackageTemplates {
  /// The value with each string rendered. Throws PackageFormatException, naming the string, when
  /// a template cannot be parsed or rendered.
  public static JsonDocument Render(JsonElement root) {
    ScriptObject model = [];
    if (root.ValueKind == JsonValueKind.Object) {
      foreach (JsonProperty member in root.EnumerateObject()) {
        model[member.Name] = ToScript(member.Value);
      }
    }

    JsonNode? rendered = RenderNode(root, model, "$");
    return JsonDocument.Parse(rendered?.ToJsonString() ?? "null");
  }

  private static JsonNode? RenderNode(JsonElement element, ScriptObject model, string path) {
    switch (element.ValueKind) {
      case JsonValueKind.Object: {
          JsonObject result = [];
          foreach (JsonProperty member in element.EnumerateObject()) {
            result[member.Name] = RenderNode(member.Value, model, Member(path, member.Name));
          }

          return result;
        }
      case JsonValueKind.Array: {
          JsonArray result = [];
          int i = 0;
          foreach (JsonElement item in element.EnumerateArray()) {
            result.Add(RenderNode(item, model, $"{path}[{i++}]"));
          }

          return result;
        }
      case JsonValueKind.String:
        return JsonValue.Create(RenderString(element.GetString()!, model, path));
      default:
        return JsonNode.Parse(element.GetRawText());
    }
  }

  private static string RenderString(string text, ScriptObject model, string path) {
    Template template = Template.Parse(text);
    if (template.HasErrors) {
      throw Invalid(path, text, string.Join("; ", template.Messages), null);
    }

    TemplateContext context = new();
    context.PushGlobal(model);
    try {
      return template.Render(context);
    }
    catch (Exception ex) when (ex is ScriptRuntimeException or InvalidOperationException) {
      throw Invalid(path, text, ex.Message, ex);
    }
  }

  /// The path of a member, as PackageFileParser names it.
  private static string Member(string path, string name) =>
      name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
          ? $"{path}.{name}"
          : $"{path}[\"{name}\"]";

  /// A JSON value as a Scriban value: an object a ScriptObject, an array a ScriptArray.
  private static object? ToScript(JsonElement element) {
    switch (element.ValueKind) {
      case JsonValueKind.Object: {
          ScriptObject result = [];
          foreach (JsonProperty member in element.EnumerateObject()) {
            result[member.Name] = ToScript(member.Value);
          }

          return result;
        }
      case JsonValueKind.Array:
        return new ScriptArray(element.EnumerateArray().Select(ToScript));
      case JsonValueKind.String:
        return element.GetString();
      case JsonValueKind.Number:
        return element.TryGetInt64(out long integer) ? integer : element.GetDouble();
      case JsonValueKind.True:
        return true;
      case JsonValueKind.False:
        return false;
      default:
        return null;
    }
  }

  private static PackageFormatException Invalid(
      string path, string text, string reason, Exception? inner) =>
      new($"Invalid package file at {path}: '{text}' is not a valid template (text with"
          + $" variables such as {{{{version}}}}): {reason}.", inner);
}
