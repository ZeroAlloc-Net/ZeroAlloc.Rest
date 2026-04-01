using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace ZeroAlloc.Rest;

/// <summary>
/// Adapts any <see cref="ZeroAlloc.Serialisation.ISerializer{T}"/> to <see cref="IRestSerializer"/>,
/// bridging the IBufferWriter-based zero-alloc path to the Stream-based REST surface.
/// </summary>
/// <remarks>
/// This adapter is intentionally typed to a single T. For open-generic REST scenarios where T is
/// unknown at registration time, use the format-specific IRestSerializer implementations directly.
/// </remarks>
public sealed class RestSerializerAdapter<T> : IRestSerializer
{
    private readonly ZeroAlloc.Serialisation.ISerializer<T> _serializer;
    private readonly string _contentType;

    public RestSerializerAdapter(ZeroAlloc.Serialisation.ISerializer<T> serializer, string contentType)
    {
        _serializer = serializer;
        _contentType = contentType;
    }

    public string ContentType => _contentType;

    [RequiresDynamicCode("Serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("Serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask<TResult?> DeserializeAsync<TResult>(Stream stream, CancellationToken ct = default)
    {
        if (typeof(TResult) != typeof(T))
            throw new InvalidOperationException(
                $"{nameof(RestSerializerAdapter<T>)} can only deserialize {typeof(T).Name}, not {typeof(TResult).Name}.");

        var bytes = await ReadAllBytesAsync(stream, ct).ConfigureAwait(false);
        var result = _serializer.Deserialize(bytes);
        return (TResult?)(object?)result;
    }

    [RequiresDynamicCode("Serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("Serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask SerializeAsync<TValue>(Stream stream, TValue value, CancellationToken ct = default)
    {
        if (value is not T typed)
            throw new InvalidOperationException(
                $"{nameof(RestSerializerAdapter<T>)} can only serialize {typeof(T).Name}, not {typeof(TValue).Name}.");

        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, typed);
        await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            var remaining = (int)(stream.Length - stream.Position);
            var buf = new byte[remaining];
            var totalRead = 0;
            while (totalRead < remaining)
            {
                var read = await stream.ReadAsync(buf, totalRead, remaining - totalRead, ct).ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }
            return buf;
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, 81920, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}
