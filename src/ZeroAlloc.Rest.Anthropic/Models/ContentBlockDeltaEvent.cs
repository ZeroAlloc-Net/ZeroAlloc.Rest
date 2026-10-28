using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A content block grows.</summary>
/// <param name="Index">The block index.</param>
/// <param name="Delta">The change.</param>
public sealed record ContentBlockDeltaEvent(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] ContentDelta Delta) : MessageStreamEvent;
