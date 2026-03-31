using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MessagePack;

public sealed class MessagePackRestSerializer : IRestSerializer
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackRestSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    public MessagePackRestSerializer(MessagePackSerializerOptions options)
        => _options = options;

    public string ContentType => "application/x-msgpack";

    [RequiresDynamicCode("MessagePack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MessagePack serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        return await MessagePackSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
    }

    [RequiresDynamicCode("MessagePack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MessagePack serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await MessagePackSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
}
