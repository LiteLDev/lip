using System.IO.Abstractions;
using Flurl;
using Flurl.Http;

namespace Lip.Core.Infrastructure;

public interface IFileDownloader {
  Task DownloadFile(Url url, IFileInfo destination);
}

/// Writes the body of a successful response to one HTTPS GET of a URL, following redirects, to a
/// file (protocol/package.md §2.2, protocol/userspace.md §3).
public class FileDownloader(IUserInteraction userInteraction) : IFileDownloader {
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task DownloadFile(Url url, IFileInfo destination) {
    using IFlurlResponse response = await url
        .AllowAnyHttpStatus()
        .GetAsync(HttpCompletionOption.ResponseHeadersRead);

    if (response.StatusCode is < 200 or >= 300) {
      throw new HttpRequestException($"Failed to download {url}: HTTP {response.StatusCode}");
    }

    long? length = response.ResponseMessage.Content.Headers.ContentLength;
    await _userInteraction.RunWithProgress($"Downloading {url}", async progress => {
      await using Stream body = await response.GetStreamAsync();
      await using Stream file = destination.Create();
      byte[] buffer = new byte[81920];
      long total = 0;
      int read;
      while ((read = await body.ReadAsync(buffer)) > 0) {
        await file.WriteAsync(buffer.AsMemory(0, read));
        total += read;
        if (length > 0) {
          progress.Report(100.0 * total / length.Value);
        }
      }
    });
  }
}
