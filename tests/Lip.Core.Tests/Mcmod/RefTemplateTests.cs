using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class RefTemplateTests {
  private static readonly MetaTarget _target = new() {
    Source = ["./a/"],
    TemplateId = "LegacyMoney",
    TemplateVersion = SemVersion.Parse("0.22.0"),
  };

  [Fact]
  public void Expand_AllPlaceholders_Substitutes() {
    // Arrange
    string template = "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/"
        + "{{id}}-{{platform}}-{{runtime}}.mcmod";

    // Act
    Ref r = RefTemplate.Expand(template, _target, new(McmodPlatform.Server, "win-x64"));

    // Assert
    Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-server-win-x64.mcmod",
        r.Target);
  }

  [Fact]
  public void Expand_MissingId_ThrowsFormatException() {
    // Arrange
    MetaTarget target = _target with { TemplateId = null };

    // Act & Assert
    Assert.Throws<FormatException>(
        () => RefTemplate.Expand("./{{id}}/", target, new(McmodPlatform.Server, "win-x64")));
  }

  [Theory]
  [InlineData("./{{id}}/{{runtime}}", null)]
  [InlineData("./{{name}}/", "{{name}}")]
  [InlineData("./{{rid}}/", "{{rid}}")]
  [InlineData("./{{ id }}/", "{{ id }}")]
  [InlineData("./a{{b", "{{b")]
  [InlineData("./{{id}}/{{x", "{{x")]
  [InlineData("./{{{id}}/", "{{{id}}")]
  public void FindUnknownPlaceholder_ReturnsFirstUnknown(string template, string? expected) {
    // Act & Assert
    Assert.Equal(expected, RefTemplate.FindUnknownPlaceholder(template));
  }
}
