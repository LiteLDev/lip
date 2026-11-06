using System.Text.Json;
using Semver;

namespace Lip.Core.Mcmod;

/// A version constraint used during resolution. Null wherever one is accepted means "any".
public interface IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version);
}

/// A tooth file of format 4 (protocol/package.md §3), or the one Section 5 derives from a tooth
/// file of an earlier format for an environment. Json is the document itself (for format 4) or
/// the derived document.
public record ToothFile {
  public required string Tooth { get; init; }

  public required SemVersion Version { get; init; }

  /// The entries of each variant ("" for the default variant), in document order.
  public required IReadOnlyDictionary<string, IReadOnlyList<ToothEntry>> Variants { get; init; }

  public required JsonElement Json { get; init; }

  /// The entry of a variant whose platform and runtime are those of the environment
  /// (protocol/package.md §4 step 2), or null. No two entries of one variant have the same
  /// platform and runtime, so there is at most one.
  public ToothEntry? SelectEntry(string variant, LipEnvironment environment) =>
      Variants.TryGetValue(variant, out IReadOnlyList<ToothEntry>? entries)
          ? entries.FirstOrDefault(e => e.Contains(environment))
          : null;
}

/// An entry of a tooth file (tooth.schema.json "entry"). Json is the entry as written, which the
/// workspace file records (protocol/workspace.md §7.1).
///
/// PreserveFiles and RemoveFiles are its "preserve" and "purge". Derived marks an entry that
/// protocol/package.md §5 derived from a tooth file of format 3 while resolving; it only decides
/// which dependencies name the variant "" for "client" (ToothDependency.ClientCompat), and is not
/// recorded.
public record ToothEntry {
  public required McmodPlatform Platform { get; init; }

  public required string Runtime { get; init; }

  public IReadOnlyList<ToothDependency> Dependencies { get; init; } = [];

  public IReadOnlyList<EntryAsset> Assets { get; init; } = [];

  public IReadOnlyList<PathGlob> PreserveFiles { get; init; } = [];

  public IReadOnlyList<PathGlob> RemoveFiles { get; init; } = [];

  public Hooks Hooks { get; init; } = new();

  public bool Derived { get; init; }

  public required JsonElement Json { get; init; }

  public bool Contains(LipEnvironment environment) =>
      Platform == environment.Platform && Runtime == environment.Runtime;

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

/// A dependency (protocol/package.md §4): the source of the key, and the range of the value.
public record ToothDependency(Ref Source, VersionRange Range) {
  /// A dependency of an entry derived from format 3: its variant "client" names the variant ""
  /// of a package of format 4 (protocol/package.md §5).
  public bool ClientCompat { get; init; }
}

public enum AssetType {
  Self,
  Zip,
  Mcmod,
}

/// An asset of an entry (tooth.schema.json "asset").
public record EntryAsset(
    AssetType Type,
    IReadOnlyList<string> Urls,
    IReadOnlyList<EntryPlacement> Placements);

public enum PlacementForm {
  File,
  Directory,
  Glob,
}

/// A member of the "place" of an asset, whose form Src decides (tooth.schema.json "place").
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
