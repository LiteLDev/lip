using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Security.Cryptography;
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
/// URL is downloaded once; the workspace file records their SHA-256, which is verified when they
/// are reopened. Local ZIP files are read into memory where they are, not cached; reopening one
/// from the workspace file verifies its SHA-256. Local directories are read where they are. git
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
          : OpenFile(target.Target, target.Target, target.LocalPath),
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
    return OpenGit(
        $"{spec}@rev:{normalized}", new GitArchiveIdentity(GetRepoUrl(spec), normalized));
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
        $"{spec}@{version.Version}", new GitArchiveIdentity(GetRepoUrl(spec), tagged.Commit));
  }

  public async Task<ArchiveHandle> OpenLocked(string key, ArchiveIdentity identity) {
    switch (identity) {
      case GitArchiveIdentity git:
        return await OpenGit(key, git);

      case ZipArchiveIdentity zip: {
          string sha256 = zip.Sha256.ToLowerInvariant();
          if (Ref.TryParse(zip.Url, allowFile: true, out Ref? fileRef, out _)
              && fileRef.Kind == RefKind.File) {
            // A local file is read where it was; it must still have the recorded content.
            ArchiveHandle local = OpenFile(key, zip.Url, fileRef.LocalPath);
            string found = ((ZipArchiveIdentity)local.Identity).Sha256;
            if (found != sha256) {
              throw new ArchiveUnavailableException(
                  $"{key}: {fileRef.LocalPath} now has sha256:{found}, "
                      + $"but the workspace file records sha256:{sha256}.");
            }

            return local;
          }

          IFileInfo file = await DownloadVerified(zip.Url, key, sha256, ZipValidator.Validate);
          ZipValidator.Validate(file, key);
          return new ArchiveHandle(key, new ZipSource(file), zip);
        }

      case DirectoryArchiveIdentity directory:
        return OpenDirectory(key, directory.Path);

      default:
        throw new ArgumentException($"Unknown archive identity: {identity}", nameof(identity));
    }
  }

  public async Task<OpenedAsset> OpenAsset(IReadOnlyList<string> urls, AssetFormat format) {
    List<string> errors = [];
    foreach (string url in urls) {
      try {
        if (format == AssetFormat.Zip && !url.StartsWith("https://", StringComparison.Ordinal)) {
          throw new ArchiveUnavailableException($"{url} is not an https URL.");
        }

        // A cached copy that is not what the asset must be (e.g. corrupted) is downloaded again.
        IFileInfo? cached = await _cacheService.TryGetFile(CacheKeys.Http(url));
        if (cached is not null && CheckAsset(cached, url, format) is not null) {
          cached.Delete();
        }

        IFileInfo file = await Download(url, url, (f, key) => {
          if (CheckAsset(f, key, format) is string error) {
            throw new ArchiveUnavailableException(error);
          }
        });
        return new OpenedAsset(
            AssetSource(file, format), new AssetIdentity(url, await ComputeSha256(file)));
      }
      catch (ArchiveUnavailableException ex) {
        errors.Add(ex.Message);
      }
    }

    throw new ArchiveUnavailableException(urls.Count == 0
        ? "The asset has no URLs."
        : $"Failed to download the asset: {string.Join("; ", errors)}");
  }

  public async Task<ISource> OpenLockedAsset(AssetIdentity identity, AssetFormat format) {
    IFileInfo file = await DownloadVerified(
        identity.Url, identity.Url, identity.Sha256.ToLowerInvariant(), (f, key) => {
          if (CheckAsset(f, key, format) is string error) {
            throw new ArchiveUnavailableException(error);
          }
        });
    return AssetSource(file, format);
  }

  /// The files of a downloaded asset.
  private static ISource AssetSource(IFileInfo file, AssetFormat format) => format switch {
    AssetFormat.Zip => new ZipSource(file),
    AssetFormat.LegacyFile => new SingleFileSource(file),
    _ => new ArchiveSource(file),
  };

  /// Why a downloaded file is not what an asset of the format must be, or null.
  private static string? CheckAsset(IFileInfo file, string key, AssetFormat format) {
    switch (format) {
      case AssetFormat.Zip:
        try {
          ZipValidator.Validate(file, key);
          return null;
        }
        catch (ArchiveUnavailableException ex) {
          return ex.Message;
        }

      case AssetFormat.LegacyArchive:
        return IsArchive(file) ? null : $"{key} is not an archive.";
      default:
        return null;
    }
  }

  /// The cached download of a URL, downloaded again when it is missing or has other content;
  /// fails when the URL serves other content than the SHA-256 recorded.
  private async Task<IFileInfo> DownloadVerified(
      string url, string key, string sha256, Action<IFileInfo, string> validate) {
    IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(url));
    if (file is null || await ComputeSha256(file) != sha256) {
      // Missing, corrupted, or the URL served other bytes since: download it again.
      file?.Delete();
      file = await Download(url, key, validate);

      string actual = await ComputeSha256(file);
      if (actual != sha256) {
        throw new ArchiveUnavailableException(
            $"{key}: {url} now has sha256:{actual}, "
                + $"but the workspace file records sha256:{sha256}.");
      }
    }

    return file;
  }

  /// The git repository URL of a remote repository (protocol/package.md §2.1), without any
  /// GitHub proxy applied.
  public static string GetRepoUrl(string spec) => $"https://{spec}.git";

  private async Task<ArchiveHandle> OpenUrl(string url) {
    // Sources never contain the fragment here, but strip it anyway since it is part of no key.
    string key = url.Split('#', 2)[0];

    IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(key));
    if (file is not null) {
      try {
        ZipValidator.Validate(file, key);
      }
      catch (ArchiveUnavailableException) {
        // A corrupted cached copy: download it again.
        file.Delete();
        file = null;
      }
    }

    // Download validates what it downloads.
    file ??= await Download(key, key, ZipValidator.Validate);

    return new ArchiveHandle(
        key, new ZipSource(file), new ZipArchiveIdentity(await ComputeSha256(file), key));
  }

  /// Opens a local directory, identified by its absolute path.
  private ArchiveHandle OpenDirectory(string key, string path) {
    string full = _fileSystem.Path.TrimEndingDirectorySeparator(_fileSystem.Path.GetFullPath(path));
    if (!_fileSystem.Directory.Exists(full)) {
      throw new ArchiveUnavailableException($"{key}: {full} is not a directory.");
    }

    return new ArchiveHandle(
        key, new DirectorySource(_fileSystem, full), new DirectoryArchiveIdentity(full));
  }

  /// Opens a local ZIP file, identified by its content.
  private ArchiveHandle OpenFile(string key, string url, string path) {
    if (!_fileSystem.File.Exists(path)) {
      throw new ArchiveUnavailableException($"{key}: {path} does not exist.");
    }

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

    return new ArchiveHandle(
        key,
        new ZipSource(content),
        new ZipArchiveIdentity(Convert.ToHexStringLower(SHA256.HashData(content)), url));
  }

  private async Task<ArchiveHandle> OpenGit(string key, GitArchiveIdentity identity) {
    try {
      // Lists the tree up front, so a missing commit fails here rather than on first access.
      // Only regular files are exposed: symlinks and gitlinks are not part of git archives.
      GitTreeSource source = await GitTreeSource.Create(
          _gitRemoteClient, _cacheService, identity.RepoUrl, identity.Commit);
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
  /// directory and is moved into place once complete. A candidate whose response validate
  /// rejects (e.g. one that is not a valid ZIP archive, like a proxy error page) is skipped.
  private async Task<IFileInfo> Download(
      string url, string archiveKey, Action<IFileInfo, string>? validate) {
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
            validate?.Invoke(destination, archiveKey);
            return;
          }
          catch (Exception ex) when (IsUnavailable(ex) || ex is ArchiveUnavailableException) {
            exceptions.Add(new Exception($"Failed to download from {downloadUrl}", ex));
          }
        }

        throw new ArchiveUnavailableException(
            $"{archiveKey}: download failed: "
                + string.Join("; ", exceptions.Select(e => e.InnerException?.Message ?? e.Message)),
            new AggregateException(exceptions));
      });
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"{archiveKey}: cannot store the download in the cache: {ex.Message}", ex);
    }
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

  /// Whether a file is an archive ArchiveSource can open.
  private static bool IsArchive(IFileInfo file) {
    try {
      _ = new ArchiveSource(file).Keys.Count();
      return true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return false;
    }
  }

  private static async Task<string> ComputeSha256(IFileInfo file) {
    using Stream stream = file.OpenRead();
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
  }

  // Everything but cancellation and programming errors makes an archive unavailable.
  private static bool IsUnavailable(Exception ex) =>
      ex is not (OperationCanceledException or ArchiveUnavailableException
          or ArgumentNullException or InvalidCastException or NullReferenceException);

  private record TaggedVersion(SpecVersion Version, string Commit);
}
