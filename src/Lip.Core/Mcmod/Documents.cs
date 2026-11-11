using System.Text.Json;
using Semver;

namespace Lip.Core.Mcmod;

/// A version constraint used during resolution. Null wherever one is accepted means "any".
public interface IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version);
}

/// A package file (protocol/lip.toml.md), or the one protocol/lip-compatibility.md, "tooth.json"
/// derives from a tooth.json for an environment. Json is the package file as read (for lip.toml) or
/// the derived one.
public record PackageFile {
  /// The package id.
  public required string Id { get; init; }

  public required SemVersion Version { get; init; }

  /// The entries of each variant ("" for the default variant), in document order.
  public required IReadOnlyDictionary<string, IReadOnlyList<PackageEntry>> Variants { get; init; }

  public required JsonElement Json { get; init; }

  /// The entry of a variant whose side and platform are those of the environment
  /// (protocol/lip-package.md, "Resolution" step 2), or null. No two entries of one variant have
  /// the same side and platform, so there is at most one.
  public PackageEntry? SelectEntry(string variant, LipEnvironment environment) =>
      Variants.TryGetValue(variant, out IReadOnlyList<PackageEntry>? entries)
          ? entries.FirstOrDefault(e => e.Contains(environment))
          : null;
}

/// An entry of a package file (lip.schema.toml, the items of "variants"). Json is the entry as
/// written, which the state file records (protocol/state.json.md).
///
/// Dependencies, PreserveFiles and RemoveFiles are its "deps", "preserve" and "purge".
public record PackageEntry {
  public required McmodSide Side { get; init; }

  public required string Platform { get; init; }

  public IReadOnlyList<PackageDependency> Dependencies { get; init; } = [];

  public IReadOnlyList<EntryAsset> Assets { get; init; } = [];

  public IReadOnlyList<PathGlob> PreserveFiles { get; init; } = [];

  public IReadOnlyList<PathGlob> RemoveFiles { get; init; } = [];

  public Hooks Hooks { get; init; } = new();

  public required JsonElement Json { get; init; }

  public bool Contains(LipEnvironment environment) =>
      Side == environment.Side && Platform == environment.Platform;

  /// The index of the asset of type "mcmod", or null.
  public int? McmodAsset {
    get {
      for (int i = 0; i < Assets.Count; i++) {
        if (Assets[i].Type == AssetType.Mcmod) {
          return i;
        }
      }

      return null;
    }
  }
}

/// A dependency (protocol/lip-package.md, "Resolution"): the source of the key, and the range of
/// the value.
public record PackageDependency(Ref Source, VersionRange Range);

public enum AssetType {
  Self,
  Zip,
  Mcmod,
}

/// An asset of an entry (lip.schema.toml, the items of "assets").
public record EntryAsset(
    AssetType Type,
    IReadOnlyList<string> Urls,
    IReadOnlyList<EntryPlacement> Placements);

public enum PlacementForm {
  File,
  Directory,
  Glob,
}

/// A member of the "place" of an asset, whose form Src decides (lip.schema.toml "place").
public record EntryPlacement(string Src, string Dest) {
  public PlacementForm Form =>
      PathGlob.HasMetacharacters(Src) ? PlacementForm.Glob
      : Src == "" || Src.EndsWith('/') ? PlacementForm.Directory
      : PlacementForm.File;
}

public record Hooks {
  public IReadOnlyList<string> PreInstall { get; init; } = [];

  public IReadOnlyList<string> PostInstall { get; init; } = [];

  public IReadOnlyList<string> PreUninstall { get; init; } = [];

  public IReadOnlyList<string> PostUninstall { get; init; } = [];
}
