using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// One server-sent event of a streamed message. The <c>type</c> member selects the derived type; an event of a
/// type this package does not model reads as a plain <see cref="MessageStreamEvent"/>, so a caller can skip it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(MessageStartEvent), "message_start")]
[JsonDerivedType(typeof(ContentBlockStartEvent), "content_block_start")]
[JsonDerivedType(typeof(ContentBlockDeltaEvent), "content_block_delta")]
[JsonDerivedType(typeof(ContentBlockStopEvent), "content_block_stop")]
[JsonDerivedType(typeof(MessageDeltaEvent), "message_delta")]
[JsonDerivedType(typeof(MessageStopEvent), "message_stop")]
[JsonDerivedType(typeof(PingEvent), "ping")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public record MessageStreamEvent;
