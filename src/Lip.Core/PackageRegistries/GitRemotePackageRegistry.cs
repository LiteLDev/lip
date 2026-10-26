using System.Collections.Concurrent;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Semver;

namespace Lip.Core.PackageRegistries;

/// <summary>
/// Resolves packages directly from their git repositories: versions come from <c>refs/tags/v*</c>
/// and manifests from the root <c>tooth.json</c> blob of the tagged commit, without fetching
/// anything else.
/// </summary>
/// <remarks>
/// Tags are parsed like the former Go module proxy path: the tag name (including the leading
/// <c>v</c>) is canonicalized with <see cref="Golang.Org.X.Mod.Semver.Canonical"/>, so <c>v1.2</c>
/// becomes <c>1.2.0</c> and build metadata (e.g. <c>+incompatible</c>) is dropped. Since a version may
/// therefore come from a non-canonical tag, the original ref is remembered and used for fetching.
/// When several tags map to the same version, the canonical spelling (<c>v{version}</c>) wins,
/// otherwise the ordinally smallest tag name.
/// </remarks>
public class GitRemotePackageRegistry(IGitRemoteClient gitRemoteClient) : IPackageRegistry {
  private const string _tagsRefPrefix = "refs/tags/";

  private const string _tagPrefix = $"{_tagsRefPrefix}v";

  private readonly IGitRemoteClient _gitRemoteClient = gitRemoteClient;

  private readonly ConcurrentDictionary<string, Task<IReadOnlyDictionary<SemVersion, GitRemoteRef>>> _tagCache = new();

  public async Task<IOrderedEnumerable<SemVersion>> GetAvailableVersions(PackageId packageId) {
    IReadOnlyDictionary<SemVersion, GitRemoteRef> tags = await GetVersionTags(packageId);

    return tags.Keys.Order(SemVersion.PrecedenceComparer);
  }

  public async Task<PackageManifest> GetPackageManifest(PackageSpec packageSpec) {
    Url repoUrl = GetRepoUrl(packageSpec.Id);
    string commit = await ResolveCommit(packageSpec);

    IReadOnlyList<GitTreeEntry> tree = await _gitRemoteClient.ListTree(repoUrl, commit);
    GitTreeEntry manifestEntry = tree.FirstOrDefault(entry => entry.Path == "tooth.json")
        ?? throw new InvalidOperationException(
            $"Package {packageSpec} has no tooth.json at the root of commit {commit}.");

    // Only regular files (0100644 / 0100755); e.g. a symlink blob holds its target path, not JSON.
    if ((manifestEntry.Mode & 0xF000) != 0x8000) {
      throw new InvalidOperationException(
          $"tooth.json of package {packageSpec} is not a regular file (mode {Convert.ToString(manifestEntry.Mode, 8)}).");
    }

    PackageManifest? manifest = null;
    await _gitRemoteClient.FetchBlobs(repoUrl, [manifestEntry.ObjectId], async (objectId, stream) => {
      if (objectId == manifestEntry.ObjectId) {
        manifest = await PackageManifest.FromStream(stream);
      }
    });

    return manifest ?? throw new InvalidOperationException(
        $"Failed to fetch tooth.json (blob {manifestEntry.ObjectId}) of package {packageSpec}.");
  }

  /// <summary>
  /// Gets the canonical git repository URL of a package (without any GitHub proxy applied).
  /// </summary>
  public static Url GetRepoUrl(PackageId packageId) => Url.Parse($"https://{packageId.Path}.git");

  /// <summary>
  /// Finds the tag ref backing the given package version. The ref name may differ from
  /// <c>refs/tags/v{version}</c> when the tag is non-canonical (e.g. <c>v1.2</c> for 1.2.0).
  /// </summary>
  public async Task<GitRemoteRef> ResolveTag(PackageSpec packageSpec) {
    IReadOnlyDictionary<SemVersion, GitRemoteRef> tags = await GetVersionTags(packageSpec.Id);

    return tags.TryGetValue(packageSpec.Version.WithoutMetadata(), out GitRemoteRef? tag)
        ? tag
        : throw new InvalidOperationException(
            $"Package {packageSpec} not found: no tag for version {packageSpec.Version} in {GetRepoUrl(packageSpec.Id)}.");
  }

  /// <summary>
  /// Resolves the commit object id a package version is tagged at (annotated tags are peeled).
  /// </summary>
  public async Task<string> ResolveCommit(PackageSpec packageSpec) {
    GitRemoteRef tag = await ResolveTag(packageSpec);

    return tag.PeeledObjectId ?? tag.ObjectId;
  }

  /// <summary>
  /// Parses a tag ref name (e.g. <c>refs/tags/v1.2.3</c>) into a version, or returns null if the
  /// tag is not a version tag.
  /// </summary>
  public static SemVersion? ParseTagVersion(string refName) {
    if (!refName.StartsWith(_tagPrefix, StringComparison.Ordinal)) {
      return null;
    }

    string canonical = Golang.Org.X.Mod.Semver.Canonical(refName[_tagsRefPrefix.Length..]);
    if (canonical == string.Empty) {
      return null;
    }

    return SemVersion.TryParse(canonical[1..], SemVersionStyles.Strict, out SemVersion? version)
        ? version
        : null;
  }

  private async Task<IReadOnlyDictionary<SemVersion, GitRemoteRef>> GetVersionTags(PackageId packageId) {
    // Variants share a repository, so cache by path only.
    string key = packageId.Path;

    Task<IReadOnlyDictionary<SemVersion, GitRemoteRef>> task = _tagCache.GetOrAdd(key, _ => ListVersionTags(packageId));

    try {
      return await task;
    } catch {
      // Do not keep failures (e.g. network errors) cached so that later calls can retry.
      _tagCache.TryRemove(new(key, task));
      throw;
    }
  }

  private async Task<IReadOnlyDictionary<SemVersion, GitRemoteRef>> ListVersionTags(PackageId packageId) {
    IReadOnlyList<GitRemoteRef> refs = await _gitRemoteClient.ListRefs(GetRepoUrl(packageId), [_tagPrefix]);

    Dictionary<SemVersion, GitRemoteRef> tags = [];
    foreach (GitRemoteRef gitRef in refs.OrderBy(r => r.Name, StringComparer.Ordinal)) {
      SemVersion? version = ParseTagVersion(gitRef.Name);
      if (version is null) {
        continue;
      }

      if (!tags.TryGetValue(version, out GitRemoteRef? existing)
          || (gitRef.Name == $"{_tagPrefix}{version}" && existing.Name != gitRef.Name)) {
        tags[version] = gitRef;
      }
    }

    return tags;
  }
}
