using System.Collections.Concurrent;
using System.IO.Abstractions;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// Obtains archives of sources and assets (protocol/package.md §2, §4 step 1; protocol/workspace.md
/// §4 step 2, §5).
///
/// Downloaded ZIP archives and assets are cached under the key of their URL (CacheKeys.Http), so a
/// URL is downloaded once until Forget. Local ZIP files are read into memory, not cached. Local
/// directories are read where they are. git
/// archives are lazy GitTreeSources: only the tree is listed, blobs are fetched on demand. A ref
/// (branch, tag) is resolved with ls-refs; a rev must be a full commit id, since an abbreviated
/// one cannot be resolved without fetching the history.
public class ArchiveProvider(
    IGitRemoteClient gitRemoteClient,
    ICacheService cacheService,
    IFileSystem fileSystem,
    IFileDownloader fileDownloader,
    Url? githubProxy = null) : IArchiveProvider {
  private const string _headsRefPrefix = "refs/heads/";
  private const string _tagsRefPrefix = "refs/tags/";
  private const string _versionTagRefPrefix = $"{_tagsRefPrefix}v";

  private readonly IGitRemoteClient _gitRemoteClient = gitRemoteClient;
  private readonly ICacheService _cacheService = cacheService;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IFileDownloader _fileDownloader = fileDownloader;
  private readonly Url? _githubProxy = githubProxy;

  private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<TaggedVersion>>>>
      _tagCache = new();

  public async Task<ArchiveHandle> Open(Ref target) {
    return target.Kind switch {
      RefKind.Url => await OpenUrl(target.Target),
      RefKind.File => _fileSystem.Directory.Exists(target.LocalPath)
          ? OpenDirectory(target.Target, target.LocalPath)
          : OpenFile(target.Target, target.LocalPath),
      _ => throw new ArgumentException(
          $"Remote repositories are opened with OpenVersion or OpenCommit, not Open: {target}",
          nameof(target)),
    };
  }

  public async Task<string> ResolveGitRef(string spec, GitRef gitRef) {
    if (gitRef.Kind == GitRefKind.Rev) {
      return gitRef.Name;
    }

    string repoUrl = GetRepoUrl(spec);
    string refName = gitRef.Kind switch {
      GitRefKind.Version => $"{_versionTagRefPrefix}{gitRef.Name}",
      GitRefKind.Branch => _headsRefPrefix + gitRef.Name,
      _ => _tagsRefPrefix + gitRef.Name,
    };

    IReadOnlyList<GitRemoteRef> refs;
    try {
      refs = await _gitRemoteClient.ListRefs(repoUrl, [refName]);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"Failed to list the refs of {repoUrl}: {ex.Message}", ex);
    }

    // ref-prefix also matches longer names (e.g. "main" matches "main-old").
    GitRemoteRef found = refs.FirstOrDefault(r => r.Name == refName)
        ?? throw new ArchiveUnavailableException(
            $"{repoUrl} has no {gitRef.Kind.ToString().ToLowerInvariant()} '{gitRef.Name}'.");
    return found.PeeledObjectId ?? found.ObjectId;
  }

  public Task<ArchiveHandle> OpenCommit(string spec, string commit) {
    string normalized = GitRef.Rev(commit).Name;
    return OpenGit($"{spec}@rev:{normalized}", spec, normalized);
  }

  public async Task<IReadOnlyList<SpecVersion>> ListVersions(string spec) {
    return [.. (await GetTaggedVersions(spec)).Select(v => v.Version)];
  }

  public async Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version) {
    TaggedVersion tagged = (await GetTaggedVersions(spec))
        .FirstOrDefault(v => v.Version.Tag == version.Tag)
        ?? throw new ArchiveUnavailableException(
            $"{GetRepoUrl(spec)} has no version tag {version.Tag}.");

    return await OpenGit($"{spec}@{version.Version}", spec, tagged.Commit.ToLowerInvariant());
  }

  public async Task Forget(string url) {
    IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(url));
    file?.Delete();
  }

  public async Task<ISource> OpenAsset(IReadOnlyList<string> urls) {
    List<string> errors = [];
    foreach (string url in urls) {
      try {
        if (!url.StartsWith("https://", StringComparison.Ordinal)) {
          throw new ArchiveUnavailableException($"{url} is not an https URL.");
        }

        return new ZipSource(await DownloadZip(url));
      }
      catch (ArchiveUnavailableException ex) {
        errors.Add(ex.Message);
      }
    }

    throw new ArchiveUnavailableException(urls.Count == 0
        ? "The asset has no URLs."
        : $"Failed to download the asset: {string.Join("; ", errors)}");
  }
  /// The git repository URL of a remote repository (protocol/package.md §2.1), without any
  /// GitHub proxy applied.
  public static string GetRepoUrl(string spec) => $"https://{spec}.git";

  private async Task<ArchiveHandle> OpenUrl(string url) {
    return new ArchiveHandle(url, new ZipSource(await DownloadZip(url)));
  }

  /// The cached download of the ZIP archive of a URL, keyed by the URL without its fragment
  /// (protocol/userspace.md §3). A cached copy that is not a valid ZIP archive (e.g. corrupted)
  /// is downloaded again.
  private async Task<IFileInfo> DownloadZip(string url) {
    url = url.Split('#', 2)[0];
    IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(url));
    if (file is not null) {
      try {
        ZipValidator.Validate(file, url);
        return file;
      }
      catch (ArchiveUnavailableException) {
        file.Delete();
      }
    }

    // Download validates what it downloads.
    return await Download(url);
  }

  /// Opens a local directory.
  private ArchiveHandle OpenDirectory(string key, string path) {
    string full = _fileSystem.Path.TrimEndingDirectorySeparator(_fileSystem.Path.GetFullPath(path));
    return new ArchiveHandle(key, new DirectorySource(_fileSystem, full));
  }

  /// Opens a local ZIP file.
  private ArchiveHandle OpenFile(string key, string path) {
    // Read once, so that the files installed are exactly the bytes validated and hashed, even if
    // the file changes meanwhile.
    byte[] content;
    try {
      content = _fileSystem.File.ReadAllBytes(path);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException($"{key}: cannot read {path}: {ex.Message}", ex);
    }

    using (MemoryStream stream = new(content, writable: false)) {
      ZipValidator.Validate(stream, key);
    }

    return new ArchiveHandle(key, new ZipSource(content));
  }

  private async Task<ArchiveHandle> OpenGit(string key, string spec, string commit) {
    string repoUrl = GetRepoUrl(spec);
    try {
      // Lists the tree up front, so a missing commit fails here rather than on first access.
      // Only regular files are exposed: symlinks and gitlinks are not part of git archives.
      GitTreeSource source = await GitTreeSource.Create(
          _gitRemoteClient, _cacheService, repoUrl, commit);
      return new ArchiveHandle(key, source, commit);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"{key}: failed to list commit {commit} of {repoUrl}: {ex.Message}", ex);
    }
  }

  private async Task<IReadOnlyList<TaggedVersion>> GetTaggedVersions(string spec) {
    Lazy<Task<IReadOnlyList<TaggedVersion>>> lazy = _tagCache.GetOrAdd(
        spec, _ => new(() => ListTaggedVersions(spec)));

    try {
      return await lazy.Value;
    }
    catch {
      // Do not keep failures (e.g. network errors) cached so that later calls can retry.
      _tagCache.TryRemove(new(spec, lazy));
      throw;
    }
  }

  private async Task<IReadOnlyList<TaggedVersion>> ListTaggedVersions(string spec) {
    string repoUrl = GetRepoUrl(spec);

    IReadOnlyList<GitRemoteRef> refs;
    try {
      refs = await _gitRemoteClient.ListRefs(repoUrl, [_versionTagRefPrefix]);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"Failed to list tags of {repoUrl}: {ex.Message}", ex);
    }

    List<TaggedVersion> versions = [];
    foreach (GitRemoteRef gitRef in refs) {
      if (ParseVersionTag(gitRef.Name) is SemVersion version) {
        string tag = gitRef.Name[_tagsRefPrefix.Length..];
        versions.Add(new(new SpecVersion(version, tag), gitRef.PeeledObjectId ?? gitRef.ObjectId));
      }
    }

    return [.. versions
        .OrderByDescending(v => v.Version.Version, SemVersion.PrecedenceComparer)
        .ThenByDescending(v => v.Version.Tag, StringComparer.Ordinal)];
  }

  /// Parses a tag ref name into a version if the tag is a version tag (protocol/package.md §2.1).
  public static SemVersion? ParseVersionTag(string refName) {
    if (!refName.StartsWith(_versionTagRefPrefix, StringComparison.Ordinal)) {
      return null;
    }

    return SemVersion.TryParse(
        refName[_versionTagRefPrefix.Length..], SemVersionStyles.Strict, out SemVersion? version)
        ? version
        : null;
  }

  /// Downloads a URL into the cache under CacheKeys.Http(url), via the GitHub proxy first if one
  /// is configured, unless it is cached already. The download goes to the cache's staging
  /// directory and is moved into place once complete. A candidate whose response is not a valid
  /// ZIP archive (e.g. a proxy error page) is skipped.
  private async Task<IFileInfo> Download(string url) {
    try {
      return await _cacheService.GetOrCreateFile(CacheKeys.Http(url), async destination => {
        List<Exception> exceptions = [];

        foreach (Url downloadUrl in GetDownloadUrls(url)) {
          try {
            // Leftovers of a failed candidate must not be mistaken for this one's content.
            destination.Refresh();
            if (destination.Exists) {
              destination.Delete();
            }

            await _fileDownloader.DownloadFile(downloadUrl, destination);
            destination.Refresh();
            ZipValidator.Validate(destination, url);
            return;
          }
          catch (Exception ex) when (IsUnavailable(ex) || ex is ArchiveUnavailableException) {
            exceptions.Add(new Exception($"Failed to download from {downloadUrl}", ex));
          }
        }

        throw new ArchiveUnavailableException(
            $"{url}: download failed: "
                + string.Join("; ", exceptions.Select(e => e.InnerException?.Message ?? e.Message)),
            new AggregateException(exceptions));
      });
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"{url}: cannot store the download in the cache: {ex.Message}", ex);
    }
  }

  private IEnumerable<Url> GetDownloadUrls(string url) {
    Url original = Url.Parse(url);
    return GithubProxy.Rewrite(_githubProxy, original) is Url proxied ? [proxied, original] : [original];
  }

  // Everything but cancellation and programming errors makes an archive unavailable.
  private static bool IsUnavailable(Exception ex) =>
      ex is not (OperationCanceledException or ArchiveUnavailableException
          or ArgumentNullException or InvalidCastException or NullReferenceException);

  private record TaggedVersion(SpecVersion Version, string Commit);
}
