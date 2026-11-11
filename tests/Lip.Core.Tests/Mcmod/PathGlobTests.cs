using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

public class PathGlobTests {
  [Theory]
  [InlineData("config.json", "config.json", true)]
  [InlineData("config.json", "a/config.json", false)]
  [InlineData("*.json", "config.json", true)]
  [InlineData("*.json", "a/config.json", false)]
  [InlineData("**/*.json", "config.json", true)]
  [InlineData("**/*.json", "a/b/config.json", true)]
  [InlineData("a/**", "a", true)]
  [InlineData("a/**", "a/b/c", true)]
  [InlineData("a/*/c", "a/b/c", true)]
  [InlineData("a/*/c", "a/b/x/c", false)]
  [InlineData("config/", "config/", true)]
  [InlineData("config/", "config/a.json", true)]
  [InlineData("config/", "config/x/a.json", true)]
  [InlineData("config/", "config", false)]
  [InlineData("config", "config/", false)]
  [InlineData("**/data/", "x/data/y.bin", true)]
  [InlineData("**/data/", "data/", true)]
  [InlineData("**/", "file.txt", true)]
  [InlineData("**/", "a/b/file.txt", true)]
  [InlineData("**/**/", "file.txt", true)]
  [InlineData("x/**/", "file.txt", false)]
  [InlineData("x/**/", "x/file.txt", true)]
  [InlineData("a.?", "a.b", true)]
  [InlineData("a.?", "a.?", true)]
  [InlineData("a.?", "a.bc", false)]
  [InlineData("*", ".env", true)]
  [InlineData("config/*", "config/.secret", true)]
  [InlineData("[ab].txt", "b.txt", true)]
  [InlineData("[ab].txt", "c.txt", false)]
  [InlineData("[!ab].txt", "c.txt", true)]
  [InlineData("[!ab].txt", "a.txt", false)]
  [InlineData("log[0-9].txt", "log7.txt", true)]
  [InlineData("log[0-9].txt", "logx.txt", false)]
  [InlineData("[[:digit:]][[:alpha:]]", "1a", true)]
  [InlineData("[[:digit:]][[:alpha:]]", "a1", false)]
  [InlineData("[]a]", "]", true)]
  [InlineData("[!]a]", "b", true)]
  [InlineData("[[.-.]]", "-", true)]
  [InlineData("[[=x=]]", "x", true)]
  [InlineData("a?c", "a/c", false)]
  public void IsMatch_ReturnsExpected(string pattern, string path, bool expected) {
    // Act & Assert
    Assert.Equal(expected, new PathGlob(pattern).IsMatch(path));
  }

  /// Ignoring case, A-Z equal a-z, and no other characters are folded (protocol/lip-workspace.md,
  /// "Environment and layout").
  [Theory]
  [InlineData("Config.json", "config.JSON", true)]
  [InlineData("CONFIG/", "config/a.json", true)]
  [InlineData("*.LOG", "a.log", true)]
  [InlineData("**/Data/", "x/DATA/y.bin", true)]
  [InlineData("[ab].txt", "B.TXT", true)]
  [InlineData("[!ab].txt", "B.txt", false)]
  [InlineData("[!ab].txt", "c.txt", true)]
  [InlineData("[A-C]", "b", true)]
  [InlineData("[A-C]", "d", false)]
  [InlineData("[[:upper:]]", "a", true)]
  [InlineData("[[:lower:]]", "A", true)]
  [InlineData("[!Z-a]", "_", false)]
  [InlineData("[!Z-a]", "A", false)]
  [InlineData("[!Z-a]", "b", true)]
  [InlineData("É.txt", "é.txt", false)]
  [InlineData("*é", "Xé", true)]
  [InlineData("*É", "xé", false)]
  public void IsMatch_IgnoringCase_FoldsAsciiLettersOnly(
      string pattern, string path, bool expected) {
    // Act & Assert
    Assert.Equal(expected, new PathGlob(pattern).IsMatch(path, ignoreCase: true));
  }

  [Theory]
  [InlineData("Config.json", "config.json")]
  [InlineData("[ab].txt", "B.txt")]
  [InlineData("*.LOG", "a.log")]
  public void IsMatch_NotIgnoringCase_TellsCaseApart(string pattern, string path) {
    // Act & Assert
    Assert.False(new PathGlob(pattern).IsMatch(path));
  }

  [Theory]
  [InlineData("")]
  [InlineData("/a")]
  [InlineData("a//b")]
  [InlineData("a\\b")]
  [InlineData("[^a]")]
  [InlineData("[[:word:]]")]
  [InlineData("[z-a]")]
  public void Constructor_InvalidPattern_ThrowsFormatException(string pattern) {
    // Act & Assert
    FormatException ex = Assert.Throws<FormatException>(() => new PathGlob(pattern));
    Assert.Contains($"'{pattern}'", ex.Message);
  }

  /// A '[' that starts no complete bracket expression matches itself (POSIX fnmatch).
  [Theory]
  [InlineData("foo[", "foo[", true)]
  [InlineData("foo[", "foo", false)]
  [InlineData("a[]", "a[]", true)]
  [InlineData("[ab", "[ab", true)]
  [InlineData("[ab", "a", false)]
  [InlineData("[!a", "[!a", true)]
  [InlineData("[^a", "[^a", true)]
  [InlineData("*[", "x[", true)]
  [InlineData("[[:alpha]", "[a", true)]
  [InlineData("[a/b]", "[a/b]", true)]
  public void IsMatch_UnclosedBracket_MatchesLiteralBracket(
      string pattern, string path, bool expected) {
    // Act & Assert
    Assert.Equal(expected, new PathGlob(pattern).IsMatch(path));
  }
}
