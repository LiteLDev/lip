using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class RefTemplateTests {
  private static readonly SemVersion _version = SemVersion.Parse("0.22.0");

  [Fact]
  public void Expand_AllPlaceholders_Substitutes() {
    // Arrange
    string template = "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/"
        + "LegacyMoney-{{platform}}-{{runtime}}.mcmod";

    // Act
    Ref r = RefTemplate.Expand(template, _version, new(McmodPlatform.Server, "win-x64"));

    // Assert
    Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-server-win-x64.mcmod",
        r.Target);
  }

  [Fact]
  public void Expand_MissingVersion_ThrowsFormatException() {
    // Act & Assert
    Assert.Throws<FormatException>(
        () => RefTemplate.Expand("./{{version}}/", null, new(McmodPlatform.Server, "win-x64")));
  }

  [Fact]
  public void Expand_NoVersionPlaceholder_NeedsNoVersion() {
    // Act
    Ref r = RefTemplate.Expand("./{{platform}}/", null, new(McmodPlatform.Client, "win-x64"));

    // Assert
    Assert.Equal("./client/", r.Target);
  }

  [Fact]
  public void Substitute_IdPlaceholder_ThrowsFormatException() {
    // Act & Assert
    Assert.Throws<FormatException>(
        () => RefTemplate.Substitute("./{{id}}/", "1.0.0", new(McmodPlatform.Server, "win-x64")));
  }

  [Theory]
  [InlineData("./{{version}}/{{platform}}/{{runtime}}", null)]
  [InlineData("./{{id}}/", "{{id}}")]
  [InlineData("./{{name}}/", "{{name}}")]
  [InlineData("./{{rid}}/", "{{rid}}")]
  [InlineData("./{{ version }}/", "{{ version }}")]
  [InlineData("./a{{b", "{{b")]
  [InlineData("./{{version}}/{{x", "{{x")]
  [InlineData("./{{{version}}/", "{{{version}}")]
  public void FindUnknownPlaceholder_ReturnsFirstUnknown(string template, string? expected) {
    // Act & Assert
    Assert.Equal(expected, RefTemplate.FindUnknownPlaceholder(template));
  }
}
