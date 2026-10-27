using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Lip.Core.Mcmod.Compat;
using Semver;

namespace Lip.Core.Mcmod.Resolution;

/// Resolve(ref, base, range) (spec §5.2), with the tooth.json bridge of spec §9.
///
/// One instance caches archives, spec version lists and documents by archive key, so a ref
/// reached on several paths (or resolved again by the graph solver) is fetched and read once.
/// PlatformUnknownException is not "no candidate": it propagates to the caller.
///
/// A tooth.json at a tag that declares another version than the tag is a candidate with the tag
/// version, as in lip v3; onWarning is told once per archive.
public class Resolver(
    IArchiveProvider archiveProvider,
    IArchiveReader archiveReader,
    IToothCompat toothCompat,
    Action<string>? onWarning = null) : IResolver {
  private const string _arrow = " → ";

  private readonly IArchiveProvider _archiveProvider = archiveProvider;
  private readonly IArchiveReader _archiveReader = archiveReader;
  private readonly IToothCompat _toothCompat = toothCompat;
  private readonly Action<string> _onWarning = onWarning ?? (_ => { });
  private readonly ConcurrentDictionary<string, byte> _warned = new();

  private readonly ConcurrentDictionary<string, Lazy<Task<ArchiveHandle>>> _archives = new();
  private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SpecVersion>>>>
      _versions = new();
  private readonly ConcurrentDictionary<string, Lazy<Task<ArchiveDocument>>> _documents = new();

  public async Task<ResolveResult> Resolve(
      Ref reference,
      ArchiveHandle baseArchive,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool toothClientCompat = false) {
    List<Candidate> candidates = [];
    List<string> diagnostics = [];
    await foreach (Candidate candidate in ResolveLazy(
        reference, baseArchive, range, environment, toothClientCompat, diagnostics.Add)) {
      candidates.Add(candidate);
    }

    return new ResolveResult(candidates, diagnostics);
  }

  public async IAsyncEnumerable<Candidate> ResolveLazy(
      Ref reference,
      ArchiveHandle baseArchive,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool toothClientCompat = false,
      Action<string>? onDiagnostic = null,
      [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    Walk walk = new(range, environment, onDiagnostic ?? (_ => { }), cancellationToken);
    await foreach (Candidate candidate in ResolveRef(
        walk, reference, baseArchive, toothClientCompat, ChainPath.Empty)) {
      yield return candidate;
    }
  }

  /// Step 1: obtains the archive(s) a ref points to and resolves each of them.
  private async IAsyncEnumerable<Candidate> ResolveRef(
      Walk walk,
      Ref reference,
      ArchiveHandle baseArchive,
      bool toothClientCompat,
      ChainPath path) {
    walk.CancellationToken.ThrowIfCancellationRequested();
    if (path.Hops.Count + 1 > ManifestFormat.MaxChainHops) {
      walk.Report(path.Then(reference.ToString()),
          $"the chain is longer than {ManifestFormat.MaxChainHops} hops");
      yield break;
    }

    if (reference.Kind != RefKind.Spec) {
      ChainPath refPath = path.Then(reference.ToString());
      ArchiveHandle archive;
      try {
        archive = await OpenArchive(reference, baseArchive);
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report(refPath, ex.Message);
        yield break;
      }

      await foreach (Candidate candidate in ResolveArchive(
          walk, reference, archive, tagVersion: null, toothClientCompat, path, refPath)) {
        yield return candidate;
      }

      yield break;
    }

    IReadOnlyList<SpecVersion> versions;
    try {
      versions = await ListVersions(reference.Target);
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(path.Then(reference.ToString()), ex.Message);
      yield break;
    }

    List<SpecVersion> matching = [
      .. versions.Where(v => walk.Range?.IsSatisfiedBy(v.Version) ?? true),
    ];
    if (matching.Count == 0) {
      walk.Report(path.Then(reference.ToString()), versions.Count == 0
          ? "the repository has no v<SemVer> tags"
          : $"no tag version satisfies {walk.Range}");
      yield break;
    }

    foreach (SpecVersion version in matching) {
      ChainPath refPath = path.Then($"{reference.Target}@{version.Tag}"
          + (reference.Variant == "" ? "" : $"#{reference.Variant}"));
      walk.CancellationToken.ThrowIfCancellationRequested();
      ArchiveHandle archive;
      try {
        archive = await OpenVersion(reference.Target, version);
      }
      catch (ArchiveUnavailableException ex) {
        walk.Report(refPath, ex.Message);
        continue;
      }

      await foreach (Candidate candidate in ResolveArchive(
          walk, reference, archive, version.Version, toothClientCompat, path, refPath)) {
        yield return candidate;
      }
    }
  }

  /// Steps 2–4 for one archive. path holds the hops before this ref; refPath the
  /// diagnostic path including it.
  private async IAsyncEnumerable<Candidate> ResolveArchive(
      Walk walk,
      Ref reference,
      ArchiveHandle archive,
      SemVersion? tagVersion,
      bool toothClientCompat,
      ChainPath path,
      ChainPath refPath) {
    ArchiveDocument document;
    try {
      document = await ReadDocument(archive);
    }
    catch (ArchiveUnavailableException ex) {
      walk.Report(refPath, ex.Message);
      yield break;
    }

    // §9.3: a tooth.json dependency labelled "client" means the default variant of an archive
    // that uses manifest.json.
    string variant = reference.Variant;
    if (toothClientCompat && variant == "client"
        && document is RoutingArchiveDocument or ConcreteArchiveDocument) {
      variant = "";
    }

    // The chain records the ref as it was resolved, so that it reproduces the resolution.
    Ref hopRef = variant == reference.Variant ? reference : reference.WithVariant(variant);

    if (path.Visited.Contains((archive.Key, variant))) {
      walk.Report(refPath, $"cycle: ({archive.Key}, \"{variant}\") already appears in the chain");
      yield break;
    }

    // §4.3: a document at the root of a tag must carry the tag's version, if any.
    SemVersion? documentVersion = document switch {
      RoutingArchiveDocument routing => routing.Document.Version,
      ConcreteArchiveDocument concrete => concrete.Document.Version,
      _ => null,
    };
    if (tagVersion is not null && documentVersion is not null
        && !documentVersion.Equals(tagVersion)) {
      walk.Report(refPath,
          $"the document version {documentVersion} differs from the tag version {tagVersion}");
      yield break;
    }

    ImmutableHashSet<(string, string)> visited = path.Visited.Add((archive.Key, variant));

    switch (document) {
      case RoutingArchiveDocument routing: {
          await foreach (Candidate candidate in ResolveRouting(
              walk, hopRef, routing.Document, variant, archive, path, refPath, visited)) {
            yield return candidate;
          }

          break;
        }

      case ConcreteArchiveDocument concrete: {
          string? reason = RejectConcrete(walk, concrete.Document, variant);
          if (reason is not null) {
            walk.Report(refPath, reason);
            break;
          }

          yield return new Candidate(
              new McmodPackage(concrete.Document, concrete.Manifest),
              archive,
              path.Hops.Add(new ChainHop(hopRef, null)));
          break;
        }

      case ToothArchiveDocument tooth: {
          IPackage? package = _toothCompat.Select(tooth, variant, walk.Environment);
          if (package is null) {
            walk.Report(refPath,
                $"tooth.json has no variant for \"{variant}\" in this environment");
            break;
          }

          // lip v3 always used the tag version, whatever the tooth.json at the tag declares.
          if (tagVersion is not null && package is ToothPackage toothPackage
              && !toothPackage.Version.Equals(tagVersion)) {
            if (SemVersion.PrecedenceComparer.Compare(toothPackage.Version, tagVersion) != 0
                && _warned.TryAdd(archive.Key, 0)) {
              _onWarning($"{archive.Key}: tooth.json declares version {toothPackage.Version}; "
                  + $"using the tag version {tagVersion}.");
            }

            package = toothPackage.WithVersion(tagVersion);
          }

          if (walk.Range is not null && !walk.Range.IsSatisfiedBy(package.Version)) {
            walk.Report(refPath, $"version {package.Version} does not satisfy {walk.Range}");
            break;
          }

          yield return new Candidate(
              package, archive, path.Hops.Add(new ChainHop(reference, null)));
          break;
        }

      default:
        throw new InvalidOperationException($"Unknown document type: {document.GetType()}");
    }
  }

  /// Step 3: selects the most specific matching artifact and resolves its refs in order.
  private async IAsyncEnumerable<Candidate> ResolveRouting(
      Walk walk,
      Ref reference,
      RoutingDocument document,
      string variant,
      ArchiveHandle archive,
      ChainPath path,
      ChainPath refPath,
      ImmutableHashSet<(string, string)> visited) {
    List<(Artifact Artifact, int Index)> matching = [
      .. document.Artifacts
          .Select((artifact, index) => (artifact, index))
          .Where(a => a.artifact.Variant == variant)
          // The rid is checked first so that an unknown platform only fails when it decides.
          .Where(a => a.artifact.Rids?.Contains(walk.Environment.Rid) ?? true)
          .Where(a => a.artifact.Matches(walk.Environment)),
    ];
    if (matching.Count == 0) {
      walk.Report(refPath, $"no artifact for variant \"{variant}\" matches the environment "
          + $"({walk.Environment.Platform?.ToSpecString() ?? "unknown platform"}, "
          + $"{walk.Environment.Rid})");
      yield break;
    }

    int most = matching.Max(a => a.Artifact.SelectorCount);
    List<(Artifact Artifact, int Index)> best = [
      .. matching.Where(a => a.Artifact.SelectorCount == most),
    ];
    if (best.Count > 1) {
      walk.Report(refPath, "artifacts "
          + string.Join(", ", best.Select(a => a.Index))
          + " match the environment equally well");
      yield break;
    }

    (Artifact selected, int selectedIndex) = best[0];

    List<Ref> refs = [];
    foreach (string template in selected.Refs) {
      try {
        refs.Add(RefTemplate.Expand(template, document, walk.Environment));
      }
      catch (FormatException ex) {
        walk.Report(refPath, $"invalid document: {ex.Message}");
        yield break;
      }
    }

    ChainPath next = new(
        path.Hops.Add(new ChainHop(reference, selectedIndex)),
        visited,
        refPath.Labels.SetItem(refPath.Labels.Count - 1,
            $"{refPath.Labels[^1]}[artifact {selectedIndex}]"));
    foreach (Ref r in refs) {
      await foreach (Candidate candidate in ResolveRef(
          walk, r, archive, toothClientCompat: false, next)) {
        yield return candidate;
      }
    }
  }

  /// Step 4: returns why a concrete document is not a candidate, or null when it is. The
  /// platform is consulted last so that an unknown platform only fails when it decides.
  private static string? RejectConcrete(Walk walk, ConcreteDocument document, string variant) {
    if (variant != "") {
      return $"{document.Name} is a concrete package and has no variant \"{variant}\"";
    }

    if (walk.Range is not null && !walk.Range.IsSatisfiedBy(document.Version)) {
      return $"version {document.Version} does not satisfy {walk.Range}";
    }

    if (document.Rid is not null && document.Rid != walk.Environment.Rid) {
      return $"rid {document.Rid} differs from the environment rid {walk.Environment.Rid}";
    }

    if (document.Platform != PackagePlatform.Universal) {
      McmodPlatform platform = walk.Environment.RequirePlatform();
      bool matches = document.Platform == PackagePlatform.Server
          ? platform == McmodPlatform.Server
          : platform == McmodPlatform.Client;
      if (!matches) {
        return $"platform {document.Platform.ToString().ToLowerInvariant()} differs from the "
            + $"environment platform {platform.ToSpecString()}";
      }
    }

    return null;
  }

  private Task<ArchiveHandle> OpenArchive(Ref reference, ArchiveHandle baseArchive) {
    string cacheKey = reference.Kind == RefKind.Relative
        ? $"{baseArchive.Key}!{reference.Target}"
        : reference.Target;
    return Cached(_archives, cacheKey, () => _archiveProvider.Open(reference, baseArchive));
  }

  private Task<IReadOnlyList<SpecVersion>> ListVersions(string spec) =>
      Cached(_versions, spec, () => _archiveProvider.ListVersions(spec));

  private Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version) => Cached(
      _archives, $"{spec}@{version.Tag}", () => _archiveProvider.OpenVersion(spec, version));

  private Task<ArchiveDocument> ReadDocument(ArchiveHandle archive) =>
      Cached(_documents, archive.Key, () => _archiveReader.Read(archive));

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
      IVersionConstraint? Range,
      LipEnvironment Environment,
      Action<string> Sink,
      CancellationToken CancellationToken) {
    public void Report(ChainPath path, string reason) => Sink(
        $"{string.Join(_arrow, path.Labels)}: {reason}");
  }

  /// The chain so far: hops before the current ref, the (archive key, variant) pairs they
  /// visited, and human-readable labels for diagnostics.
  private sealed record ChainPath(
      ImmutableList<ChainHop> Hops,
      ImmutableHashSet<(string, string)> Visited,
      ImmutableList<string> Labels) {
    public static readonly ChainPath Empty = new([], [], []);

    public ChainPath Then(string label) => this with { Labels = Labels.Add(label) };
  }
}
