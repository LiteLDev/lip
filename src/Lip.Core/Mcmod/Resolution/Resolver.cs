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
///
/// A remote repository with a ref (@branch:, @tag:, @rev:, @version) has the single archive of the
/// commit it resolves to; only a tag "v" + version gives it a tag version. The git archive must be
/// a package whose package id equals the repository, and, with a package file, whose version
/// equals its tag version; the version of a package read from a tooth.json is its tag version,
/// whatever it declares (onWarning is told once per archive).
public class Resolver(
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    Action<string>? onWarning = null) : ICausalResolver {
  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly Action<string> _onWarning = onWarning ?? (_ => { });
  private readonly ConcurrentDictionary<string, byte> _warned = new();

  private readonly ConcurrentDictionary<string, Lazy<Task<ArchiveHandle>>> _archives = new();
  private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SpecVersion>>>>
      _versions = new();
  private readonly ConcurrentDictionary<string, Lazy<Task<PackageDocument>>> _documents = new();

  /// The builds file of the repository of the server, read once.
  private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _serverBuilds = new();

  /// Commits of refs ("spec@branch:name"), so that a branch resolves to one commit for the whole
  /// operation.
  private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _commits = new();

  /// Makes a version ref resolve to the commit a workspace recorded for it, as updating it gives
  /// the same commit even when its tag has moved since (protocol/lip-workspace.md, "Operations",
  /// "Updating").
  public void PinCommit(Ref reference, string commit) {
    if (reference is { Kind: RefKind.Spec, GitRef: { Kind: GitRefKind.Version } gitRef }) {
      _commits.TryAdd(
          $"{PackageNames.Fold(reference.Target)}@{gitRef}",
          new(() => Task.FromResult(commit.ToLowerInvariant())));
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

    // Step 1: the archives the source names.
    if (source.Kind != RefKind.Spec) {
      ArchiveHandle archive;
      try {
        archive = await Cached(
            _archives, source.Target, () => _archiveProvider.Open(source.WithVariant("")));
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report(source.ToString(), ex.Message, ex);
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
            _archives, $"{source.Target}@{version.Tag}",
            () => _archiveProvider.OpenVersion(source.Target, version));
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report($"{source.Target}@{version.Tag}", ex.Message, ex);
        continue;
      }

      if (await ResolveArchive(walk, archive, version.Version) is Candidate candidate) {
        yield return candidate;
      }
    }
  }

  /// Step 1 for a remote repository with a ref (protocol/lip-package.md, "Remote repositories"):
  /// the single archive of the commit the ref selects. Only a version tag gives it a tag version.
  private async Task<Candidate?> ResolvePinned(Walk walk, GitRef gitRef) {
    Ref source = walk.Source;
    ArchiveHandle archive;
    try {
      string commit = await Cached(
          _commits, $"{PackageNames.Fold(source.Target)}@{gitRef}",
          () => _archiveProvider.ResolveGitRef(source.Target, gitRef));
      RunTrace.Write("resolve", $"{source.Target}@{gitRef} is the commit {commit}");
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
      walk.Report(source.ToString(), ex.Message, ex);
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
      document = source.Kind == RefKind.Spec && BedrockServer.IsRepository(source.Target)
          ? await ServerDocument(source.Target, tagVersion)
          : await Cached(_documents, archive.Key, () => _archiveReader.Read(archive));
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

  /// The package that lip defines for a version of the server (protocol/lip-package.md, "The
  /// server"), whose build the builds file of the repository names. Throws
  /// ArchiveUnavailableException when the commit has no tag version, or the builds file cannot
  /// be read or names no build of the version.
  public async Task<PackageDocument> ServerDocument(string repository, SemVersion? tagVersion) {
    if (tagVersion is null) {
      throw new ArchiveUnavailableException(
          $"lip installs {BedrockServer.Repository} by version only (e.g."
          + $" '{BedrockServer.Repository}@1.21.0'), not by branch, tag or commit.");
    }

    string builds = await Cached(_serverBuilds, PackageNames.Fold(repository), async () => {
      GitRef branch = GitRef.Parse($"branch:{BedrockServer.BuildsBranch}");
      ArchiveHandle archive = await _archiveProvider.OpenCommit(
          repository, await _archiveProvider.ResolveGitRef(repository, branch));
      try {
        await using Stream stream = await archive.Source.OpenRead(BedrockServer.BuildsFileName);
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
      }
      catch (Exception ex) when (ex is not ArchiveUnavailableException
          and not OperationCanceledException) {
        throw new ArchiveUnavailableException(
            $"Could not read {BedrockServer.BuildsFileName} of {archive.Key}, which lists the"
            + " builds of the server.", ex);
      }
    });
    string build = BedrockServer.Build(builds, tagVersion)
        ?? throw new ArchiveUnavailableException(
            $"{BedrockServer.BuildsFileName} of {repository} names no build of the version"
            + $" {tagVersion} of the server.");
    RunTrace.Write("resolve", $"The server {tagVersion} is the build {build}");
    JsonElement json = BedrockServer.PackageFile(tagVersion, build);
    return new PackageDocument(PackageFileParser.Parse(json), null, json);
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
    // platform lip installs for. Whether --platform can help depends on the workspace, which is
    // not known here, so the advice is conditional.
    string platformNote = entries.Any(e => e.Side == environment.Side)
        ? $"; it does not support the platform {environment.Platform} (if the server runs on"
          + " another platform and the workspace has no packages yet, pass it with --platform)"
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
