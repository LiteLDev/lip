using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// Template expansion of target sources (spec §4.2): {{version}}, {{platform}} and {{runtime}}.
/// Any other "{{" makes the document invalid, which IDocumentParser checks with
/// FindUnknownPlaceholder; Expand assumes a validated document.
public static partial class RefTemplate {
  /// Returns the first "{{" that does not begin a known placeholder, up to its "}}" or the end of
  /// the template, or null.
  public static string? FindUnknownPlaceholder(string template) =>
      UnknownPlaceholderRegex().Match(template) is { Success: true } m ? m.Value : null;

  /// Whether a template contains a known placeholder.
  public static bool HasPlaceholder(string template) => KnownPlaceholderRegex().IsMatch(template);

  /// Expands a template with the version of a package and an environment, without parsing the
  /// result. Throws FormatException when the package has no version to use.
  public static string Substitute(string template, string? version, LipEnvironment environment) =>
      PlaceholderRegex().Replace(template, m => m.Value[2..^2] switch {
        "version" => version
            ?? throw new FormatException(
                $"'{template}' uses {{{{version}}}} but there is no version."),
        "platform" => environment.Platform.ToSpecString(),
        "runtime" => environment.Runtime,
        _ => throw new FormatException($"Unknown placeholder {m.Value} in '{template}'."),
      });

  /// Expands a template and parses it as a ref. Throws FormatException when the package has no
  /// version to use or the result is not a valid ref.
  public static Ref Expand(string template, SemVersion? version, LipEnvironment environment) =>
      Ref.Parse(Substitute(template, version?.ToString(), environment));

  [GeneratedRegex(@"\{\{.*?\}\}")]
  private static partial Regex PlaceholderRegex();

  [GeneratedRegex(@"\{\{(version|platform|runtime)\}\}")]
  private static partial Regex KnownPlaceholderRegex();

  [GeneratedRegex(@"\{\{(?!(?:version|platform|runtime)\}\})(?:[^}]|\}(?!\}))*(?:\}\})?")]
  private static partial Regex UnknownPlaceholderRegex();
}
