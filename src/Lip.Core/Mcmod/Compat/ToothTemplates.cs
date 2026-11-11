using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Compat;

/// Puts the templates of a tooth.json back into the package file migrated from it (lip migrate,
/// protocol/lip-compatibility.md, "lip migrate"). The migration converts the values of the
/// tooth.json as rendered (protocol/lip-compatibility.md, "tooth.json"); this finds, in each string
/// of the result, the rendered values of the templates of the tooth.json, also after the
/// conversions of paths, and writes the templates there instead (protocol/lip.toml.md), with the
/// variable of the package id (tooth, or path for formats 1 and 2) renamed to id.
///
/// A string is written as a template only when it renders, with the top-level members of the
/// result as written as its variables, to the string the migration converted; any other string
/// is written as text, in an escape block when it holds "{{" or "{%". So the package file read
/// from the result is the one migrated.
public static partial class ToothTemplates {
  /// A template of the tooth.json: its rendered value, and its text as a template of lip.toml.
  private sealed record Source(string Value, string Template);

  /// A string of the result: where it is, and the value the migration converted.
  private sealed record Slot(JsonNode Parent, string? Key, int Index, string Value) {
    public void Write(string text) {
      if (Key is not null) {
        Parent[Key] = text;
      } else {
        Parent[Index] = text;
      }
    }
  }

  /// A copy of the migrated package file with its strings written as templates where they can be.
  public static JsonObject Restore(JsonObject migrated, JsonElement tooth) {
    List<Source> sources = Sources(tooth);
    JsonObject result = migrated.DeepClone().AsObject();
    List<Slot> slots = [];
    Collect(result, slots);

    List<string> written = [.. slots.Select(s => Candidate(s.Value, sources) ?? Text(s.Value))];
    for (int i = 0; i < slots.Count; i++) {
      slots[i].Write(written[i]);
    }

    // A template may use a top-level member that is itself written as a template, so they are
    // checked until none changes. Each change writes a template as text, so this ends.
    bool changed = true;
    while (changed) {
      changed = false;
      JsonElement root = Element(result);
      for (int i = 0; i < slots.Count; i++) {
        if (written[i] != Text(slots[i].Value) && Render(written[i], root) != slots[i].Value) {
          written[i] = Text(slots[i].Value);
          slots[i].Write(written[i]);
          changed = true;
        }
      }
    }

    // The whole file read back is the one migrated; were it not, the strings are written as text.
    using (JsonDocument read = PackageTemplates.Render(Element(result))) {
      if (!JsonNode.DeepEquals(JsonNode.Parse(read.RootElement.GetRawText()), migrated)) {
        RunTrace.Write("migrate", "the templates do not read back as migrated; writing text");
        for (int i = 0; i < slots.Count; i++) {
          slots[i].Write(Text(slots[i].Value));
        }

        return result;
      }
    }

    RunTrace.Write("migrate", $"tooth.json has {sources.Count} templates; "
        + $"{written.Where((w, i) => w != Text(slots[i].Value)).Count()} strings of"
        + $" {PackageFormat.FileName} keep them");
    return result;
  }

  /// The templates of the tooth.json, each with its value rendered as lip v0.37.0 rendered it:
  /// for format 3, with the members of the root as variables; for formats 1 and 2, with
  /// $(variable) read as {{variable}}, and the variables version and path.
  private static List<Source> Sources(JsonElement tooth) {
    bool v3 = ToothCompat.FormatVersion(tooth) == 3;
    Dictionary<string, object> model = v3
        ? JsonSerializer.Deserialize<Dictionary<string, object>>(tooth) ?? []
        : new() {
          ["version"] = String(tooth, "version") ?? "",
          ["path"] = String(tooth, "tooth") ?? "",
        };

    List<string> texts = [];
    CollectStrings(tooth, texts);
    List<Source> sources = [];
    foreach (string text in texts.Distinct(StringComparer.Ordinal)) {
      string template = v3 ? text : DollarVariableRegex().Replace(text, "{{$1}}");
      if (!template.Contains("{{", StringComparison.Ordinal)) {
        continue;
      }

      string value;
      try {
        value = Scriban.Template.Parse(template).Render(model);
      }
      catch (Exception ex) when (ex is InvalidOperationException
          or Scriban.Syntax.ScriptRuntimeException) {
        continue;
      }

      if (value != "" && value != template) {
        sources.Add(new Source(value, RenameVariable(template, v3 ? "tooth" : "path")));
      }
    }

    // Longer values first, so that a value inside another one does not split it.
    return [.. sources.OrderByDescending(s => s.Value.Length)];
  }

  /// The string with the rendered values it contains replaced by their templates; null when it
  /// contains none.
  private static string? Candidate(string value, List<Source> sources) {
    string candidate = value;
    foreach (Source source in sources) {
      foreach (Func<string, string> convert in _conversions) {
        string from = convert(source.Value);
        if (from != "" && candidate.Contains(from, StringComparison.Ordinal)) {
          candidate = candidate.Replace(from, convert(source.Template), StringComparison.Ordinal);
          break;
        }
      }
    }

    return candidate == value ? null : candidate;
  }

  /// The conversions protocol/lip-compatibility.md, "tooth.json" applies to paths and globs, and
  /// the trailing '*' of a directory placement that formats 1 and 2 drop.
  private static readonly Func<string, string>[] _conversions = [
    s => s,
    s => s.Replace('\\', '/'),
    ToothCompat.NormalizePath,
    s => s.TrimEnd('*'),
    s => ToothCompat.NormalizePath(s.TrimEnd('*')),
  ];

  /// The template with the variable name, where not a member of another value, renamed to id.
  private static string RenameVariable(string template, string name) =>
      CodeBlockRegex().Replace(template, block => Regex.Replace(
          block.Value, $@"(?<![\w.]){Regex.Escape(name)}\b", "id"));

  /// The string as a template that renders to itself: itself, or, when it holds "{{" or "{%",
  /// in an escape block {%{ }%} with as many '%' as it takes for the block not to end inside it.
  private static string Text(string value) {
    if (!value.Contains("{{", StringComparison.Ordinal)
        && !value.Contains("{%", StringComparison.Ordinal)) {
      return value;
    }

    string percent = "%";
    while (value.Contains($"}}{percent}}}", StringComparison.Ordinal)
        || value.EndsWith($"}}{percent}", StringComparison.Ordinal)) {
      percent += "%";
    }

    return $"{{{percent}{{{value}}}{percent}}}";
  }

  /// A template rendered as PackageTemplates renders it; null when it cannot be.
  private static string? Render(string text, JsonElement root) {
    try {
      return PackageTemplates.Render(text, root);
    }
    catch (PackageFormatException) {
      return null;
    }
  }

  private static void Collect(JsonNode? node, List<Slot> slots) {
    switch (node) {
      case JsonObject obj:
        foreach ((string key, JsonNode? member) in obj.ToList()) {
          if (member is JsonValue value && value.GetValueKind() == JsonValueKind.String) {
            slots.Add(new Slot(obj, key, -1, value.GetValue<string>()));
          } else {
            Collect(member, slots);
          }
        }

        break;
      case JsonArray array:
        for (int i = 0; i < array.Count; i++) {
          if (array[i] is JsonValue value && value.GetValueKind() == JsonValueKind.String) {
            slots.Add(new Slot(array, null, i, value.GetValue<string>()));
          } else {
            Collect(array[i], slots);
          }
        }

        break;
    }
  }

  private static void CollectStrings(JsonElement element, List<string> strings) {
    switch (element.ValueKind) {
      case JsonValueKind.Object:
        foreach (JsonProperty member in element.EnumerateObject()) {
          CollectStrings(member.Value, strings);
        }

        break;
      case JsonValueKind.Array:
        foreach (JsonElement item in element.EnumerateArray()) {
          CollectStrings(item, strings);
        }

        break;
      case JsonValueKind.String:
        strings.Add(element.GetString()!);
        break;
    }
  }

  private static string? String(JsonElement root, string key) =>
      root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
          ? value.GetString()
          : null;

  private static JsonElement Element(JsonNode node) {
    using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
    return document.RootElement.Clone();
  }

  [GeneratedRegex(@"\$\(([^)]+?)\)")]
  private static partial Regex DollarVariableRegex();

  [GeneratedRegex(@"\{\{.*?\}\}", RegexOptions.Singleline)]
  private static partial Regex CodeBlockRegex();
}
