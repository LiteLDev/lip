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
