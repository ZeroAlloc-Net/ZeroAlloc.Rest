using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>The answer of <c>POST /api/embed</c>.</summary>
/// <param name="Model">The model that produced the embeddings.</param>
/// <param name="Embeddings">One vector per input, in input order.</param>
/// <param name="TotalDuration">The total time in nanoseconds, when Ollama reports it.</param>
/// <param name="LoadDuration">The model load time in nanoseconds, when Ollama reports it.</param>
/// <param name="PromptEvalCount">The number of prompt tokens evaluated, when Ollama reports it.</param>
public sealed record EmbedResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("embeddings")] IReadOnlyList<float[]> Embeddings,
    [property: JsonPropertyName("total_duration")] long? TotalDuration = null,
    [property: JsonPropertyName("load_duration")] long? LoadDuration = null,
    [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount = null);
