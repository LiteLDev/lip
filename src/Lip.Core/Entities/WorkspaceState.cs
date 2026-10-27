using System.Text.Json.Serialization;

namespace Lip.Core.Entities;

/// The v3 lock (tooth_lock.json), read only to migrate it (see LockStore).
public record WorkspaceState {
  private const int _currentFormatVersion = 3;
  private const string _currentFormatUuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";

  [JsonInclude]
  // [JsonRequired] // For compatibility, it cannot be required.
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
  // [JsonRequired] // For compatibility, it cannot be required.
  [JsonPropertyName("format_uuid")]
  public string FormatUuid {
    get => _currentFormatUuid;
    init {
      if (value != _currentFormatUuid) {
        throw new ArgumentException($"Unsupported format UUID: {value}", nameof(FormatUuid));
      }
    }
  }

  [JsonRequired]
  [JsonPropertyName("packages")]
  public List<WorkspaceStatePackage> Packages { get; init; } = [];
}
