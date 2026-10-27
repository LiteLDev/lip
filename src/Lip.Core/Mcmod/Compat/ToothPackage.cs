using System.Text.Json;
using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// A tooth.json package with its v3 variant selected (spec §9.1). Built by ToothCompat.
public sealed class ToothPackage : IPackage {
  public const string KindName = "tooth";

  public ToothPackage(PackageManifest toothManifest, string label, PackageManifestVariant variant) {
    ToothManifest = toothManifest;
    Label = label;
    Variant = variant;
    Manifest = JsonSerializer.SerializeToElement(toothManifest);

    // Spec §9.3: a dependency key "<path>[#label]" is a ref and its value a v3 range. The path
    // may resolve to a manifest.json package whose identity is its name, so the edge has no
    // name constraint.
    InstallEdges = [
      .. variant.Dependencies.Select(d => new InstallEdge(
          Name: null,
          Version: new SemVersionRangeConstraint(d.Value),
          Refs: [Ref.Parse(d.Key.ToString())],
          ToothClientCompat: true)),
    ];
  }

  public string Kind => KindName;

  /// The tooth value (spec §9.1).
  public string Identity => ToothManifest.Path;

  public SemVersion Version => ToothManifest.Version;

  public IReadOnlyList<InstallEdge> InstallEdges { get; }

  public IReadOnlyList<Dep> Conflicts => [];

  /// The v3 form of the tooth.json (v1/v2 are migrated), serialized like the v3 lock did.
  public JsonElement Manifest { get; }

  /// The whole tooth.json, migrated to v3.
  public PackageManifest ToothManifest { get; }

  /// The v3 label the variant was selected with. Differs from the ref variant only when §9.2
  /// mapped "" to "client".
  public string Label { get; }

  /// The variants matching Label and the environment rid, merged as by
  /// PackageManifest.GetVariant.
  public PackageManifestVariant Variant { get; }

  /// Returns this package with another version, e.g. the tag version of a spec archive whose
  /// tooth.json declares a stale version (lip v3 always used the tag version).
  public ToothPackage WithVersion(SemVersion version) =>
      new(ToothManifest with { Version = version }, Label, Variant);

  public override string ToString() => $"{Identity}@{Version}";
}
