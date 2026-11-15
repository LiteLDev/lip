using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lip.Core.Entities;

namespace Lip.Core.Json;

/// Types lip serializes, with metadata generated at build time instead of read by reflection.
/// Not for the types with init-only properties that have default values (see ReflectionJson).
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(PackageManifestAsset.AssetType))]
internal sealed partial class LipJsonContext : JsonSerializerContext;
