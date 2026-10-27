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

/// Obtains archives for ref targets (spec §2, §3, §5.2 step 1, §8).
///
/// Downloaded ZIP archives are cached under the key of their URL (CacheKeys.Http), so a URL is
/// downloaded once; the lock records their SHA-256, which is verified when they are reopened.
/// ZIP archives nested in another archive are not cached: they are read into memory from their
/// parent archive, which is reopened (or re-resolved through the recorded chain) to get them back.
/// Local ZIP files (file refs of user requests) are read into memory where they are, not cached;
/// reopening one from the lock verifies its SHA-256. git archives are lazy GitTreeSources: only
/// the tree is listed, blobs are fetched on demand. A git ref (branch, tag) is resolved with
/// ls-refs; a rev must be a full commit id, since an abbreviated one cannot be resolved without
/// fetching the history. tooth.json assets (OpenAsset) are downloaded into the same cache as url
/// archives.
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

  public async Task<ArchiveHandle> Open(Ref target, ArchiveHandle? baseArchive) {
    if (target.Kind == RefKind.Relative && baseArchive is null) {
      throw new ArchiveUnavailableException(
          $"{target}: a relative ref is only meaningful inside an archive.");
    }

    return target.Kind switch {
      RefKind.Relative => target.IsDirectory
          ? await OpenSubdirectory(target, baseArchive!)
          : await OpenNestedZip(target, baseArchive!),
      RefKind.Url => await OpenUrl(target.Target),
      RefKind.File => OpenFile(target.Target, target.Target, target.LocalPath),
      _ => throw new ArgumentException(
          $"Spec refs are opened with OpenVersion or OpenCommit, not Open: {target}",
          nameof(target)),
    };
  }

  public async Task<string> ResolveGitRef(string spec, GitRef gitRef) {
    if (gitRef.Kind == GitRefKind.Rev) {
      return gitRef.Name;
    }

    string repoUrl = GetRepoUrl(spec);
    string refName = (gitRef.Kind == GitRefKind.Branch ? _headsRefPrefix : _tagsRefPrefix)
        + gitRef.Name;

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
        $"{spec}@rev:{normalized}", new GitArchiveIdentity(GetRepoUrl(spec), normalized, ""));
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

      case ZipArchiveIdentity zip: {
          if (zip.Url is null) {
            // The caller reopens the parent archive by resolving the recorded chain again.
            throw new ArchiveUnavailableException(
                $"{key}: a nested ZIP archive is not cached; it is read from its parent archive.");
          }

          string sha256 = zip.Sha256.ToLowerInvariant();
          if (Ref.TryParse(zip.Url, allowFile: true, out Ref? fileRef, out _)
              && fileRef.Kind == RefKind.File) {
            // A local file is read where it was; it must still have the recorded content.
            ArchiveHandle local = OpenFile(key, zip.Url, fileRef.LocalPath);
            string found = ((ZipArchiveIdentity)local.Identity).Sha256;
            if (found != sha256) {
              throw new ArchiveUnavailableException(
                  $"{key}: {fileRef.LocalPath} now has sha256:{found}, "
                      + $"but the lock records sha256:{sha256}.");
            }

            return zip.Path is null ? local : OpenZip(key, (ZipSource)local.Source, zip);
          }

          IFileInfo? file = await _cacheService.TryGetFile(CacheKeys.Http(zip.Url));
          if (file is null || await ComputeSha256(file) != sha256) {
            // Missing, corrupted, or the URL served other bytes since: download it again.
            file?.Delete();
            file = await Download(zip.Url, key);

            string actual = await ComputeSha256(file);
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

  public async Task<ISource> OpenAsset(IReadOnlyList<string> urls, bool isArchive) {
    List<string> errors = [];
    foreach (string url in urls) {
      try {
        if (!isArchive) {
          return new SingleFileSource(await Download(url, url, validate: null));
        }

        // A cached copy that is no archive (e.g. corrupted) is downloaded again.
        IFileInfo? cached = await _cacheService.TryGetFile(CacheKeys.Http(url));
        if (cached is not null && !IsArchive(cached)) {
          cached.Delete();
        }

        return new ArchiveSource(await Download(url, url, validate: (file, key) => {
          if (!IsArchive(file)) {
            throw new ArchiveUnavailableException($"{key} is not an archive.");
          }
        }));
      }
      catch (ArchiveUnavailableException ex) {
        errors.Add(ex.Message);
      }
    }

    throw new ArchiveUnavailableException(urls.Count == 0
        ? "The asset has no URLs."
        : $"Failed to download the asset: {string.Join("; ", errors)}");
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

    byte[] content;
    try {
      HashSet<string> keys = [.. baseArchive.Source.Keys];
      if (!keys.Contains(relativePath)) {
        string reason = keys.Any(
            k => k.StartsWith(relativePath + "/", StringComparison.Ordinal))
            ? "is a directory (directory refs end with '/')"
            : "does not exist";
        throw new ArchiveUnavailableException(
            $"{key}: '{relativePath}' {reason} in {baseArchive.Key}.");
      }

      using Stream stream = await baseArchive.Source.OpenRead(relativePath);
      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      content = memory.ToArray();
    }
    catch (Exception ex) when (IsUnavailable(ex)) {
      throw new ArchiveUnavailableException(
          $"{key}: failed to read '{relativePath}' from {baseArchive.Key}: {ex.Message}", ex);
    }

    string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
    using (MemoryStream stream = new(content, writable: false)) {
      ZipValidator.Validate(stream, key);
    }

    return OpenZip(key, new ZipSource(content), new ZipArchiveIdentity(sha256, null));
  }

  private async Task<ArchiveHandle> OpenUrl(string url) {
    // Ref targets never contain the fragment, but strip it anyway since it is part of no key.
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
    file ??= await Download(key, key);

    return OpenZip(
        key, new ZipSource(file), new ZipArchiveIdentity(await ComputeSha256(file), key));
  }

  /// Validates a ZIP file (§2) and opens it, restricted to identity.Path if that is set.
  private static ArchiveHandle OpenZipFile(
      string key, IFileInfo file, ZipArchiveIdentity identity) {
    ZipValidator.Validate(file, key);
    return OpenZip(key, new ZipSource(file), identity);
  }

  /// Opens a validated ZIP archive, restricted to identity.Path if that is set.
  private static ArchiveHandle OpenZip(string key, ZipSource zip, ZipArchiveIdentity identity) {
    ISource source = zip;
    if (identity.Path is string path) {
      source = new PrefixSource(source, path);
      EnsureNotEmpty(source, key);
    }

    return new ArchiveHandle(key, source, identity);
  }

  /// Opens a local ZIP file (a file ref of a user request), identified by its content.
  private ArchiveHandle OpenFile(string key, string url, string path) {
    if (!_fileSystem.File.Exists(path)) {
      throw new ArchiveUnavailableException(_fileSystem.Directory.Exists(path)
          ? $"{key}: {path} is a directory; lip installs a local package only from a .mcmod or "
              + ".zip file."
          : $"{key}: {path} does not exist.");
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

  /// Downloads a URL into the cache under CacheKeys.Http(url), via the GitHub proxy first if one
  /// is configured, unless it is cached already. The download goes to the cache's staging
  /// directory and is moved into place once complete. A candidate whose response validate
  /// rejects (by default: one that is not a valid ZIP archive, e.g. a proxy error page) is
  /// skipped.
  private Task<IFileInfo> Download(string url, string archiveKey) =>
      Download(url, archiveKey, ZipValidator.Validate);

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

  private static void EnsureNotEmpty(ISource source, string key) {
    if (!source.Keys.Any()) {
      throw new ArchiveUnavailableException($"{key}: directory does not exist or is empty.");
    }
  }

  private static string GetRelativeKey(Ref target, ArchiveHandle baseArchive) =>
      $"{baseArchive.Key}!{target.Target[2..]}";

  private static string JoinPath(string directory, string relativePath) =>
      directory == "" ? relativePath : $"{directory}/{relativePath}";

  // Everything but cancellation and programming errors makes an archive unavailable.
  private static bool IsUnavailable(Exception ex) =>
      ex is not (OperationCanceledException or ArchiveUnavailableException
          or ArgumentNullException or InvalidCastException or NullReferenceException);

  private record TaggedVersion(SpecVersion Version, string Commit);
}
