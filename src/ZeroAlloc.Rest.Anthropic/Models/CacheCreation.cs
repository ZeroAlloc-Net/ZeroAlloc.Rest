using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>Cache writes split by time to live.</summary>
/// <param name="Ephemeral5mInputTokens">Tokens written with the five minute time to live.</param>
/// <param name="Ephemeral1hInputTokens">Tokens written with the one hour time to live.</param>
public sealed record CacheCreation(
    [property: JsonPropertyName("ephemeral_5m_input_tokens")] int? Ephemeral5mInputTokens,
    [property: JsonPropertyName("ephemeral_1h_input_tokens")] int? Ephemeral1hInputTokens);
