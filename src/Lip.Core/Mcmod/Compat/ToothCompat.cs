using System.Globalization;
using System.Text;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// A tooth.json converted to format 4 (spec §9): the concrete file package a ref variant selects,
/// and the assets its archive is made of. Assets[i] is mounted at Assets[i].Mount.
public record ToothConversion(McmodPackage Package, IReadOnlyList<ToothAsset> Assets) {
  /// The archive of the package: the tooth.json archive's key and identity, with every asset
  /// mounted at its mount. "self" assets are the tooth.json archive itself; the other assets are
  /// downloaded through the provider when their files are first needed (at install time).
  public ArchiveHandle OpenArchive(ArchiveHandle self, IArchiveProvider archiveProvider) => new(
      self.Key,
      new MountSource([
        .. Assets.Select(a => (a.Mount, a.Type == PackageManifestAsset.AssetType.Self
            ? self.Source
            : (Sources.ISource)new DeferredSource(() => archiveProvider.OpenAsset(
                a.Urls, isArchive: a.Type != PackageManifestAsset.AssetType.Uncompressed)))),
      ]),
      self.Identity);
}

/// A tooth.json asset (spec §9.4): its files are mounted at Mount in the archive of the
/// converted package.
public record ToothAsset(string Mount, PackageManifestAsset.AssetType Type, IReadOnlyList<string> Urls);

/// tooth.json compatibility (spec §9): converts a tooth.json (format 1–3; 1 and 2 are first
/// migrated to 3 by PackageManifestMigration) to the format 4 package a ref variant selects.
///
/// - §9.1: the identity is the "tooth" value, followed by "#label" when the selected v3 label
///   is not "" (GetIdentity); the version is its "version" (the tag version wins over it; the
///   resolver warns). The variant (§9.2) is selected exactly like PackageManifest.GetVariant
///   does, but for the environment rid instead of lip's own: every variant with the label whose
///   platform matches the rid, merged. Null when no variant applies; other errors of GetVariant
///   (e.g. duplicate dependency keys across merged variants) propagate, as they failed the
///   operation in v3 too.
/// - §9.2: a ref variant is a v3 label, except that "" means "client" when the environment
///   platform is client and the tooth.json has a variant labeled "client".
/// - §9.3: every v3 dependency "&lt;path&gt;[#label]": range becomes an install dependency with
///   that ref, the v3 range semantics and no name; its "client" label means "" when the target
///   archive uses manifest.json (InstallDep.ToothClientCompat, applied by the resolver).
/// - §9.2, §9.4–§9.7: the package is a file package. Asset i of the variant is mounted at "i" in
///   its archive (ToothConversion.OpenArchive); its placements become AssetPlacements, expanded
///   at install time (ToothPlacements, §9.5). preserve_files, remove_files and scripts map as
///   described at PreserveFiles, RemoveFiles and ConvertScripts (§9.6, §9.7).
public class ToothCompat : IToothCompat {
  /// The lock kind of converted packages, whose recorded manifest is the tooth.json (in its v3
  /// form, with the installed version).
  public const string KindName = "tooth";

  public const string ClientLabel = "client";

  public ToothConversion? Convert(
      ToothArchiveDocument document,
      string variant,
      LipEnvironment environment,
      SemVersion? version) => Convert(document.Manifest, variant, environment, version);

  /// See IToothCompat.Convert.
  public static ToothConversion? Convert(
      PackageManifest manifest,
      string variant,
      LipEnvironment environment,
      SemVersion? version = null) {
    string label = GetLabel(manifest, variant, environment);

    PackageManifestVariant selected;
    try {
      selected = manifest.GetVariant(label, environment.Rid);
    }
    catch (KeyNotFoundException) {
      return null;
    }

    if (version is not null) {
      manifest = manifest with { Version = version };
    }

    List<ToothAsset> assets = [];
    List<AssetPlacement> placements = [];
    for (int i = 0; i < selected.Assets.Count; i++) {
      PackageManifestAsset asset = selected.Assets[i];
      string mount = i.ToString(CultureInfo.InvariantCulture);
      assets.Add(new ToothAsset(mount, asset.Type, [.. asset.Urls.Select(u => u.ToString())]));
      placements.AddRange(asset.Placements.Select(p => new AssetPlacement(
          mount,
          p.Type == PackageManifestAssetPlacement.PlacementType.Directory,
          p.Src,
          p.Dst)));
    }

    ConcreteDocument document = new() {
      IsMarked = true,
      Name = GetIdentity(manifest.Path, label),
      Version = manifest.Version,
      Type = "files",
      InstallDependencies = [
        .. selected.Dependencies.Select(d => new InstallDep {
          Version = new SemVersionRangeConstraint(d.Value),
          Refs = [Ref.Parse(d.Key.ToString())],
          ToothClientCompat = true,
        }),
      ],
      AssetPlacements = placements,
      PreserveFiles = [.. PreserveFiles(selected.PreserveFiles.Select(GlobListJsonConverter.Format))
          .Select(TryParseGlob).OfType<PathGlob>()],
      RemoveFiles = [.. RemoveFiles(selected.RemoveFiles.Select(GlobListJsonConverter.Format))
          .Select(TryParseGlob).OfType<PathGlob>()],
      Scripts = ConvertScripts(selected.Scripts),
    };

    return new ToothConversion(
        new McmodPackage(document, JsonSerializer.SerializeToElement(manifest)) {
          Kind = KindName,
          Variant = label,
        },
        assets);
  }

  /// Rebuilds the package of a lock entry of kind "tooth" by converting its recorded tooth.json
  /// again: the label is the variant of the last chain hop (the ref that reached the tooth.json
  /// archive), the environment comes from the lock (spec §5.1), and the version is the recorded
  /// one. Null when the tooth.json has no such variant for the environment.
  public static async Task<ToothConversion?> FromLocked(
      LockedPackage package, LipEnvironment environment) {
    (PackageManifest manifest, string variant) = await ReadLocked(package);
    SemVersion version = SemVersion.Parse(package.Version, SemVersionStyles.Strict);

    return Convert(manifest, variant, environment, version);
  }

  /// The identity of the package a tooth.json variant converts to (spec §9.1): the tooth path,
  /// followed by "#label" for a labelled variant. v3 installed every labelled variant as a
  /// package of its own, which may depend on its sibling variants.
  public static string GetIdentity(string tooth, string label) =>
      label == "" ? tooth : $"{tooth}#{label}";

  /// The recorded tooth.json of a lock entry and the variant of its last chain hop.
  private static async Task<(PackageManifest Manifest, string Variant)> ReadLocked(
      LockedPackage package) {
    PackageManifest manifest = await PackageManifest.FromStream(
        new MemoryStream(Encoding.UTF8.GetBytes(package.Manifest.GetRawText())));
    string variant = package.Chain.Count > 0
        ? Ref.Parse(package.Chain[^1].Ref, allowFile: true).Variant
        : "";
    return (manifest, variant);
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

  /// preserveFiles patterns for preserve_files patterns. v3 matched a pattern against the path
  /// relative to the workspace root and against the bare file name, so a pattern without '/'
  /// matches the file name in every directory ("**/" is prepended). '\' becomes '/'.
  public static IEnumerable<string> PreserveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .Select(p => p.Contains('/') ? p : $"**/{p}");

  /// removeFiles patterns for remove_files patterns. v3 matched a pattern against both files and
  /// directories (a directory also with a trailing '/'), and removed a matched directory with
  /// everything in it, so a pattern without a trailing '/' matches files and directories (both
  /// forms are returned). '\' becomes '/'.
  public static IEnumerable<string> RemoveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .SelectMany(p => p.EndsWith('/') ? [p] : new[] { p, p + "/" });

  /// v3 ran "install" scripts after the files were placed, together with "post_install", and
  /// "uninstall" scripts before the files were removed, together with "pre_uninstall".
  public static Scripts ConvertScripts(PackageManifestScripts scripts) => new() {
    PreInstall = scripts.PreInstall,
    PostInstall = [.. scripts.Install, .. scripts.PostInstall],
    PreUninstall = [.. scripts.PreUninstall, .. scripts.Uninstall],
    PostUninstall = scripts.PostUninstall,
  };

  /// The format 4 glob of a converted pattern, or null for a pattern v3 could never match (empty
  /// segments, a leading '/'), which the conversion drops.
  public static PathGlob? TryParseGlob(string pattern) {
    try {
      return new PathGlob(pattern);
    }
    catch (FormatException) {
      return null;
    }
  }
}
