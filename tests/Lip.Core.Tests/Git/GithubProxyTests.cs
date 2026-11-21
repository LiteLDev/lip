using Flurl;
using Lip.Core.Git;

namespace Lip.Core.Tests.Git;

/// github_proxy (protocol/config.toml.md, "User configuration"): a GitHub request for the URL U
/// is sent to the URI, followed by "/" unless it ends with "/", followed by U.
public class GithubProxyTests {
  [Theory]
  [InlineData("https://github.com/LiteLDev/lip.git")]
  [InlineData("https://GitHub.com/LiteLDev/lip.git")]
  [InlineData("https://api.github.com/repos/LiteLDev/lip/releases")]
  [InlineData("https://codeload.github.com/LiteLDev/lip/zip/refs/tags/v1.0.0")]
  [InlineData("https://raw.githubusercontent.com/LiteLDev/lip/main/README.md")]
  [InlineData("https://release-assets.githubusercontent.com/x")]
  public void IsGithub_GithubHosts_IsTrue(string url) {
    Assert.True(GithubProxy.IsGithub(Url.Parse(url)));
  }

  [Theory]
  [InlineData("https://levimc.org/api/v4/mods/index.json")]
  [InlineData("https://notgithub.com/a")]
  [InlineData("https://github.com.example.com/a")]
  [InlineData("https://githubusercontent.com/a")]
  [InlineData("http://github.com/a")]
  public void IsGithub_OtherUrls_IsFalse(string url) {
    Assert.False(GithubProxy.IsGithub(Url.Parse(url)));
  }

  [Theory]
  [InlineData("https://proxy.example", "https://proxy.example/")]
  [InlineData("https://proxy.example/", "https://proxy.example/")]
  [InlineData("https://proxy.example/gh", "https://proxy.example/gh/")]
  [InlineData("https://proxy.example/github.com", "https://proxy.example/")]
  [InlineData("https://proxy.example/github.com/", "https://proxy.example/")]
  [InlineData("https://proxy.example/https://github.com", "https://proxy.example/")]
  [InlineData("https://proxy.example/https://github.com/", "https://proxy.example/")]
  [InlineData("https://proxy.example/gh/GitHub.com", "https://proxy.example/gh/")]
  public void Prefix_DropsAnEndingForGithubAndEndsWithSlash(string proxy, string expected) {
    Assert.Equal(expected, GithubProxy.Prefix(Url.Parse(proxy)));
  }

  [Theory]
  [InlineData(
      "https://proxy.example",
      "https://github.com/LiteLDev/lip.git/info/refs?service=git-upload-pack",
      "https://proxy.example/https://github.com/LiteLDev/lip.git/info/refs?service=git-upload-pack")]
  [InlineData(
      "https://proxy.example/github.com",
      "https://raw.githubusercontent.com/LiteLDev/lip/main/README.md",
      "https://proxy.example/https://raw.githubusercontent.com/LiteLDev/lip/main/README.md")]
  [InlineData(
      "https://proxy.example/gh/",
      "https://api.github.com/repos/LiteLDev/lip",
      "https://proxy.example/gh/https://api.github.com/repos/LiteLDev/lip")]
  public void Rewrite_GithubRequest_IsTheProxyFollowedByTheWholeUrl(
      string proxy, string url, string expected) {
    Assert.Equal(expected, GithubProxy.Rewrite(Url.Parse(proxy), Url.Parse(url))?.ToString());
  }

  [Fact]
  public void Rewrite_WithoutProxy_IsNull() {
    Assert.Null(GithubProxy.Rewrite(null, Url.Parse("https://github.com/a")));
  }

  [Fact]
  public void Rewrite_OtherHost_IsNull() {
    Assert.Null(GithubProxy.Rewrite(
        Url.Parse("https://proxy.example"), Url.Parse("https://levimc.org/api/v4/mc/versions/")));
  }
}
