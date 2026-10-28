using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>The body of <c>POST /api/embed</c>.</summary>
/// <param name="Model">The embedding model, such as <c>nomic-embed-text</c>.</param>
/// <param name="Input">The texts to embed. A single text is still written as an array of one.</param>
public sealed record EmbedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input);
