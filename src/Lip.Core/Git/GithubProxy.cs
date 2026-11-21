using Flurl;

namespace Lip.Core.Git;

/// The `github_proxy` configuration key (protocol/config.toml.md, "User configuration"): a URI
/// that a GitHub request is sent through, as the URI followed by the whole URL of the request; an
/// empty or absent one is no proxy, passed here as null.
public static class GithubProxy {
  /// Endings of a github_proxy value that it is read without, so that a value written for the
  /// URL of a request without its host still works.
  private static readonly string[] _endings = ["/https://github.com", "/github.com"];

  /// Whether a request to url is a GitHub request: an HTTPS request whose host is github.com,
  /// or ends with .github.com or .githubusercontent.com.
  public static bool IsGithub(Url url) {
    if (!string.Equals(url.Scheme, "https", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    string host = url.Host.ToLowerInvariant();
    return host == "github.com"
        || host.EndsWith(".github.com", StringComparison.Ordinal)
        || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
  }

  /// What the URL of a request is appended to: the value of the proxy, without an ending of
  /// _endings, followed by "/".
  public static string Prefix(Url proxy) {
    string value = proxy.ToString();
    if (value.EndsWith('/')) {
      value = value[..^1];
    }
    foreach (string ending in _endings) {
      if (value.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) {
        value = value[..^ending.Length];
        break;
      }
    }
    return value + "/";
  }

  /// The URL of a GitHub request sent through the proxy, or null if no proxy applies.
  public static Url? Rewrite(Url? proxy, Url url) {
    if (proxy is null || !IsGithub(url)) {
      return null;
    }

    string original = url.ToString();
    string proxied = Prefix(proxy) + original;
    return proxied == original ? null : Url.Parse(proxied);
  }
}
