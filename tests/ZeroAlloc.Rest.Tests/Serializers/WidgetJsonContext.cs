using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Tests.Serializers;

public sealed record Widget(int Id, string Name);

public sealed record Unregistered(int Id);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Widget))]
internal sealed partial class WidgetJsonContext : JsonSerializerContext;
