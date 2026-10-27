using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Globbing;
using Lip.Core.Json;
using Lip.Core.Migration.PackageManifests;
using Semver;

namespace Lip.Core.Entities;

public record PackageManifest {
  private const int _currentFormatVersion = 3;
  private const string _currentFormatUuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  [JsonInclude]
  [JsonRequired]
  [JsonPropertyName("format_version")]
  public int FormatVersion {
    get => _currentFormatVersion;
    init {
      if (value != _currentFormatVersion) {
        throw new ArgumentException($"Unsupported format version: {value}", nameof(FormatVersion));
      }
    }
  }

  [JsonInclude]
  [JsonRequired]
  [JsonPropertyName("format_uuid")]
  public string FormatUuid {
    get => _currentFormatUuid;
    init {
      if (value != _currentFormatUuid) {
        throw new ArgumentException($"Unsupported format UUID: {value}", nameof(FormatUuid));
      }
    }
  }

  [JsonPropertyName("tooth")]
  public required string Path {
    get;
    init => field = (Golang.Org.X.Mod.Module.CheckPath(value) is null)
        ? value
        : throw new FormatException($"Invalid package path: {value}");
  }

  [JsonConverter(typeof(SemVersionJsonConverter))]
  [JsonPropertyName("version")]
  public required SemVersion Version { get; init; }

  [JsonPropertyName("info")]
  public PackageManifestInfo Info { get; init; } = new();

  [JsonPropertyName("variants")]
  public List<PackageManifestVariant> Variants { get; init; } = [];

  public static async Task<PackageManifest> FromStream(Stream stream) {
    JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream);

    return PackageManifestMigration.Migrate(jsonDocument);
  }

  public PackageManifestVariant GetVariant(string label) =>
      GetVariant(label, RuntimeInformation.RuntimeIdentifier);

  /// Merges every variant with the given label whose platform matches the runtime identifier
  /// (exactly or as a glob). Throws KeyNotFoundException when no variant matches.
  public PackageManifestVariant GetVariant(string label, string rid) {
    string platform = rid;

    List<PackageManifestVariant> matchingVariants = [];
    foreach (PackageManifestVariant variant in Variants) {
      if (label != variant.Label) {
        continue;
      }

      if (
          variant.Platform != ""
          && !(platform == variant.Platform)
          && !Glob.Parse(variant.Platform).IsMatch(platform)) {
        continue;
      }

      matchingVariants.Add(variant);
    }

    // At least one full match is required to indicate that the current
    // platform is supported for the given variant.
    if (matchingVariants.All(
        v => (v.Label != label) && (v.Platform != platform)
    )) {
      throw new KeyNotFoundException($"No variant found for label '{label}' and platform '{platform}'.");
    }

    return new() {
      Label = label,
      Platform = platform,
      Dependencies = matchingVariants
            .SelectMany(variant => variant.Dependencies)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
      Assets = [.. matchingVariants.SelectMany(variant => variant.Assets)],
      PreserveFiles = [.. matchingVariants.SelectMany(variant => variant.PreserveFiles)],
      RemoveFiles = [.. matchingVariants.SelectMany(variant => variant.RemoveFiles)],
      Scripts = new() {
        PreInstall = matchingVariants.SingleOrDefault(v => v.Scripts.PreInstall.Count > 0)?.Scripts.PreInstall ?? [],
        Install = matchingVariants.SingleOrDefault(v => v.Scripts.Install.Count > 0)?.Scripts.Install ?? [],
        PostInstall = matchingVariants.SingleOrDefault(v => v.Scripts.PostInstall.Count > 0)?.Scripts.PostInstall ?? [],
        PreUninstall = matchingVariants.SingleOrDefault(v => v.Scripts.PreUninstall.Count > 0)?.Scripts.PreUninstall ?? [],
        Uninstall = matchingVariants.SingleOrDefault(v => v.Scripts.Uninstall.Count > 0)?.Scripts.Uninstall ?? [],
        PostUninstall = matchingVariants.SingleOrDefault(v => v.Scripts.PostUninstall.Count > 0)?.Scripts.PostUninstall ?? [],
      }
    };
  }
}
