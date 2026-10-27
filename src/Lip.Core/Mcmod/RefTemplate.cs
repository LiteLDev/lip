using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// Template expansion of artifact refs (spec §4.1): {{name}}, {{version}}, {{platform}} and
/// {{rid}}. Any other {{...}} makes the document invalid, which IDocumentParser checks with
/// FindUnknownPlaceholder; Expand assumes a validated document.
public static partial class RefTemplate {
  private static readonly HashSet<string> _known = ["name", "version", "platform", "rid"];

  /// Returns the first placeholder that is not allowed, or null.
  public static string? FindUnknownPlaceholder(string template) => PlaceholderRegex()
      .Matches(template)
      .Select(m => m.Value)
      .FirstOrDefault(p => !_known.Contains(p[2..^2]));

  /// Expands a template and parses it as a ref. Throws FormatException when a referenced
  /// document key is missing or the result is not a valid ref, and PlatformUnknownException
  /// when {{platform}} is used without a known platform.
  public static Ref Expand(string template, RoutingDocument document, LipEnvironment environment) {
    string expanded = PlaceholderRegex().Replace(template, m => m.Value[2..^2] switch {
      "name" => document.Name
          ?? throw new FormatException($"'{template}' uses {{{{name}}}} but the document has no name."),
      "version" => document.Version?.ToString()
          ?? throw new FormatException(
              $"'{template}' uses {{{{version}}}} but the document has no version."),
      "platform" => environment.RequirePlatform().ToSpecString(),
      "rid" => environment.Rid,
      _ => throw new FormatException($"Unknown placeholder {m.Value} in '{template}'."),
    });

    return Ref.Parse(expanded);
  }

  [GeneratedRegex(@"\{\{.*?\}\}")]
  private static partial Regex PlaceholderRegex();
}
