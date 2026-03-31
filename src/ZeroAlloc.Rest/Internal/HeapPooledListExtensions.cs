using ZeroAlloc.Collections;

namespace ZeroAlloc.Rest.Internal;

internal static class HeapPooledListExtensions
{
    internal static void Append(this HeapPooledList<char> list, ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
            list.Add(value[i]);
    }

    internal static void Append(this HeapPooledList<char> list, char value)
        => list.Add(value);
}
