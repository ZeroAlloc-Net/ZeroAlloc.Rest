using System.Text.Json.Serialization;
using System.Text.Json;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A tool call the model made, replayed in the history.</summary>
/// <param name="Id">The tool use id.</param>
/// <param name="Name">The tool name.</param>
/// <param name="Input">The tool input, written through unchanged.</param>
/// <param name="CacheControl">A cache breakpoint after this block.</param>
public sealed record ToolUseBlockParam(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("input")] JsonElement Input,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl = null) : ContentBlockParam;
