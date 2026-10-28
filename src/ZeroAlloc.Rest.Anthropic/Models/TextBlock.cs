using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>Text the model wrote.</summary>
/// <param name="Text">The text.</param>
public sealed record TextBlock([property: JsonPropertyName("text")] string Text) : ContentBlock;
