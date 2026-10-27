using System.Text.Json;
using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Mcmod;

/// An edge of the dependency graph that lip installs (spec §6): a dependencies entry with refs,
/// or an installDependencies entry. ToothClientCompat marks edges mapped from tooth.json
/// dependencies (spec §9.3): a "client" variant is treated as "" when the target archive uses
/// manifest.json.
public record InstallEdge(
    string? Name,
    IVersionConstraint? Version,
    IReadOnlyList<Ref> Refs,
    bool ToothClientCompat = false);

/// A concrete package, i.e. a resolution candidate's payload: either a concrete manifest.json
/// (McmodPackage) or a tooth.json package with its variant selected (spec §9).
public interface IPackage {
  /// "mcmod" or "tooth"; recorded in the lock.
  public string Kind { get; }

  /// The package name; compared ignoring ASCII case.
  public string Identity { get; }

  public SemVersion Version { get; }

  public IReadOnlyList<InstallEdge> InstallEdges { get; }

  public IReadOnlyList<Dep> Conflicts { get; }

  /// The root document as JSON, recorded in the lock so that uninstall needs no network.
  public JsonElement Manifest { get; }
}

public record McmodPackage(ConcreteDocument Document, JsonElement Manifest) : IPackage {
  public const string KindName = "mcmod";

  public string Kind => KindName;

  public string Identity => Document.Name;

  public SemVersion Version => Document.Version;

  /// Unmarked documents are self-contained: they never create install edges.
  public IReadOnlyList<InstallEdge> InstallEdges => Document.IsMarked
      ? [
        .. Document.Dependencies
            .Where(d => d.Refs is not null)
            .Select(d => new InstallEdge(d.Name, d.Version, d.Refs!)),
        .. Document.InstallDependencies
            .Select(d => new InstallEdge(d.Name, d.Version, d.Refs)),
      ]
      : [];

  public IReadOnlyList<Dep> Conflicts => Document.Conflicts;
}

/// The root document of an archive (spec §2.4).
public abstract record ArchiveDocument;

public record RoutingArchiveDocument(RoutingDocument Document) : ArchiveDocument;

public record ConcreteArchiveDocument(ConcreteDocument Document, JsonElement Manifest)
    : ArchiveDocument;

/// A tooth.json root; Manifest is the v3 form after migration of v1/v2.
public record ToothArchiveDocument(PackageManifest Manifest, JsonElement Json) : ArchiveDocument;

public static class PackageNames {
  public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

  public static bool AreEqual(string a, string b) => Comparer.Equals(a, b);
}
