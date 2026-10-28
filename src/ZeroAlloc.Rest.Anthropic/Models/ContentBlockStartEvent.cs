using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A content block begins.</summary>
/// <param name="Index">The block index.</param>
/// <param name="ContentBlock">The block, with empty text or input.</param>
public sealed record ContentBlockStartEvent(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("content_block")] ContentBlock ContentBlock) : MessageStreamEvent;
