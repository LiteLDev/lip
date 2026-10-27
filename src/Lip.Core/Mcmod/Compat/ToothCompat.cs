using Lip.Core.Entities;

namespace Lip.Core.Mcmod.Compat;

/// tooth.json compatibility (spec §9).
///
/// - §9.1: a tooth.json archive is one concrete package whose identity is its "tooth" value and
///   whose version is its "version". Select picks the v3 variant exactly like
///   PackageManifest.GetVariant, but for the environment rid instead of lip's own runtime
///   identifier, and returns null where GetVariant throws KeyNotFoundException. Other errors of
///   GetVariant (e.g. duplicate dependency keys across merged variants) propagate, as they
///   failed the operation in v3 too. The resolver checks the version range.
/// - §9.2: a ref variant is a v3 label, except that "" means "client" when the environment
///   platform is client and the tooth.json has a variant labeled "client".
/// - §9.3: ToothPackage.InstallEdges carries each v3 dependency as an InstallEdge with
///   ToothClientCompat = true. This class does not apply the client-to-"" mapping itself: the
///   resolver does, when such an edge's ref has variant "client" and its target archive turns
///   out to use manifest.json (IResolver.Resolve's toothClientCompat parameter).
public class ToothCompat : IToothCompat {
  public const string ClientLabel = "client";

  public IPackage? Select(
      ToothArchiveDocument document,
      string variant,
      LipEnvironment environment) => Select(document.Manifest, variant, environment);

  /// See IToothCompat.Select.
  public static ToothPackage? Select(
      PackageManifest manifest,
      string variant,
      LipEnvironment environment) {
    string label = GetLabel(manifest, variant, environment);

    PackageManifestVariant selected;
    try {
      selected = manifest.GetVariant(label, environment.Rid);
    }
    catch (KeyNotFoundException) {
      return null;
    }

    return new ToothPackage(manifest, label, selected);
  }

  /// The v3 label a ref variant selects (spec §9.2).
  public static string GetLabel(
      PackageManifest manifest,
      string variant,
      LipEnvironment environment) =>
      variant == ""
          && environment.Platform == McmodPlatform.Client
          && manifest.Variants.Any(v => v.Label == ClientLabel)
          ? ClientLabel
          : variant;
}
