using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

// The only JsonSerializerContext the sample carries: JsonContextSerializer reads and writes
// through its source-generated JsonTypeInfo alone, so no type reaches JsonSerializer through
// reflection.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(PingLevel))]
[JsonSerializable(typeof(PingPoint?))]
internal sealed partial class PingJsonContext : JsonSerializerContext;
