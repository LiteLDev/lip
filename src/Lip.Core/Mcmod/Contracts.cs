using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// An asset of a candidate (protocol/workspace.md §4 step 2), opened on first use: the archive
/// of the package for "self", otherwise downloaded, or opened from the asset identifier the
/// workspace file records.
public sealed class AssetHandle {
  private readonly Lazy<Task<(ISource Source, AssetIdentity? Identity)>> _open;

  private AssetHandle(
      AssetType type,
      AssetIdentity? recorded,
      Func<Task<(ISource, AssetIdentity?)>> open) {
    Type = type;
    Recorded = recorded;
    _open = new(open);
  }

  public AssetType Type { get; }

  /// The asset identifier the workspace file records for it, or null.
  public AssetIdentity? Recorded { get; }

  /// The archive of the package itself.
  public static AssetHandle Self(ArchiveHandle archive) =>
      new(AssetType.Self, null, () => Task.FromResult<(ISource, AssetIdentity?)>(
          (archive.Source, null)));

  /// An asset downloaded from the first of its URLs that can be obtained.
  public static AssetHandle Download(
      IArchiveProvider provider, EntryAsset asset, AssetFormat format) =>
      new(asset.Type, null, async () => {
        OpenedAsset opened = await provider.OpenAsset(asset.Urls, format);
        return (opened.Source, opened.Identity);
      });

  /// An asset the workspace file records, verified against its identifier.
  public static AssetHandle Locked(
      IArchiveProvider provider, AssetType type, AssetIdentity identity, AssetFormat format) =>
      new(type, identity, async () => (await provider.OpenLockedAsset(identity, format), identity));

  /// The format an asset of an entry is downloaded as.
  public static AssetFormat FormatOf(EntryAsset asset, bool legacy) => asset.Type switch {
    AssetType.Uncompressed => AssetFormat.LegacyFile,
    _ when legacy => AssetFormat.LegacyArchive,
    _ => AssetFormat.Zip,
  };

  /// The files of the asset. Throws ArchiveUnavailableException when it cannot be obtained.
  public async Task<ISource> Open() => (await OpenCore()).Source;

  /// The asset identifier (null for "self"), downloading the asset when it is not recorded.
  public async Task<AssetIdentity?> GetIdentity() =>
      Type == AssetType.Self ? null : Recorded ?? (await OpenCore()).Identity;

  private Task<(ISource Source, AssetIdentity? Identity)> OpenCore() => _open.Value;
}

/// A candidate (protocol/package.md §4): a package with its selected entry, the archive it is
/// in, the source it was resolved for, and the assets of its entry, in order.
public record Candidate(
    Package Package,
    ArchiveHandle Archive,
    Ref Source,
    IReadOnlyList<AssetHandle> Assets) {
  private Task<ModManifest?>? _modManifest;

  /// The mod manifest of a mod (protocol/package.md §4): the manifest.json at the root of the
  /// archive of its asset of type "mcmod"; null for a package that is not a mod. Obtains that
  /// asset. Throws McmodInstallException when the asset cannot be obtained or the mod manifest
  /// is missing or invalid.
  public Task<ModManifest?> GetModManifest() => _modManifest ??= LoadModManifest();

  private async Task<ModManifest?> LoadModManifest() {
    if (Package.Entry.McmodAsset is not int index) {
      return null;
    }

    string name = Package.Identity;
    try {
      ISource source = await Assets[index].Open();
      if (!source.Keys.Contains(ToothFormat.ModManifestFileName)) {
        throw new McmodInstallException(
            $"{name}: the asset of type \"mcmod\" has no {ToothFormat.ModManifestFileName}.");
      }

      await using Stream stream = await source.OpenRead(ToothFormat.ModManifestFileName);
      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      using JsonDocument json = ToothParser.ParseJson(memory.ToArray());
      JsonElement root = json.RootElement;
      if (root.ValueKind != JsonValueKind.Object
          || !root.TryGetProperty("name", out JsonElement modName)
          || modName.ValueKind != JsonValueKind.String
          || !PackageNames.IsValid(modName.GetString()!)) {
        throw new McmodInstallException(
            $"{name}: the {ToothFormat.ModManifestFileName} of its asset of type \"mcmod\" is not"
            + " a JSON object whose \"name\" is an id.");
      }

      return new ModManifest(modName.GetString()!, root.Clone());
    }
    catch (Exception ex) when (ex is ArchiveUnavailableException or FormatException
        or IOException or InvalidDataException) {
      throw new McmodInstallException(
          $"{name}: cannot read the mod manifest of its asset of type \"mcmod\": {ex.Message}",
          ex);
    }
  }

  /// The candidate of a package in an archive, whose assets are downloaded when first needed.
  public static Candidate Create(
      Package package, ArchiveHandle archive, Ref source, IArchiveProvider provider) => new(
          package,
          archive,
          source,
          [
            .. package.Entry.Assets.Select(a => a.Type == AssetType.Self
                ? AssetHandle.Self(archive)
                : AssetHandle.Download(
                    provider, a, AssetHandle.FormatOf(a, package.Entry.Legacy))),
          ]);
}

/// The mod manifest of a mod (protocol/package.md §4) and its mod name, its "name".
public record ModManifest(string Name, JsonElement Json);

/// Diagnostics explain every path that produced no candidate.
public record ResolveResult(IReadOnlyList<Candidate> Candidates, IReadOnlyList<string> Diagnostics);

/// Resolve(source, range, environment) (protocol/package.md §4).
public interface IResolver {
  /// clientCompat: the source is the key of a dependency of an entry derived from format 3,
  /// whose variant "client" names the variant "" of a package of format 4 (protocol/package.md
  /// §5).
  public Task<ResolveResult> Resolve(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool clientCompat = false);

  /// The same candidate sequence as Resolve, produced lazily: archives (e.g. older tags of a
  /// remote repository) are opened only when the caller asks for more candidates. Diagnostics
  /// are reported through onDiagnostic as they arise. The default implementation enumerates the
  /// result of Resolve.
  public async IAsyncEnumerable<Candidate> ResolveLazy(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool clientCompat = false,
      Action<string>? onDiagnostic = null,
      [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    ResolveResult result = await Resolve(source, range, environment, clientCompat);
    foreach (string diagnostic in result.Diagnostics) {
      onDiagnostic?.Invoke(diagnostic);
    }

    foreach (Candidate candidate in result.Candidates) {
      yield return candidate;
    }
  }
}

/// A request (protocol/workspace.md §1): a source and a range.
public record PackageRequest(Ref Ref, IVersionConstraint? Range);

/// Thrown when no package set satisfies the requests (protocol/workspace.md §3).
public class DependencyResolutionException(string message, IReadOnlyList<string> diagnostics)
    : Exception(message) {
  public IReadOnlyList<string> Diagnostics { get; } = diagnostics;
}

/// A solved package set: S in installation order; for every member (by index into Order) the
/// members satisfying its dependencies; and for every request the member satisfying it.
public record SolvedGraph(
    IReadOnlyList<Candidate> Order,
    IReadOnlyList<IReadOnlyList<int>> Dependencies,
    IReadOnlyList<int> RequestSatisfiers);

/// Finds the package set S (protocol/workspace.md §3).
public interface IGraphSolver {
  /// Pinned candidates (installed packages to keep) are members of S as they are; their
  /// dependencies must be satisfied too. Returns S in installation order (protocol/workspace.md
  /// §5). Throws DependencyResolutionException.
  public async Task<IReadOnlyList<Candidate>> Solve(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment) =>
      (await SolveGraph(requests, pinned, environment)).Order;

  /// Like Solve, but also reports which member satisfies which dependency and request.
  public Task<SolvedGraph> SolveGraph(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment);
}

/// A package installed in the workspace once an operation is complete, with its mod name (null
/// for a file package).
public record InstalledMember(string Identity, SemVersion Version, string? ModName);

/// Members: the packages installed in the workspace once the operation is complete, which decide
/// the dependencies of a mod manifest (protocol/workspace.md §4 step 3).
public record InstallContext(
    string WorkspaceRoot,
    LipEnvironment Environment,
    LockFile Lock,
    bool DryRun,
    bool IgnoreScripts,
    IReadOnlyList<InstalledMember>? Members = null);

/// Installs and uninstalls packages (protocol/workspace.md §4), in two phases: planning, which
/// checks everything and changes nothing, then applying the plan through a transaction that the
/// caller commits or rolls back.
public interface IMcmodInstaller {
  /// Plans installing the package into the workspace as the view shows it, and adds the planned
  /// writes to the view. Fetches the archive and the assets (a dry run reports the plan too).
  /// Throws McmodInstallException when the package cannot be installed, e.g. when a file is
  /// owned by another installed package.
  public Task<PackagePlan> PlanInstall(
      Candidate candidate, InstallContext context, WorkspaceView view);

  /// Plans uninstalling the package from the workspace as the view shows it, and adds the
  /// planned deletions to the view. A dry run reports the plan.
  public Task<PackagePlan> PlanUninstall(
      LockedPackage package, InstallContext context, WorkspaceView view);

  /// Runs the steps of a plan, making every file change through the transaction. Throws
  /// McmodInstallException when a step fails; the caller rolls the transaction back.
  public Task Apply(
      PackagePlan plan, WorkspaceTransaction transaction, CancellationToken cancellationToken);
}

/// Reads and writes the workspace file (protocol/workspace.md §7.1), called the lock in the code,
/// which lives in &lt;root&gt;/.lip/workspace.json, and names the other files in &lt;root&gt;/.lip.
public interface ILockStore {
  /// Returns an empty lock when none exists. Throws LockFormatException when the lock exists
  /// but cannot be read, or when the workspace has files of earlier formats (§7.6).
  public Task<LockFile> Load(string workspaceRoot);

  /// Writes the lock: Stage, then CommitStaged.
  public Task Save(string workspaceRoot, LockFile lockFile);

  /// The path of the lock of a workspace.
  public string GetLockPath(string workspaceRoot);

  /// The path of the workspace lock file (WorkspaceMutex), &lt;root&gt;/.lip/lock.
  public string GetMutexPath(string workspaceRoot);

  /// The path of the journal (WorkspaceTransaction), &lt;root&gt;/.lip/journal.
  public string GetJournalPath(string workspaceRoot);

  /// Creates &lt;root&gt;/.lip when it does not exist.
  public void CreateLipDirectory(string workspaceRoot);

  /// Writes the lock to &lt;root&gt;/.lip/workspace.json.new, replacing an earlier staged lock.
  public Task Stage(string workspaceRoot, LockFile lockFile);

  /// Moves the staged lock into place in one rename, the point at which an operation commits.
  /// Does nothing when no lock is staged. Throws only when the lock was not moved.
  public void CommitStaged(string workspaceRoot);

  /// Deletes the staged lock, if any.
  public void DiscardStaged(string workspaceRoot);
}

/// The workspace file (protocol/workspace.md §7.1, workspace.schema.json).
public record LockFile {
  [JsonPropertyName("format_version")]
  public int FormatVersion { get; init; } = ToothFormat.FormatVersion;

  /// "server" or "client", null until known. lip writes "server".
  [JsonPropertyName("platform")]
  public string? Platform { get; init; }

  [JsonPropertyName("runtime")]
  public string? Runtime { get; init; }

  /// In installation order.
  [JsonPropertyName("packages")]
  public List<LockedPackage> Packages { get; init; } = [];
}

/// An installed package in the workspace file (protocol/workspace.md §7.1).
public record LockedPackage {
  [JsonPropertyName("tooth")]
  public required string Tooth { get; init; }

  /// The variant of its source.
  [JsonPropertyName("variant")]
  public required string Variant { get; init; }

  [JsonPropertyName("version")]
  public required string Version { get; init; }

  /// Whether a request named it (as opposed to a dependency package).
  [JsonPropertyName("explicit")]
  public required bool Explicit { get; init; }

  /// The source of the request or the key of the dependency it was resolved for; a local path
  /// as the file: URI of its absolute path.
  [JsonPropertyName("source")]
  public required string Source { get; init; }

  [JsonPropertyName("archive")]
  public required ArchiveIdentity Archive { get; init; }

  /// One member for each asset of the entry: null for "self".
  [JsonPropertyName("assets")]
  public required List<AssetIdentity?> Assets { get; init; }

  /// The selected entry, in the form protocol/package.md §5 reads it for the environment.
  [JsonPropertyName("entry")]
  public required JsonElement Entry { get; init; }

  /// Owned files, workspace-relative, '/'-separated and in the mods/ form.
  [JsonPropertyName("files")]
  public List<string> Files { get; init; } = [];

  /// The format_version of the tooth file the entry was read from when it is not 4: an entry
  /// derived from format 3 keeps the meaning of format 3 (protocol/package.md §5), which the
  /// entry itself does not tell. Omitted for format 4.
  [JsonPropertyName("tooth_format_version")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? ToothFormatVersion { get; init; }

  /// The identity (protocol/package.md §4).
  [JsonIgnore]
  public string Identity => Package.GetIdentity(Tooth, Variant);

  /// Whether the entry is one derived from an earlier format.
  [JsonIgnore]
  public bool IsLegacy => ToothFormatVersion is not null && ToothFormatVersion != ToothFormat.FormatVersion;
}
