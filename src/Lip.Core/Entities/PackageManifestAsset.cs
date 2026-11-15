using System.Text.Json.Serialization;
using Flurl;

namespace Lip.Core.Entities;

public record PackageManifestAsset {
  [JsonConverter(typeof(JsonStringEnumConverter<AssetType>))]
  public enum AssetType {
    [JsonStringEnumMemberName("self")]
    Self,
    [JsonStringEnumMemberName("tar")]
    Tar,
    [JsonStringEnumMemberName("tgz")]
    Tgz,
    [JsonStringEnumMemberName("uncompressed")]
    Uncompressed,
    [JsonStringEnumMemberName("zip")]
    Zip,
  }

  [JsonPropertyName("type")]
  public required AssetType Type { get; init; }

  [JsonConverter(typeof(Core.Json.UrlListJsonConverter))]
  [JsonPropertyName("urls")]
  public List<Url> Urls { get; init; } = [];

  [JsonPropertyName("placements")]
  public List<PackageManifestAssetPlacement> Placements { get; init; } = [];
}
