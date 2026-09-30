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
/// built-in formatter, such as <see cref="int"/>, <see cref="string"/> or arrays of them, need no registration,
/// and neither do unmanaged types, such as an enum or a struct of value fields, which MemoryPack copies as raw memory.
/// Any other type throws <see cref="InvalidOperationException"/> before it is read or written.
/// </remarks>
public sealed class MemoryPackRestSerializer : IRestSerializer
{
    private readonly FrozenSet<Type> _types;

    /// <summary>
    /// Creates a serializer with no registered types. It serves only the types MemoryPack handles with a
    /// built-in formatter, such as <see cref="int"/>, <see cref="string"/> or arrays of them, and unmanaged types
    /// such as enums, which MemoryPack copies as raw memory. Any other type,
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
        types.Freeze();
        _types = types.Types.ToFrozenSet();
    }

    public string ContentType => "application/x-memorypack";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        EnsureServed<T>();
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        // MemoryPack reads from one contiguous span, so the body is gathered first: into a pooled
        // buffer, cleared before it goes back, never into an array of this call's own.
        var length = stream.CanSeek ? RemainingLength(stream) : -1;
        using var body = new PooledBuffer(length < 0 ? UnknownLengthRent : length);
        await body.FillAsync(stream, length, ct).ConfigureAwait(false);
        return Read<T>(body.Written);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        EnsureServed<T>();
        using var buffer = new PooledBuffer(UnknownLengthRent);
        MemoryPackSerializer.Serialize(buffer, value);
        await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
    }

    // The first rent for a body of unknown length. The buffer doubles from here as needed.
    private const int UnknownLengthRent = 4096;

    private static int RemainingLength(Stream stream)
    {
        var remaining = stream.Length - stream.Position;
        if (remaining > Array.MaxLength)
            throw new IOException($"The MemoryPack body is {remaining} bytes, more than an array can hold.");
        return (int)remaining;
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

        // An enum, or a struct holding only unmanaged fields, is written and read as raw memory: MemoryPack's
        // fast path in Serialize, and Read's matching one, never look a formatter up.
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>()) return;

        if (type.IsArray || type.IsGenericType)
        {
            throw new InvalidOperationException(
                $"MemoryPackRestSerializer cannot serialize {Display(type)}: MemoryPack has no formatter registered for it, "
                + "and building one would take reflection that trimming and Native AOT remove. Use an array of a registered "
                + "[MemoryPackable] type or of a built-in type instead, or register a formatter for it through "
                + "MemoryPackFormatterProvider.Register first.");
        }

        throw new InvalidOperationException(
            $"MemoryPackRestSerializer cannot serialize {Display(type)}: MemoryPack has no formatter registered for it, "
            + "and finding one would take reflection that trimming and Native AOT remove. Mark the type [MemoryPackable] "
            + $"and register it: new MemoryPackRestSerializer(types => types.Add<{Display(type)}>()).");
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

    // An IBufferWriter over ArrayPool<byte>.Shared. It grows by renting a larger array, and every
    // array it lets go of is cleared first: a body can hold tokens or PII.
    private sealed class PooledBuffer : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer;
        private int _written;

        // Set while a span handed out by GetMemory or GetSpan has not been advanced over. A writer
        // or a stream read that fails part way may have written into it, so the whole array is
        // cleared then, not just the bytes advanced over.
        private bool _spanOutstanding;

        internal PooledBuffer(int initialSize)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialSize, 1));
        }

        internal ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _written);

        internal ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

        // Reads `stream` to its end, or until `length` bytes when the length is known.
        internal async ValueTask FillAsync(Stream stream, int length, CancellationToken ct)
        {
            while (length < 0 || _written < length)
            {
                var read = await stream.ReadAsync(GetMemory(1), ct).ConfigureAwait(false);
                Advance(read);
                if (read == 0)
                    return;
            }
        }

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _written)
                throw new ArgumentOutOfRangeException(nameof(count));
            _written += count;
            _spanOutstanding = false;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            _spanOutstanding = true;
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            _spanOutstanding = true;
            return _buffer.AsSpan(_written);
        }

        public void Dispose()
        {
            var buffer = _buffer;
            buffer.AsSpan(0, DirtyLength).Clear();
            _buffer = [];
            _written = 0;
            _spanOutstanding = false;
            if (buffer.Length > 0)
                ArrayPool<byte>.Shared.Return(buffer);
        }

        private void Ensure(int sizeHint)
        {
            var needed = Math.Max(sizeHint, 1);
            if (_buffer.Length - _written >= needed)
                return;

            var required = (long)_written + needed;
            if (required > Array.MaxLength)
                throw new IOException("The MemoryPack body is larger than an array can hold.");
            var size = (int)Math.Min(Math.Max(required, (long)_buffer.Length * 2), Array.MaxLength);
            var larger = ArrayPool<byte>.Shared.Rent(size);
            _buffer.AsSpan(0, _written).CopyTo(larger);
            _buffer.AsSpan(0, DirtyLength).Clear();
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = larger;
            _spanOutstanding = false;
        }

        // The bytes of the array that may hold body data.
        private int DirtyLength => _spanOutstanding ? _buffer.Length : _written;
    }
}
