using System;
using System.Collections.Generic;
using MemoryPack;

namespace ZeroAlloc.Rest.MemoryPack;

/// <summary>
/// The <c>[MemoryPackable]</c> types a <see cref="MemoryPackRestSerializer"/> serves.
/// </summary>
/// <remarks>
/// MemoryPack otherwise finds a type's formatter through reflection, which Native AOT and trimming remove.
/// <see cref="Add{T}"/> registers the formatter through the type's generated static
/// <c>RegisterFormatter</c> instead, so no reflection is involved.
/// </remarks>
public sealed class MemoryPackRestTypes
{
    private readonly HashSet<Type> _types = [];

    internal MemoryPackRestTypes()
    {
    }

    internal IReadOnlyCollection<Type> Types => _types;

    /// <summary>
    /// Registers <typeparamref name="T"/>'s MemoryPack formatter, and the formatter for arrays of it,
    /// and lets the serializer read and write the type.
    /// </summary>
    /// <typeparam name="T">A <c>[MemoryPackable]</c> type.</typeparam>
    /// <returns>This builder, so calls chain.</returns>
    public MemoryPackRestTypes Add<T>() where T : IMemoryPackable<T>
    {
        T.RegisterFormatter();
        _types.Add(typeof(T));
        return this;
    }
}
