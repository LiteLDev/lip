using System.Text.Json.Serialization;

namespace Lip.Core.Entities;

/// A package of the v3 lock (tooth_lock.json), read only to migrate it (see LockStore).
public record WorkspaceStatePackage {
  [JsonPropertyName("files")]
  public required List<string> Files { get; init; }

  [JsonPropertyName("locked")]
  public required bool IsExplicit { get; init; }

  [JsonPropertyName("manifest")]
  public required PackageManifest Manifest { get; init; }

  [JsonPropertyName("variant")]
  public required string Variant {
    get;
    init => field = PackageId.IsValidVariant(value)
        ? value
        : throw new ArgumentException($"Invalid package variant: {value}", nameof(Variant));
  }
}
