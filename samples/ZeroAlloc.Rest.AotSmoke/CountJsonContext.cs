using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.AotSmoke;

// The source-generated metadata ICountApi's responses are read through.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long))]
internal sealed partial class CountJsonContext : JsonSerializerContext;
