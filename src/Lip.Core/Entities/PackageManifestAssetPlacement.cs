using System.Text.Json.Serialization;

namespace Lip.Core.Entities;

public record PackageManifestAssetPlacement {
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum PlacementType {
    [JsonStringEnumMemberName("file")]
    File,
    [JsonStringEnumMemberName("dir")]
    Directory,
  }

  [JsonPropertyName("type")]
  public required PlacementType Type { get; init; }

  [JsonPropertyName("src")]
  public required string Src { get; init; }

  [JsonPropertyName("dest")]
  public required string Dst {
    get;
    init => field = IsValidDst(value)
        ? value
        : throw new ArgumentException(
            $"In tooth.json, the placement destination '{value}' is not allowed: it must be a "
            + "relative path inside the workspace, without '..'.");
  }

  public static bool IsValidDst(string path) => !(Path.IsPathFullyQualified(path) || Path.IsPathRooted(path) || path.Contains(".."));
}
