using DotNet.Globbing;

namespace Lip.Core.Mcmod.Compat;

/// Carries out the placements of the assets of an entry derived from format 3 (protocol/package.md
/// §5) by the matching rules of lip v0.37.0.
public static class ToothPlacements {
  /// One (key, target) pair per file placed, in the order lip v0.37.0 wrote them: by asset, then
  /// by key in the asset's own order, then by placement. A key matched by several placements is
  /// placed several times; a later placement of the same target overwrites an earlier one. keys
  /// are the keys of the MountSource the assets are mounted in; targets are workspace paths.
  public static IEnumerable<(string Key, string Target)> Expand(
      IReadOnlyList<AssetPlacement> placements, IReadOnlyCollection<string> keys) {
    foreach (IGrouping<string, AssetPlacement> asset in placements.GroupBy(p => p.Mount)) {
      string prefix = asset.Key + "/";
      List<string> assetKeys = [
        .. keys
            .Where(k => k == asset.Key || k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k == asset.Key ? "" : k[prefix.Length..]),
      ];

      foreach ((string key, string target) in Match(assetKeys, [.. asset])) {
        yield return (MountSource.Key(asset.Key, key), NormalizeTarget(target));
      }
    }
  }

  /// Every (key, target path) pair the placements produce, ordered by key first and placement
  /// second; keys no placement matches are omitted. A file placement takes the key equal to src,
  /// else the keys matching the glob src, placed under dest with their file name; a directory
  /// placement takes the keys below src ("" and "." meaning everything), placed under dest with
  /// their path relative to src. Paths are combined and compared as lip v3 did, through
  /// System.IO.Path on the current OS.
  public static IReadOnlyList<(string Key, string TargetPath)> Match(
      IEnumerable<string> keys, IReadOnlyList<AssetPlacement> placements) {
    // Globs are parsed lazily, so a pattern only used as an exact path is never parsed.
    Glob?[] globs = new Glob?[placements.Count];

    List<(string Key, string TargetPath)> matches = [];

    foreach (string key in keys) {
      for (int i = 0; i < placements.Count; i++) {
        AssetPlacement placement = placements[i];

        if (!placement.IsDirectory) {
          if (placement.Src == key) {
            matches.Add((key, placement.Dest));
          } else if ((globs[i] ??= Glob.Parse(placement.Src)).IsMatch(key)) {
            matches.Add((key, Path.Combine(placement.Dest, Path.GetFileName(key))));
          }

          continue;
        }

        // Keep compatibility with older versions of the manifest where src could be empty.
        string relativePath = Path.GetRelativePath(
            string.IsNullOrEmpty(placement.Src) ? "." : placement.Src,
            key);

        if (!relativePath.StartsWith("..")) {
          matches.Add((key, Path.Combine(placement.Dest, relativePath)));
        }
      }
    }

    return matches;
  }

  /// A target path in the form of a workspace-relative placement dest: '/'-separated, without
  /// empty or "." segments, ".." segments applied. lip v3 resolved targets the same way as full
  /// paths. A path that leaves the workspace keeps its leading ".." segments, and a rooted path
  /// is returned as it is, so that installing rejects them.
  public static string NormalizeTarget(string target) {
    if (Path.IsPathRooted(target) || target.StartsWith('/') || target.StartsWith('\\')) {
      return target.Replace('\\', '/');
    }

    List<string> segments = [];
    foreach (string segment in target.Replace('\\', '/').Split('/')) {
      if (segment is "" or ".") {
        continue;
      }

      if (segment == ".." && segments.Count > 0 && segments[^1] != "..") {
        segments.RemoveAt(segments.Count - 1);
      } else {
        segments.Add(segment);
      }
    }

    return string.Join('/', segments);
  }
}
