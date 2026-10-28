using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The first event: the message with empty content.</summary>
/// <param name="Message">The message so far.</param>
public sealed record MessageStartEvent([property: JsonPropertyName("message")] MessageResponse Message)
    : MessageStreamEvent;
