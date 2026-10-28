using System.Buffers;
using System.ComponentModel;
using System.Globalization;

namespace ZeroAlloc.Rest;

/// <summary>
/// Builds a request URL for a generated client, writing route text, escaped values and the query
/// string into one buffer, so the URL costs one string and nothing else (#428). Generated code
/// creates one over a stack buffer and calls <see cref="ToStringAndClear"/> once.
/// </summary>
/// <remarks>
/// A value is escaped exactly as <see cref="Uri.EscapeDataString(string)"/> escapes it. A value of a
/// BCL type is formatted straight into a stack buffer, with the invariant culture, instead of into
/// a string first. When the buffer fills, it moves to an array rented from
/// <see cref="ArrayPool{T}.Shared"/>, which <see cref="ToStringAndClear"/> returns.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct RestUrlBuilder
{
    // Large enough for any value of a type generated code formats into it: decimal, Guid, Int128
    // and the "O" form of DateTimeOffset all fit.
    private const int FormatBufferLength = 64;

    private char[]? _rented;
    private Span<char> _chars;
    private int _length;
    private bool _hasQuery;

    // The URL so far when it is one string value that escaping left unchanged, so that
    // ToStringAndClear can return that string instead of copying it.
    private string? _sole;

    /// <summary>Starts an empty URL in <paramref name="initialBuffer"/>.</summary>
    /// <param name="initialBuffer">The buffer to write into until it fills, usually on the stack.</param>
    public RestUrlBuilder(Span<char> initialBuffer)
    {
        _rented = null;
        _chars = initialBuffer;
        _length = 0;
        _hasQuery = false;
        _sole = null;
    }

    /// <summary>Appends route text as it is, without escaping.</summary>
    /// <param name="value">The text.</param>
    public void AppendLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        _sole = null;
        EnsureCapacity(value.Length);
        value.AsSpan().CopyTo(_chars[_length..]);
        _length += value.Length;
    }

    /// <summary>
    /// Starts a query parameter: <c>?</c> before the first one this builder writes, <c>&amp;</c> before
    /// every other, then the name and <c>=</c>.
    /// </summary>
    /// <param name="name">The parameter's name, written as it is.</param>
    public void AppendQueryName(string name)
    {
        AppendChar(_hasQuery ? '&' : '?');
        _hasQuery = true;
        AppendLiteral(name);
        AppendChar('=');
    }

    /// <summary>Appends <paramref name="value"/> escaped as <see cref="Uri.EscapeDataString(string)"/> escapes it.</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public void AppendEscaped(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var empty = _length == 0;
        AppendEscapedSpan(value);
        _sole = empty && _length == value.Length ? value : null;
    }

    /// <summary>
    /// Formats <paramref name="value"/> with the invariant culture and appends it escaped as
    /// <see cref="Uri.EscapeDataString(string)"/> escapes it.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string, or null for the type's default format.</param>
    public void AppendEscaped<T>(T value, string? format)
        where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[FormatBufferLength];
        if (value.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
        {
            _sole = null;
            AppendEscapedSpan(buffer[..written]);
        }
        else
        {
            AppendEscaped(value.ToString(format, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Appends a <c>{**name}</c> catch-all value: each segment between two <c>/</c> is escaped as
    /// <see cref="Uri.EscapeDataString(string)"/> escapes it, and every <c>/</c> is kept.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public void AppendPathEscaped(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var empty = _length == 0;
        AppendPathEscapedSpan(value);
        _sole = empty && _length == value.Length ? value : null;
    }

    /// <summary>
    /// Formats <paramref name="value"/> with the invariant culture and appends it as a
    /// <c>{**name}</c> catch-all value; see <see cref="AppendPathEscaped(string)"/>.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string, or null for the type's default format.</param>
    public void AppendPathEscaped<T>(T value, string? format)
        where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[FormatBufferLength];
        if (value.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
        {
            _sole = null;
            AppendPathEscapedSpan(buffer[..written]);
        }
        else
        {
            AppendPathEscaped(value.ToString(format, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Returns the URL and returns any rented buffer to the pool. The builder is empty afterwards.
    /// </summary>
    /// <returns>The URL.</returns>
    public string ToStringAndClear()
    {
        var result = _sole ?? (_length == 0 ? string.Empty : new string(_chars[.._length]));
        var rented = _rented;
        this = default;
        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
        return result;
    }

    private void AppendChar(char value)
    {
        _sole = null;
        EnsureCapacity(1);
        _chars[_length++] = value;
    }

    private void AppendEscapedSpan(scoped ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return;
        int written;
#if NET9_0_OR_GREATER
        while (!Uri.TryEscapeDataString(value, _chars[_length..], out written))
#else
        while (!Internal.UriDataEscaper.TryEscapeDataString(value, _chars[_length..], out written))
#endif
            Grow(value.Length);
        _length += written;
    }

    private void AppendPathEscapedSpan(scoped ReadOnlySpan<char> value)
    {
        while (true)
        {
            var slash = value.IndexOf('/');
            if (slash < 0)
            {
                AppendEscapedSpan(value);
                return;
            }
            AppendEscapedSpan(value[..slash]);
            EnsureCapacity(1);
            _chars[_length++] = '/';
            value = value[(slash + 1)..];
        }
    }

    private void EnsureCapacity(int additional)
    {
        if (_chars.Length - _length < additional)
            Grow(additional);
    }

    // At least doubles the buffer, so escaping a long value that does not fit retries a few times
    // at most. An escaped character takes at most 9: three UTF-8 bytes, each written as %XX.
    private void Grow(int additional)
    {
        var size = Math.Max(_chars.Length * 2, _length + Math.Max(additional, 16));
        var next = ArrayPool<char>.Shared.Rent(size);
        _chars[.._length].CopyTo(next);
        var previous = _rented;
        _chars = _rented = next;
        if (previous is not null)
            ArrayPool<char>.Shared.Return(previous);
    }
}
