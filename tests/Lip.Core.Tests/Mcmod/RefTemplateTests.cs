using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class RefTemplateTests {
  private static readonly RoutingDocument _document = new() {
    IsMarked = true,
    Name = "LegacyMoney",
    Version = SemVersion.Parse("0.22.0"),
    Artifacts = [],
  };

  [Fact]
  public void Expand_AllPlaceholders_Substitutes() {
    // Arrange
    string template = "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/"
        + "{{name}}-{{platform}}-{{rid}}.mcmod";

    // Act
    Ref r = RefTemplate.Expand(template, _document, new(McmodPlatform.Server, "win-x64"));

    // Assert
    Assert.Equal(
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
        + "LegacyMoney-server-win-x64.mcmod",
        r.Target);
  }

  [Fact]
  public void Expand_MissingName_ThrowsFormatException() {
    // Arrange
    RoutingDocument document = _document with { Name = null };

    // Act & Assert
    Assert.Throws<FormatException>(
        () => RefTemplate.Expand("./{{name}}/", document, new(McmodPlatform.Server, "win-x64")));
  }

  [Theory]
  [InlineData("./{{name}}/{{rid}}", null)]
  [InlineData("./{{os}}/", "{{os}}")]
  [InlineData("./{{ name }}/", "{{ name }}")]
  public void FindUnknownPlaceholder_ReturnsFirstUnknown(string template, string? expected) {
    // Act & Assert
    Assert.Equal(expected, RefTemplate.FindUnknownPlaceholder(template));
  }
}
