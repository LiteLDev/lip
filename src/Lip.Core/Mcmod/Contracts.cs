using System.Text.Json;
using System.Text.Json.Serialization;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// An asset of a candidate (protocol/lip-workspace.md, "Installing and uninstalling" step 2),
/// opened on first use: the archive of the package for "self", otherwise downloaded.
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

/// A candidate (protocol/lip-package.md, "Resolution"): a package with its selected entry, the
/// archive it is in, the source it was resolved for, and the assets of its entry, in order.
public record Candidate(
    Package Package,
    ArchiveHandle Archive,
    Ref Source,
    IReadOnlyList<AssetHandle> Assets) {
  private Task<ModManifest?>? _modManifest;

  /// The mod manifest of a mod (protocol/lip-package.md, "Resolution"): the manifest.json at the
  /// root of the archive of its asset of type "mcmod"; null for a package that is not a mod.
  /// Obtains that asset. Throws McmodInstallException when the asset cannot be obtained or the mod
  /// manifest is missing or invalid.
  public Task<ModManifest?> GetModManifest() => _modManifest ??= LoadModManifest();

  private async Task<ModManifest?> LoadModManifest() {
    if (Package.Entry.McmodAsset is not int index) {
      return null;
    }

    string name = Package.Identity;
    string label = $"{name} {Package.Version}";
    string manifestFile = PackageFormat.ModManifestFileName;
    RunTrace.Write("install", $"Reading the mod name of {name}@{Package.Version} from the"
        + $" {manifestFile} of its asset of type mcmod ({index}).");
    ISource source;
    try {
      source = await Assets[index].Open();
    }
    catch (ArchiveUnavailableException ex) {
      throw new AssetUnavailableException(
          $"Cannot install {label}: lip could not obtain its asset of type \"mcmod\" ({index}) to"
          + $" read the mod name from its {manifestFile}.",
          ex,
          this);
    }

    try {
      if (!source.Keys.Contains(manifestFile)) {
        throw new McmodInstallException(
            $"Cannot install {label}: its asset of type \"mcmod\" has no {manifestFile} at the"
            + " root of its archive, so lip cannot tell the mod name. This is a problem of the"
            + " package; report it to its author.");
      }

      await using Stream stream = await source.OpenRead(manifestFile);
      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      using JsonDocument json = PackageFileParser.ParseJson(memory.ToArray(), manifestFile);
      JsonElement root = json.RootElement;
      if (root.ValueKind != JsonValueKind.Object
          || !root.TryGetProperty("name", out JsonElement modName)
          || modName.ValueKind != JsonValueKind.String
          || !PackageNames.IsValid(modName.GetString()!)) {
        string found = root.ValueKind != JsonValueKind.Object
            ? $"a JSON {Kind(root)}"
            : !root.TryGetProperty("name", out JsonElement value)
                ? "an object without \"name\""
                : value.ValueKind != JsonValueKind.String
                    ? $"an object whose \"name\" is a {Kind(value)}"
                    : $"an object whose \"name\" is \"{value.GetString()}\"";
        throw new McmodInstallException(
            $"Cannot install {label}: the {manifestFile} of its asset of type \"mcmod\" must be a"
            + " JSON object"
            + " whose \"name\" is the mod name (letters, digits, '.', '_' and '-', starting with"
            + $" a letter or digit), but it is {found}. This is a problem of the package; report"
            + " it to its author.");
      }

      RunTrace.Write("install", $"The mod name of {name} is '{modName.GetString()}'.");
      return new ModManifest(modName.GetString()!);
    }
    catch (Exception ex) when (ex is ArchiveUnavailableException or FormatException
        or IOException or InvalidDataException) {
      // The asset was obtained, but its files cannot be read.
      // The resolver keeps this exception, so the reason reaches the user as its cause.
      throw new McmodInstallException(
          $"Cannot install {label}: lip could not read the {manifestFile} of its asset of type"
          + " \"mcmod\" to tell the mod name.",
          ex);
    }
  }

  private static string Kind(JsonElement element) => element.ValueKind switch {
    JsonValueKind.Array => "array",
    JsonValueKind.String => "string",
    JsonValueKind.Number => "number",
    JsonValueKind.True or JsonValueKind.False => "boolean",
    JsonValueKind.Null => "null",
    _ => "object",
  };

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

/// The manifest.json at the root of the mcmod asset of a mod (protocol/lip-package.md,
/// "Resolution"), of which lip reads only the mod name, its "name".
public record ModManifest(string Name);

/// Resolve(source, range, environment) (protocol/lip-package.md, "Resolution").
public interface IResolver {
  /// The candidate sequence, produced lazily: archives (e.g. older tags of a remote repository)
  /// are opened only when the caller asks for more candidates. Diagnostics, which explain every
  /// path that produced no candidate, are reported through onDiagnostic as they arise.
  public IAsyncEnumerable<Candidate> ResolveLazy(
      Ref source,
      IVersionConstraint? range,
      LipEnvironment environment,
      Action<string>? onDiagnostic = null);
}

/// A request (protocol/lip-workspace.md, "Terms"): a source and a range.
public record PackageRequest(Ref Ref, IVersionConstraint? Range);

/// Thrown when no package set satisfies the requests (protocol/lip-workspace.md, "Valid states").
/// Reasons: why, one line each, organized by which package needs what. Hints: what the user can
/// do about it, in plain sentences.
public class DependencyResolutionException(
    string message,
    Exception? innerException = null,
    IReadOnlyList<string>? reasons = null,
    IReadOnlyList<string>? hints = null) : Exception(message, innerException) {
  public IReadOnlyList<string> Reasons { get; } = reasons ?? [];

  public IReadOnlyList<string> Hints { get; } = hints ?? [];
}

/// A solved package set: S in installation order; for every member (by index into Order) the
/// members satisfying its dependencies; and for every request the member satisfying it.
/// Notes: for every member (by index into Order), why it is not the newest version its
/// requirement accepts, or null.
public record SolvedGraph(
    IReadOnlyList<Candidate> Order,
    IReadOnlyList<IReadOnlyList<int>> Dependencies,
    IReadOnlyList<int> RequestSatisfiers,
    IReadOnlyList<string?>? Notes = null);

/// How packages and sources are named in messages: as a package argument names them
/// (protocol/lip.md, "Package arguments"), so a package of github.com/LiteLDev is named without
/// that prefix, and with the casing of the source it was resolved for.
public static class PackageDisplay {
  /// The name of a candidate, without its version.
  public static string Name(Candidate candidate) =>
      Name(candidate.Package.Id, candidate.Package.Variant, candidate.Source);

  /// The name of an installed package, without its version.
  public static string Name(LockedPackage package) =>
      Name(package.Id, package.Variant,
          Ref.TryParse(package.Source, out Ref? source, out _) ? source : null);

  /// The name of a package of an id and variant resolved for a source: the location of the
  /// source when it is the remote repository of the package id, which keeps its casing.
  private static string Name(string id, string variant, Ref? source) {
    if (source is not { Kind: RefKind.Spec } || !PackageNames.AreEqual(source.Target, id)) {
      return Package.GetIdentity(id, variant);
    }

    return PackageArguments.Abbreviate(source.Target)
        + (variant.Length == 0 ? "" : $"#{variant}");
  }

  /// The name of a candidate with its version, such as "LeviLamina 1.0.0".
  public static string NameAndVersion(Candidate candidate) =>
      $"{Name(candidate)} {candidate.Package.Version}";

  /// A source as a package argument names it, with its variant and ref.
  public static string Name(Ref source) {
    if (source.Kind != RefKind.Spec) {
      return source.ToString();
    }

    return PackageArguments.Abbreviate(source.Target)
        + (source.Variant.Length == 0 ? "" : $"#{source.Variant}")
        + (source.GitRef is null ? "" : $"@{source.GitRef}");
  }

  /// Where to report a problem of a package to its author: the issues page of its repository,
  /// when its source is a remote repository on github.com; otherwise null.
  public static string? IssuesUrl(Candidate candidate) {
    if (candidate.Source.Kind != RefKind.Spec) {
      return null;
    }

    string[] segments = candidate.Source.Target.Split('/');
    return segments.Length >= 3
        && segments[0].Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? $"https://github.com/{segments[1]}/{segments[2]}/issues"
            : null;
  }
}

public record InstallContext(
    string WorkspaceRoot,
    LipEnvironment Environment,
    LockFile Lock,
    bool DryRun);

/// The state file (protocol/state.json.md, state.schema.json).
public record LockFile {
  [JsonPropertyName(PackageFormat.FormatKey)]
  public int Format { get; init; } = PackageFormat.Format;

  /// "server" or "client". lip writes "server".
  [JsonPropertyName("side")]
  public required string Side { get; init; }

  /// A .NET runtime identifier (RID).
  [JsonPropertyName("platform")]
  public required string Platform { get; init; }

  /// In installation order.
  [JsonPropertyName("packages")]
  public required List<LockedPackage> Packages { get; init; }
}

/// An installed package in the state file (protocol/state.json.md).
public record LockedPackage {
  /// Its package file, as protocol/lip-package.md, "Resolution" read it (for a package read from a
  /// tooth.json, the one protocol/lip-compatibility.md, "tooth.json" derives from it for the
  /// environment of the workspace). Its "id" is the package id and its "version" the version of the
  /// package.
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

  /// The package id, the "id" of the manifest.
  [JsonIgnore]
  public string Id => StringMember("id");

  /// The version, the "version" of the manifest.
  [JsonIgnore]
  public string Version => StringMember("version");

  /// The identity (protocol/lip-package.md, "Resolution").
  [JsonIgnore]
  public string Identity => Package.GetIdentity(Id, Variant);

  private string StringMember(string name) =>
      Manifest.ValueKind == JsonValueKind.Object
      && Manifest.TryGetProperty(name, out JsonElement value)
      && value.ValueKind == JsonValueKind.String
          ? value.GetString()!
          : "";
}
