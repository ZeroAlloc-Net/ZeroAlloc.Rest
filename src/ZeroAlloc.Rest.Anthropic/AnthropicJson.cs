using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The public, AOT-safe way to serialize this package's types from another assembly. It exposes the
/// source-generated metadata only, and nothing here falls back to reflection.
/// </summary>
public static class AnthropicJson
{
    /// <summary>
    /// A resolver that covers every type of this package. A resolver alone carries no serializer options, so pass
    /// <see cref="Options"/> with it: <c>new SystemTextJsonSerializer(AnthropicJson.Resolver, AnthropicJson.Options)</c>.
    /// </summary>
    public static IJsonTypeInfoResolver Resolver => AnthropicJsonContext.Default;

    /// <summary>
    /// The options the wire format needs: null members are omitted, and a type discriminator may come in any
    /// position. Copy them for a serializer that takes a resolver; do not rely on the resolver for these.
    /// </summary>
    public static JsonSerializerOptions Options => AnthropicJsonContext.Default.Options;

    /// <summary>Metadata to write a <see cref="MessageRequest"/>.</summary>
    public static JsonTypeInfo<MessageRequest> MessageRequest => AnthropicJsonContext.Default.MessageRequest;

    /// <summary>Metadata to read a <see cref="MessageResponse"/>.</summary>
    public static JsonTypeInfo<MessageResponse> MessageResponse => AnthropicJsonContext.Default.MessageResponse;

    /// <summary>Metadata to read a <see cref="MessageStreamEvent"/> from the data of one server-sent event.</summary>
    public static JsonTypeInfo<MessageStreamEvent> MessageStreamEvent => AnthropicJsonContext.Default.MessageStreamEvent;
}
