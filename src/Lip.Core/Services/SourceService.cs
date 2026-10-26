using System.IO.Abstractions;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Sources;

namespace Lip.Core.Services;

public interface ISourceService {
  Task<ISource> Get(LocalPackageSpec localPackageSpec);
  Task<ISource> Get(PackageSpec packageSpec);
  Task<ISource> Get(RemotePackageSpec remotePackageSpec);
  Task<ISource> Get(Url url, bool isArchive);
}

public class SourceService(
    IFileDownloader fileDownloader,
    IGitRunner gitRunner,
    IUserInteraction userInteraction,
    ICacheService cacheService,
    Url? githubProxy) : ISourceService {
  private readonly IFileDownloader _fileDownloader = fileDownloader;
  private readonly IGitRunner _gitRunner = gitRunner;
  private readonly IUserInteraction _userInteraction = userInteraction;

  private readonly ICacheService _cacheService = cacheService;

  private readonly Url? _githubProxy = githubProxy;

  public async Task<ISource> Get(LocalPackageSpec localPackageSpec) {
    if (!localPackageSpec.ArchiveFile.Exists) {
      throw new FileNotFoundException($"The specified local package file does not exist: {localPackageSpec.ArchiveFile.FullName}");
    }

    return new ArchiveSource(localPackageSpec.ArchiveFile);
  }

  public async Task<ISource> Get(PackageSpec packageSpec) {
    return await GetPackageViaGit(packageSpec);
  }

  public async Task<ISource> Get(RemotePackageSpec remotePackageSpec) {
    return await Get(remotePackageSpec.ArchiveUrl, isArchive: true);
  }

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

  private async Task<ISource> GetPackageViaGit(PackageSpec packageSpec) {
    Url repoUrl = Url.Parse($"https://{packageSpec.Id.Path}.git");

    if (_githubProxy is not null && repoUrl.Host == "github.com") {
      repoUrl = _githubProxy
          .Clone()
          .AppendPathSegments(repoUrl.PathSegments);
    }

    string @ref = $"v{packageSpec.Version}";

    Url keyUrl = repoUrl.Clone();
    keyUrl.Scheme = $"git+{keyUrl.Scheme}";
    keyUrl.Fragment = @ref;

    IDirectoryInfo repoDir = await _cacheService.GetOrCreateDirectory(keyUrl, async cacheDir => {
      await _userInteraction.PrintInfo($"Cloning git repository from '{repoUrl}'...");

      await _gitRunner.Clone(repoUrl, cacheDir.FullName, branch: @ref, depth: 1);
    });

    return new DirectorySource(repoDir);
  }
}
