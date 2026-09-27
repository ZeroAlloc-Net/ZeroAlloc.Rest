using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MessagePack;

public sealed class MessagePackRestSerializer : IRestSerializer
{
    private const string ReflectionMessage =
        "MessagePackSerializerOptions.Standard, and options whose resolver falls back to it, build formatters "
        + "with reflection and dynamic code, which need members the trimmer may remove and code Native AOT "
        + "cannot generate. For Native AOT, serialize with a JsonSerializerContext through SystemTextJsonSerializer, "
        + "or with MemoryPackRestSerializer and registered types.";

    private readonly MessagePackSerializerOptions _options;

    /// <summary>Serializes with <see cref="MessagePackSerializerOptions.Standard"/>, which uses reflection.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MessagePackRestSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    /// <summary>
    /// Serializes with these options. Their resolver may use reflection, which this constructor
    /// cannot rule out, so it carries the trim and AOT annotations whatever the resolver is.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MessagePackRestSerializer(MessagePackSerializerOptions options)
        => _options = options;

    public string ContentType => "application/x-msgpack";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        return await MessagePackSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await MessagePackSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
}
