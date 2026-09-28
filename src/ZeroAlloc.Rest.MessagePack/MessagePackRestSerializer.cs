using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MessagePack;

/// <summary>An <see cref="IRestSerializer"/> for application/x-msgpack, backed by MessagePack.</summary>
public sealed class MessagePackRestSerializer : IRestSerializer
{
    private const string ReflectionMessage =
        "MessagePackSerializerOptions.Standard, and options whose resolver falls back to it, build formatters "
        + "with reflection and dynamic code, which need members the trimmer may remove and code Native AOT "
        + "cannot generate. For Native AOT, pass a resolver built from a [GeneratedMessagePackResolver] class "
        + "and the built-in formatters to the MessagePackRestSerializer(IFormatterResolver) constructor.";

    private readonly MessagePackSerializerOptions _options;

    /// <summary>Serializes with <see cref="MessagePackSerializerOptions.Standard"/>, which uses reflection.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MessagePackRestSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    /// <summary>
    /// Serializes with these options. Their resolver may use reflection, which this constructor
    /// cannot rule out, so it carries the trim and AOT annotations whatever the resolver is. To pass
    /// options with a resolver that does not, use
    /// <see cref="MessagePackRestSerializer(IFormatterResolver, MessagePackSerializerOptions?)"/>.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MessagePackRestSerializer(MessagePackSerializerOptions options)
        => _options = options;

    /// <summary>
    /// Serializes through <paramref name="resolver"/> only, with a copy of <paramref name="options"/>, or
    /// of default options, whose resolver is replaced by it. Nothing falls back to
    /// <see cref="MessagePackSerializerOptions.Standard"/>: a type the resolver has no formatter for
    /// throws <see cref="MessagePackSerializationException"/>.
    /// </summary>
    /// <remarks>
    /// For Native AOT, compose the resolver from source-generated and built-in formatters only, such as
    /// a <c>[GeneratedMessagePackResolver]</c> class followed by <c>BuiltinResolver.Instance</c> in
    /// <c>CompositeResolver.Create</c>. Like a <c>JsonSerializerContext</c> for System.Text.Json, the
    /// resolver is the caller's choice: this constructor adds no reflection-based resolver of its own.
    /// </remarks>
    public MessagePackRestSerializer(IFormatterResolver resolver, MessagePackSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        // Not options ?? Standard: that would reach StandardResolver only to replace it.
        _options = options is null ? new MessagePackSerializerOptions(resolver) : options.WithResolver(resolver);
    }

    public string ContentType => "application/x-msgpack";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        return await MessagePackSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await MessagePackSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
}
