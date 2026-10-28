using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The usage a <c>message_delta</c> event carries. Every member is optional on the wire and is cumulative, so a
/// member the event omits reads as null, never as 0: merge with the start event's <see cref="Usage"/> by taking
/// only the members that are not null.
/// </summary>
/// <param name="InputTokens">Input tokens, when the event carries them.</param>
/// <param name="OutputTokens">Output tokens so far.</param>
/// <param name="CacheCreationInputTokens">Tokens written to the cache, when the event carries them.</param>
/// <param name="CacheReadInputTokens">Tokens read from the cache, when the event carries them.</param>
/// <param name="CacheCreation">The cache writes split by time to live, when the event carries them.</param>
public sealed record MessageDeltaUsage(
    [property: JsonPropertyName("input_tokens")] int? InputTokens,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    [property: JsonPropertyName("cache_creation_input_tokens")] int? CacheCreationInputTokens = null,
    [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens = null,
    [property: JsonPropertyName("cache_creation")] CacheCreation? CacheCreation = null);
