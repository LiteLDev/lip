using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class VersionRangeTests {
  // Ported from LeviLamina src-test/common/VersionTest.cpp
  // (VersionRequirementTest.MatchesSupportedRangeSyntax), commit
  // 5811462b56614841386fabd853a0c08af7eeb9e8.
  [Theory]
  [InlineData(">=1.2.0 <2.0.0", "1.2.0", true)]
  [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
  [InlineData(">= 1.2.0 < 2.0.0 || = 3.0.0", "3.0.0", true)]
  [InlineData(">1.2.3", "1.2.4", true)]
  [InlineData("<=1.2.3", "1.2.4", false)]
  [InlineData("^1.2.3", "1.9.0", true)]
  [InlineData("^1.2.3", "2.0.0", false)]
  [InlineData("^0.2.3", "0.2.9", true)]
  [InlineData("^0.2.3", "0.3.0", false)]
  [InlineData("^0.0.3", "0.0.4", false)]
  [InlineData("^0.0", "0.0.9", true)]
  [InlineData("^0.0", "0.1.0", false)]
  [InlineData("^0", "0.9.9", true)]
  [InlineData("^0", "1.0.0", false)]
  [InlineData("~1.2.3", "1.2.9", true)]
  [InlineData("~1.2.3", "1.3.0", false)]
  [InlineData("~1", "1.9.9", true)]
  [InlineData("~1", "2.0.0", false)]
  [InlineData("1", "1.9.9", true)]
  [InlineData("1", "2.0.0", false)]
  [InlineData("1.2", "1.2.99", true)]
  [InlineData("1.2", "1.3.0", false)]
  [InlineData("1.2.x", "1.2.99", true)]
  [InlineData("1.2.x", "1.3.0", false)]
  [InlineData("1.2.X", "1.2.99", true)]
  [InlineData("*", "42.0.0", true)]
  [InlineData("x", "42.0.0", true)]
  [InlineData("X", "42.0.0", true)]
  [InlineData("1.2.3 - 2.0.0", "2.0.0", true)]
  [InlineData("1.2.3 - 2.0.0", "2.0.1", false)]
  [InlineData("1.2 - 2.3", "2.3.99", true)]
  [InlineData("1.2 - 2.3", "2.4.0", false)]
  // From VersionRequirementTest.PreservesLegacyBareVersionBehavior (the "=" cases only; the
  // bare full version case is rejected by the spec, see Parse_BareFullVersion_Throws).
  [InlineData("=1.2.3", "1.2.3+other", true)]
  [InlineData("=1.2.3", "1.2.4", false)]
  // From supportsConstantEvaluation().
  [InlineData("^1.2.3 || =2.0.0", "1.9.0", true)]
  [InlineData("^1.2.3 || =2.0.0", "2.0.0", true)]
  [InlineData("^1.2.3 || =2.0.0", "3.0.0", false)]
  // From VersionRequirementTest.AppliesPrereleaseGatingPerAlternative.
  [InlineData(">=1.2.3 <2.0.0", "1.3.0-beta.1", false)]
  [InlineData(">=1.3.0-beta.1 <2.0.0", "1.3.0-beta.2", true)]
  [InlineData(">=1.3.0-beta.1 <2.0.0", "1.4.0-beta.1", false)]
  [InlineData(">=1.3.0-beta.1 || >=2.0.0", "2.0.0-beta.1", false)]
  [InlineData(">=1.3.0-beta.1 || >=2.0.0-beta.1", "2.0.0-beta.2", true)]
  [InlineData("*", "1.0.0-alpha", false)]
  public void IsSatisfiedBy_LeviLaminaTestTable_MatchesLeviLamina(
      string range, string version, bool expected) {
    // Arrange
    VersionRange r = VersionRange.Parse(range);

    // Act
    bool result = r.IsSatisfiedBy(SemVersion.Parse(version));

    // Assert
    Assert.Equal(expected, result);
  }

  // Ranges seen in the wild: LeviLamina docs (mod.md, data.md), its ModLoadPlannerTest.cpp, and
  // the lip spec's examples.
  [Theory]
  [InlineData("^1.2.3", "1.2.3", true)]
  [InlineData("^1.2.3", "1.2.2", false)]
  [InlineData(">=2.0.0 <3.0.0", "2.5.1", true)]
  [InlineData(">=2.0.0 <3.0.0", "3.0.0", false)]
  [InlineData(">=1.0.0", "1.0.0", true)]
  [InlineData(">=1.0.0", "0.9.9", false)]
  [InlineData("^1.0.0", "1.99.0", true)]
  [InlineData("^0.17.0", "0.17.3", true)]
  [InlineData("^0.17.0", "0.18.0", false)]
  [InlineData(">=26.51.0 <26.52.0", "26.51.8", true)]
  [InlineData(">=26.51.0 <26.52.0", "26.52.0", false)]
  [InlineData(">=26.51.0 <26.52.0", "26.51.9-rc.1", false)]
  public void IsSatisfiedBy_RealWorldRange_Matches(string range, string version, bool expected) {
    // Arrange
    VersionRange r = VersionRange.Parse(range);

    // Act
    bool result = r.IsSatisfiedBy(SemVersion.Parse(version));

    // Assert
    Assert.Equal(expected, result);
  }

  [Theory]
  [InlineData("", "1.0.0", true)]
  [InlineData(" \t\r\n", "0.0.0", true)]
  [InlineData("", "1.0.0-alpha", false)]
  [InlineData("=*", "1.0.0", true)]
  [InlineData("x.x.x", "3.2.1", true)]
  [InlineData("1.x", "1.5.0", true)]
  [InlineData("1.x", "2.0.0", false)]
  [InlineData("1.x.x", "1.0.0", true)]
  [InlineData("=1.2", "1.2.7", true)]
  [InlineData("=1.2", "1.3.0", false)]
  [InlineData("=1.2.3-beta.1", "1.2.3-beta.1", true)]
  [InlineData("=1.2.3+build", "1.2.3+other", true)]
  [InlineData(">1", "1.9.9", false)]
  [InlineData(">1", "2.0.0", true)]
  [InlineData(">1.2", "1.3.0", true)]
  [InlineData(">1.2", "1.2.9", false)]
  [InlineData("<1.2", "1.1.9", true)]
  [InlineData("<1.2", "1.2.0", false)]
  [InlineData("<=1.2", "1.2.9", true)]
  [InlineData("<=1.2", "1.3.0", false)]
  [InlineData("^0.0.x", "0.0.9", true)]
  [InlineData("^0.0.x", "0.1.0", false)]
  [InlineData("^1.2.3-beta", "1.2.3-beta.2", true)]
  [InlineData("^1.2.3-beta", "1.3.0-beta", false)]
  [InlineData("^1.2.3-beta", "1.3.0", true)]
  [InlineData("~1.2.3-beta", "1.2.3", true)]
  [InlineData("1.2.3-alpha - 1.2.3", "1.2.3-beta", true)]
  [InlineData("1 - 2", "2.9.9", true)]
  [InlineData("1 - 2", "3.0.0", false)]
  [InlineData("65535", "65535.9.9", true)]
  [InlineData("65535", "70000.0.0", true)]
  [InlineData("^65535.1.2", "70000.0.0", true)]
  [InlineData("~1.65535", "2.0.0", false)]
  [InlineData("~1.65535", "1.65535.9", true)]
  [InlineData("^0.0.65535", "0.1.0", false)]
  [InlineData("^0.0.65535", "0.0.65535", true)]
  [InlineData(">=1.0.0", "1.0.0+meta", true)]
  [InlineData("<2.0.0", "2.0.0-rc.1", false)]
  [InlineData("  >=1.0.0\t<2.0.0  ||\n=3.0.0 ", "3.0.0", true)]
  [InlineData("> =1.0.0", "1.0.0", true)]
  [InlineData("2.0.0 - 1.0.0", "1.5.0", false)]
  public void IsSatisfiedBy_EdgeCase_FollowsVersionRequirement(
      string range, string version, bool expected) {
    // Arrange
    VersionRange r = VersionRange.Parse(range);

    // Act
    bool result = r.IsSatisfiedBy(SemVersion.Parse(version));

    // Assert
    Assert.Equal(expected, result);
  }

  // The first eight are VersionRequirementTest.RejectsInvalidRangesWithoutChangingValue.
  [Theory]
  [InlineData("||")]
  [InlineData("1.2.3 ||")]
  [InlineData(">=")]
  [InlineData("> 1.2.3 <")]
  [InlineData("1.x.3")]
  [InlineData("1.2.3 -")]
  [InlineData("(>=1.0.0)")]
  [InlineData("^1.2-beta")]
  [InlineData("|| ^1.0.0")]
  [InlineData("^1.0.0 ||| ^2.0.0")]
  [InlineData(">=*")]
  [InlineData("^x")]
  [InlineData("~*")]
  [InlineData("<x.x")]
  [InlineData("1.2.3.4")]
  [InlineData("01.2")]
  [InlineData("=1.02.3")]
  [InlineData("^1.2.3-alpha.01")]
  [InlineData("^1.2.3-")]
  [InlineData("^1.2.3+")]
  [InlineData("^1.2.3+build_1")]
  [InlineData("^65536")]
  [InlineData("^1.2.3-beta.65536")]
  [InlineData(">65535")]
  [InlineData(">65535.65535")]
  [InlineData("<=65535")]
  [InlineData("v1.2.3")]
  [InlineData("^1.")]
  [InlineData(">= >=1.0.0")]
  [InlineData("==1.0.0")]
  [InlineData("1.2.3 - *")]
  [InlineData(">=1.0.0 - 2.0.0")]
  [InlineData("^1.0.0\v")]
  [InlineData("^1.0.0 ")]
  [InlineData("^１.0.0")]
  public void Parse_InvalidRange_ThrowsFormatException(string range) {
    // Act & Assert
    Assert.Throws<FormatException>(() => VersionRange.Parse(range));
  }

  // The spec deviation; "0.2.0" is used by OEOTYAN/WorldEdit-BE's manifest.json.
  [Theory]
  [InlineData("1.2.3")]
  [InlineData("0.2.0")]
  [InlineData("1.2.3-beta")]
  [InlineData("1.2.3+build")]
  [InlineData(" 1.2.3 ")]
  [InlineData("^1.0.0 || 2.0.0")]
  [InlineData(">=1.0.0 1.5.0")]
  public void Parse_BareFullVersion_Throws(string range) {
    // Act & Assert
    Assert.Throws<FormatException>(() => VersionRange.Parse(range));
  }

  [Theory]
  [InlineData("=1.2.3")]
  [InlineData("= 1.2.3")]
  [InlineData("1.2.3 - 2.0.0")]
  [InlineData("1")]
  [InlineData("1.2")]
  [InlineData("1.x")]
  [InlineData("*")]
  public void Parse_NotABareFullVersion_Succeeds(string range) {
    // Act
    VersionRange r = VersionRange.Parse(range);

    // Assert
    Assert.Equal(range, r.Text);
  }

  [Fact]
  public void Text_AnyRange_KeepsOriginalString() {
    // Arrange
    const string text = "  >= 1.2.0  <2.0.0 || = 3.0.0 ";

    // Act
    VersionRange r = VersionRange.Parse(text);

    // Assert
    Assert.Equal(text, r.Text);
    Assert.Equal(text, r.ToString());
  }
}
