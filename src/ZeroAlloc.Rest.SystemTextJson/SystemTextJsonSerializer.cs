using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.SystemTextJson;

public sealed class SystemTextJsonSerializer : IRestSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonSerializer()
        : this(new JsonSerializerOptions(JsonSerializerDefaults.Web)) { }

    public SystemTextJsonSerializer(JsonSerializerOptions options)
        => _options = options;

    public string ContentType => "application/json";

    [RequiresDynamicCode("JSON serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("JSON serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        return await JsonSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
    }

    [RequiresDynamicCode("JSON serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("JSON serialization of arbitrary types may require unreferenced code.")]
    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await JsonSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
}
