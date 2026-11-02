using Semver;

namespace Lip.Core.Mcmod;

/// A version constraint used during resolution. Null wherever one is accepted means "any".
public interface IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version);
}

/// A root manifest.json (spec §4, §10.1). Parsed by IDocumentParser.
public abstract record ManifestDocument;

/// A document with a "targets" key (spec §4.2). Only id, version and the targets take part in
/// resolution; the rest of the document is a template for packing (spec §8).
public record MetaDocument : ManifestDocument {
  public string? Id { get; init; }

  public SemVersion? Version { get; init; }

  public required IReadOnlyList<MetaTarget> Targets { get; init; }
}

public record MetaTarget {
  public string Variant { get; init; } = "";

  /// Null when the key is absent ("any platform").
  public IReadOnlyList<McmodPlatform>? Platforms { get; init; }

  /// Null when the key is absent ("any runtime").
  public IReadOnlyList<string>? Runtimes { get; init; }

  /// Source refs before template expansion (see RefTemplate). Non-empty.
  public required IReadOnlyList<string> Source { get; init; }

  /// The id and version templates use (spec §4.2): the target's own, or else the document's;
  /// null when there is none.
  public string? TemplateId { get; init; }

  public SemVersion? TemplateVersion { get; init; }

  /// Number of selector keys written; the most specific matching target wins (spec §5.2).
  public int SelectorCount => (Platforms is null ? 0 : 1) + (Runtimes is null ? 0 : 1);

  public bool Matches(LipEnvironment environment) =>
      (Platforms is null || Platforms.Contains(environment.Platform))
      && (Runtimes is null || Runtimes.Contains(environment.Runtime));
}

/// A document without a "targets" key (spec §4.1), or a LeviLamina manifest read as one
/// (spec §10.1).
public record PackageDocument : ManifestDocument {
  public required string Id { get; init; }

  public required SemVersion Version { get; init; }

  /// Whether the document has "mod": a mod is written whole into mods/&lt;id&gt;/; any other
  /// package is a file package, whose files are its Files (spec §7).
  public required bool IsMod { get; init; }

  public bool IsFilePackage => !IsMod;

  /// Null when omitted (both platforms).
  public McmodPlatform? Platform { get; init; }

  /// Null when omitted (any runtime).
  public string? Runtime { get; init; }

  public IReadOnlyList<Dep> Dependencies { get; init; } = [];

  public IReadOnlyList<Dep> Conflicts { get; init; } = [];

  /// The install edges a tooth.json's dependencies become (spec §10.2 rule 3). Not part of the
  /// manifest.json syntax: the parser never sets them.
  public IReadOnlyList<InstallDep> InstallDependencies { get; init; } = [];

  public IReadOnlyList<PackageFile> Files { get; init; } = [];

  public IReadOnlyList<PackageArchive> Archives { get; init; } = [];

  /// The asset placements of a tooth.json (spec §10.2 rule 5), which select files by pattern:
  /// they are expanded against the archive's files at install time, into files before Files.
  /// Not part of the manifest.json syntax: the parser never sets them.
  public IReadOnlyList<AssetPlacement> AssetPlacements { get; init; } = [];

  public IReadOnlyList<PathGlob> Preserve { get; init; } = [];

  public IReadOnlyList<PathGlob> Remove { get; init; } = [];

  public Hooks Hooks { get; init; } = new();
}

/// A "dependencies" or "conflicts" entry (spec §4.1, §6). Refs is the "source" of a dependency,
/// null when absent; a dependency with a source is an install edge.
public record Dep {
  public required string Id { get; init; }

  public IVersionConstraint? Version { get; init; }

  /// Null when absent; otherwise non-empty.
  public IReadOnlyList<Ref>? Refs { get; init; }
}

/// An install edge of a tooth.json dependency (spec §10.2 rule 3): any identity satisfies it.
public record InstallDep {
  public IVersionConstraint? Version { get; init; }

  public required IReadOnlyList<Ref> Refs { get; init; }

  /// A "client" variant means "" when the target archive uses manifest.json; see InstallEdge.
  public bool ToothClientCompat { get; init; }
}

public enum PackageFileForm {
  File,
  Directory,
  Glob,
}

/// A "files" entry (spec §4.1, §7). Src is a path in the package's archive, or in the archive
/// named by From; Dest a path in the workspace.
public record PackageFile(string? From, string Src, string Dest) {
  public PackageFileForm Form =>
      PathGlob.HasMetacharacters(Src) ? PackageFileForm.Glob
      : Src == "" || Src.EndsWith('/') ? PackageFileForm.Directory
      : PackageFileForm.File;
}

public enum PackageArchiveFormat {
  /// Detected from the content: zip, tar or tgz.
  Detect,
  Zip,
  Tar,
  Tgz,
  File,
}

/// An "archives" entry (spec §4.1, §7).
public record PackageArchive(
    string Name,
    IReadOnlyList<string> Urls,
    string? Sha256,
    PackageArchiveFormat Format);

/// A tooth.json asset placement (spec §10.2 rule 5): Src is matched against the files of the
/// asset mounted at Mount in the archive (a directory name), by the v3 rules: a file placement
/// takes the file named Src or the files matching the glob Src, a directory placement the files
/// below the directory Src. Dest is a workspace path.
public record AssetPlacement(string Mount, bool IsDirectory, string Src, string Dest);

public record Hooks {
  public IReadOnlyList<string> PreInstall { get; init; } = [];

  public IReadOnlyList<string> PostInstall { get; init; } = [];

  public IReadOnlyList<string> PreUninstall { get; init; } = [];

  public IReadOnlyList<string> PostUninstall { get; init; } = [];
}
