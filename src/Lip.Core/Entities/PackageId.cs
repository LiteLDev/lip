using System.Text.RegularExpressions;

namespace Lip.Core.Entities;

public partial record PackageId(string Path, string Variant) {
  public string Path { get; init; } = Golang.Org.X.Mod.Module.CheckPath(Path) is Exception error
      ? throw new FormatException(
          $"'{Path}' is not a valid package path: it must be a Go module path, e.g. "
          + "github.com/owner/repo.", error)
      : Path;

  public string Variant { get; init; } = IsValidVariant(Variant)
      ? Variant
      : throw new FormatException(VariantError(Variant));

  public override string ToString() => $"{Path}{(Variant != string.Empty ? "#" : string.Empty)}{Variant}";

  public static bool IsValidVariant(string variant) => VariantRegex().IsMatch(variant);

  /// Why a string is not a variant label, in words for the user.
  public static string VariantError(string variant) =>
      $"'{variant}' is not a valid variant label: it must be empty, or one or two words of "
      + "lowercase letters, digits and '_' separated by '/', e.g. client.";

  public static PackageId Parse(string s) {
    Match match = SelfRegex().Match(s);
    if (!match.Success) {
      throw new FormatException(
          $"'{s}' is not a valid package identity: it must be a package path, optionally "
          + "followed by '#' and a variant label, e.g. github.com/owner/repo#client.");
    }

    string path = match.Groups["path"].Value;
    string variant = match.Groups["label"].Value;

    return new PackageId(path, variant);
  }

  [GeneratedRegex(@"^(?<path>[^#]+)(?:#(?<label>[^#]*))?$")]
  private static partial Regex SelfRegex();

  [GeneratedRegex(@"^([a-z0-9_]+(/[a-z0-9_]+)?)?$")]
  private static partial Regex VariantRegex();
}
