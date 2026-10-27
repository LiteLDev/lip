using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// Obtains archives for ref targets (spec §2, §3, §5.2 step 1, §8).
///
/// ZIP archives (downloaded or nested in another archive) are stored in the cache under the
/// content key "sha256:{hex}", so the lock can find them again by their identity. A downloaded
/// URL additionally maps to its content hash under "url-sha256:{url}", so it is downloaded once.
/// git archives are lazy GitTreeSources: only the tree is listed, blobs are fetched on demand.
public class ArchiveProvider(
    IGitRemoteClient gitRemoteClient,
    ICacheService cacheService,
    IFileSystem fileSystem,
    IFileDownloader fileDownloader,
    Url? githubProxy = null) : IArchiveProvider {
  private const string _tagsRefPrefix = "refs/tags/";
  private const string _versionTagRefPrefix = $"{_tagsRefPrefix}v";

  private readonly IGitRemoteClient _gitRemoteClient = gitRemoteClient;
  private readonly ICacheService _cacheService = cacheService;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IFileDownloader _fileDownloader = fileDownloader;
  private readonly Url? _githubProxy = githubProxy;

  private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<TaggedVersion>>>>
      _tagCache = new();

  public ArchiveHandle OpenWorkspace(string workspaceRoot) {
    string root = Path.TrimEndingDirectorySeparator(_fileSystem.Path.GetFullPath(workspaceRoot));
    return OpenDirectory(root, root);
  }

  public async Task<ArchiveHandle> Open(Ref target, ArchiveHandle baseArchive) {
    return target.Kind switch {
      RefKind.Relative => target.IsDirectory
          ? await OpenSubdirectory(target, baseArchive)
          : await OpenNestedZip(target, baseArchive),
      RefKind.Url => await OpenUrl(target.Target),
      _ => throw new ArgumentException(
          $"Spec refs are opened with OpenVersion, not Open: {target}", nameof(target)),
    };
  }

  public async Task<IReadOnlyList<SpecVersion>> ListVersions(string spec) {
    return [.. (await GetTaggedVersions(spec)).Select(v => v.Version)];
  }

  public async Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version) {
    TaggedVersion tagged = (await GetTaggedVersions(spec))
        .FirstOrDefault(v => v.Version.Tag == version.Tag)
        ?? throw new ArchiveUnavailableException(
            $"{GetRepoUrl(spec)} has no version tag {version.Tag}.");

    return await OpenGit(
        $"{spec}@{version.Version}", new GitArchiveIdentity(GetRepoUrl(spec), tagged.Commit, ""));
  }

  public async Task<ArchiveHandle> OpenLocked(string key, ArchiveIdentity identity) {
    switch (identity) {
      case GitArchiveIdentity git:
        return await OpenGit(key, git);

      case DirectoryArchiveIdentity directory:
        return OpenDirectory(key, directory.Path);

      case ZipArchiveIdentity zip: {
          string sha256 = zip.Sha256.ToLowerInvariant();
          IFileInfo? file = await GetCachedContent(sha256);

          if (file is null) {
            if (zip.Url is null) {
              throw new ArchiveUnavailableException(
                  $"{key}: ZIP archive sha256:{sha256} is not in the cache and has no URL.");
            }

            (file, string actual) = await Download(zip.Url, key);
            if (actual != sha256) {
              throw new ArchiveUnavailableException(
                  $"{key}: {zip.Url} now has sha256:{actual}, "
                      + $"but the lock records sha256:{sha256}.");
            }
          }

          return OpenZipFile(key, file, zip);
        }

      default:
        throw new ArgumentException($"Unknown archive identity: {identity}", nameof(identity));
    }
  }

  /// Gets the git repository URL of a spec (spec §3), without any GitHub proxy applied.
  public static string GetRepoUrl(string spec) => $"https://{spec}.git";

  private async Task<ArchiveHandle> OpenSubdirectory(Ref target, ArchiveHandle baseArchive) {
    string key = GetRelativeKey(target, baseArchive);
    string relativePath = target.RelativePath;

    ArchiveHandle handle;
    switch (baseArchive.Identity) {
      case GitArchiveIdentity git:
        handle = await OpenGit(key, git with { Path = JoinPath(git.Path, relativePath) });
        break;

      case DirectoryArchiveIdentity directory:
        handle = OpenDirectory(
            key,
            _fileSystem.Path.Combine([directory.Path, .. relativePath.Split('/')]));
        break;

      case ZipArchiveIdentity zip:
        handle = new ArchiveHandle(
            key,
            new PrefixSource(baseArchive.Source, relativePath),
            zip with { Path = JoinPath(zip.Path ?? "", relativePath) });
        break;

      default:
        throw new ArgumentException(
            $"Unknown archive identity: {baseArchive.Identity}", nameof(baseArchive));
    }

    EnsureNotEmpty(handle.Source, key);
    return handle;
  }

  private async Task<ArchiveHandle> OpenNestedZip(Ref target, ArchiveHandle baseArchive) {
    string key = GetRelativeKey(target, baseArchive);
    string relativePath = target.RelativePath;

    IFileInfo file;
    string sha256;
    try {
      if (baseArchive.Identity is DirectoryArchiveIdentity directory) {
        // A directory archive may be a whole workspace: do not list it to find one file.
        string path = _fileSystem.Path.Combine([directory.Path, .. relativePath.Split('/')]);
        if (!_fileSystem.File.Exists(path)) {
          string reason = _fileSystem.Directory.Exists(path)
              ? "is a directory (directory refs end with '/')"
              : "does not exist";
          throw new ArchiveUnavailableException(
              $"{key}: '{relativePath}' {reason} in {baseArchive.Key}.");
        }
      } else {
        HashSet<string> keys = [.. baseArchive.Source.Keys];
        if (!keys.Contains(relativePath)) {
          string reason = keys.Any(
              k => k.StartsWith(relativePath + "/", StringComparison.Ordinal))
              ? "is a directory (directory refs end with '/')"
              : "does not exist";
          throw new ArchiveUnavailableException(
              $"{key}: '{relativePath}' {reason} in {baseArchive.Key}.");
        }
      }

      (file, sha256) = await StoreInCache(async destination => {
        using Stream content = await baseArchive.Source.OpenRead(relativePath);
        using Stream output = destination.Create();
        await content.CopyToAsync(output);
      });
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"{key}: failed to read '{relativePath}' from {baseArchive.Key}: {ex.Message}", ex);
    }

    return OpenZipFile(key, file, new ZipArchiveIdentity(sha256, null));
  }

  private async Task<ArchiveHandle> OpenUrl(string url) {
    // Ref targets never contain the fragment, but strip it anyway since it is part of no key.
    string key = url.Split('#', 2)[0];

    string? sha256 = await ReadUrlHash(key);
    IFileInfo? file = sha256 is null ? null : await GetCachedContent(sha256);

    if (file is null) {
      (file, sha256) = await Download(key, key);
      await WriteUrlHash(key, sha256);
    }

    return OpenZipFile(key, file, new ZipArchiveIdentity(sha256!, key));
  }

  /// Validates a ZIP file (§2) and opens it, restricted to identity.Path if that is set.
  private static ArchiveHandle OpenZipFile(
      string key, IFileInfo file, ZipArchiveIdentity identity) {
    ZipValidator.Validate(file, key);

    ISource source = new ZipSource(file);
    if (identity.Path is string path) {
      source = new PrefixSource(source, path);
      EnsureNotEmpty(source, key);
    }

    return new ArchiveHandle(key, source, identity);
  }

  private ArchiveHandle OpenDirectory(string key, string path) {
    IDirectoryInfo directory = _fileSystem.DirectoryInfo.New(path);
    if (!directory.Exists) {
      throw new ArchiveUnavailableException($"{key}: directory {path} does not exist.");
    }

    return new ArchiveHandle(
        key, new DirectorySource(directory), new DirectoryArchiveIdentity(directory.FullName));
  }

  private async Task<ArchiveHandle> OpenGit(string key, GitArchiveIdentity identity) {
    try {
      // Lists the tree up front, so a missing commit fails here rather than on first access.
      // Only regular files are exposed: symlinks and gitlinks are not part of git archives (§2.2).
      GitTreeSource source = await GitTreeSource.Create(
          _gitRemoteClient, _cacheService, identity.RepoUrl, identity.Commit, identity.Path);
      if (identity.Path != "") {
        EnsureNotEmpty(source, key);
      }

      return new ArchiveHandle(key, source, identity);
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"{key}: failed to list commit {identity.Commit} of {identity.RepoUrl}: {ex.Message}",
          ex);
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

  /// Parses a tag ref name into a version if the tag is named exactly "v" + SemVer 2.0.0 (§3).
  public static SemVersion? ParseVersionTag(string refName) {
    if (!refName.StartsWith(_versionTagRefPrefix, StringComparison.Ordinal)) {
      return null;
    }

    return SemVersion.TryParse(
        refName[_versionTagRefPrefix.Length..], SemVersionStyles.Strict, out SemVersion? version)
        ? version
        : null;
  }

  /// Downloads a URL into the content cache, via the GitHub proxy first if one is configured.
  /// A candidate whose response is not a valid ZIP archive (e.g. a proxy error page) is skipped.
  private async Task<(IFileInfo File, string Sha256)> Download(string url, string archiveKey) {
    List<Exception> exceptions = [];

    foreach (Url downloadUrl in GetDownloadUrls(url)) {
      try {
        (IFileInfo file, string sha256) = await StoreInCache(
            destination => _fileDownloader.DownloadFile(downloadUrl, destination));
        ZipValidator.Validate(file, archiveKey);
        return (file, sha256);
      }
      catch (Exception ex) when (IsUnavailable(ex) || ex is ArchiveUnavailableException) {
        exceptions.Add(new Exception($"Failed to download from {downloadUrl}", ex));
      }
    }

    throw new ArchiveUnavailableException(
        $"{archiveKey}: download failed: "
            + string.Join("; ", exceptions.Select(e => e.InnerException?.Message ?? e.Message)),
        new AggregateException(exceptions));
  }

  private IEnumerable<Url> GetDownloadUrls(string url) {
    Url original = Url.Parse(url);

    if (_githubProxy is null
        || !string.Equals(original.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
      return [original];
    }

    Url proxyUrl = _githubProxy.Clone().AppendPathSegments(original.PathSegments);
    if (!string.IsNullOrEmpty(original.Query)) {
      proxyUrl.Query = original.Query;
    }

    return proxyUrl.ToString() == original.ToString() ? [original] : [proxyUrl, original];
  }

  /// Writes content to a temporary file, then moves it into the cache under its SHA-256.
  private async Task<(IFileInfo File, string Sha256)> StoreInCache(Func<IFileInfo, Task> write) {
    string tempDirectory = LipDirectories.Temp;
    _fileSystem.Directory.CreateDirectory(tempDirectory);
    IFileInfo temp = _fileSystem.FileInfo.New(
        _fileSystem.Path.Combine(tempDirectory, Guid.NewGuid().ToString()));

    try {
      await write(temp);
      temp.Refresh();
      string sha256 = await ComputeSha256(temp);

      // Evicts a corrupted cached copy first, since GetOrCreateFile returns any existing file.
      await GetCachedContent(sha256);

      IFileInfo file = await _cacheService.GetOrCreateFile(
          GetContentCacheKey(sha256),
          file => {
            _fileSystem.File.Move(temp.FullName, file.FullName);
            return Task.CompletedTask;
          });
      return (file, sha256);
    }
    finally {
      temp.Refresh();
      if (temp.Exists) {
        temp.Delete();
      }
    }
  }

  /// Returns the cached content with the given SHA-256, or null if it is not cached. A cached
  /// file whose content does not match its hash is deleted.
  private async Task<IFileInfo?> GetCachedContent(string sha256) {
    IFileInfo? file = await _cacheService.TryGetFile(GetContentCacheKey(sha256));
    if (file is null || await ComputeSha256(file) == sha256) {
      return file;
    }

    file.Delete();
    return null;
  }

  private async Task<string?> ReadUrlHash(string url) {
    IFileInfo? file = await _cacheService.TryGetFile(GetUrlCacheKey(url));
    if (file is null) {
      return null;
    }

    string sha256 = (await _fileSystem.File.ReadAllTextAsync(file.FullName)).Trim();
    return sha256.Length == 64 && sha256.All(Uri.IsHexDigit) ? sha256.ToLowerInvariant() : null;
  }

  private async Task WriteUrlHash(string url, string sha256) {
    IFileInfo file = await _cacheService.GetOrCreateFile(
        GetUrlCacheKey(url),
        file => _fileSystem.File.WriteAllTextAsync(file.FullName, sha256));

    // A stale mapping exists when the cached content was evicted or the URL serves new bytes.
    if ((await _fileSystem.File.ReadAllTextAsync(file.FullName)).Trim() != sha256) {
      await _fileSystem.File.WriteAllTextAsync(file.FullName, sha256);
    }
  }

  private static async Task<string> ComputeSha256(IFileInfo file) {
    using Stream stream = file.OpenRead();
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
  }

  private static void EnsureNotEmpty(ISource source, string key) {
    if (!source.Keys.Any()) {
      throw new ArchiveUnavailableException($"{key}: directory does not exist or is empty.");
    }
  }

  private static string GetRelativeKey(Ref target, ArchiveHandle baseArchive) =>
      $"{baseArchive.Key}!{target.Target[2..]}";

  private static string JoinPath(string directory, string relativePath) =>
      directory == "" ? relativePath : $"{directory}/{relativePath}";

  private static string GetContentCacheKey(string sha256) => $"sha256:{sha256}";

  // Keyed by a hash of the URL: CacheService uses the escaped key as a file name, and URLs can
  // be longer than file names may be.
  private static string GetUrlCacheKey(string url) =>
      $"url-sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))}";

  // Everything but cancellation and programming errors makes an archive unavailable.
  private static bool IsUnavailable(Exception ex) =>
      ex is not (OperationCanceledException or ArchiveUnavailableException
          or ArgumentNullException or InvalidCastException or NullReferenceException);

  private record TaggedVersion(SpecVersion Version, string Commit);
}
