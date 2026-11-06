using System.Text.Json.Serialization;
using DotNet.Globbing;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Entities;

public record PackageManifestVariant {
  [JsonPropertyName("label")]
  public string Label {
    get;
    init => field = PackageId.IsValidVariant(value)
        ? value
        : throw new FormatException("In tooth.json, a variant has a label that is not valid. "
            + PackageId.VariantError(value));
  } = "";

  [JsonPropertyName("platform")]
  public string Platform { get; init; } = "";

  [JsonConverter(typeof(PackageIdToSemVersionRangeDictionary))]
  [JsonPropertyName("dependencies")]
  public Dictionary<PackageId, SemVersionRange> Dependencies { get; init; } = [];

  [JsonPropertyName("assets")]
  public List<PackageManifestAsset> Assets { get; init; } = [];

  [JsonConverter(typeof(GlobListJsonConverter))]
  [JsonPropertyName("preserve_files")]
  public List<Glob> PreserveFiles { get; init; } = [];

  [JsonConverter(typeof(GlobListJsonConverter))]
  [JsonPropertyName("remove_files")]
  public List<Glob> RemoveFiles { get; init; } = [];

  [JsonPropertyName("scripts")]
  public PackageManifestScripts Scripts { get; init; } = new();
}
