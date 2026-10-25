using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.AotSmoke;

// The source-generated metadata IEventApi's event data is read through.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Tick))]
[JsonSerializable(typeof(FeedStart))]
[JsonSerializable(typeof(FeedTick))]
internal sealed partial class EventJsonContext : JsonSerializerContext;
