using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Lip.Core.Json;

/// The types lip reads from TOML, with metadata generated at build time instead of read by
/// reflection, so that the trimmed lip keeps what reading them needs.
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class LipTomlContext : TomlSerializerContext;
