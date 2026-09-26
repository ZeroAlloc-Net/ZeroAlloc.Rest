using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ZeroAlloc.Rest;

/// <summary>Helpers for reading an <see cref="HttpError"/>.</summary>
public static class HttpErrorExtensions
{
    private const string RetryAfterHeader = "Retry-After";

    // RFC 9110 section 5.6.7: IMF-fixdate, then the two obsolete forms a recipient must accept.
    private static readonly string[] s_httpDateFormats =
    [
        "r",
        "dddd, dd'-'MMM'-'yy HH':'mm':'ss 'GMT'",
        "ddd MMM d HH':'mm':'ss yyyy",
    ];

    /// <summary>
    /// Reads the <c>Retry-After</c> header, in delta-seconds or HTTP-date form, as the time to wait
    /// before retrying.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="timeProvider">
    /// The clock an HTTP-date is measured against. Defaults to <see cref="TimeProvider.System"/>.
    /// It is not used for delta-seconds.
    /// </param>
    /// <returns>
    /// The time to wait; <see cref="TimeSpan.Zero"/> for a date in the past; or
    /// <see langword="null"/> when the header is absent or invalid. A negative delta-seconds value
    /// is invalid. A delta-seconds value above <see cref="int.MaxValue"/> is clamped to it, as
    /// RFC 9111 section 1.2.2 does. When the header has several values, only the first is used.
    /// </returns>
    public static TimeSpan? GetRetryAfter(this HttpError error, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!TryGetFirstValue(error.Headers, out var raw))
            return null;

        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        // delta-seconds is digits only, so a sign or a fraction makes it invalid.
        if (char.IsAsciiDigit(value[0]))
        {
            foreach (var c in value)
            {
                if (!char.IsAsciiDigit(c))
                    return null;
            }

            // A value this long is out of long's range too, and definitely above int.MaxValue.
            return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                ? TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue))
                : TimeSpan.FromSeconds(int.MaxValue);
        }

        if (!DateTimeOffset.TryParseExact(value, s_httpDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowInnerWhite | DateTimeStyles.AssumeUniversal, out var date))
        {
            return null;
        }

        var remaining = date - (timeProvider ?? TimeProvider.System).GetUtcNow();
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private static bool TryGetFirstValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers, [NotNullWhen(true)] out string? value)
    {
        // The generated client's dictionary ignores case; an HttpError built by hand may not.
        if (!headers.TryGetValue(RetryAfterHeader, out var values))
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, RetryAfterHeader, StringComparison.OrdinalIgnoreCase))
                {
                    values = header.Value;
                    break;
                }
            }
        }

        value = values is { Count: > 0 } ? values[0] : null;
        return value is not null;
    }
}
