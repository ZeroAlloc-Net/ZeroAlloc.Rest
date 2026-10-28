using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The changed top-level members of the message.</summary>
/// <param name="StopReason">Why generation stopped, see <see cref="StopReasons"/>.</param>
/// <param name="StopSequence">The stop sequence that matched, if any.</param>
public sealed record MessageDeltaBody(
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence);
