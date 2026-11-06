using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lip.Core.Entities;

/// The info of a tooth file of format 3; a member is null when the tooth file does not have it.
public partial record PackageManifestInfo {
  [JsonPropertyName("name")]
  public string? Name { get; init; }

  [JsonPropertyName("description")]
  public string? Description { get; init; }

  [JsonPropertyName("tags")]
  public List<string>? Tags {
    get;
    init {
      string? invalidTag = value?.FirstOrDefault(tag => !TagRegex().IsMatch(tag));
      field = (invalidTag is null)
          ? value
          : throw new FormatException($"Invalid package tag: {invalidTag}");
    }
  }

  [GeneratedRegex(@"^[a-z0-9-]+(:[a-z0-9-]+)?$")]
  private static partial Regex TagRegex();

  [JsonPropertyName("avatar_url")]
  public string? AvatarUrl { get; init; }
}
