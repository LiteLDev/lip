using System.Collections.Concurrent;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Semver;

namespace Lip.Core.Mcmod.Resolution;

/// Why one path of a Resolve call produced no candidate: the text of the diagnostic, and the
/// exception behind it, if any (e.g. the network failure of a download).
public record ResolveDiagnostic(string Message, Exception? Cause);

/// A resolver that also hands over the exception behind each diagnostic, so that a failure can
/// keep it as its cause.
public interface ICausalResolver : IResolver {
  public IAsyncEnumerable<Candidate> ResolveLazyWithCauses(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      Action<ResolveDiagnostic> onDiagnostic);
}

/// Resolve(source, range, environment) (protocol/lip-package.md, "Resolution"), reading an archive
/// without a package file as protocol/lip-compatibility.md, "tooth.json" does.
///
/// One instance caches archives, version lists and package files by archive key, so a source
/// reached on several paths (or resolved again by the graph solver) is fetched and read once.
/// The resolvers of one session (PublicApi.OperationSession) are given the same ResolverCache:
/// what one read of the network, the builds list and the remote repositories, the others do not
/// read again. What is read of a URL or a local path, and the commits pinned, are of one
/// resolver only.
///
/// A remote repository with a ref (@branch:, @tag:, @rev:, @version) has the single archive of the
/// commit it resolves to; only a tag "v" + version gives it a tag version. The git archive must be
/// a package whose package id equals the repository, and, with a package file, whose version
/// equals its tag version; the version of a package read from a tooth.json is its tag version,
/// whatever it declares (onWarning is told once per archive).
public class Resolver(
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    Action<string>? onWarning = null,
    Func<Task<string>>? downloadServerBuilds = null,
    ResolverCache? cache = null) : ICausalResolver {
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly Action<string> _onWarning = onWarning ?? (_ => { });
  private readonly ConcurrentDictionary<string, byte> _warned = new();

  /// What was read of the remote repositories and of the server, which a session shares.
  private readonly ResolverCache _remote = cache ?? new ResolverCache();

  /// What was read of the URLs and the local paths, whose archives the archive provider of
  /// this resolver opened: a local path may hold something else the next time.
  private readonly ResolverCache _local = new();

  /// The builds list of the server (protocol/lip-package.md, "The server"), downloaded once.
  private readonly Func<Task<string>> _downloadServerBuilds =
      downloadServerBuilds ?? BedrockServer.Download;

  /// The commits that version refs are pinned to (PinCommit).
  private readonly ConcurrentDictionary<string, string> _pinned = new();

  /// Makes a version ref resolve to the commit a workspace recorded for it, as updating it gives
  /// the same commit even when its tag has moved since (protocol/lip-workspace.md, "Operations",
  /// "Updating").
  public void PinCommit(Ref reference, string commit) {
    if (reference is { Kind: RefKind.Spec, GitRef: { Kind: GitRefKind.Version } gitRef }) {
      _pinned.TryAdd($"{PackageNames.Fold(reference.Target)}@{gitRef}", commit.ToLowerInvariant());
    }
  }

  public IAsyncEnumerable<Candidate> ResolveLazy(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      Action<string>? onDiagnostic = null) {
    return ResolveLazyWithCauses(
        source, range, environment, d => onDiagnostic?.Invoke(d.Message));
  }

  /// ResolveLazy, reporting with each diagnostic the exception behind it, so that a failure can
  /// keep it as its cause.
  public async IAsyncEnumerable<Candidate> ResolveLazyWithCauses(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      Action<ResolveDiagnostic> onDiagnostic) {
    Walk walk = new(source, range, environment, onDiagnostic);
    RunTrace.Write("resolve", $"Resolving {source} {range?.ToString() ?? "(any version)"}"
        + $" for ({environment.Side.ToSpecString()}, {environment.Platform})");

    if (BedrockServer.IsServer(source)) {
      await foreach (Candidate candidate in ResolveServer(walk)) {
        yield return candidate;
      }

      yield break;
    }

    // Step 1: the archives the source names.
    if (source.Kind != RefKind.Spec) {
      ArchiveHandle archive;
      try {
        archive = await Cached(
            _local.Archives, source.Target, () => _archiveProvider.Open(source.WithVariant("")));
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report(source.ToString(), ex.Message, ex);
        yield break;
      }

      if (await ResolveArchive(walk, archive, tagVersion: null, _local) is Candidate candidate) {
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
          _remote.Versions, source.Target, () => _archiveProvider.ListVersions(source.Target));
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(source.ToString(), ex.Message, ex);
      yield break;
    }

    RunTrace.Write("resolve", $"{source.Target} has {versions.Count} version tags"
        + (versions.Count == 0 ? "" : $": {string.Join(", ", versions.Select(v => v.Tag))}"));
    List<SpecVersion> matching = [.. versions.Where(v => range?.IsSatisfiedBy(v.Version) ?? true)];
    if (matching.Count == 0) {
      walk.Report(source.ToString(), versions.Count == 0
          ? "the repository has no version tags (tags named like 'v1.2.3'), so it has no version"
              + " to install; a branch, tag or commit can be named with '@branch:<name>',"
              + " '@tag:<name>' or '@rev:<commit>'"
          : $"no version tag satisfies {range}; the repository has the versions"
              + $" {ListVersions(versions)}");
      yield break;
    }

    foreach (SpecVersion version in matching) {
      ArchiveHandle archive;
      try {
        archive = await Cached(
            _remote.Archives, $"{source.Target}@{version.Tag}",
            () => _archiveProvider.OpenVersion(source.Target, version));
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report($"{source.Target}@{version.Tag}", ex.Message, ex);
        continue;
      }

      if (await ResolveArchive(walk, archive, version.Version, _remote) is Candidate candidate) {
        yield return candidate;
      }
    }
  }

  /// The builds list of the server. Throws ArchiveUnavailableException when it cannot be
  /// obtained.
  public Task<BedrockServer> Server() => Cached(
      _remote.Server, "", async () => BedrockServer.Parse(await _downloadServerBuilds()));

  /// The candidates of the server (protocol/lip-package.md, "The server"): the packages lip
  /// defines for the versions of the builds list, from highest to lowest.
  private async IAsyncEnumerable<Candidate> ResolveServer(Walk walk) {
    Ref source = walk.Source;
    if (source.GitRef is { Kind: not GitRefKind.Version }) {
      walk.Report(source.ToString(),
          $"lip installs {BedrockServer.Repository} by version only (e.g."
          + $" '{BedrockServer.Repository}@1.21.0'), not by branch, tag or commit");
      yield break;
    }

    BedrockServer server;
    try {
      server = await Server();
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(source.ToString(), ex.Message, ex);
      yield break;
    }

    SemVersion? only = source.GitRef is GitRef gitRef
        ? SemVersion.Parse(gitRef.Name, SemVersionStyles.Strict)
        : null;
    List<SemVersion> matching = [
      .. server.Versions.Where(v => (only is null || v.Equals(only))
          && (walk.Range?.IsSatisfiedBy(v) ?? true)),
    ];
    if (matching.Count == 0) {
      walk.Report(source.ToString(),
          $"the server has no version {(only is null ? $"that satisfies {walk.Range}" : only)};"
          + " it has the versions " + string.Join(", ", server.ReleaseVersions.Take(10))
          + (server.ReleaseVersions.Count() > 10
              ? $" and {server.ReleaseVersions.Count() - 10} older ones"
              : "")
          + " (newest first)");
      yield break;
    }

    foreach (SemVersion version in matching) {
      JsonElement json = server.PackageFile(version);
      PackageFile packageFile = PackageFileParser.Parse(json);
      ArchiveHandle archive = BedrockServer.Archive(version);
      PackageEntry? entry = packageFile.SelectEntry(source.Variant, walk.Environment);
      if (entry is null) {
        ReportNoEntry(walk, archive, packageFile, "package file", source.Variant);
        continue;
      }

      RunTrace.Write("resolve", $"Candidate {packageFile.Id}@{version}, defined by lip");
      yield return Candidate.Create(
          new Package(packageFile.Id, source.Variant, version, entry) { Manifest = json },
          archive,
          source,
          _archiveProvider);
    }
  }

  /// Step 1 for a remote repository with a ref (protocol/lip-package.md, "Remote repositories"):
  /// the single archive of the commit the ref selects. Only a version tag gives it a tag version.
  private async Task<Candidate?> ResolvePinned(Walk walk, GitRef gitRef) {
    Ref source = walk.Source;
    ArchiveHandle archive;
    try {
      string key = $"{PackageNames.Fold(source.Target)}@{gitRef}";
      // A branch resolves to one commit for the whole operation, and for the whole session.
      string commit = _pinned.TryGetValue(key, out string? pinned)
          ? pinned
          : await Cached(
              _remote.Commits, key, () => _archiveProvider.ResolveGitRef(source.Target, gitRef));
      RunTrace.Write("resolve", $"{source.Target}@{gitRef} is the commit {commit}");
      // The archive of a version ref is named like that of the same version without a ref.
      archive = gitRef.Kind == GitRefKind.Version
          ? await Cached(
              _remote.Archives, $"{source.Target}@{gitRef} {commit}",
              async () => (await _archiveProvider.OpenCommit(source.Target, commit)) with {
                Key = $"{source.Target}@{gitRef}",
              })
          : await Cached(
              _remote.Archives, $"{source.Target}@rev:{commit}",
              () => _archiveProvider.OpenCommit(source.Target, commit));
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(source.ToString(), ex.Message, ex);
      return null;
    }

    SemVersion? tagVersion = gitRef.Kind switch {
      GitRefKind.Version => SemVersion.Parse(gitRef.Name, SemVersionStyles.Strict),
      GitRefKind.Tag => ArchiveProvider.ParseVersionTag($"refs/tags/{gitRef.Name}"),
      _ => null,
    };
    return await ResolveArchive(walk, archive, tagVersion, _remote);
  }

  /// Step 2 for one archive: the candidate of the package, or null. The package file of the
  /// archive is read once per cache.
  private async Task<Candidate?> ResolveArchive(
      Walk walk, ArchiveHandle archive, SemVersion? tagVersion, ResolverCache cache) {
    Ref source = walk.Source;
    PackageDocument document;
    try {
      // A version ref names its archive like the version, whatever commit it is pinned to.
      document = await Cached(
          cache.Documents,
          archive.Commit is null ? archive.Key : $"{archive.Key} {archive.Commit}",
          () => _archiveReader.Read(archive));
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(archive.Key, ex.Message, ex);
      return null;
    }

    // protocol/lip-package.md, "Remote repositories": the git archive of a remote repository is a
    // package of its own package id, whose version is the tag version, if any.
    string fileName = document.IsLegacy ? Compat.ToothCompat.ToothFileName : PackageFormat.FileName;
    if (source.Kind == RefKind.Spec && !PackageNames.AreEqual(document.Id, source.Target)) {
      walk.Report(archive.Key,
          $"its {fileName} declares the package id {document.Id}, not {source.Target}, so it"
          + " is not a package of this repository");
      return null;
    }

    SemVersion version = document.Version;
    if (tagVersion is not null && !version.Equals(tagVersion)) {
      if (!document.IsLegacy) {
        walk.Report(archive.Key,
            $"its {fileName} declares the version {version}, not the tag version {tagVersion};"
            + " the package author must make them match");
        return null;
      }

      // protocol/lip-compatibility.md, "tooth.json": the version of a package read from a
      // tooth.json is its tag version.
      if (SemVersion.PrecedenceComparer.Compare(version, tagVersion) != 0
          && _warned.TryAdd(archive.Key, 0)) {
        _onWarning($"{archive.Key}: its tooth.json declares the version {version}, but it is"
            + $" tagged as version {tagVersion}; using the tag version {tagVersion}.");
      }

      version = tagVersion;
    }

    PackageFile packageFile;
    try {
      packageFile = document.ReadFor(walk.Environment, version);
    }
    catch (PackageFormatException ex) {
      walk.Report(archive.Key, ex.Message, ex);
      return null;
    }

    string variant = source.Variant;
    PackageEntry? entry = packageFile.SelectEntry(variant, walk.Environment);
    if (entry is null) {
      ReportNoEntry(walk, archive, packageFile, fileName, variant);
      return null;
    }

    if (walk.Range is not null && !walk.Range.IsSatisfiedBy(version)) {
      walk.Report(archive.Key, $"version {version} does not satisfy {walk.Range}");
      return null;
    }

    RunTrace.Write("resolve", $"Candidate {packageFile.Id}"
        + (variant == "" ? "" : $"#{variant}") + $"@{version} from {archive.Key}");
    return Candidate.Create(
        new Package(packageFile.Id, variant, version, entry) { Manifest = packageFile.Json },
        archive,
        source,
        _archiveProvider);
  }

  /// Reports that a package file (named fileName) has no entry of a variant for the environment,
  /// with the entries it has instead.
  private static void ReportNoEntry(
      Walk walk, ArchiveHandle archive, PackageFile packageFile, string fileName, string variant) {
    LipEnvironment environment = walk.Environment;
    string variantName = variant == "" ? "the default variant" : $"the variant \"{variant}\"";
    string wanted = $"no entry of {variantName} for the side"
        + $" {environment.Side.ToSpecString()} and the platform {environment.Platform}";
    if (!packageFile.Variants.TryGetValue(variant, out IReadOnlyList<PackageEntry>? entries)
        || entries.Count == 0) {
      walk.Report(archive.Key, packageFile.Variants.Count == 0
          ? $"its {fileName} has {wanted}; it has no entries at all"
          : $"its {fileName} has {wanted}; its variants are "
              + string.Join(", ", packageFile.Variants.Keys.Select(
                  v => v == "" ? "the default variant" : $"\"{v}\"")));
      return;
    }

    string available = string.Join(", ", entries.Select(
        e => $"({e.Side.ToSpecString()}, {e.Platform})"));
    // An entry for another platform of the same side: the package does not support the
    // platform lip installs for, the only one (protocol/lip.md, "Environment").
    string platformNote = entries.Any(e => e.Side == environment.Side)
        ? $"; it does not support the platform {environment.Platform}, the only platform lip"
          + " installs packages for"
        : "";
    walk.Report(
        archive.Key, $"its {fileName} has {wanted}; it has entries for {available}{platformNote}");
  }

  /// The versions of a version list, newest first, cut short when long.
  private static string ListVersions(IReadOnlyList<SpecVersion> versions) {
    const int Shown = 10;
    List<string> shown = [.. versions.Select(v => v.Version.ToString()).Distinct()];
    return string.Join(", ", shown.Take(Shown))
        + (shown.Count > Shown ? $" and {shown.Count - Shown} older ones" : "")
        + " (newest first)";
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
      Action<ResolveDiagnostic> Sink) {
    public void Report(string archive, string reason, Exception? cause = null) {
      RunTrace.Write("resolve", $"Rejected {archive} for {Source}: {reason}");
      Sink(new ResolveDiagnostic($"{archive}: {reason}", cause));
    }
  }
}

/// What the resolvers of one session (PublicApi.OperationSession) share: the archives, the
/// version lists and the package files read of the remote repositories, the commits that their
/// branches and tags were resolved to, and the builds list of the server.
public sealed class ResolverCache {
  internal ConcurrentDictionary<string, Lazy<Task<ArchiveHandle>>> Archives { get; } = new();

  internal ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SpecVersion>>>> Versions {
    get;
  } = new();

  internal ConcurrentDictionary<string, Lazy<Task<PackageDocument>>> Documents { get; } = new();

  internal ConcurrentDictionary<string, Lazy<Task<BedrockServer>>> Server { get; } = new();

  /// By "spec@ref", the commit that the ref was resolved to.
  internal ConcurrentDictionary<string, Lazy<Task<string>>> Commits { get; } = new();
}
