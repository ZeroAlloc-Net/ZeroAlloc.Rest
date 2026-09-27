using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.SystemTextJson;

/// <summary>An <see cref="IRestSerializer"/> for application/json, backed by System.Text.Json.</summary>
public sealed class SystemTextJsonSerializer : IRestSerializer
{
    private const string ReflectionMessage =
        "Reflection-based JSON serialization needs members the trimmer may remove and code Native AOT "
        + "cannot generate. Pass a JsonSerializerContext, such as the one generated from your OpenAPI spec.";

    private readonly JsonSerializerOptions _options;

    /// <summary>Serializes with reflection and <see cref="JsonSerializerDefaults.Web"/>.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public SystemTextJsonSerializer()
        : this(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    {
    }

    /// <summary>
    /// Serializes with these options. Without a type info resolver on them, reflection supplies one.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Locks the options with the reflection resolver when they have none. This is the only
        // place reflection is chosen, which is why this constructor carries the annotations.
        if (!options.IsReadOnly)
            options.MakeReadOnly(populateMissingResolver: true);
        _options = options;
    }

    public string ContentType => "application/json";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        return await JsonSerializer.DeserializeAsync(stream, TypeInfo<T>(), ct).ConfigureAwait(false);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await JsonSerializer.SerializeAsync(stream, value, TypeInfo<T>(), ct).ConfigureAwait(false);

    // TryGetTypeInfo is not annotated: it only asks the resolver the options already have.
    private JsonTypeInfo<T> TypeInfo<T>()
    {
        if (_options.TryGetTypeInfo(typeof(T), out var typeInfo))
            return (JsonTypeInfo<T>)typeInfo;
        throw new InvalidOperationException(
            $"{typeof(T)} is not registered with the JSON type info resolver this SystemTextJsonSerializer "
            + $"was created with. Add [JsonSerializable(typeof({typeof(T).Name}))] to your JsonSerializerContext. "
            + "For a client generated from an OpenAPI spec, regenerate it: its generated JsonContext covers "
            + "every request and response type.");
    }
}
