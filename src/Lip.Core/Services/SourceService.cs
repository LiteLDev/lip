using System.IO.Abstractions;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.PackageRegistries;
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
    IGitRemoteClient gitRemoteClient,
    GitRemotePackageRegistry gitRemotePackageRegistry,
    IUserInteraction userInteraction,
    ICacheService cacheService,
    Url? githubProxy) : ISourceService {
  private readonly IFileDownloader _fileDownloader = fileDownloader;
  private readonly IGitRemoteClient _gitRemoteClient = gitRemoteClient;
  private readonly GitRemotePackageRegistry _gitRemotePackageRegistry = gitRemotePackageRegistry;
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

  /// Returns a lazy source over the tree of the commit the package version is tagged at. Only
  /// the tree is listed here; file contents are fetched on demand (see GitTreeSource).
  private async Task<ISource> GetPackageViaGit(PackageSpec packageSpec) {
    Url repoUrl = GitRemotePackageRegistry.GetRepoUrl(packageSpec.Id);
    string commit = await _gitRemotePackageRegistry.ResolveCommit(packageSpec);

    await _userInteraction.PrintInfo(
        $"Listing files of {packageSpec.Id.Path}@{packageSpec.Version} (commit {commit[..Math.Min(12, commit.Length)]})...");

    return await GitTreeSource.Create(_gitRemoteClient, _cacheService, repoUrl, commit);
  }
}
