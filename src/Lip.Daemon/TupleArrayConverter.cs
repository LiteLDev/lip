using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace Lip.Daemon;

/// Writes a value tuple as a JSON array of its items, e.g. the result of List as
/// [string[], string[]] (refs/lipd.md §3) rather than {"Item1": ..., "Item2": ...}.
public sealed class TupleArrayConverter : JsonConverter {
  public override bool CanRead => false;

  public override bool CanConvert(Type objectType) =>
      objectType.IsValueType && objectType.IsGenericType
      && objectType.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true;

  public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
    ITuple tuple = (ITuple)value!;
    writer.WriteStartArray();
    for (int i = 0; i < tuple.Length; i++) {
      serializer.Serialize(writer, tuple[i]);
    }
    writer.WriteEndArray();
  }

  public override object? ReadJson(
      JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
      throw new NotSupportedException();
}
