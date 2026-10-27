using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod.Parsing;

/// Stands in for VersionRange while it is implemented separately. "invalid" fails to parse.
internal record FakeVersionRange(string Text) : IVersionConstraint {
  public static IVersionConstraint Parse(string text) => text == "invalid"
      ? throw new FormatException("fake parse error")
      : new FakeVersionRange(text);

  public bool IsSatisfiedBy(SemVersion version) => true;
}
