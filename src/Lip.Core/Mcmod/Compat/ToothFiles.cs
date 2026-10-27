using System.Diagnostics;
using System.IO.Abstractions;
using DotNet.Globbing;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Compat;

/// The file operations of tooth.json v1–v3 packages (spec §9: tooth archives follow the v1–v3
/// rules), shared by ToothInstaller and the v3 PackageInstaller.
public static class ToothFiles {
  /// Places the assets of a variant under root, in manifest order. "self" assets are read from
  /// self; other assets are fetched from their URLs, trying each in turn. Existing files are
  /// overwritten. Returns the absolute paths written, in order (a path may repeat).
  public static async Task<List<string>> PlaceAssets(
      IFileSystem fileSystem,
      ISourceService sourceService,
      PackageManifestVariant variant,
      ISource self,
      string root) {
    List<string> placedFiles = [];

    foreach (PackageManifestAsset asset in variant.Assets) {
      ISource assetSource = asset.Type switch {
        PackageManifestAsset.AssetType.Self => self,
        PackageManifestAsset.AssetType.Uncompressed =>
            await GetSource(sourceService, asset.Urls, isArchive: false),
        PackageManifestAsset.AssetType.Tar =>
            await GetSource(sourceService, asset.Urls, isArchive: true),
        PackageManifestAsset.AssetType.Tgz =>
            await GetSource(sourceService, asset.Urls, isArchive: true),
        PackageManifestAsset.AssetType.Zip =>
            await GetSource(sourceService, asset.Urls, isArchive: true),
        _ => throw new UnreachableException(),
      };

      // Match placements first, so lazy sources only download the files that are placed.
      IReadOnlyList<(string Key, string TargetPath)> matches = PlacementMatcher.Match(
          assetSource.Keys,
          asset.Placements);

      if (matches.Count == 0) {
        continue;
      }

      await assetSource.Prefetch(matches.Select(m => m.Key).Distinct());

      foreach ((string key, string targetPath) in matches) {
        string fullPath = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, targetPath));

        using Stream sourceStream = await assetSource.OpenRead(key);
        using Stream targetStream = fileSystem.CreateFileWithDirectory(fullPath);
        await sourceStream.CopyToAsync(targetStream);

        placedFiles.Add(fullPath);
      }
    }

    return placedFiles;
  }

  /// Whether a file matches the variant's preserve_files. relativePath is relative to the
  /// workspace root and '/'-separated. The bare file name is matched as well, so that older
  /// manifests relying on name-only matching keep preserving the same files.
  public static bool IsPreserved(PackageManifestVariant variant, string relativePath) {
    string fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];

    return variant.PreserveFiles.Any(glob => glob.IsMatch(relativePath) || glob.IsMatch(fileName));
  }

  /// Removes every path under root matching the variant's remove_files. Globs are matched
  /// against '/'-separated paths relative to root; directories are also matched with a
  /// trailing '/' and removed recursively.
  public static void RemoveFiles(
      IFileSystem fileSystem,
      PackageManifestVariant variant,
      string root) {
    foreach (Glob glob in variant.RemoveFiles) {
      List<string> pathsToRemove = [];

      foreach (string path in fileSystem.Directory.EnumerateFileSystemEntries(
          root,
          "*",
          SearchOption.AllDirectories)) {
        // Match the glob against the path relative to the root, so a pattern like `extra.log`
        // only matches `./extra.log` instead of any file with the same name in any
        // subdirectory.
        string relativePath = GetRelativePath(fileSystem, root, path);

        if (string.IsNullOrEmpty(relativePath)) {
          continue;
        }

        // A trailing separator is required to match a directory, so also match patterns like
        // `temp/` against directories.
        if (glob.IsMatch(relativePath)
            || (fileSystem.Directory.Exists(path) && glob.IsMatch($"{relativePath}/"))) {
          pathsToRemove.Add(path);
        }
      }

      // Delete deeper paths first so that removing a directory recursively does not invalidate
      // the other paths collected above.
      foreach (string path in pathsToRemove.OrderByDescending(
          p => p.Count(c => c is '/' or '\\'))) {
        if (fileSystem.File.Exists(path)) {
          fileSystem.File.Delete(path);
        } else if (fileSystem.Directory.Exists(path)) {
          fileSystem.Directory.Delete(path, recursive: true);
        }
      }
    }
  }

  /// Returns path relative to root with '/' as the separator, "" for the root itself, or the
  /// full '/'-separated path when it is outside root.
  public static string GetRelativePath(IFileSystem fileSystem, string root, string path) {
    static string Normalize(string value) => value
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');

    // Resolve both paths through the file system abstraction, because the absolute path of a
    // relative path is not based on the process working directory when a mock file system is
    // used.
    string rootPath = Normalize(fileSystem.Path.GetFullPath(root)).TrimEnd('/');
    string fullPath = Normalize(fileSystem.Path.GetFullPath(path));

    if (fullPath == rootPath) {
      return string.Empty;
    }

    // Require a directory boundary after the root, so a sibling directory with the same prefix
    // is not treated as a path inside the root.
    return fullPath.StartsWith($"{rootPath}/", StringComparison.OrdinalIgnoreCase)
        ? fullPath[(rootPath.Length + 1)..]
        : fullPath;
  }

  /// Gets a source from the first URL that works.
  public static async Task<ISource> GetSource(
      ISourceService sourceService,
      IEnumerable<Url> urls,
      bool isArchive) {
    List<Exception> exceptions = [];

    foreach (Url url in urls) {
      try {
        return await sourceService.Get(url, isArchive);
      }
      catch (Exception ex) {
        exceptions.Add(new Exception($"Failed to get file source from {url}", ex));
      }
    }

    throw new AggregateException("Failed to get file source from all provided URLs", exceptions);
  }
}
