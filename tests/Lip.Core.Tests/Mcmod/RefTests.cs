using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

/// Sources (protocol/package.md §2).
public class RefTests {
  [Theory]
  [InlineData("https://example.com/x.zip", RefKind.Url, "https://example.com/x.zip", "")]
  [InlineData("https://example.com/x.zip#a/b", RefKind.Url, "https://example.com/x.zip", "a/b")]
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
  [InlineData("./quickjs/")]
  [InlineData("./a/b.zip#lua")]
  [InlineData("http://example.com/x.zip")]
  [InlineData("file:///c/x.zip")]
  [InlineData("github.com/a/b@v1.0.0")]
  [InlineData("github.com/a/b@1.0")]
  [InlineData("github.com/a/b@branch:")]
  [InlineData("github.com/a/b@branch:a b")]
  [InlineData("github.com/a/b@tag:x.lock")]
  [InlineData("github.com/a/b@rev:0123abc")]
  [InlineData("github.com/a/b@rev:0123456789abcdef0123456789abcdef01234567\n")]
  [InlineData("github.com/a/b#lua\n")]
  [InlineData("github.com/a/b#c#d")]
  [InlineData("https://")]
  [InlineData("https://example.com/a b.zip")]
  [InlineData("https://example.com/é.zip")]
  [InlineData("https://example.com/%zz.zip")]
  [InlineData("https://example.com/a[1].zip")]
  [InlineData("https://example.com/a.zip?x[0]=1")]
  [InlineData("https://example.com/a.zip#lua#x")]
  [InlineData("")]
  public void Parse_InvalidRef_ThrowsFormatException(string s) {
    // Act & Assert
    Assert.Throws<FormatException>(() => Ref.Parse(s));
  }

  [Theory]
  [InlineData("https://example.com/x.zip@tag:v1")]
  [InlineData("https://example.com/a%20b.zip")]
  [InlineData("https://[::1]/a.zip")]
  public void Parse_AtInUrl_IsPartOfTheLocation(string s) {
    // Act
    Ref r = Ref.Parse(s);

    // Assert
    Assert.Equal(RefKind.Url, r.Kind);
    Assert.Equal(s, r.Target);
    Assert.Null(r.GitRef);
  }

  [Theory]
  [InlineData("github.com/a/b@1.0.0", "github.com/a/b", "", GitRefKind.Version, "1.0.0")]
  [InlineData("github.com/a/b#lua@1.0.0-rc.1", "github.com/a/b", "lua", GitRefKind.Version,
      "1.0.0-rc.1")]
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
    string path = OperatingSystem.IsWindows() ? @"C:\mods\a #1.zip" : "/mods/a #1.zip";
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
  public void FromFile_Directory_ReturnsFileRef() {
    // Arrange
    string path = OperatingSystem.IsWindows() ? @"C:\src\pkg" : "/src/pkg";

    // Act
    Ref r = Ref.FromFile(path);

    // Assert
    Assert.Equal(RefKind.File, r.Kind);
    Assert.Equal(path, r.LocalPath);
    Assert.Equal(r, Ref.Parse(r.ToString(), allowFile: true));
  }

  [Fact]
  public void WithVariant_ReplacesVariant() {
    // Act
    Ref r = Ref.Parse("github.com/a/b#client").WithVariant("");

    // Assert
    Assert.Equal("github.com/a/b", r.ToString());
  }

  [Fact]
  public void WithGitRef_NotARemoteRepository_Throws() {
    // Act & Assert
    Assert.Throws<InvalidOperationException>(
        () => Ref.Parse("https://example.com/x.zip").WithGitRef(GitRef.Parse("1.0.0")));
  }
}
