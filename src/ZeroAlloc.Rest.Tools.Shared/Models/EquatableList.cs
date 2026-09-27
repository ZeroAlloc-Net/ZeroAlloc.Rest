using System.Collections;

namespace ZeroAlloc.Rest.Tools;

// A read-only list compared by its items, so the intermediate model is value-equal.
internal sealed class EquatableList<T> : IReadOnlyList<T>, IEquatable<EquatableList<T>>
{
    private readonly T[] _items;

    internal EquatableList(IEnumerable<T> items) => _items = [.. items];

    internal static EquatableList<T> Empty { get; } = new([]);

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(EquatableList<T>? other)
        => other is not null && _items.AsSpan().SequenceEqual(other._items, EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => Equals(obj as EquatableList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[" + string.Join(", ", _items) + "]";
}
