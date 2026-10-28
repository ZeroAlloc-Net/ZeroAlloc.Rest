using System.Text.Json.Serialization;
using System.Text.Json;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A tool the model may call.</summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">What the tool does and when to use it.</param>
/// <param name="InputSchema">The JSON Schema of the tool input, written through unchanged.</param>
/// <param name="CacheControl">A cache breakpoint on this tool.</param>
public sealed record ToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("input_schema")] JsonElement InputSchema,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl);
