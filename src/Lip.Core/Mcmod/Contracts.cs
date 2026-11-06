using System.Text.Json;
using System.Text.Json.Serialization;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// An asset of a candidate (protocol/workspace.md §4 step 2), opened on first use: the archive
/// of the package for "self", otherwise downloaded.
public sealed class AssetHandle {
  private readonly Lazy<Task<ISource>> _open;

  private AssetHandle(AssetType type, Func<Task<ISource>> open) {
    Type = type;
    _open = new(open);
  }

  public AssetType Type { get; }

  /// The archive of the package itself.
  public static AssetHandle Self(ArchiveHandle archive) =>
      new(AssetType.Self, () => Task.FromResult(archive.Source));

  /// An asset downloaded from the first of its URLs that can be obtained.
  public static AssetHandle Download(IArchiveProvider provider, EntryAsset asset) =>
      new(asset.Type, () => provider.OpenAsset(asset.Urls));

  /// The files of the asset. Throws ArchiveUnavailableException when it cannot be obtained.
  public Task<ISource> Open() => _open.Value;
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

      return new ModManifest(modName.GetString()!);
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
                : AssetHandle.Download(provider, a)),
          ]);
}

/// The manifest.json at the root of the mcmod asset of a mod (protocol/package.md §4), of which
/// lip reads only the mod name, its "name".
public record ModManifest(string Name);

/// Resolve(source, range, environment) (protocol/package.md §4).
public interface IResolver {
  /// The candidate sequence, produced lazily: archives (e.g. older tags of a remote repository)
  /// are opened only when the caller asks for more candidates. Diagnostics, which explain every
  /// path that produced no candidate, are reported through onDiagnostic as they arise.
  /// clientCompat: the source is the key of a dependency of an entry derived from format 3,
  /// whose variant "client" names the variant "" of a package of format 4 (protocol/package.md
  /// §5).
  public IAsyncEnumerable<Candidate> ResolveLazy(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      bool clientCompat = false,
      Action<string>? onDiagnostic = null);
}

/// A request (protocol/workspace.md §1): a source and a range.
public record PackageRequest(Ref Ref, IVersionConstraint? Range);

/// Thrown when no package set satisfies the requests (protocol/workspace.md §3).
public class DependencyResolutionException(string message) : Exception(message);

/// A solved package set: S in installation order; for every member (by index into Order) the
/// members satisfying its dependencies; and for every request the member satisfying it.
public record SolvedGraph(
    IReadOnlyList<Candidate> Order,
    IReadOnlyList<IReadOnlyList<int>> Dependencies,
    IReadOnlyList<int> RequestSatisfiers);

public record InstallContext(
    string WorkspaceRoot,
    LipEnvironment Environment,
    LockFile Lock,
    bool DryRun,
    bool IgnoreScripts);

/// The workspace file (protocol/workspace.md §7.1, workspace.schema.json).
public record LockFile {
  [JsonPropertyName("format_version")]
  public int FormatVersion { get; init; } = ToothFormat.FormatVersion;

  /// "server" or "client". lip writes "server".
  [JsonPropertyName("platform")]
  public required string Platform { get; init; }

  [JsonPropertyName("runtime")]
  public required string Runtime { get; init; }

  /// In installation order.
  [JsonPropertyName("packages")]
  public required List<LockedPackage> Packages { get; init; }
}

/// An installed package in the workspace file (protocol/workspace.md §7.1).
public record LockedPackage {
  /// Its tooth file, of format 4 (for a tooth file of an earlier format, the one
  /// protocol/package.md §5 derives from it for the environment of the workspace). Its "tooth" is
  /// the tooth path and its "version" the version of the package.
  [JsonPropertyName("manifest")]
  public required JsonElement Manifest { get; init; }

  /// The variant of its source.
  [JsonPropertyName("variant")]
  public required string Variant { get; init; }

  /// Whether a request named it (as opposed to a dependency package).
  [JsonPropertyName("explicit")]
  public required bool Explicit { get; init; }

  /// The source of the request or the key of the dependency it was resolved for; a local path
  /// as the file: URI of its absolute path.
  [JsonPropertyName("ref")]
  public required string Source { get; init; }

  /// For a package from a remote repository, the commit its archive was at (lowercase hex);
  /// otherwise null, and omitted.
  [JsonPropertyName("commit")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Commit { get; init; }

  /// Owned files, workspace-relative, '/'-separated and in the mods/ form.
  [JsonPropertyName("files")]
  public required List<string> Files { get; init; }

  /// The tooth path, the "tooth" of the manifest.
  [JsonIgnore]
  public string Tooth => StringMember("tooth");

  /// The version, the "version" of the manifest.
  [JsonIgnore]
  public string Version => StringMember("version");

  /// The identity (protocol/package.md §4).
  [JsonIgnore]
  public string Identity => Package.GetIdentity(Tooth, Variant);

  private string StringMember(string name) =>
      Manifest.ValueKind == JsonValueKind.Object
      && Manifest.TryGetProperty(name, out JsonElement value)
      && value.ValueKind == JsonValueKind.String
          ? value.GetString()!
          : "";
}
