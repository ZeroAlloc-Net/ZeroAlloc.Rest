using System;
using System.Buffers;
using System.Collections.Frozen;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MemoryPack;

/// <summary>
/// An <see cref="IRestSerializer"/> for MemoryPack's binary format, safe under trimming and Native AOT.
/// </summary>
/// <remarks>
/// Every <c>[MemoryPackable]</c> type the serializer reads or writes is registered up front:
/// <c>new MemoryPackRestSerializer(types => types.Add&lt;User&gt;())</c>. Types MemoryPack serves with a
/// built-in formatter, such as <see cref="int"/>, <see cref="string"/> or arrays of them, need no registration.
/// Any other type throws <see cref="InvalidOperationException"/> before it is read or written.
/// </remarks>
public sealed class MemoryPackRestSerializer : IRestSerializer
{
    private readonly FrozenSet<Type> _types;

    /// <summary>
    /// Creates a serializer with no registered types. It serves only the types MemoryPack handles with a
    /// built-in formatter, such as <see cref="int"/>, <see cref="string"/> or arrays of them. Any other type,
    /// including every <c>[MemoryPackable]</c> type, throws <see cref="InvalidOperationException"/> naming the
    /// type and the fix: <c>new MemoryPackRestSerializer(types => types.Add&lt;T&gt;())</c>.
    /// </summary>
    public MemoryPackRestSerializer()
    {
        _types = FrozenSet<Type>.Empty;
    }

    /// <summary>Creates a serializer for the types <paramref name="configure"/> adds.</summary>
    /// <param name="configure">Adds each <c>[MemoryPackable]</c> type through <see cref="MemoryPackRestTypes.Add{T}"/>.</param>
    public MemoryPackRestSerializer(Action<MemoryPackRestTypes> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var types = new MemoryPackRestTypes();
        configure(types);
        _types = types.Types.ToFrozenSet();
    }

    public string ContentType => "application/x-memorypack";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        EnsureServed<T>();
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        var bytes = await ReadAllBytesAsync(stream, ct).ConfigureAwait(false);
        return Read<T>(bytes);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        EnsureServed<T>();
        var buffer = new ArrayBufferWriter<byte>();
        MemoryPackSerializer.Serialize(buffer, value);
        await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
    }

    // MemoryPackSerializer.Deserialize<T> without its reflection-based formatter lookup: the same unmanaged
    // fast path, then a MemoryPackReader over pooled optional state, both returned when the read ends.
    private static T? Read<T>(ReadOnlySpan<byte> buffer)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            if (buffer.Length < Unsafe.SizeOf<T>())
                MemoryPackSerializationException.ThrowInvalidRange(Unsafe.SizeOf<T>(), buffer.Length);
            return Unsafe.ReadUnaligned<T>(in MemoryMarshal.GetReference(buffer));
        }

        using var state = MemoryPackReaderOptionalStatePool.Rent(options: null);
        var reader = new MemoryPackReader(buffer, state);
        try
        {
            T? value = default;
            reader.ReadValue(ref value);
            return value;
        }
        finally
        {
            reader.Dispose();
        }
    }

    // A formatter MemoryPack already holds is used as is. A [MemoryPackable] type, or an array or nullable
    // of one, must also have been added to this serializer, so behaviour never depends on whether some
    // other code happened to touch the type first. Nothing here looks a formatter up by reflection.
    private void EnsureServed<T>()
    {
        var type = typeof(T);
        var owner = RegistrationOwner(type);
        var hasFormatter = MemoryPackFormatterProvider.IsRegistered<T>();

        if (_types.Contains(owner))
        {
            if (hasFormatter) return;
            throw new InvalidOperationException(
                $"MemoryPackRestSerializer cannot serialize {Display(type)}: {Display(owner)} is registered, but "
                + $"MemoryPack has no formatter for {Display(type)}. Use {Display(owner)} or an array of it.");
        }

        if (IsMemoryPackable(owner))
        {
            throw new InvalidOperationException(
                $"MemoryPackRestSerializer has no registration for {Display(owner)}. Register it: "
                + $"new MemoryPackRestSerializer(types => types.Add<{Display(owner)}>()).");
        }

        if (hasFormatter) return;
        throw new InvalidOperationException(
            $"MemoryPackRestSerializer cannot serialize {Display(type)}: MemoryPack has no formatter registered for it, "
            + "and finding one would take reflection that trimming and Native AOT remove. Mark the type [MemoryPackable] "
            + $"and register it: new MemoryPackRestSerializer(types => types.Add<{Display(type)}>()). For a collection, "
            + "use an array of a registered type, or register a formatter through MemoryPackFormatterProvider.Register first.");
    }

    private static Type RegistrationOwner(Type type)
    {
        while (true)
        {
            if (type.IsArray && type.GetElementType() is { } element)
                type = element;
            else if (Nullable.GetUnderlyingType(type) is { } underlying)
                type = underlying;
            else
                return type;
        }
    }

    private static bool IsMemoryPackable(Type type)
        => typeof(IMemoryPackFormatterRegister).IsAssignableFrom(type);

    private static string Display(Type type)
    {
        if (type.IsArray && type.GetElementType() is { } element)
            return Display(element) + "[]";
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0) name = name[..tick];
        return name + "<" + string.Join(", ", Array.ConvertAll(type.GetGenericArguments(), Display)) + ">";
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
