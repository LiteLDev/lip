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
  [InlineData("github.com/a/b@1.0.0")]
  [InlineData("github.com/a/b@branch:")]
  [InlineData("github.com/a/b@branch:a b")]
  [InlineData("github.com/a/b@tag:x.lock")]
  [InlineData("github.com/a/b@rev:0123abc")]
  [InlineData("./a/@branch:main")]
  [InlineData("https://example.com/x.zip@tag:v1")]
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

  [Theory]
  [InlineData("github.com/a/b@branch:main", "github.com/a/b", "", GitRefKind.Branch, "main")]
  [InlineData("github.com/a/b#lua@tag:v1.0.0", "github.com/a/b", "lua", GitRefKind.Tag, "v1.0.0")]
  [InlineData("github.com/a/b@branch:feat/a#b@c", "github.com/a/b", "", GitRefKind.Branch,
      "feat/a#b@c")]
  [InlineData("github.com/a/b@rev:0123456789ABCDEF0123456789abcdef01234567", "github.com/a/b", "",
      GitRefKind.Rev, "0123456789abcdef0123456789abcdef01234567")]
  public void Parse_SpecWithGitRef_SplitsTargetVariantAndGitRef(
      string s, string target, string variant, GitRefKind kind, string name) {
    // Act
    Ref r = Ref.Parse(s);

    // Assert
    Assert.Equal(RefKind.Spec, r.Kind);
    Assert.Equal(target, r.Target);
    Assert.Equal(variant, r.Variant);
    Assert.Equal(kind, r.GitRef?.Kind);
    Assert.Equal(name, r.GitRef?.Name);
    Assert.Equal(r, Ref.Parse(r.ToString()));
  }

  [Fact]
  public void Parse_FileRef_OnlyWhenAllowed() {
    // Arrange
    string path = OperatingSystem.IsWindows() ? @"C:\mods\a #1.mcmod" : "/mods/a #1.mcmod";
    string text = Ref.FromFile(path, "lua").ToString();

    // Act
    Ref r = Ref.Parse(text, allowFile: true);

    // Assert
    Assert.Equal(RefKind.File, r.Kind);
    Assert.Equal(path, r.LocalPath);
    Assert.Equal("lua", r.Variant);
    Assert.Throws<FormatException>(() => Ref.Parse(text));
  }

  [Fact]
  public void WithVariant_ReplacesVariant() {
    // Act
    Ref r = Ref.Parse("github.com/a/b#client").WithVariant("");

    // Assert
    Assert.Equal("github.com/a/b", r.ToString());
  }
}
