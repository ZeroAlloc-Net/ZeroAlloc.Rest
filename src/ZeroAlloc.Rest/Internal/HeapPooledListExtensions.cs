using ZeroAlloc.Collections;

namespace ZeroAlloc.Rest.Internal;

public static class HeapPooledListExtensions
{
    public static void Append(this HeapPooledList<char> list, ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
            list.Add(value[i]);
    }

    public static void Append(this HeapPooledList<char> list, char value)
        => list.Add(value);
}
