using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Resolution;
using Lip.Core.Services;
using Moq;
using Semver;

namespace Lip.Core.RegressionTests.Compatibility;

/// Checks that every corpus entry lip v3 could install (<c>baseline_installable && git_tag</c> in
/// corpus.json) still resolves and solves on the format 4 pipeline:
/// - Resolve(package, workspace, =version, (server, win-x64)) yields a candidate, unless the
///   tooth.json at the tag declares the variant only for other platforms (not installable on
///   win-x64, with or without lip v4);
/// - only the root document is downloaded while resolving, and the self-asset placements are
///   matched against the git tree keys;
/// - GraphSolver.Solve of that single request succeeds (entries whose dependencies cannot be
///   satisfied upstream, which lip v3 could not install either, are listed below).
/// RISK entries (upstream breakage, see README.md) are skipped.
///
/// Selection via the LIP_COMPAT_CORPUS environment variable:
/// - unset or "sample": the newest version of every package (the CI default);
/// - "full": every entry;
/// - anything else: a regular expression matched against "package@version".
/// LIP_COMPAT_CORPUS_PARALLELISM sets how many entries are resolved concurrently (default 16).
public class CorpusTests {
  private const string _skipReason = "upstream breakage";

  private static readonly LipEnvironment _environment = new(McmodPlatform.Server, "win-x64");

  private const string _translatorApi =
      "no LLTranslatorApi tag in range has a valid tooth.json (v26.x declares '>=26.20.*')";
  private const string _noLeviLamina18 = "LeviLamina has only 1.8.0 prerelease tags";
  private const string _noLeviLamina2630 = "LeviLamina has no 26.30.x tag";

  // Entries outside the README's RISK list that fail for reasons outside lip's control, keyed by
  // "package@version". Only the lipr-hosted (rewritten) manifest made them installable before.
  private static readonly Dictionary<string, string> _knownUpstreamBreakage = new() {
    ["github.com/spygamingog/Animated-Mojang-Android#client@1.0.0"] =
        "tooth.json at the tag uses the unsupported asset type 'levipack'",
    ["github.com/Kashifro/LL-ModMenu#client@1.0.0"] = "tag v1.0.0 was deleted upstream",
  };

  // Entries that resolve, but whose dependencies cannot be satisfied from git, so solving them
  // may fail. lip v3's DependencySolver fails on every one of them as well (checked against
  // 9903e79); the v3 corpus test only checked that each direct dependency had some version.
  private static readonly Dictionary<string, string> _unsatisfiableUpstream = new() {
    ["github.com/LordBombardir/LLPowerRanks@1.0.7"] = _translatorApi,
    ["github.com/LordBombardir/LLPowerRanks@1.0.8"] = _translatorApi,
    ["github.com/LordBombardir/LLPowerRanks@1.0.9"] = _translatorApi,
    ["github.com/LordBombardir/LLPowerRanks@1.1.0"] = _translatorApi,
    ["github.com/LordBombardir/LLPowerRanks@1.1.1"] = _translatorApi,
    ["github.com/LordBombardir/LLPowerRanks@1.1.3"] = _translatorApi,
    ["github.com/LordBombardir/LLPowerRanks@1.1.4"] = _translatorApi,
    ["github.com/LordBombardir/LLStructureLoader@1.0.0"] = _translatorApi,
    ["github.com/LordBombardir/LLStructureLoader@1.0.1"] = _translatorApi,
    ["github.com/LordBombardir/LLStructureLoader@1.0.2"] = _translatorApi,
    ["github.com/LordBombardir/LLStructureLoader@1.1.0"] = _translatorApi,
    ["github.com/LordBombardir/LLStructureLoader@1.1.1"] = _translatorApi,
    ["github.com/MiracleForest/iStructuralShift@1.0.0"] =
        "LeviLamina 26.20.x has no 'server' variant",
    ["github.com/MiracleForest/iScale@1.0.0"] = "LeviLamina 26.20.x has no 'server' variant",
    ["github.com/engsr6982/FastMiner@0.14.0-rc.1"] = _noLeviLamina18,
    ["github.com/engsr6982/FastMiner#client@0.14.0-rc.1"] = _noLeviLamina18,
    ["github.com/PuceLi/NameTagPlus@1.0.0"] = _noLeviLamina18,
    ["github.com/PuceLi/NameTagPlus#client@1.0.0"] = _noLeviLamina18,
    ["github.com/MiracleForest/iAutomaticFishing#client@1.0.0"] = _noLeviLamina18,
    ["github.com/MiracleForest/iAutomaticFishing#server@1.0.0"] = _noLeviLamina18,
    ["github.com/VincentZyuApps/levilamina-plugin-mclistener-ws-server@0.6.3-alpha.3"] =
        _noLeviLamina18,
    ["github.com/FeixiangTMC/Stipuleroo#client@0.0.1"] = _noLeviLamina18,
    ["github.com/MiracleForest/iAutomaticFishing#client@1.4.0"] = _noLeviLamina2630,
    ["github.com/MiracleForest/iAutomaticFishing#server@1.4.0"] = _noLeviLamina2630,
    ["github.com/MiracleForest/iInfiniteNightVision#client@1.2.0"] = _noLeviLamina2630,
    ["github.com/loitietu/LOICollectionA@1.17.1"] = "LeviLamina has no 26.50.x tag",
    ["github.com/loitietu/LOICollectionA#client@1.17.1"] = "LeviLamina has no 26.50.x tag",
  };

  private static readonly Lazy<ConcurrentDictionary<string, Task<string?>>> _results = new(StartAll);

  private static readonly GitRemoteClient _client = new(githubProxy: null);

  // Tag lists are shared by all entries; everything else is fetched per entry, so that the blobs
  // an entry downloads can be attributed to it.
  private static readonly ListRefsCache _cachedClient = new(_client);

  private static readonly IUserInteraction _userInteraction = new Mock<IUserInteraction>().Object;

  private static readonly CacheService _cacheService = new(new FileSystem(), _userInteraction);

  private static readonly ArchiveProvider _sharedProvider = CreateProvider(_cachedClient);

  // One resolver for all solves, so that dependencies (LeviLamina above all) are read once.
  private static readonly Resolver _sharedResolver = CreateResolver(_sharedProvider);

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
    Ref reference = Ref.Parse(entry.Package);
    ExactVersionConstraint version = new(SemVersion.Parse(entry.Version, SemVersionStyles.Strict));

    // 1. Resolve the entry, recording what is downloaded.
    RecordingGitRemoteClient recorder = new(_cachedClient);
    ArchiveProvider provider = CreateProvider(recorder);
    ResolveResult result = await CreateResolver(provider).Resolve(
        reference, null, version, _environment);

    if (result.Candidates.Count == 0) {
      // Declared, but not for this platform: not installable here, with or without git.
      if (await DeclaresVariant(provider, reference, version)) {
        return null;
      }

      return "no candidate:\n  " + string.Join("\n  ", result.Diagnostics);
    }

    Candidate candidate = result.Candidates[0];

    // 2. Self-asset placements are resolved against the git tree. Only the root document is
    // downloaded.
    // The self assets of a converted tooth.json are the mounts of the tag itself; the other
    // mounts (URL assets) are not opened.
    if (candidate.Package is McmodPackage { Kind: ToothCompat.KindName } tooth
        && candidate.Archive.Source is MountSource mounts) {
      foreach ((string mount, Sources.ISource source) in mounts.Mounts.Where(
          m => m.Source is not DeferredSource)) {
        List<string> keys = [.. source.Keys];
        foreach (AssetPlacement placement in tooth.Document.AssetPlacements.Where(
            p => p.Mount == mount)) {
          // Many manifests place build outputs that are not committed, so a placement may match
          // nothing (as before). It must match, though, whenever the tree has the path it names.
          if (ToothPlacements.Match(keys, [placement]).Count == 0
              && TreeHasPath(keys, placement)) {
            return $"self placement {(placement.IsDirectory ? "dir" : "file")} "
                + $"'{placement.Src}' -> '{placement.Dest}' matches no key";
          }
        }
      }
    }

    List<string> unexpected = recorder.UnexpectedBlobs();
    if (unexpected.Count > 0) {
      return $"{unexpected.Count} blob(s) other than the root document were downloaded while "
          + $"only resolving keys: {string.Join(", ", unexpected)}";
    }

    // 3. The request solves.
    try {
      await new GraphSolver(_sharedResolver).Solve(
          [new PackageRequest(reference, version)], [], _environment);
    }
    catch (DependencyResolutionException) when (
        _unsatisfiableUpstream.ContainsKey(Key(entry.Package, entry.Version))) {
      return null;
    }
    catch (DependencyResolutionException ex) {
      return $"solving failed: {ex.Message}\n"
          + string.Join("\n", ex.Diagnostics.Take(30));
    }

    return null;
  }

  private static async Task<bool> DeclaresVariant(
      ArchiveProvider provider, Ref reference, ExactVersionConstraint version) {
    SpecVersion? tag = (await provider.ListVersions(reference.Target))
        .FirstOrDefault(v => version.IsSatisfiedBy(v.Version));
    if (tag is null) {
      return false;
    }

    ArchiveDocument document = await new ArchiveReader(new DocumentParser()).Read(
        await provider.OpenVersion(reference.Target, tag));
    return document is ToothArchiveDocument tooth
        && tooth.Manifest.Variants.Any(v => v.Label == reference.Variant);
  }

  private static ArchiveProvider CreateProvider(IGitRemoteClient client) => new(
      client, _cacheService, new FileSystem(), new FileDownloader(_userInteraction));

  private static Resolver CreateResolver(ArchiveProvider provider) => new(
      provider, new ArchiveReader(new DocumentParser()), new ToothCompat());

  private static bool TreeHasPath(List<string> keys, AssetPlacement placement) {
    string src = placement.Src.Replace('\\', '/');

    return !placement.IsDirectory
        ? keys.Contains(src)
        : src.Trim('/') is "" or "."
            ? keys.Count > 0
            : keys.Any(k => k.StartsWith(src.Trim('/') + "/", StringComparison.Ordinal));
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

  /// Shares tag lists between all entries; forwards everything else.
  private class ListRefsCache(IGitRemoteClient inner) : IGitRemoteClient {
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<GitRemoteRef>>>> _refs = new();

    public async Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) {
      List<string> prefixes = [.. refPrefixes];
      string key = $"{repoUrl}\n{string.Join("\n", prefixes)}";
      Lazy<Task<IReadOnlyList<GitRemoteRef>>> lazy = _refs.GetOrAdd(
          key, _ => new(() => inner.ListRefs(repoUrl, prefixes)));
      try {
        return await lazy.Value;
      }
      catch {
        _refs.TryRemove(new(key, lazy));
        throw;
      }
    }

    public Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) =>
        inner.ListTree(repoUrl, commitish);

    public Task FetchBlobs(Url repoUrl, IReadOnlyCollection<string> blobObjectIds, Func<string, Stream, Task> onBlob) =>
        inner.FetchBlobs(repoUrl, blobObjectIds, onBlob);
  }

  /// Forwards to another client and records the blobs it downloads and the root documents of
  /// the trees it lists.
  private class RecordingGitRemoteClient(IGitRemoteClient inner) : IGitRemoteClient {
    private readonly ConcurrentBag<string> _fetched = [];
    private readonly ConcurrentDictionary<string, byte> _rootDocuments = new();

    /// Blobs downloaded that are not a root tooth.json or manifest.json.
    public List<string> UnexpectedBlobs() => [.. _fetched.Where(id => !_rootDocuments.ContainsKey(id))];

    public Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) =>
        inner.ListRefs(repoUrl, refPrefixes);

    public async Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) {
      IReadOnlyList<GitTreeEntry> tree = await inner.ListTree(repoUrl, commitish);
      foreach (GitTreeEntry entry in tree.Where(e => e.Path is "tooth.json" or "manifest.json")) {
        _rootDocuments[entry.ObjectId] = 0;
      }

      return tree;
    }

    public Task FetchBlobs(Url repoUrl, IReadOnlyCollection<string> blobObjectIds, Func<string, Stream, Task> onBlob) {
      foreach (string id in blobObjectIds) {
        _fetched.Add(id);
      }

      return inner.FetchBlobs(repoUrl, blobObjectIds, onBlob);
    }
  }
}
