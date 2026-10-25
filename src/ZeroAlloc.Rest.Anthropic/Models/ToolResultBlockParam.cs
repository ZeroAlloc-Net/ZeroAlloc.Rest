using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The result of a tool call, sent back to the model.</summary>
/// <param name="ToolUseId">The id of the tool use this answers.</param>
/// <param name="Content">The result text.</param>
/// <param name="IsError">True when the tool failed.</param>
/// <param name="CacheControl">A cache breakpoint after this block.</param>
public sealed record ToolResultBlockParam(
    [property: JsonPropertyName("tool_use_id")] string ToolUseId,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("is_error")] bool? IsError,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl = null) : ContentBlockParam;
