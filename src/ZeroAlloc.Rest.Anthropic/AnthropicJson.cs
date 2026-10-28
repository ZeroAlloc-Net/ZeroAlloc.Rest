using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The public, AOT-safe way to serialize this package's types from another assembly. It exposes the
/// source-generated metadata only, and nothing here falls back to reflection.
/// </summary>
public static class AnthropicJson
{
    /// <summary>
    /// The source-generated context for every type of this package, with the options the wire format needs.
    /// Build the serializer from it: <c>new SystemTextJsonSerializer(AnthropicJson.Context)</c>. That constructor
    /// is free of trim and AOT annotations and takes the context's options along with its metadata.
    /// </summary>
    public static JsonSerializerContext Context => AnthropicJsonContext.Default;

    /// <summary>
    /// The options the wire format needs: null members are omitted, and a type discriminator may come in any
    /// position. They are the options of <see cref="Context"/>.
    /// </summary>
    public static JsonSerializerOptions Options => AnthropicJsonContext.Default.Options;

    /// <summary>Metadata to write a <see cref="MessageRequest"/>.</summary>
    public static JsonTypeInfo<MessageRequest> MessageRequest => AnthropicJsonContext.Default.MessageRequest;

    /// <summary>Metadata to read a <see cref="MessageResponse"/>.</summary>
    public static JsonTypeInfo<MessageResponse> MessageResponse => AnthropicJsonContext.Default.MessageResponse;

    /// <summary>Metadata to read a <see cref="MessageStreamEvent"/> from the data of one server-sent event.</summary>
    public static JsonTypeInfo<MessageStreamEvent> MessageStreamEvent => AnthropicJsonContext.Default.MessageStreamEvent;
}
