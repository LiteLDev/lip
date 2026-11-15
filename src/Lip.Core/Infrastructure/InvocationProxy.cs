using System.Net;
using System.Net.Http;

namespace Lip.Core.Infrastructure;

/// The proxy of the current invocation (Invocation): the one that its environment variables
/// http_proxy, https_proxy, all_proxy and no_proxy (or their uppercase names) name, as .NET reads
/// them for a process; without them, the proxy of the system. The daemon carries out the
/// commands of clients with different environment variables in one process
/// (protocol/lip-daemon.md, "Carrying out a command"), whose own proxy the first of them would
/// otherwise fix.
public sealed class InvocationProxy : IWebProxy {
  public static readonly InvocationProxy Instance = new();

  private InvocationProxy() {
    Credentials = new ProxyCredentials();
  }

  public ICredentials? Credentials { get; set; }

  public Uri? GetProxy(Uri destination) {
    Settings settings = Read(Invocation.Current);
    if (!settings.FromEnvironment) {
      return HttpClient.DefaultProxy.GetProxy(destination);
    }

    if (settings.IsBypassed(destination)) {
      return null;
    }

    return destination.Scheme == Uri.UriSchemeHttps
        ? settings.Https ?? settings.All
        : settings.Http ?? settings.All;
  }

  public bool IsBypassed(Uri host) {
    Settings settings = Read(Invocation.Current);
    return settings.FromEnvironment
        ? GetProxy(host) is null
        : HttpClient.DefaultProxy.IsBypassed(host);
  }

  private static Settings Read(Invocation invocation) {
    string? Variable(string name) =>
        invocation.GetEnvironmentVariable(name) is { Length: > 0 } lower
            ? lower
            : invocation.GetEnvironmentVariable(name.ToUpperInvariant()) is { Length: > 0 } upper
                ? upper
                : null;

    // As .NET does, HTTP_PROXY is ignored in a CGI program, whose request may set it.
    string? http = invocation.GetEnvironmentVariable("http_proxy") is { Length: > 0 } value
        ? value
        : invocation.GetEnvironmentVariable("GATEWAY_INTERFACE") is null
            ? invocation.GetEnvironmentVariable("HTTP_PROXY")
            : null;
    Uri? httpProxy = Parse(http);
    Uri? httpsProxy = Parse(Variable("https_proxy"));
    Uri? allProxy = Parse(Variable("all_proxy"));
    string[] bypass = (Variable("no_proxy") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return new Settings(
        httpProxy is not null || httpsProxy is not null || allProxy is not null,
        httpProxy, httpsProxy, allProxy, bypass);
  }

  /// A proxy URL as .NET reads it from the environment: without a scheme, an HTTP proxy.
  private static Uri? Parse(string? value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return null;
    }

    string url = value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value;
    return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            || uri.Scheme.StartsWith("socks", StringComparison.Ordinal))
        ? uri
        : null;
  }

  private sealed record Settings(
      bool FromEnvironment, Uri? Http, Uri? Https, Uri? All, string[] Bypass) {
    public bool IsBypassed(Uri destination) {
      string host = destination.IdnHost;
      foreach (string entry in Bypass) {
        if (entry == "*") {
          return true;
        }

        string domain = entry.TrimStart('*');
        if (domain.StartsWith('.')
            ? host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, domain[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }

      return false;
    }
  }

  /// The user name and password in the URL of the proxy, as .NET takes them from it.
  private sealed class ProxyCredentials : ICredentials {
    public NetworkCredential? GetCredential(Uri uri, string authType) {
      Settings settings = Read(Invocation.Current);
      if (!settings.FromEnvironment) {
        return HttpClient.DefaultProxy.Credentials?.GetCredential(uri, authType);
      }

      foreach (Uri? candidate in (Uri?[])[settings.Http, settings.Https, settings.All]) {
        if (candidate is { UserInfo.Length: > 0 }
            && string.Equals(candidate.Host, uri.Host, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == uri.Port) {
          string[] parts = candidate.UserInfo.Split(':', 2);
          return new NetworkCredential(
              Uri.UnescapeDataString(parts[0]),
              parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
      }

      return null;
    }
  }
}
