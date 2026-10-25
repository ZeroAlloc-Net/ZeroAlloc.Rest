using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>More text.</summary>
/// <param name="Text">The added text.</param>
public sealed record TextDelta([property: JsonPropertyName("text")] string Text) : ContentDelta;
