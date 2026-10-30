using Semver;

namespace Lip.Core.Mcmod;

/// A version constraint used during resolution. Null wherever one is accepted means "any".
public interface IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version);
}

/// A root manifest.json (spec §4). Parsed by IDocumentParser.
public abstract record ManifestDocument {
  /// Whether the document carries formatVersion 4 and the format UUID. Unmarked documents are
  /// plain LeviLamina manifests: accepted only as self-contained concrete packages (never as
  /// routing documents, and without install edges).
  public required bool IsMarked { get; init; }
}

/// A document with an "artifacts" key (spec §4.1). Always marked.
public record RoutingDocument : ManifestDocument {
  public string? Name { get; init; }

  public SemVersion? Version { get; init; }

  public required IReadOnlyList<Artifact> Artifacts { get; init; }

  public IReadOnlyList<Ref> Refs { get; init; } = [];
}

public record Artifact {
  public string Variant { get; init; } = "";

  /// Null when the key is absent ("any platform").
  public IReadOnlyList<McmodPlatform>? Platforms { get; init; }

  /// Null when the key is absent ("any rid").
  public IReadOnlyList<string>? Rids { get; init; }

  /// Ref strings before template expansion (see RefTemplate). Non-empty.
  public required IReadOnlyList<string> Refs { get; init; }

  /// Number of selector keys written; the most specific matching artifact wins (spec §5.2).
  public int SelectorCount => (Platforms is null ? 0 : 1) + (Rids is null ? 0 : 1);

  public bool Matches(LipEnvironment environment) =>
      (Platforms is null || Platforms.Contains(environment.Platform))
      && (Rids is null || Rids.Contains(environment.Rid));
}

public enum PackagePlatform {
  Universal,
  Server,
  Client,
}

/// A document without an "artifacts" key (spec §4.2).
public record ConcreteDocument : ManifestDocument {
  public required string Name { get; init; }

  public required SemVersion Version { get; init; }

  /// "files" for a file package; any other value is a mod (LeviLamina semantics).
  public required string Type { get; init; }

  public bool IsFilePackage => Type == "files";

  public PackagePlatform Platform { get; init; } = PackagePlatform.Universal;

  public string? Rid { get; init; }

  public IReadOnlyList<Ref> Refs { get; init; } = [];

  public IReadOnlyList<Dep> Dependencies { get; init; } = [];

  public IReadOnlyList<Dep> OptionalDependencies { get; init; } = [];

  public IReadOnlyList<Dep> Conflicts { get; init; } = [];

  public IReadOnlyList<InstallDep> InstallDependencies { get; init; } = [];

  public IReadOnlyList<Placement> Placements { get; init; } = [];

  /// The asset placements of a tooth.json converted to format 4 (spec §9.5), which select files
  /// by pattern: they are expanded against the archive's files at install time, into placements
  /// before Placements. Not part of the manifest.json syntax: the parser never sets them.
  public IReadOnlyList<AssetPlacement> AssetPlacements { get; init; } = [];

  public IReadOnlyList<PathGlob> PreserveFiles { get; init; } = [];

  public IReadOnlyList<PathGlob> RemoveFiles { get; init; } = [];

  public Scripts Scripts { get; init; } = new();
}

public record Dep {
  public required string Name { get; init; }

  public IVersionConstraint? Version { get; init; }

  /// Null when absent; otherwise non-empty.
  public IReadOnlyList<Ref>? Refs { get; init; }
}

public record InstallDep {
  public string? Name { get; init; }

  public IVersionConstraint? Version { get; init; }

  public required IReadOnlyList<Ref> Refs { get; init; }

  /// Set on the edges converted from tooth.json dependencies (spec §9.3); see InstallEdge. Not
  /// part of the manifest.json syntax.
  public bool ToothClientCompat { get; init; }
}

/// src is a path in the archive, dest a path in the workspace. "" or a trailing '/' marks the
/// directory form; src and dest always have the same form.
public record Placement(string Src, string Dest) {
  public bool IsDirectory => Src == "" || Src.EndsWith('/');
}

/// A tooth.json asset placement (spec §9.5): Src is matched against the files of the asset
/// mounted at Mount in the archive (a directory name), by the v3 rules: a file placement takes the
/// file named Src or the files matching the glob Src, a directory placement the files below the
/// directory Src. Dest is a workspace path.
public record AssetPlacement(string Mount, bool IsDirectory, string Src, string Dest);

public record Scripts {
  public IReadOnlyList<string> PreInstall { get; init; } = [];

  public IReadOnlyList<string> PostInstall { get; init; } = [];

  public IReadOnlyList<string> PreUninstall { get; init; } = [];

  public IReadOnlyList<string> PostUninstall { get; init; } = [];
}
