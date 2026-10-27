using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

public class RefTests {
  [Theory]
  [InlineData("./quickjs/", RefKind.Relative, "./quickjs/", "")]
  [InlineData("./a/b.mcmod#lua", RefKind.Relative, "./a/b.mcmod", "lua")]
  [InlineData("https://example.com/x.mcmod", RefKind.Url, "https://example.com/x.mcmod", "")]
  [InlineData("https://example.com/x.mcmod#a/b", RefKind.Url, "https://example.com/x.mcmod", "a/b")]
  [InlineData("github.com/LiteLDev/LeviLamina", RefKind.Spec, "github.com/LiteLDev/LeviLamina", "")]
  [InlineData("github.com/LiteLDev/LeviLamina#client", RefKind.Spec, "github.com/LiteLDev/LeviLamina", "client")]
  public void Parse_ValidRef_ReturnsParts(string s, RefKind kind, string target, string variant) {
    // Act
    Ref r = Ref.Parse(s);

    // Assert
    Assert.Equal(kind, r.Kind);
    Assert.Equal(target, r.Target);
    Assert.Equal(variant, r.Variant);
    Assert.Equal(s, r.ToString());
  }

  [Theory]
  [InlineData("github.com/a/b#")]
  [InlineData("github.com/a/b#UPPER")]
  [InlineData("./")]
  [InlineData("./a//b")]
  [InlineData("./a/../b")]
  [InlineData("./a/./b")]
  [InlineData("./a\\b")]
  [InlineData("http://example.com/x.zip")]
  [InlineData("file:///c/x.zip")]
  [InlineData("https://")]
  [InlineData("")]
  public void Parse_InvalidRef_ThrowsFormatException(string s) {
    // Act & Assert
    Assert.Throws<FormatException>(() => Ref.Parse(s));
  }

  [Fact]
  public void RelativePath_DirectoryRef_StripsPrefixAndTrailingSlash() {
    // Arrange
    Ref r = Ref.Parse("./sub/dir/");

    // Act & Assert
    Assert.Equal("sub/dir", r.RelativePath);
    Assert.True(r.IsDirectory);
  }

  [Fact]
  public void WithVariant_ReplacesVariant() {
    // Act
    Ref r = Ref.Parse("github.com/a/b#client").WithVariant("");

    // Assert
    Assert.Equal("github.com/a/b", r.ToString());
  }
}
