namespace Lip.Core.Mcmod;

/// Constants of the package file (protocol/package.md §1, lip.schema.toml) and of the workspace
/// file (protocol/workspace.md §7.1, state.schema.json).
public static class PackageFormat {
  /// The package file: the TOML file at the root of an archive that makes it a package.
  public const string FileName = "lip.toml";

  /// The format of package files, and of the state file and the configuration.
  public const int Format = 4;

  public const string FormatKey = "format";

  /// The LeviLamina manifest at the root of the mcmod asset of a mod, whose "name" is the mod name
  /// (protocol/package.md §4). lip reads nothing else of it and never writes it.
  public const string ModManifestFileName = "manifest.json";
}
