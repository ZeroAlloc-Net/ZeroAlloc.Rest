using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>More of a tool input, as a fragment of JSON text.</summary>
/// <param name="PartialJson">The added JSON fragment.</param>
public sealed record InputJsonDelta([property: JsonPropertyName("partial_json")] string PartialJson)
    : ContentDelta;
