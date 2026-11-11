using System.IO.Abstractions;
using System.Net;
using Flurl;
using Flurl.Http;

namespace Lip.Core.Infrastructure;

public interface IFileDownloader {
  Task DownloadFile(Url url, IFileInfo destination);
}

/// Writes the body of a successful response to one HTTPS GET of a URL, following redirects, to a
/// file (protocol/lip-package.md, "URLs", protocol/lip-userspace.md, "Cache").
///
/// Failures of the network or of the server are HttpRequestExceptions naming the URL, with the
/// HTTP status code when the server answered; failures of writing the file are IOExceptions or
/// UnauthorizedAccessExceptions naming the file.
public class FileDownloader(IUserInteraction userInteraction) : IFileDownloader {
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task DownloadFile(Url url, IFileInfo destination) {
    RunTrace.Write("download", $"Downloading {url} to {destination.FullName}");

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
        Stream file;
        try {
          file = destination.Create();
        }
        catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
          throw FileSystemExtensions.WrapFileError(
              $"Could not create the file {destination.FullName} to save the download of {url}"
              + " in.", ex);
        }

        await using (file) {
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
                  $"Could not write the download of {url} to {destination.FullName}.", ex);
            }

            total += read;
            if (length > 0) {
              progress.Report(100.0 * total / length.Value);
            }
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

  private static bool IsGitHub(Url url) {
    string host = url.Host.ToLowerInvariant();
    return host is "github.com" or "codeload.github.com"
        || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal)
        || url.Path.Contains("github.com/", StringComparison.OrdinalIgnoreCase);
  }
}
