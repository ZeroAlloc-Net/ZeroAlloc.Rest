using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MemoryPack;

public sealed class MemoryPackRestSerializer : IRestSerializer
{
    public string ContentType => "application/x-memorypack";

    [RequiresDynamicCode("MemoryPack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MemoryPack serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        var bytes = await ReadAllBytesAsync(stream, ct).ConfigureAwait(false);
        return MemoryPackSerializer.Deserialize<T>(bytes);
    }

    [RequiresDynamicCode("MemoryPack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MemoryPack serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        var buffer = new ArrayBufferWriter<byte>();
        MemoryPackSerializer.Serialize(buffer, value);
        await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            var remaining = (int)(stream.Length - stream.Position);
            var buffer = new byte[remaining];
            var totalRead = 0;
            while (totalRead < remaining)
            {
                var read = await stream.ReadAsync(buffer, totalRead, remaining - totalRead, ct).ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }
            return buffer;
        }
        else
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, 81920, ct).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
