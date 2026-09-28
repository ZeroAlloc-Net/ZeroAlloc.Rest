using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

// IRestSerializer carries no trim or AOT annotations: this implementation stays trim and AOT safe
// by going only through PingJsonContext's source-generated JsonTypeInfo and the non-generic
// JsonSerializer overloads that take one. A type the context does not cover throws
// NotSupportedException instead of falling back to reflection.
internal sealed class JsonContextSerializer : IRestSerializer
{
    public string ContentType => "application/json";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var value = await JsonSerializer.DeserializeAsync(stream, TypeInfo<T>(), ct).ConfigureAwait(false);
        return (T?)value;
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await JsonSerializer.SerializeAsync(stream, value, TypeInfo<T>(), ct).ConfigureAwait(false);

    private static JsonTypeInfo TypeInfo<T>()
        => PingJsonContext.Default.GetTypeInfo(typeof(T))
            ?? throw new NotSupportedException(
                $"{typeof(T)} is not registered with PingJsonContext. Add [JsonSerializable(typeof({typeof(T).Name}))].");
}
