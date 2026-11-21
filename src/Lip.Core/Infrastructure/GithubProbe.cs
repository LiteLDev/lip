using Flurl;
using Flurl.Http;
using Lip.Core.Git;

namespace Lip.Core.Infrastructure;

/// The reachability check of GitHub (protocol/lip-tui.md, "GitHub reachability"): an HTTPS GET of
/// the references of the repository of lip, directly or through a GitHub proxy, which succeeds
/// when a response of status 2xx arrives in time.
///
/// send sends a GET of a URL, following redirects, and returns the status of the response once
/// its headers arrive; it throws when no response arrives, and stops when the token is cancelled.
/// Without it, the request goes through Flurl, with the proxy of the current invocation. timeout
/// is how long the check waits for the response: Timeout when not given.
public sealed class GithubProbe(
    Func<Url, CancellationToken, Task<int>>? send = null, TimeSpan? timeout = null) {
  /// The URL of the request of the check.
  public const string ProbeUrl =
      "https://github.com/LiteLDev/lip.git/info/refs?service=git-upload-pack";

  /// How long the check waits for the response (protocol/lip-tui.md, "GitHub reachability").
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

  private readonly Func<Url, CancellationToken, Task<int>> _send = send ?? Send;
  private readonly TimeSpan _timeout = timeout ?? Timeout;

  /// Checks that GitHub is reached, directly when proxy is null or empty, otherwise through the
  /// proxy, a URI prefix as github_proxy is (protocol/config.toml.md). Returns null when it is,
  /// otherwise why it is not. Throws OperationCanceledException when the token is cancelled.
  public async Task<string?> Check(string? proxy, CancellationToken cancellationToken = default) {
    Url url = ProbeUrl;
    if (!string.IsNullOrEmpty(proxy)) {
      if (!Uri.TryCreate(proxy, UriKind.Absolute, out Uri? uri)
          || uri.Scheme is not ("http" or "https")) {
        return $"'{proxy}' is not an http:// or https:// URL.";
      }

      url = GithubProxy.Rewrite(Url.Parse(proxy), url) ?? url;
    }

    using CancellationTokenSource timeout =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(_timeout);
    long started = Environment.TickCount64;
    string through = string.IsNullOrEmpty(proxy)
        ? ""
        : " through " + Services.ConfigService.DescribeProxy(proxy);
    try {
      int status = await _send(url, timeout.Token);
      RunTrace.Write("github", $"GitHub check{through}: status {status} after"
          + $" {Environment.TickCount64 - started} ms");
      return status is >= 200 and < 300
          ? null
          : $"GitHub answered with the HTTP status {status}.";
    }
    catch (Exception ex) when (ex is OperationCanceledException or FlurlHttpException
        or HttpRequestException or IOException) {
      cancellationToken.ThrowIfCancellationRequested();
      RunTrace.Write("github", $"GitHub check{through} failed after"
          + $" {Environment.TickCount64 - started} ms: {ex}");
      return timeout.IsCancellationRequested
          ? $"No answer from GitHub within {_timeout.TotalSeconds:0.#} seconds."
          : $"Could not connect to GitHub: {(ex.InnerException ?? ex).Message}";
    }
  }

  private static async Task<int> Send(Url url, CancellationToken cancellationToken) {
    using IFlurlResponse response = await url
        .WithHeader("User-Agent", "lip")
        .AllowAnyHttpStatus()
        .GetAsync(HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    return response.StatusCode;
  }
}
