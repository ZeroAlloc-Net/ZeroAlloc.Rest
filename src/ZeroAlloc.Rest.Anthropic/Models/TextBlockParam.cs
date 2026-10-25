using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A text block.</summary>
/// <param name="Text">The text.</param>
/// <param name="CacheControl">A cache breakpoint after this block.</param>
public sealed record TextBlockParam(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl = null) : ContentBlockParam;
