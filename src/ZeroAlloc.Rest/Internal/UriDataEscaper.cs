#if !NET9_0_OR_GREATER
using System.Buffers;
using System.Text;

namespace ZeroAlloc.Rest.Internal;

// net8.0 has no Uri.TryEscapeDataString, which arrived in .NET 9, and Uri.EscapeDataString(string)
// costs a string per value. This writes the same text EscapeDataString does, into a span: every
// character but the RFC 3986 unreserved ones becomes its UTF-8 bytes, each as an uppercase %XX,
// and an unpaired surrogate is escaped as U+FFFD, as EscapeDataString does.
internal static class UriDataEscaper
{
    private const string HexUpper = "0123456789ABCDEF";

    // Returns false, with nothing written that the caller keeps, when destination is too small.
    internal static bool TryEscapeDataString(ReadOnlySpan<char> value, Span<char> destination, out int written)
    {
        written = 0;
        Span<byte> utf8 = stackalloc byte[4];
        var position = 0;
        while (position < value.Length)
        {
            var c = value[position];
            if (IsUnreserved(c))
            {
                if (written >= destination.Length)
                    return false;
                destination[written++] = c;
                position++;
                continue;
            }

            if (Rune.DecodeFromUtf16(value[position..], out var rune, out var consumed) != OperationStatus.Done)
                rune = Rune.ReplacementChar;
            position += consumed;

            var byteCount = rune.EncodeToUtf8(utf8);
            if (destination.Length - written < byteCount * 3)
                return false;
            for (var i = 0; i < byteCount; i++)
            {
                destination[written++] = '%';
                destination[written++] = HexUpper[utf8[i] >> 4];
                destination[written++] = HexUpper[utf8[i] & 0xF];
            }
        }
        return true;
    }

    private static bool IsUnreserved(char c)
        => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~';
}
#endif
