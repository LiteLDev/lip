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
    Ref r = Ref.Parse(s, allowFile: false);

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
    Assert.Throws<FormatException>(() => Ref.Parse(s, allowFile: false));
  }

  [Fact]
  public void Parse_VersionWithLeadingV_SaysToDropIt() {
    // Act
    FormatException ex = Assert.Throws<FormatException>(
        () => Ref.Parse("github.com/a/b@v1.0.0", allowFile: false));

    // Assert
    Assert.Contains("'github.com/a/b@v1.0.0'", ex.Message);
    Assert.Contains("'1.0.0'", ex.Message);
  }

  [Theory]
  [InlineData("https://example.com/x.zip@tag:v1")]
  [InlineData("https://example.com/a%20b.zip")]
  [InlineData("https://[::1]/a.zip")]
  public void Parse_AtInUrl_IsPartOfTheLocation(string s) {
    // Act
    Ref r = Ref.Parse(s, allowFile: false);

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
    Ref r = Ref.Parse(s, allowFile: false);

    // Assert
    Assert.Equal(RefKind.Spec, r.Kind);
    Assert.Equal(target, r.Target);
    Assert.Equal(variant, r.Variant);
    Assert.Equal(kind, r.GitRef?.Kind);
    Assert.Equal(name, r.GitRef?.Name);
    Assert.Equal(r, Ref.Parse(r.ToString(), allowFile: false));
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
    Assert.Throws<FormatException>(() => Ref.Parse(text, allowFile: false));
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

  [Theory]
  [InlineData("pkg%41dir", "pkg%2541dir")]
  [InlineData("a%20b", "a%2520b")]
  [InlineData("a #1", "a%20%231")]
  [InlineData("why?.zip", "why%3F.zip")]
  [InlineData("模组 é", "%E6%A8%A1%E7%BB%84%20%C3%A9")]
  [InlineData("a;b=c@d", "a;b=c@d")]
  public void FromFile_SpecialCharacters_PercentEncodesThemAndRoundTrips(
      string name, string encoded) {
    // Arrange: a '%' in a path is no escape (protocol/workspace.md §7.1, RFC 8089).
    string path = OperatingSystem.IsWindows() ? $@"C:\ws\{name}\x" : $"/ws/{name}/x";
    string uri = OperatingSystem.IsWindows()
        ? $"file:///C:/ws/{encoded}/x"
        : $"file:///ws/{encoded}/x";

    // Act
    Ref r = Ref.FromFile(path, "lua");
    Ref parsed = Ref.Parse(r.ToString(), allowFile: true);

    // Assert
    Assert.Equal(uri, r.Target);
    Assert.Equal(path, r.LocalPath);
    Assert.Equal(r, parsed);
    Assert.Equal(path, parsed.LocalPath);
  }

  [Fact]
  public void Parse_FileUri_DecodesEveryPercentEncodedOctetOnce() {
    // Arrange
    string uri = OperatingSystem.IsWindows()
        ? "file:///c:/ws/pkg%2541dir/%E6%A8%A1"
        : "file:///ws/pkg%2541dir/%E6%A8%A1";

    // Act
    Ref r = Ref.Parse(uri, allowFile: true);

    // Assert
    Assert.Equal(
        OperatingSystem.IsWindows() ? @"c:\ws\pkg%41dir\模" : "/ws/pkg%41dir/模", r.LocalPath);
    Assert.Equal(uri, r.Target);
  }

  [Theory]
  [InlineData("a?x=1")]
  [InlineData("a%zz")]
  [InlineData("a%2Fb")]
  [InlineData("%FF")]
  [InlineData("a b")]
  [InlineData("[a]")]
  public void Parse_InvalidFileUri_ThrowsFormatException(string name) {
    // Arrange
    string uri = (OperatingSystem.IsWindows() ? "file:///C:/ws/" : "file:///ws/") + name;

    // Act & Assert
    Assert.Throws<FormatException>(() => Ref.Parse(uri, allowFile: true));
  }

  [Fact]
  public void FromFile_UncPathOnWindows_RoundTrips() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    // Arrange
    string path = @"\\server\share\a%41 #";

    // Act
    Ref r = Ref.FromFile(path);

    // Assert
    Assert.Equal("file://server/share/a%2541%20%23", r.Target);
    Assert.Equal(path, r.LocalPath);
    Assert.Equal(r, Ref.Parse(r.ToString(), allowFile: true));
  }

  [Fact]
  public void FromFile_UncHostWithAtOrColonOnWindows_EscapesThem() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "UNC paths are Windows paths.");

    // Arrange: a reg-name has neither '@' (userinfo) nor ':' (port) [RFC3986].
    string path = @"\\server@SSL\share\x";

    // Act
    Ref r = Ref.FromFile(path);

    // Assert
    Assert.Equal("file://server%40SSL/share/x", r.Target);
    Assert.Equal(path, r.LocalPath);
    Assert.Equal(r, Ref.Parse(r.ToString(), allowFile: true));
  }

  [Theory]
  [InlineData(@"\\localhost\share\x")]
  [InlineData(@"\\LOCALHOST\c$\x")]
  public void FromFile_UncPathOnLocalhostOnWindows_RoundTrips(string path) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "UNC paths are Windows paths.");

    // Act
    Ref r = Ref.FromFile(path);

    // Assert
    Assert.Equal(path, r.LocalPath);
    Assert.Equal(r, Ref.Parse(r.ToString(), allowFile: true));
  }

  [Theory]
  [InlineData("file://user@server/share/x")]
  [InlineData("file://server:80/share/x")]
  public void Parse_FileUriWithUserinfoOrPort_ThrowsFormatException(string uri) {
    Assert.Throws<FormatException>(() => Ref.Parse(uri, allowFile: true));
  }

  [Fact]
  public void WithVariant_ReplacesVariant() {
    // Act
    Ref r = Ref.Parse("github.com/a/b#client", allowFile: false).WithVariant("");

    // Assert
    Assert.Equal("github.com/a/b", r.ToString());
  }
}
