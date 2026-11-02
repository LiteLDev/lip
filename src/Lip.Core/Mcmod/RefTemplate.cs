using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// Template expansion of target sources (spec §4.2): {{id}}, {{version}}, {{platform}} and
/// {{runtime}}. Any other {{...}} makes the document invalid, which IDocumentParser checks with
/// FindUnknownPlaceholder; Expand assumes a validated document.
public static partial class RefTemplate {
  private static readonly HashSet<string> _known = ["id", "version", "platform", "runtime"];

  /// Returns the first placeholder that is not allowed, or null.
  public static string? FindUnknownPlaceholder(string template) => PlaceholderRegex()
      .Matches(template)
      .Select(m => m.Value)
      .FirstOrDefault(p => !_known.Contains(p[2..^2]));

  /// Whether a template contains a known placeholder.
  public static bool HasPlaceholder(string template) => KnownPlaceholderRegex().IsMatch(template);

  /// Expands a template with the id and version of a target and an environment, without
  /// parsing the result. Throws FormatException when the target has no id or version to use.
  public static string Substitute(
      string template, string? id, string? version, LipEnvironment environment) =>
      PlaceholderRegex().Replace(template, m => m.Value[2..^2] switch {
        "id" => id
            ?? throw new FormatException($"'{template}' uses {{{{id}}}} but there is no id."),
        "version" => version
            ?? throw new FormatException(
                $"'{template}' uses {{{{version}}}} but there is no version."),
        "platform" => environment.Platform.ToSpecString(),
        "runtime" => environment.Runtime,
        _ => throw new FormatException($"Unknown placeholder {m.Value} in '{template}'."),
      });

  /// Expands a template and parses it as a ref. Throws FormatException when the target has no
  /// id or version to use or the result is not a valid ref.
  public static Ref Expand(string template, MetaTarget target, LipEnvironment environment) =>
      Ref.Parse(Substitute(
          template, target.TemplateId, target.TemplateVersion?.ToString(), environment));

  [GeneratedRegex(@"\{\{.*?\}\}")]
  private static partial Regex PlaceholderRegex();

  [GeneratedRegex(@"\{\{(id|version|platform|runtime)\}\}")]
  private static partial Regex KnownPlaceholderRegex();
}
