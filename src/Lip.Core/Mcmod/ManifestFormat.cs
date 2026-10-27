namespace Lip.Core.Mcmod;

/// Constants of package format version 4 (manifest.json / .mcmod).
public static class ManifestFormat {
  public const int Version = 4;
  public const string Uuid = "0c6b977a-64ad-441f-9a29-c7f4b196882f";

  public const string FormatVersionKey = "formatVersion";
  public const string FormatUuidKey = "formatUuid";

  public const string ManifestFileName = "manifest.json";

  /// Root file of the legacy format (v1–v3), read for compatibility only (spec §9).
  public const string ToothFileName = "tooth.json";

  /// A resolution chain longer than this many hops yields no candidate (spec §5.2).
  public const int MaxChainHops = 8;
}
