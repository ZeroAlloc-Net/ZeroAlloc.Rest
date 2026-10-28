using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>One turn of the conversation sent to the model.</summary>
/// <param name="Role">Who speaks.</param>
/// <param name="Content">The content blocks of the turn.</param>
public sealed record MessageParam(
    [property: JsonPropertyName("role")] MessageRole Role,
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlockParam> Content);
