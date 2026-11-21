using System.IO.Abstractions;
using System.Net;
using Flurl;
using Flurl.Http;
using Lip.Core.Git;

namespace Lip.Core.Infrastructure;

/// Downloads the body of a successful response to an HTTPS GET of a URL, following redirects
/// (protocol/lip-package.md, "URLs", protocol/lip-userspace.md, "Cache"). A request to GitHub is
/// sent through github_proxy first when one is set, then directly when that fails
/// (protocol/config.toml.md, "User configuration"), so that every download of lip honours it.
///
/// validate, when given, checks what an address gave, named by the URL it was downloaded from,
/// and throws when it is not what was asked for (e.g. a proxy's error page, or a file whose
/// SHA-256 differs): the next address is then tried, as when the request fails.
public interface IFileDownloader {
  /// Downloads url to the file destination, which each address writes over.
  Task DownloadFile(Url url, IFileInfo destination, Action<Url>? validate = null);

  /// Downloads url into memory. direct: the request is sent to url only, never through
  /// github_proxy.
  Task<byte[]> Download(Url url, Action<byte[], Url>? validate = null, bool direct = false);
}

/// Failures of the network or of the server are HttpRequestExceptions naming the URL, with the
/// HTTP status code when the server answered; failures of writing the file are IOExceptions or
/// UnauthorizedAccessExceptions naming the file. With one address, its failure is thrown as it
/// is; with the proxy and GitHub, an HttpRequestException naming both, with both failures.
///
/// githubProxy: the value of github_proxy as a URL; null when it is not set or empty.
public class FileDownloader(IUserInteraction userInteraction, Url? githubProxy = null)
    : IFileDownloader {
  private readonly IUserInteraction _userInteraction = userInteraction;
  private readonly Url? _githubProxy = githubProxy;

  public async Task DownloadFile(Url url, IFileInfo destination, Action<Url>? validate = null) {
    await FromFirstAddress(url, direct: false, async address => {
      RunTrace.Write("download", $"Downloading {address} to {destination.FullName}");
      await Download(
          address,
          () => {
            try {
              return destination.Create();
            }
            catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
              throw FileSystemExtensions.WrapFileError(
                  $"Could not create the file {destination.FullName} to save the download of"
                  + $" {address} in.", ex);
            }
          },
          disposeDestination: true,
          destination.FullName);
      destination.Refresh();
      validate?.Invoke(address);
      return true;
    });
  }

  public async Task<byte[]> Download(
      Url url, Action<byte[], Url>? validate = null, bool direct = false) {
    return await FromFirstAddress(url, direct, async address => {
      RunTrace.Write("download", $"Downloading {address} into memory");
      using MemoryStream stream = new();
      await Download(address, () => stream, disposeDestination: false, "memory");
      byte[] content = stream.ToArray();
      validate?.Invoke(content, address);
      return content;
    });
  }

  /// The result of attempt for the first address of url for which it succeeds: through the
  /// GitHub proxy, when it applies and direct is false, then url itself.
  private async Task<T> FromFirstAddress<T>(Url url, bool direct, Func<Url, Task<T>> attempt) {
    Url? proxied = direct ? null : GithubProxy.Rewrite(_githubProxy, url);
    if (proxied is null) {
      return await attempt(url);
    }

    Exception proxyFailure;
    try {
      RunTrace.Write("download", $"Downloading {url} through the GitHub proxy: {proxied}");
      return await attempt(proxied);
    }
    catch (Exception ex) when (IsFailureOfAddress(ex)) {
      RunTrace.Write("download", $"Downloading from {proxied} failed, trying {url}:"
          + $" {ex.GetType().Name}: {ex.Message}");
      // A github_proxy that is a URI but not a usable URL prefix fails only here.
      proxyFailure = HasUriFormatCause(ex)
          ? new HttpRequestException(
              $"The GitHub proxy set in github_proxy does not give a valid URL ({proxied}). Set"
              + " a valid URL with 'lip config set github_proxy <URL>', or remove it with"
              + " 'lip config delete github_proxy'.", ex)
          : ex;
    }

    try {
      return await attempt(url);
    }
    catch (Exception ex) when (IsFailureOfAddress(ex)) {
      throw new HttpRequestException(
          $"Could not download {url}, neither through the GitHub proxy ({proxied}) nor"
          + " directly.",
          new AggregateException("Every download address failed.", proxyFailure, ex));
    }
  }

  /// Whether ex is a failure of one address, after which the next one is tried: anything but
  /// cancellation and bugs.
  private static bool IsFailureOfAddress(Exception ex) =>
      ex is not (OperationCanceledException or ArgumentNullException or InvalidCastException
          or NullReferenceException);

  private static bool HasUriFormatCause(Exception ex) {
    for (Exception? current = ex; current is not null; current = current.InnerException) {
      if (current is UriFormatException) {
        return true;
      }
    }
    return false;
  }

  /// Downloads the body of url to the stream that open gives once the response has arrived,
  /// naming it destinationName in messages.
  private async Task Download(
      Url url, Func<Stream> open, bool disposeDestination, string destinationName) {
    IFlurlResponse response;
    try {
      response = await url
          .AllowAnyHttpStatus()
          .GetAsync(HttpCompletionOption.ResponseHeadersRead);
    }
    catch (FlurlHttpTimeoutException ex) {
      throw new HttpRequestException(
          $"Could not download {url}: the server did not answer in time.", ex);
    }
    catch (FlurlHttpException ex) {
      throw new HttpRequestException(
          $"Could not download {url}: the connection to the server failed.", ex);
    }

    using (response) {
      if (response.StatusCode is < 200 or >= 300) {
        throw new HttpRequestException(
            DescribeStatus(url, response), null, (HttpStatusCode)response.StatusCode);
      }

      long? length = response.ResponseMessage.Content.Headers.ContentLength;
      long total = 0;
      await _userInteraction.RunWithProgress($"Downloading {url}", async progress => {
        await using Stream body = await response.GetStreamAsync();
        Stream file = open();
        try {
          byte[] buffer = new byte[81920];
          while (true) {
            int read;
            try {
              read = await body.ReadAsync(buffer);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException) {
              throw new HttpRequestException(
                  $"The download of {url} broke off after {total} bytes"
                  + (length > 0 ? $" of {length}" : "")
                  + ". Check your network connection and try again.", ex);
            }

            if (read == 0) {
              break;
            }

            try {
              await file.WriteAsync(buffer.AsMemory(0, read));
            }
            catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
              throw FileSystemExtensions.WrapFileError(
                  $"Could not write the download of {url} to {destinationName}.", ex);
            }

            total += read;
            if (length > 0) {
              progress.Report(100.0 * total / length.Value);
            }
          }
        }
        finally {
          if (disposeDestination) {
            await file.DisposeAsync();
          }
        }
      });

      RunTrace.Write("download", $"Downloaded {total} bytes from {url}");
    }
  }

  /// The message for a response of a status other than 2xx: the URL, the status, and what the
  /// status means for the user.
  private static string DescribeStatus(Url url, IFlurlResponse response) {
    int status = response.StatusCode;
    string reason = response.ResponseMessage.ReasonPhrase is { Length: > 0 } phrase
        ? $" ({phrase})"
        : "";
    string message = $"Could not download {url}: the server answered HTTP {status}{reason}.";
    return status switch {
      // What to check is said by the hint the CLI shows for these status codes.
      401 or 404 when IsGitHub(url) && url.Path.Contains(
          "/releases/download/", StringComparison.Ordinal) => message + " GitHub answers this way"
          + " when the release has no such file, or when the repository is private.",
      401 or 404 when IsGitHub(url) => message + " GitHub answers this way when the repository"
          + " or the file does not exist, or when the repository is private.",
      401 => message + " The server does not allow lip to download this file.",
      403 => message,
      404 or 410 => message + " The file does not exist there. Check the URL, or the package"
          + " name and version that lead to it.",
      429 => message + " Too many requests were sent. Wait a while and try again.",
      >= 500 => message + " The server had a problem. Try again later.",
      _ => message,
    };
  }

  private static bool IsGitHub(Url url) =>
      GithubProxy.IsGithub(url)
      || url.Path.Contains("github.com/", StringComparison.OrdinalIgnoreCase);
}
