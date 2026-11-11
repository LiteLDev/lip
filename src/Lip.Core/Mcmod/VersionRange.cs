using Semver;

namespace Lip.Core.Mcmod;

/// A range (protocol/lip-package.md, "Terms"): a string that SemVersionRange.Parse of Semver 3.0.0
/// accepts with its default options, with the matching semantics of that range.
public class VersionRange : IVersionConstraint {
  private VersionRange(string text, SemVersionRange range) {
    Text = text;
    Range = range;
  }

  /// The range exactly as written.
  public string Text { get; }

  public SemVersionRange Range { get; }

  public override string ToString() => Text;

  /// Throws FormatException when the text is not a range.
  public static VersionRange Parse(string s) {
    ArgumentNullException.ThrowIfNull(s);
    try {
      return new VersionRange(s, SemVersionRange.Parse(s));
    }
    catch (Exception ex) when (ex is FormatException or OverflowException) {
      // Semver's own message, which says what is wrong at which position, is the inner one.
      throw new FormatException($"The version range '{s}' is not valid. {Syntax}", ex);
    }
  }

  /// How a range is written, for messages.
  public const string Syntax = "A version range is written like 1.2.3, >=1.2.0 <2.0.0,"
      + " ^1.2.0, ~1.2.0, 1.* or *, with SemVer versions without a leading 'v', and"
      + " alternatives separated by ||.";

  public static bool TryParse(string s, out VersionRange? range) {
    try {
      range = Parse(s);
      return true;
    }
    catch (FormatException) {
      range = null;
      return false;
    }
  }

  public bool IsSatisfiedBy(SemVersion version) => Range.Contains(version);
}

/// Accepts exactly one version, compared by SemVer precedence (build metadata is ignored). Used
/// to look up one version of a repository.
public sealed record ExactVersionConstraint(SemVersion Version) : IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version) =>
      SemVersion.PrecedenceComparer.Compare(Version, version) == 0;

  public override string ToString() => $"={Version}";
}
