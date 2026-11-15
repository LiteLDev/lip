using System.IO.Abstractions;
using Lip.Core.Mcmod;

namespace Lip.Core.Config;

/// An installed mod and its configuration files, paths relative to the workspace and
/// '/'-separated.
public sealed record ModConfig(string ModName, string Identity, IReadOnlyList<string> Files);

/// Finds the configuration files of the installed mods (protocol/lip-tui.md, "Configuration").
public static class ModConfigs {
  private static readonly string[] _extensions = [".json", ".ini", ".toml", ".yml", ".yaml"];

  /// The installed mods of the state file, in its order, with the configuration files in their
  /// mod directories.
  public static IReadOnlyList<ModConfig> Find(IFileSystem fs, string root, LockFile? lockFile) {
    List<ModConfig> mods = [];
    foreach (LockedPackage package in lockFile?.Packages ?? []) {
      if (!IsMod(package, lockFile!) || ModName(package) is not { } name) {
        continue;
      }

      mods.Add(new ModConfig(name, package.Identity, Files(fs, root, name)));
    }

    return mods;
  }

  /// The configuration files of the mod directory mods/name/.
  public static IReadOnlyList<string> Files(IFileSystem fs, string root, string name) {
    string directory = fs.Path.Join(root, WorkspaceLayout.ModsDirectory, name);
    string prefix = $"{WorkspaceLayout.ModsDirectory}/{name}/";
    string config = fs.Path.Join(directory, "config");
    List<string> files = [];
    if (fs.Directory.Exists(config)) {
      files.AddRange(fs.Directory.EnumerateFiles(config, "*", SearchOption.AllDirectories)
          .Select(file => prefix + fs.Path.GetRelativePath(directory, file).Replace('\\', '/')));
    }

    if (files.Count == 0 && fs.Directory.Exists(directory)) {
      files.AddRange(fs.Directory.EnumerateFiles(directory)
          .Select(fs.Path.GetFileName)
          .Where(file => file is not null
              && !file.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
              && _extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
          .Select(file => prefix + file));
      if (fs.File.Exists(fs.Path.Join(directory, "data", "config.json"))) {
        files.Add(prefix + "data/config.json");
      }
    }

    files.Sort(StringComparer.Ordinal);
    return files;
  }

  /// Whether the entry of the package for the side and the platform of the workspace has an
  /// asset of type "mcmod" (protocol/lip-package.md, "Resolution").
  private static bool IsMod(LockedPackage package, LockFile lockFile) {
    if (!McmodSideExtensions.TryParse(lockFile.Side, out McmodSide side)) {
      return false;
    }

    try {
      return PackageFileParser.Parse(package.Manifest)
          .SelectEntry(package.Variant, new LipEnvironment(side, lockFile.Platform))
          ?.McmodAsset is not null;
    }
    catch (PackageFormatException) {
      return false;
    }
  }

  /// The mod name: the directory below mods/ of the manifest.json the package owns, or of its
  /// first file there.
  private static string? ModName(LockedPackage package) {
    string? fallback = null;
    foreach (string file in package.Files) {
      string[] segments = file.Split('/');
      if (segments.Length < 3 || segments[0] != WorkspaceLayout.ModsDirectory) {
        continue;
      }

      if (segments.Length == 3 && segments[2] == "manifest.json") {
        return segments[1];
      }

      fallback ??= segments[1];
    }

    return fallback;
  }
}
