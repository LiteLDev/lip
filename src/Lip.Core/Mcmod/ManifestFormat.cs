namespace Lip.Core.Mcmod;

/// Constants of package format bf45b1 (manifest.json / .mcmod) and of the lock.
public static class ManifestFormat {
  /// The value of the "format" key of documents and locks of this format (spec §4, §9).
  public const string Format = "bf45b1";

  public const string FormatKey = "format";

  public const string ManifestFileName = "manifest.json";

  /// Root file of the legacy tooth.json format (v1–v3), read for compatibility only (spec §10.2).
  public const string ToothFileName = "tooth.json";

  /// A resolution chain longer than this many hops yields no candidate (spec §5.2).
  public const int MaxChainHops = 8;
}
