using Semver;

namespace Lip.Core.Mcmod;

/// A version range of a manifest.json (spec §1 "range"): the syntax and matching semantics of
/// node-semver ranges, as semver.satisfies applies them without the loose and includePrerelease
/// options. A full version without an operator matches exactly that version, and a prerelease
/// version matches only a comparator set with a prerelease on the same major.minor.patch.
public class VersionRange : IVersionConstraint {
  private readonly SemVersionRange _range;

  private VersionRange(string text, SemVersionRange range) {
    Text = text;
    _range = range;
  }

  /// The range exactly as written.
  public string Text { get; }

  public override string ToString() => Text;

  /// Throws FormatException when the text is not a node-semver range.
  public static VersionRange Parse(string s) {
    ArgumentNullException.ThrowIfNull(s);
    try {
      return new VersionRange(s, SemVersionRange.ParseNpm(s));
    }
    catch (OverflowException ex) {
      throw new FormatException($"Invalid version range '{s}': {ex.Message}", ex);
    }
  }

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

  public bool IsSatisfiedBy(SemVersion version) => _range.Contains(version);
}

/// Adapts a tooth.json v3 range (Semver's SemVersionRange) for spec §10.2 rule 3.
public class SemVersionRangeConstraint(SemVersionRange range) : IVersionConstraint {
  public SemVersionRange Range { get; } = range;

  public bool IsSatisfiedBy(SemVersion version) => Range.Contains(version);

  public override string ToString() => Range.ToString();
}

/// Accepts exactly one version, compared by SemVer precedence (build metadata is ignored). Used
/// for "path@version" arguments and to look up one version of a ref.
public sealed record ExactVersionConstraint(SemVersion Version) : IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version) =>
      SemVersion.PrecedenceComparer.Compare(Version, version) == 0;

  public override string ToString() => $"={Version}";
}
