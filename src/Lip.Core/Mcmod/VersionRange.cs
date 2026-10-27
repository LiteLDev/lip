using Semver;

namespace Lip.Core.Mcmod;

/// A version range of a manifest.json (spec §4.2 "range"): the syntax and matching semantics
/// of LeviLamina's ll::data::VersionRequirement, except that a bare full version without an
/// operator (e.g. "1.2.3") is rejected.
public class VersionRange : IVersionConstraint {
  private VersionRange(string text) {
    Text = text;
  }

  public string Text { get; }

  public override string ToString() => Text;

  public static VersionRange Parse(string s) => throw new NotImplementedException();

  public bool IsSatisfiedBy(SemVersion version) => throw new NotImplementedException();
}

/// Adapts a tooth.json v3 range (Semver's SemVersionRange) for §9.3.
public class SemVersionRangeConstraint(SemVersionRange range) : IVersionConstraint {
  public SemVersionRange Range { get; } = range;

  public bool IsSatisfiedBy(SemVersion version) => Range.Contains(version);

  public override string ToString() => Range.ToString();
}
