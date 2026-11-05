namespace Lip.Core.Mcmod;

/// Constants of the tooth file (protocol/package.md §1, tooth.schema.json) and of the workspace
/// file (protocol/workspace.md §7.1).
public static class ToothFormat {
  /// The file at the root of an archive that makes it a package.
  public const string ToothFileName = "tooth.json";

  /// The format_version of tooth files of this format, and of the workspace file.
  public const int FormatVersion = 4;

  public const string FormatVersionKey = "format_version";

  /// The format_uuid of tooth files of format 4.
  public const string FormatUuid = "f74a5fda-cb2a-4de8-959f-a1a76bad3102";

  /// The LeviLamina manifest of a mod, in its mod directory (protocol/workspace.md §4 step 3).
  public const string ModManifestFileName = "manifest.json";
}
