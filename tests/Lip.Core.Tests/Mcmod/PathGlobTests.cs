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
  [InlineData("a.?", "a.b", false)]
  [InlineData("a.?", "a.?", true)]
  public void IsMatch_ReturnsExpected(string pattern, string path, bool expected) {
    // Act & Assert
    Assert.Equal(expected, new PathGlob(pattern).IsMatch(path));
  }

  [Theory]
  [InlineData("")]
  [InlineData("/a")]
  [InlineData("a//b")]
  [InlineData("a\\b")]
  public void Constructor_InvalidPattern_ThrowsFormatException(string pattern) {
    // Act & Assert
    Assert.Throws<FormatException>(() => new PathGlob(pattern));
  }
}
