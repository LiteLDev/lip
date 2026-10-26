using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.PackageRegistries;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.RegressionTests.Compatibility;

/// Checks that every corpus entry lip could install before the move to pure git resolution
/// (<c>baseline_installable && git_tag</c> in corpus.json) still resolves: the version is listed,
/// the manifest at the tag parses and declares the variant, its direct dependencies have versions,
/// and the self-asset placements are matched against the git tree without downloading any file
/// other than tooth.json. RISK entries (upstream breakage, see README.md) are skipped.
///
/// Selection via the LIP_COMPAT_CORPUS environment variable:
/// - unset or "sample": the newest version of every package (the CI default);
/// - "full": every entry;
/// - anything else: a regular expression matched against "package@version".
/// LIP_COMPAT_CORPUS_PARALLELISM sets how many entries are resolved concurrently (default 16).
public class CorpusTests {
  private const string _skipReason = "upstream breakage";

  // Entries outside the README's RISK list that fail for reasons outside lip's control, keyed by
  // "package@version". Only the lipr-hosted (rewritten) manifest made them installable before.
  private static readonly Dictionary<string, string> _knownUpstreamBreakage = new() {
    ["github.com/spygamingog/Animated-Mojang-Android#client@1.0.0"] =
        "tooth.json at the tag uses the unsupported asset type 'levipack'",
  };

  private static readonly Lazy<ConcurrentDictionary<string, Task<string?>>> _results = new(StartAll);

  private static readonly GitRemoteClient _client = new(githubProxy: null);

  private static readonly GitRemotePackageRegistry _registry = new(_client);

  private static readonly CacheService _cacheService = new(
      new FileSystem(), new Mock<IUserInteraction>().Object);

  public static IEnumerable<TheoryDataRow<string, string>> Entries => SelectEntries()
      .Select(entry => new TheoryDataRow<string, string>(entry.Package, entry.Version) {
        Skip = entry.Risk || _knownUpstreamBreakage.ContainsKey(Key(entry.Package, entry.Version))
            ? _skipReason
            : null,
        TestDisplayName = Key(entry.Package, entry.Version),
      });

  [WinX64Theory]
  [MemberData(nameof(Entries))]
  public async Task Entry_ResolvesViaGit(string package, string version) {
    string? failure = await _results.Value[Key(package, version)];

    if (failure is not null) {
      Assert.Fail(failure);
    }
  }

  private static string Key(string package, string version) => $"{package}@{version}";

  /// Starts resolving all selected, non-skipped entries in the background with bounded
  /// concurrency; each test row then only awaits its own result.
  private static ConcurrentDictionary<string, Task<string?>> StartAll() {
    int parallelism = int.TryParse(
        Environment.GetEnvironmentVariable("LIP_COMPAT_CORPUS_PARALLELISM"), out int p) && p > 0 ? p : 16;
    SemaphoreSlim semaphore = new(parallelism);

    ConcurrentDictionary<string, Task<string?>> results = new();
    foreach (CorpusEntry entry in SelectEntries().Where(
        e => !e.Risk && !_knownUpstreamBreakage.ContainsKey(Key(e.Package, e.Version)))) {
      results[Key(entry.Package, entry.Version)] = Task.Run(async () => {
        await semaphore.WaitAsync();
        try {
          return await Check(entry);
        }
        catch (Exception ex) {
          return ex.ToString();
        }
        finally {
          semaphore.Release();
        }
      });
    }
    return results;
  }

  /// Returns null on success, otherwise a description of the failure.
  private static async Task<string?> Check(CorpusEntry entry) {
    PackageId packageId = PackageId.Parse(entry.Package);
    PackageSpec spec = new(packageId, SemVersion.Parse(entry.Version, SemVersionStyles.Strict));

    // 1. The version is listed.
    IEnumerable<SemVersion> versions = await _registry.GetAvailableVersions(packageId);
    if (!versions.Contains(spec.Version)) {
      return $"version {spec.Version} is not listed";
    }

    // 2. The manifest at the tag parses and declares the variant.
    PackageManifest manifest = await _registry.GetPackageManifest(spec);
    if (!manifest.Variants.Any(v => v.Label == packageId.Variant)) {
      return $"manifest does not declare variant '{packageId.Variant}'";
    }

    PackageManifestVariant variant;
    try {
      variant = manifest.GetVariant(packageId.Variant);
    }
    catch (KeyNotFoundException) {
      // Declared, but not for this platform: not installable here, with or without git.
      return null;
    }

    // 3. Direct dependencies have at least one version.
    foreach (PackageId dependency in variant.Dependencies.Keys) {
      IEnumerable<SemVersion> dependencyVersions;
      try {
        dependencyVersions = await _registry.GetAvailableVersions(dependency);
      }
      catch (Exception ex) {
        return $"dependency {dependency} does not resolve: {ex.Message}";
      }
      if (!dependencyVersions.Any()) {
        return $"dependency {dependency} has no versions";
      }
    }

    // 4. Self-asset placements are resolved against the git tree. Only tooth.json is downloaded.
    string commit = await _registry.ResolveCommit(spec);
    RecordingGitRemoteClient recorder = new(_client);
    GitTreeSource source = await GitTreeSource.Create(
        recorder, _cacheService, GitRemotePackageRegistry.GetRepoUrl(packageId), commit);
    List<string> keys = [.. source.Keys];

    foreach (PackageManifestAsset asset in variant.Assets.Where(a => a.Type == PackageManifestAsset.AssetType.Self)) {
      foreach (PackageManifestAssetPlacement placement in asset.Placements) {
        // Many manifests place build outputs that are not committed, so a placement may match
        // nothing (as before). It must match, though, whenever the tree has the path it names.
        if (PlacementMatcher.Match(keys, [placement]).Count == 0 && TreeHasPath(keys, placement)) {
          return $"self placement {placement.Type} '{placement.Src}' -> '{placement.Dst}' matches no key";
        }
      }
    }

    if (recorder.FetchedBlobCount > 0) {
      return $"{recorder.FetchedBlobCount} blob(s) were downloaded while only resolving keys";
    }

    return null;
  }

  private static bool TreeHasPath(List<string> keys, PackageManifestAssetPlacement placement) {
    string src = placement.Src.Replace('\\', '/');

    return placement.Type switch {
      PackageManifestAssetPlacement.PlacementType.File => keys.Contains(src),
      _ => src.Trim('/') is "" or "."
          ? keys.Count > 0
          : keys.Any(k => k.StartsWith(src.Trim('/') + "/", StringComparison.Ordinal)),
    };
  }

  private static IEnumerable<CorpusEntry> SelectEntries() {
    List<CorpusEntry> entries = [.. LoadCorpus().Where(e => e.BaselineInstallable && e.GitTag)];

    string selection = Environment.GetEnvironmentVariable("LIP_COMPAT_CORPUS") ?? "sample";

    if (selection == "full") {
      return entries;
    }

    if (selection == "sample") {
      // The newest version of every package (all variants of that version).
      return entries
          .GroupBy(e => e.Package.Split('#')[0])
          .SelectMany(g => {
            SemVersion newest = g
                .Select(e => SemVersion.Parse(e.Version, SemVersionStyles.Strict))
                .Max(SemVersion.PrecedenceComparer)!;
            return g.Where(e => SemVersion.Parse(e.Version, SemVersionStyles.Strict) == newest);
          });
    }

    Regex filter = new(selection, RegexOptions.IgnoreCase);
    return entries.Where(e => filter.IsMatch(Key(e.Package, e.Version)));
  }

  private static List<CorpusEntry> LoadCorpus() {
    string path = Path.Join(AppContext.BaseDirectory, "Compatibility", "corpus.json");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

    return [.. document.RootElement.GetProperty("entries").EnumerateArray().Select(e => new CorpusEntry(
        e.GetProperty("package").GetString()!,
        e.GetProperty("version").GetString()!,
        e.GetProperty("baseline_installable").GetBoolean(),
        e.GetProperty("git_tag").GetBoolean(),
        e.GetProperty("risk").GetBoolean()))];
  }

  private record CorpusEntry(string Package, string Version, bool BaselineInstallable, bool GitTag, bool Risk);

  /// Forwards to the real client and counts the blobs a source downloads.
  private class RecordingGitRemoteClient(IGitRemoteClient inner) : IGitRemoteClient {
    private int _fetchedBlobCount;

    public int FetchedBlobCount => _fetchedBlobCount;

    public Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) =>
        inner.ListRefs(repoUrl, refPrefixes);

    public Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) =>
        inner.ListTree(repoUrl, commitish);

    public Task FetchBlobs(Url repoUrl, IReadOnlyCollection<string> blobObjectIds, Func<string, Stream, Task> onBlob) {
      Interlocked.Add(ref _fetchedBlobCount, blobObjectIds.Count);
      return inner.FetchBlobs(repoUrl, blobObjectIds, onBlob);
    }
  }
}
