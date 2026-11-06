using Flurl;

namespace Lip.Core.Git;

/// The `github_proxy` configuration key (protocol/userspace.md §2).
public static class GithubProxy {
  /// The URL of a github.com request sent through the proxy, or null if no proxy applies.
  public static Url? Rewrite(Url? proxy, Url url) {
    if (proxy is null || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    Url proxied = proxy.Clone().AppendPathSegments(url.PathSegments);
    if (!string.IsNullOrEmpty(url.Query)) {
      proxied.Query = url.Query;
    }
    return proxied.ToString() == url.ToString() ? null : proxied;
  }
}
