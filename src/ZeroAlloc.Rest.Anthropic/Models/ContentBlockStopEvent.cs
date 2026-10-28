using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A content block ends.</summary>
/// <param name="Index">The block index.</param>
public sealed record ContentBlockStopEvent([property: JsonPropertyName("index")] int Index) : MessageStreamEvent;
