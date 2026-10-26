using System.Diagnostics;
using DotNet.Globbing;
using Lip.Core.Entities;

namespace Lip.Core.Services;

/// Matches source keys against asset placements to decide where each key is placed.
public static class PlacementMatcher {
  /// Returns every (key, target path) pair produced by the placements, ordered by key
  /// first and placement second. A key matched by several placements yields several
  /// pairs, and keys matched by no placement are omitted.
  public static IReadOnlyList<(string Key, string TargetPath)> Match(
      IEnumerable<string> keys,
      IEnumerable<PackageManifestAssetPlacement> placements) {
    List<PackageManifestAssetPlacement> placementList = [.. placements];

    // Globs are parsed lazily, so a pattern only used as an exact path is never parsed.
    Glob?[] globs = new Glob?[placementList.Count];

    List<(string Key, string TargetPath)> matches = [];

    foreach (string key in keys) {
      for (int i = 0; i < placementList.Count; i++) {
        PackageManifestAssetPlacement placement = placementList[i];

        switch (placement.Type) {
          case PackageManifestAssetPlacement.PlacementType.File:
            if (placement.Src == key) {
              matches.Add((key, placement.Dst));
            } else if ((globs[i] ??= Glob.Parse(placement.Src)).IsMatch(key)) {
              matches.Add((key, Path.Combine(placement.Dst, Path.GetFileName(key))));
            }

            break;

          case PackageManifestAssetPlacement.PlacementType.Directory:
            // Keep compatibility with older versions of the manifest where src could be empty.
            string relativePath = Path.GetRelativePath(
                string.IsNullOrEmpty(placement.Src) ? "." : placement.Src,
                key);

            if (!relativePath.StartsWith("..")) {
              matches.Add((key, Path.Combine(placement.Dst, relativePath)));
            }

            break;

          default:
            throw new UnreachableException();
        }
      }
    }

    return matches;
  }
}
