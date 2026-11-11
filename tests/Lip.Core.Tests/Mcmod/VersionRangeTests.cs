using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

/// Ranges (protocol/lip-package.md, "Terms"): SemVersionRange.Parse of Semver 3.0.0 with its
/// default options.
public class VersionRangeTests {
  [Theory]
  [InlineData(">=1.2.0 <2.0.0", "1.2.0", true)]
  [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
  [InlineData(">=1.2.0 <2.0.0 || 3.0.0", "3.0.0", true)]
  [InlineData(">1.2.3", "1.2.4", true)]
  [InlineData("<=1.2.3", "1.2.4", false)]
  [InlineData("^1.2.3", "1.9.0", true)]
  [InlineData("^1.2.3", "2.0.0", false)]
  [InlineData("^0.2.3", "0.2.9", true)]
  [InlineData("^0.2.3", "0.3.0", false)]
  [InlineData("~1.2.3", "1.2.9", true)]
  [InlineData("~1.2.3", "1.3.0", false)]
  [InlineData("1.2.3", "1.2.3", true)]
  [InlineData("1.2.3", "1.2.4", false)]
  [InlineData("*", "42.0.0", true)]
  [InlineData("1.*", "1.5.0", true)]
  [InlineData("1.*", "2.0.0", false)]
  public void IsSatisfiedBy_Table_FollowsSemVersionRangeParse(
      string range, string version, bool expected) {
    Assert.Equal(
        expected,
        VersionRange.Parse(range).IsSatisfiedBy(SemVersion.Parse(version, SemVersionStyles.Strict)));
  }

  [Theory]
  [InlineData("*", "1.0.0-alpha", false)]
  [InlineData("^1.0.0", "1.1.0-rc.1", false)]
  [InlineData("*-*", "1.0.0-alpha", true)]
  [InlineData(">=2.0.0-0", "2.0.0-beta", true)]
  public void IsSatisfiedBy_Prerelease_OnlyWhenTheRangeIncludesPrereleases(
      string range, string version, bool expected) {
    Assert.Equal(
        expected,
        VersionRange.Parse(range).IsSatisfiedBy(SemVersion.Parse(version, SemVersionStyles.Strict)));
  }

  [Theory]
  [InlineData("not a range")]
  [InlineData(">=1.0.0 <")]
  [InlineData("^^1.0.0")]
  public void Parse_InvalidRange_ThrowsFormatException(string range) {
    FormatException ex = Assert.Throws<FormatException>(() => VersionRange.Parse(range));
    Assert.False(VersionRange.TryParse(range, out _));

    // The message names the range; Semver's reason is kept as the inner exception.
    Assert.Contains($"'{range}'", ex.Message);
    Assert.NotNull(ex.InnerException);
  }

  [Fact]
  public void Text_AnyRange_KeepsOriginalString() {
    Assert.Equal(">= 1.0.0", VersionRange.Parse(">= 1.0.0").Text);
    Assert.Equal(">= 1.0.0", VersionRange.Parse(">= 1.0.0").ToString());
  }

  [Fact]
  public void ExactVersionConstraint_IgnoresBuildMetadata() {
    ExactVersionConstraint exact = new(SemVersion.Parse("1.2.3+a", SemVersionStyles.Strict));

    Assert.True(exact.IsSatisfiedBy(SemVersion.Parse("1.2.3+b", SemVersionStyles.Strict)));
    Assert.False(exact.IsSatisfiedBy(SemVersion.Parse("1.2.4", SemVersionStyles.Strict)));
  }
}
