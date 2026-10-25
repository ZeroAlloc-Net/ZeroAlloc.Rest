using System.Text.Json.Serialization;
using System.Text.Json;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A tool call the model made.</summary>
/// <param name="Id">The tool use id.</param>
/// <param name="Name">The tool name.</param>
/// <param name="Input">The tool input.</param>
public sealed record ToolUseBlock(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("input")] JsonElement Input) : ContentBlock;
