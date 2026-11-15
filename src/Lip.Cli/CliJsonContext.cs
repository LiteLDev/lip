using System.Text.Json;
using System.Text.Json.Serialization;
using Lip.Cli.Commands;

namespace Lip.Cli;

/// The types the commands write as JSON, with metadata generated at build time instead of read
/// by reflection, so that the trimmed lip keeps what writing them needs.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(IDictionary<string, string>))]
[JsonSerializable(typeof(ListCommand.Installed))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
