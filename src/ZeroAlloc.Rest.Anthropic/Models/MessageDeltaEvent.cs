using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>Top-level message changes, with cumulative usage.</summary>
/// <param name="Delta">The stop reason and stop sequence.</param>
/// <param name="Usage">Usage so far; members the event omits read as null.</param>
public sealed record MessageDeltaEvent(
    [property: JsonPropertyName("delta")] MessageDeltaBody Delta,
    [property: JsonPropertyName("usage")] MessageDeltaUsage? Usage) : MessageStreamEvent;
