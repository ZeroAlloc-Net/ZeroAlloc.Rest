using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>Token usage, with the prompt cache split.</summary>
/// <param name="InputTokens">Input tokens after the last cache breakpoint.</param>
/// <param name="OutputTokens">Output tokens.</param>
/// <param name="CacheCreationInputTokens">Tokens written to the cache.</param>
/// <param name="CacheReadInputTokens">Tokens read from the cache.</param>
/// <param name="CacheCreation">The cache writes split by time to live.</param>
public sealed record Usage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("cache_creation_input_tokens")] int? CacheCreationInputTokens = null,
    [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens = null,
    [property: JsonPropertyName("cache_creation")] CacheCreation? CacheCreation = null);
