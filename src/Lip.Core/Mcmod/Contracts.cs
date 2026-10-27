using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lip.Core.Mcmod;

/// Thrown when a manifest.json is not a valid document (spec §4).
public class ManifestFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// Parses a root manifest.json (spec §4).
public interface IDocumentParser {
  /// Parses UTF-8 bytes into a RoutingDocument or ConcreteDocument. Throws
  /// ManifestFormatException when the document is invalid.
  public ManifestDocument Parse(byte[] utf8Json);
}

/// tooth.json compatibility (spec §9).
public interface IToothCompat {
  /// Converts a tooth.json to the format 4 package a ref variant selects in an environment
  /// (spec §9). version, when set, replaces the version the tooth.json declares (the tag
  /// version). Returns null when no variant applies. The version range is checked by the
  /// resolver.
  public Compat.ToothConversion? Convert(
      ToothArchiveDocument document,
      string variant,
      LipEnvironment environment,
      Semver.SemVersion? version);
}

/// Reads the root document of an archive: tooth.json if present, else manifest.json (spec §2.4).
public interface IArchiveReader {
  /// Throws ArchiveUnavailableException when the archive has neither file or the document is
  /// invalid.
  public Task<ArchiveDocument> Read(ArchiveHandle archive);
}

public record ChainHop(Ref Ref, int? ArtifactIndex);

/// A concrete package produced by Resolve, with the archive it lives in and the chain that led
/// to it (spec §8).
public record Candidate(IPackage Package, ArchiveHandle Archive, IReadOnlyList<ChainHop> Chain);

/// Diagnostics explain every path that produced no candidate.
public record ResolveResult(IReadOnlyList<Candidate> Candidates, IReadOnlyList<string> Diagnostics);

/// Resolve(ref, base, range) (spec §5.2).
public interface IResolver {
  public Task<ResolveResult> Resolve(
      Ref reference,
      ArchiveHandle baseArchive,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool toothClientCompat = false);

  /// The same candidate sequence as Resolve, produced lazily: archives (e.g. older tags of a
  /// spec ref) are opened only when the caller asks for more candidates. Diagnostics are
  /// reported through onDiagnostic as they arise. The default implementation enumerates the
  /// result of Resolve.
  public async IAsyncEnumerable<Candidate> ResolveLazy(
      Ref reference,
      ArchiveHandle baseArchive,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool toothClientCompat = false,
      Action<string>? onDiagnostic = null,
      [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    ResolveResult result = await Resolve(
        reference, baseArchive, range, environment, toothClientCompat);
    foreach (string diagnostic in result.Diagnostics) {
      onDiagnostic?.Invoke(diagnostic);
    }

    foreach (Candidate candidate in result.Candidates) {
      yield return candidate;
    }
  }
}

/// One (ref, range) of an operation's request (spec §6). Base is the workspace root archive
/// for user requests.
public record PackageRequest(Ref Ref, IVersionConstraint? Range, ArchiveHandle Base);

/// Thrown when no package set satisfies the request (spec §6).
public class DependencyResolutionException(string message, IReadOnlyList<string> diagnostics)
    : Exception(message) {
  public IReadOnlyList<string> Diagnostics { get; } = diagnostics;
}

/// A solved package set: S in install order; for every member (by index into Order) the members
/// satisfying its install edges; and for every request the member satisfying it.
public record SolvedGraph(
    IReadOnlyList<Candidate> Order,
    IReadOnlyList<IReadOnlyList<int>> Dependencies,
    IReadOnlyList<int> RequestSatisfiers);

/// Finds the package set S (spec §6).
public interface IGraphSolver {
  /// Pinned candidates (installed packages to keep) are members of S as they are; their install
  /// edges must be satisfied too. Returns S in install order (spec §7: dependencies first,
  /// cycles ordered by lowercase identity). Throws DependencyResolutionException.
  public async Task<IReadOnlyList<Candidate>> Solve(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment) =>
      (await SolveGraph(requests, pinned, environment)).Order;

  /// Like Solve, but also reports which member satisfies which install edge and request.
  public Task<SolvedGraph> SolveGraph(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment);
}

public record InstallContext(
    string WorkspaceRoot,
    LipEnvironment Environment,
    LockFile Lock,
    bool DryRun,
    bool IgnoreScripts);

/// Installs and uninstalls one package kind (spec §7).
public interface IMcmodInstaller {
  public bool CanHandle(string kind);

  /// Installs the package and returns the files it now owns (workspace-relative, '/'-separated).
  /// Fails without touching the workspace when a file is owned by another locked package.
  public Task<IReadOnlyList<string>> Install(Candidate candidate, InstallContext context);

  public Task Uninstall(LockedPackage package, InstallContext context);
}

/// Reads and writes the workspace lock (spec §8), which lives in the per-user data directory.
public interface ILockStore {
  /// Returns an empty lock when none exists; reads a lip_lock.json or migrates a v3
  /// tooth_lock.json left in the workspace by earlier versions. Throws when the lock exists but
  /// cannot be read or belongs to another workspace.
  public Task<LockFile> Load(string workspaceRoot);

  public Task Save(string workspaceRoot, LockFile lockFile);
}

public record LockFile {
  [JsonPropertyName("formatVersion")]
  public int FormatVersion { get; init; } = ManifestFormat.Version;

  [JsonPropertyName("formatUuid")]
  public string FormatUuid { get; init; } = ManifestFormat.Uuid;

  /// The absolute path of the workspace the lock belongs to. ILockStore.Save sets it; null in
  /// locks read from the workspace itself.
  [JsonPropertyName("workspace")]
  public string? Workspace { get; init; }

  /// "server" or "client" (spec §5.1). lip writes "server"; null in locks migrated from v3.
  [JsonPropertyName("platform")]
  public string? Platform { get; init; }

  [JsonPropertyName("rid")]
  public string? Rid { get; init; }

  [JsonPropertyName("packages")]
  public List<LockedPackage> Packages { get; init; } = [];
}

public record LockedHop(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("artifact")] int? Artifact);

public record LockedPackage {
  [JsonPropertyName("name")]
  public required string Identity { get; init; }

  [JsonPropertyName("version")]
  public required string Version { get; init; }

  /// "mcmod" or "tooth" (IPackage.Kind).
  [JsonPropertyName("kind")]
  public required string Kind { get; init; }

  /// Whether the user asked for the package (as opposed to a dependency).
  [JsonPropertyName("explicit")]
  public required bool Explicit { get; init; }

  [JsonPropertyName("chain")]
  public required List<LockedHop> Chain { get; init; }

  [JsonPropertyName("archiveKey")]
  public required string ArchiveKey { get; init; }

  /// Null for packages migrated from a v3 lock: reinstalling them re-resolves the tag.
  [JsonPropertyName("archive")]
  public ArchiveIdentity? Archive { get; init; }

  /// Owned files, workspace-relative and '/'-separated.
  [JsonPropertyName("files")]
  public List<string> Files { get; init; } = [];

  [JsonPropertyName("manifest")]
  public required JsonElement Manifest { get; init; }
}
