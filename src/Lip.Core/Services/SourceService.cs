using System.IO.Abstractions;
using Flurl;
using Lip.Core.Infrastructure;
using Lip.Core.Sources;

namespace Lip.Core.Services;

/// Downloads the URL assets of tooth.json packages (spec §9) into the cache.
public interface ISourceService {
  Task<ISource> Get(Url url, bool isArchive);
}

public class SourceService(
    IFileDownloader fileDownloader,
    ICacheService cacheService,
    Url? githubProxy) : ISourceService {
  private readonly IFileDownloader _fileDownloader = fileDownloader;
  private readonly ICacheService _cacheService = cacheService;
  private readonly Url? _githubProxy = githubProxy;

  public async Task<ISource> Get(Url url, bool isArchive) {
    IEnumerable<Url> downloadUrls = GetDownloadUrls(url);

    IFileInfo archiveFile = await _cacheService.GetOrCreateFile(url, async cacheFile => {
      List<Exception> exceptions = [];

      foreach (Url downloadUrl in downloadUrls) {
        try {
          await _fileDownloader.DownloadFile(downloadUrl, cacheFile);
          return;
        }
        catch (Exception ex) {
          exceptions.Add(new Exception($"Failed to download from {downloadUrl}", ex));
        }
      }

      throw new AggregateException($"Failed to download file source from all URLs for {url}", exceptions);
    });

    return isArchive
        ? new ArchiveSource(archiveFile)
        : new SingleFileSource(archiveFile);
  }

  private IEnumerable<Url> GetDownloadUrls(Url url) {
    if (_githubProxy is null || url.Host != "github.com") {
      return [url];
    }

    Url proxyUrl = _githubProxy
        .Clone()
        .AppendPathSegments(url.PathSegments);

    if (!string.IsNullOrEmpty(url.Query)) {
      proxyUrl.Query = url.Query;
    }

    if (!string.IsNullOrEmpty(url.Fragment)) {
      proxyUrl.Fragment = url.Fragment;
    }

    if (proxyUrl.ToString() == url.ToString()) {
      return [url];
    }

    return [proxyUrl, url];
  }
}
