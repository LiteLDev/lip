using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Runtime.ExceptionServices;
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
/// URL is downloaded once until Forget, and read into memory when opened. Local ZIP files are
/// read into memory, not cached. Local
/// directories are read where they are. git
/// archives are lazy GitTreeSources: only the tree is listed, and only the blobs read are fetched,
/// on every read, never through the cache. A ref
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
          $"Internal error: {target} is a remote repository, which lip opens by version or"
          + " commit, not as a URL or local path."),
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

    string what = gitRef.Kind switch {
      GitRefKind.Version => $"version {gitRef.Name} (tag v{gitRef.Name})",
      GitRefKind.Branch => $"branch '{gitRef.Name}'",
      _ => $"tag '{gitRef.Name}'",
    };

    IReadOnlyList<GitRemoteRef> refs;
    try {
      refs = await _gitRemoteClient.ListRefs(repoUrl, [refName]);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"Could not look up the {what} of {spec} ({repoUrl}).", ex);
    }

    // ref-prefix also matches longer names (e.g. "main" matches "main-old").
    GitRemoteRef found = refs.FirstOrDefault(r => r.Name == refName)
        ?? throw new ArchiveUnavailableException(
            $"{spec} has no {what}. Check the spelling, and that it exists in the repository"
            + $" {repoUrl}.");
    string commit = found.PeeledObjectId ?? found.ObjectId;
    RunTrace.Write("git", $"{spec}: {refName} is the commit {commit}");
    return commit;
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
            $"{spec} has no version tag {version.Tag} (any more). Check the version.");

    return await OpenGit($"{spec}@{version.Version}", spec, tagged.Commit.ToLowerInvariant());
  }

  public async Task Forget(string url) {
    IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(url));
    if (file is null) {
      RunTrace.Write("cache", $"{url} is not cached: nothing to forget");
      return;
    }

    RunTrace.Write("cache", $"Forgetting the cached download of {url}: deleting {file.FullName}");
    DeleteCached(file, url);
  }

  public async Task<ISource> OpenAsset(IReadOnlyList<string> urls) {
    if (urls.Count == 0) {
      throw new ArchiveUnavailableException(
          "The asset lists no URLs to download it from, so the package is broken. Report this to"
          + " the package author.");
    }

    List<Exception> failures = [];
    foreach (string url in urls) {
      try {
        if (!url.StartsWith("https://", StringComparison.Ordinal)) {
          throw new ArchiveUnavailableException(
              $"The asset URL {url} is not an https URL: lip downloads assets only over https.");
        }

        RunTrace.Write("archive", $"Opening the asset {url}");
        return await DownloadZip(url);
      }
      catch (ArchiveUnavailableException ex) {
        RunTrace.Write("archive", $"Cannot use the asset URL {url}: {ex.Message}");
        failures.Add(ex);
      }
    }

    if (failures.Count == 1) {
      ExceptionDispatchInfo.Throw(failures[0]);
    }

    throw new ArchiveUnavailableException(
        $"Could not download the asset from any of its {urls.Count} URLs"
        + $" ({string.Join(", ", urls)}).",
        new AggregateException("Every URL of the asset failed.", failures));
  }
  /// The git repository URL of a remote repository (protocol/package.md §2.1), without any
  /// GitHub proxy applied.
  public static string GetRepoUrl(string spec) => $"https://{spec}.git";

  private async Task<ArchiveHandle> OpenUrl(string url) {
    RunTrace.Write("archive", $"Opening the ZIP archive {url}");
    return new ArchiveHandle(url, await DownloadZip(url));
  }

  /// The ZIP archive of a URL, from its cached download keyed by the URL without its fragment
  /// (protocol/userspace.md §3). A cached copy that is not a valid ZIP archive (e.g. corrupted)
  /// is downloaded again.
  ///
  /// The download is read into memory once and validated there, so that what is installed is
  /// exactly what was validated, even if another process deletes or replaces the cache entry
  /// meanwhile.
  private async Task<ZipSource> DownloadZip(string url) {
    url = url.Split('#', 2)[0];
    IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(url));
    if (file is not null) {
      try {
        byte[] content = _fileSystem.File.ReadAllBytes(file.FullName);
        ValidateZip(content, $"The cached download of {url} ({file.FullName})");
        RunTrace.Write("cache", $"Using the cached download of {url}: {file.FullName}");
        return CachedZipSource(url, file, content);
      }
      catch (ArchiveUnavailableException ex) {
        RunTrace.Write("cache", $"The cached download of {url} is damaged, downloading it again:"
            + $" {ex.Message}");
        DeleteCached(file, url);
      }
      catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
        RunTrace.Write("cache", $"The cached download of {url} was deleted meanwhile,"
            + " downloading it again");
      }
      catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
        RunTrace.Write("cache", $"Cannot read the cached download of {url} ({file.FullName}):"
            + $" {ex.GetType().Name}: {ex.Message}");
        throw new ArchiveUnavailableException(
            $"Could not read the cached download of {url} ({file.FullName}). If a program is using"
            + " the file, close it; otherwise delete the file or run 'lip cache clean', then try"
            + " again.", ex);
      }
    } else {
      RunTrace.Write("cache", $"{url} is not cached yet");
    }

    // Download validates what it downloads; the content read back is validated again, since
    // another process may have replaced the file meanwhile.
    IFileInfo downloaded = await Download(url);
    byte[] downloadedContent;
    try {
      downloadedContent = _fileSystem.File.ReadAllBytes(downloaded.FullName);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"Could not read the download of {url} ({downloaded.FullName}). If another lip command"
          + " is cleaning the cache, wait for it to finish; otherwise close programs that may use"
          + " the file, or run 'lip cache clean', and try again.", ex);
    }

    ValidateZip(downloadedContent, $"The download of {url} ({downloaded.FullName})");
    return CachedZipSource(url, downloaded, downloadedContent);
  }

  private static ZipSource CachedZipSource(string url, IFileInfo file, byte[] content) =>
      new(content, $"the download of {url} ({file.FullName})", cachedDownload: true);

  private static void ValidateZip(byte[] content, string subject) {
    using MemoryStream stream = new(content, writable: false);
    ZipValidator.Validate(stream, subject);
  }

  /// Deletes the cached download of a URL. Throws IOException or UnauthorizedAccessException
  /// naming the file when it cannot be deleted.
  private static void DeleteCached(IFileInfo file, string url) {
    try {
      file.Delete();
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not delete the cached download of {url} ({file.FullName}). Stop other lip"
          + " processes and programs that may use it, or delete the file yourself, then try"
          + " again.", ex);
    }
  }

  /// Opens a local directory.
  private ArchiveHandle OpenDirectory(string key, string path) {
    string full = _fileSystem.Path.TrimEndingDirectorySeparator(_fileSystem.Path.GetFullPath(path));
    RunTrace.Write("archive", $"Opening the local directory {full}");
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
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
      throw new ArchiveUnavailableException(
          $"There is no file or directory {path}. Check the path; a relative path is relative"
          + " to the current directory.", ex);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException($"Could not read the local file {path}.", ex);
    }

    RunTrace.Write("archive", $"Opening the local ZIP file {path} ({content.Length} bytes)");
    using (MemoryStream stream = new(content, writable: false)) {
      ZipValidator.Validate(stream, $"The local file {path}");
    }

    return new ArchiveHandle(key, new ZipSource(content, $"the local file {path}"));
  }

  private async Task<ArchiveHandle> OpenGit(string key, string spec, string commit) {
    string repoUrl = GetRepoUrl(spec);
    RunTrace.Write("archive", $"Opening the git archive {key}: commit {commit} of {repoUrl}");
    try {
      // Lists the tree up front, so a missing commit fails here rather than on first access.
      // Only regular files are exposed: symlinks and gitlinks are not part of git archives.
      GitTreeSource source = await GitTreeSource.Create(_gitRemoteClient, repoUrl, commit);
      return new ArchiveHandle(key, source, commit);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"Could not get the files of {key} (the commit {commit} of {repoUrl}).", ex);
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
          $"Could not list the versions of {spec} (the tags of {repoUrl}).", ex);
    }

    List<TaggedVersion> versions = [];
    foreach (GitRemoteRef gitRef in refs) {
      if (ParseVersionTag(gitRef.Name) is SemVersion version) {
        string tag = gitRef.Name[_tagsRefPrefix.Length..];
        versions.Add(new(new SpecVersion(version, tag), gitRef.PeeledObjectId ?? gitRef.ObjectId));
      }
    }

    RunTrace.Write("git", $"{spec}: {refs.Count} refs below {_versionTagRefPrefix},"
        + $" {versions.Count} of them version tags");
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
  /// is configured, unless it is cached already. The download goes to a .tmp file in the
  /// cache directory and is renamed into place once complete. A candidate whose response is not a valid
  /// ZIP archive (e.g. a proxy error page) is skipped.
  private async Task<IFileInfo> Download(string url) {
    try {
      return await _cacheService.GetOrCreateFile(CacheKeys.Http(url), async destination => {
        List<Url> downloadUrls = [.. GetDownloadUrls(url)];
        List<Exception> exceptions = [];

        for (int index = 0; index < downloadUrls.Count; index++) {
          Url downloadUrl = downloadUrls[index];
          // GetDownloadUrls puts the proxied URL, if any, first.
          bool viaProxy = downloadUrls.Count > 1 && index == 0;
          try {
            // Leftovers of a failed candidate must not be mistaken for this one's content.
            destination.Refresh();
            if (destination.Exists) {
              destination.Delete();
            }

            RunTrace.Write("archive", viaProxy
                ? $"Downloading {url} through the GitHub proxy: {downloadUrl}"
                : $"Downloading {url}");
            await _fileDownloader.DownloadFile(downloadUrl, destination);
            destination.Refresh();
            ZipValidator.Validate(destination, $"The file downloaded from {downloadUrl}");
            return;
          }
          catch (Exception ex) when (IsUnavailable(ex) || ex is ArchiveUnavailableException) {
            RunTrace.Write("archive", $"Downloading from {downloadUrl} failed:"
                + $" {ex.GetType().Name}: {ex.Message}");
            // An invalid github_proxy value is accepted by the configuration and fails only here.
            exceptions.Add(viaProxy && HasUriFormatCause(ex)
                ? new ArchiveUnavailableException(
                    $"The GitHub proxy set in github_proxy does not give a valid URL"
                    + $" ({downloadUrl}). Set a valid URL with 'lip config set github_proxy <URL>',"
                    + " or remove it with 'lip config delete github_proxy'.",
                    ex)
                : ex);
          }
        }

        throw exceptions.Count == 1
            ? new ArchiveUnavailableException($"Could not download {url}.", exceptions[0])
            : new ArchiveUnavailableException(
                $"Could not download {url}, neither through the GitHub proxy"
                + $" ({string.Join(", ", downloadUrls.SkipLast(1))}) nor directly.",
                new AggregateException("Every download address failed.", exceptions));
      });
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"Could not store the download of {url} in the lip cache ({LipDirectories.Cache}).", ex);
    }
  }

  private IEnumerable<Url> GetDownloadUrls(string url) {
    Url original = Url.Parse(url);
    return GithubProxy.Rewrite(_githubProxy, original) is Url proxied ? [proxied, original] : [original];
  }

  private static bool HasUriFormatCause(Exception ex) {
    for (Exception? current = ex; current is not null; current = current.InnerException) {
      if (current is UriFormatException) {
        return true;
      }
    }
    return false;
  }

  // Everything but cancellation and programming errors makes an archive unavailable.
  private static bool IsUnavailable(Exception ex) =>
      ex is not (OperationCanceledException or ArchiveUnavailableException
          or ArgumentNullException or InvalidCastException or NullReferenceException);

  private record TaggedVersion(SpecVersion Version, string Commit);
}
