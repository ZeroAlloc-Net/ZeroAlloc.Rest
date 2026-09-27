using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>
    /// Serializes through the context's source-generated metadata only. A type the context does not
    /// cover throws <see cref="InvalidOperationException"/>; nothing falls back to reflection.
    /// </summary>
    public SystemTextJsonSerializer(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _options = context.Options;
    }

    /// <summary>
    /// Serializes through <paramref name="resolver"/> only, with a copy of <paramref name="options"/>,
    /// or of <see cref="JsonSerializerDefaults.Web"/>, whose resolver is replaced by it.
    /// </summary>
    public SystemTextJsonSerializer(IJsonTypeInfoResolver resolver, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _options = new JsonSerializerOptions(options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web))
        {
            TypeInfoResolver = resolver,
        };
        _options.MakeReadOnly();
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
            + $"was created with. Add [JsonSerializable(typeof({DisplayName(typeof(T))}))] to your JsonSerializerContext. "
            + "For a client generated from an OpenAPI spec, regenerate it: its generated JsonContext covers "
            + "every request and response type.");
    }

    // A type as C# writes it, so the hint above can be pasted: List<Pet>, Pet[] and Int32?, where
    // Type.Name gives List`1 for a generic type.
    private static string DisplayName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return DisplayName(underlying) + "?";
        if (type.IsArray && type.GetElementType() is { } element)
            return DisplayName(element) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return (tick < 0 ? name : name[..tick]) + "<" + string.Join(", ", type.GetGenericArguments().Select(DisplayName)) + ">";
    }
}
