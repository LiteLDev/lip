using System.Collections.Concurrent;
using Semver;

namespace Lip.Core.Mcmod.Resolution;

/// Resolve(source, range, environment) (protocol/package.md §4), reading an archive without a
/// package file as protocol/compatibility.md §2 does.
///
/// One instance caches archives, version lists and package files by archive key, so a source
/// reached on several paths (or resolved again by the graph solver) is fetched and read once.
///
/// A remote repository with a ref (@branch:, @tag:, @rev:, @version) has the single archive of the
/// commit it resolves to; only a tag "v" + version gives it a tag version. The git archive must be
/// a package whose package id equals the repository, and, with a package file, whose version
/// equals its tag version; the version of a package read from a tooth.json is its tag version,
/// whatever it declares (onWarning is told once per archive).
public class Resolver(
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    Action<string>? onWarning = null) : IResolver {
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly Action<string> _onWarning = onWarning ?? (_ => { });
  private readonly ConcurrentDictionary<string, byte> _warned = new();

  private readonly ConcurrentDictionary<string, Lazy<Task<ArchiveHandle>>> _archives = new();
  private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SpecVersion>>>>
      _versions = new();
  private readonly ConcurrentDictionary<string, Lazy<Task<PackageDocument>>> _documents = new();

  /// Commits of refs ("spec@branch:name"), so that a branch resolves to one commit for the whole
  /// operation.
  private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _commits = new();

  /// Makes a version ref resolve to the commit a workspace recorded for it, as updating it gives
  /// the same commit even when its tag has moved since (protocol/workspace.md §5, "Updating").
  public void PinCommit(Ref reference, string commit) {
    if (reference is { Kind: RefKind.Spec, GitRef: { Kind: GitRefKind.Version } gitRef }) {
      _commits.TryAdd(
          $"{PackageNames.Fold(reference.Target)}@{gitRef}",
          new(() => Task.FromResult(commit.ToLowerInvariant())));
    }
  }

  public async IAsyncEnumerable<Candidate> ResolveLazy(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool clientCompat = false,
      Action<string>? onDiagnostic = null) {
    Walk walk = new(source, range, environment, clientCompat, onDiagnostic ?? (_ => { }));

    // Step 1: the archives the source names.
    if (source.Kind != RefKind.Spec) {
      ArchiveHandle archive;
      try {
        archive = await Cached(
            _archives, source.Target, () => _archiveProvider.Open(source.WithVariant("")));
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report(source.ToString(), ex.Message);
        yield break;
      }

      if (await ResolveArchive(walk, archive, tagVersion: null) is Candidate candidate) {
        yield return candidate;
      }

      yield break;
    }

    if (source.GitRef is GitRef gitRef) {
      if (await ResolvePinned(walk, gitRef) is Candidate candidate) {
        yield return candidate;
      }

      yield break;
    }

    IReadOnlyList<SpecVersion> versions;
    try {
      versions = await Cached(
          _versions, source.Target, () => _archiveProvider.ListVersions(source.Target));
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(source.ToString(), ex.Message);
      yield break;
    }

    List<SpecVersion> matching = [.. versions.Where(v => range?.IsSatisfiedBy(v.Version) ?? true)];
    if (matching.Count == 0) {
      walk.Report(source.ToString(), versions.Count == 0
          ? "the repository has no version tags"
          : $"no tag version satisfies {range}");
      yield break;
    }

    foreach (SpecVersion version in matching) {
      ArchiveHandle archive;
      try {
        archive = await Cached(
            _archives, $"{source.Target}@{version.Tag}",
            () => _archiveProvider.OpenVersion(source.Target, version));
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report($"{source.Target}@{version.Tag}", ex.Message);
        continue;
      }

      if (await ResolveArchive(walk, archive, version.Version) is Candidate candidate) {
        yield return candidate;
      }
    }
  }

  /// Step 1 for a remote repository with a ref (protocol/package.md §2.1): the single archive of
  /// the commit the ref selects. Only a version tag gives it a tag version.
  private async Task<Candidate?> ResolvePinned(Walk walk, GitRef gitRef) {
    Ref source = walk.Source;
    ArchiveHandle archive;
    try {
      string commit = await Cached(
          _commits, $"{PackageNames.Fold(source.Target)}@{gitRef}",
          () => _archiveProvider.ResolveGitRef(source.Target, gitRef));
      // The archive of a version ref is named like that of the same version without a ref.
      archive = gitRef.Kind == GitRefKind.Version
          ? await Cached(
              _archives, $"{source.Target}@{gitRef} {commit}",
              async () => (await _archiveProvider.OpenCommit(source.Target, commit)) with {
                Key = $"{source.Target}@{gitRef}",
              })
          : await Cached(
              _archives, $"{source.Target}@rev:{commit}",
              () => _archiveProvider.OpenCommit(source.Target, commit));
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(source.ToString(), ex.Message);
      return null;
    }

    SemVersion? tagVersion = gitRef.Kind switch {
      GitRefKind.Version => SemVersion.Parse(gitRef.Name, SemVersionStyles.Strict),
      GitRefKind.Tag => ArchiveProvider.ParseVersionTag($"refs/tags/{gitRef.Name}"),
      _ => null,
    };
    return await ResolveArchive(walk, archive, tagVersion);
  }

  /// Step 2 for one archive: the candidate of the package, or null.
  private async Task<Candidate?> ResolveArchive(
      Walk walk, ArchiveHandle archive, SemVersion? tagVersion) {
    Ref source = walk.Source;
    PackageDocument document;
    try {
      document = await Cached(_documents, archive.Key, () => _archiveReader.Read(archive));
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(archive.Key, ex.Message);
      return null;
    }

    // protocol/package.md §2.1: the git archive of a remote repository is a package of its own
    // package id, whose version is the tag version, if any.
    string fileName = document.IsLegacy ? Compat.ToothCompat.ToothFileName : PackageFormat.FileName;
    if (source.Kind == RefKind.Spec && !PackageNames.AreEqual(document.Id, source.Target)) {
      walk.Report(archive.Key,
          $"its {fileName} declares the package id {document.Id}, not {source.Target}");
      return null;
    }

    SemVersion version = document.Version;
    if (tagVersion is not null && !version.Equals(tagVersion)) {
      if (!document.IsLegacy) {
        walk.Report(archive.Key,
            $"its {fileName} declares the version {version}, not the tag version {tagVersion}");
        return null;
      }

      // protocol/compatibility.md §2: the version of a package read from a tooth.json is its tag
      // version.
      if (SemVersion.PrecedenceComparer.Compare(version, tagVersion) != 0
          && _warned.TryAdd(archive.Key, 0)) {
        _onWarning($"{archive.Key}: tooth.json declares version {version}; "
            + $"using the tag version {tagVersion}.");
      }

      version = tagVersion;
    }

    PackageFile packageFile;
    try {
      packageFile = document.ReadFor(walk.Environment, version);
    }
    catch (PackageFormatException ex) {
      walk.Report(archive.Key, ex.Message);
      return null;
    }

    // protocol/compatibility.md §2: a dependency of a tooth.json on the variant "client" of a
    // package that has a package file names its variant "".
    string variant = source.Variant;
    if (walk.ClientCompat && variant == Compat.ToothCompat.ClientLabel && !document.IsLegacy) {
      variant = "";
    }

    PackageEntry? entry = packageFile.SelectEntry(variant, walk.Environment);
    if (entry is null) {
      walk.Report(archive.Key, $"{fileName} has no entry of the variant \"{variant}\" for "
          + $"({walk.Environment.Platform.ToSpecString()}, {walk.Environment.Runtime})");
      return null;
    }

    if (walk.Range is not null && !walk.Range.IsSatisfiedBy(version)) {
      walk.Report(archive.Key, $"version {version} does not satisfy {walk.Range}");
      return null;
    }

    return Candidate.Create(
        new Package(packageFile.Id, variant, version, entry) { Manifest = packageFile.Json },
        archive,
        source,
        _archiveProvider);
  }

  /// Shares one task per key. Failed tasks are evicted so that a transient failure is retried
  /// by the next caller.
  private static async Task<T> Cached<T>(
      ConcurrentDictionary<string, Lazy<Task<T>>> cache,
      string key,
      Func<Task<T>> factory) {
    Lazy<Task<T>> entry = cache.GetOrAdd(key, _ => new Lazy<Task<T>>(factory));
    try {
      return await entry.Value;
    }
    catch {
      cache.TryRemove(new KeyValuePair<string, Lazy<Task<T>>>(key, entry));
      throw;
    }
  }

  /// The parameters shared by a whole Resolve call.
  private sealed record Walk(
      Ref Source,
      IVersionConstraint? Range,
      LipEnvironment Environment,
      bool ClientCompat,
      Action<string> Sink) {
    public void Report(string archive, string reason) => Sink($"{archive}: {reason}");
  }
}
