using System.Globalization;
using System.Net;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

public sealed class HttpErrorExtensionsTests
{
    private static readonly DateTimeOffset s_now = new(2015, 10, 21, 7, 27, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider s_clock = new(s_now);

    [Fact]
    public void DeltaSeconds_IsThatManySeconds()
        => Assert.Equal(TimeSpan.FromSeconds(120), WithRetryAfter("120").GetRetryAfter(s_clock));

    [Fact]
    public void DeltaSeconds_WithSurroundingWhitespace_IsAccepted()
        => Assert.Equal(TimeSpan.FromSeconds(5), WithRetryAfter("  5 ").GetRetryAfter(s_clock));

    [Theory]
    [InlineData("2147483648")]                    // int.MaxValue + 1
    [InlineData("99999999999999999999")]           // 20 digits, above long.MaxValue too
    public void DeltaSeconds_AboveIntMaxValue_IsClampedToIntMaxValueSeconds(string value)
        => Assert.Equal(TimeSpan.FromSeconds(int.MaxValue), WithRetryAfter(value).GetRetryAfter(s_clock));

    [Theory]
    [InlineData("Wed, 21 Oct 2015 07:28:00 GMT")]      // IMF-fixdate
    [InlineData("Wednesday, 21-Oct-15 07:28:00 GMT")]  // obsolete RFC 850
    [InlineData("Wed Oct 21 07:28:00 2015")]           // obsolete asctime
    public void HttpDate_IsTheTimeUntilThatDate(string value)
        => Assert.Equal(TimeSpan.FromMinutes(1), WithRetryAfter(value).GetRetryAfter(s_clock));

    [Fact]
    public void HttpDate_Asctime_AcceptsTheSpacePaddedSingleDigitDay()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2015, 10, 1, 7, 27, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromMinutes(1), WithRetryAfter("Thu Oct  1 07:28:00 2015").GetRetryAfter(clock));
    }

    [Fact]
    public void HttpDate_InThePast_IsZero()
        => Assert.Equal(TimeSpan.Zero, WithRetryAfter("Wed, 21 Oct 2015 07:00:00 GMT").GetRetryAfter(s_clock));

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Wed, 32 Oct 2015 07:28:00 GMT")]
    public void InvalidValue_IsNull(string value)
        => Assert.Null(WithRetryAfter(value).GetRetryAfter(s_clock));

    [Fact]
    public void AbsentHeader_IsNull()
    {
        var error = new HttpError(HttpStatusCode.TooManyRequests, new Dictionary<string, IReadOnlyList<string>>());

        Assert.Null(error.GetRetryAfter(s_clock));
    }

    [Fact]
    public void HandBuiltOrdinalDictionary_IsSearchedIgnoringCase()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["retry-after"] = new[] { "30" },
        };

        Assert.Equal(TimeSpan.FromSeconds(30), new HttpError(HttpStatusCode.ServiceUnavailable, headers).GetRetryAfter());
    }

    [Fact]
    public void SeveralValues_OnlyTheFirstIsUsed()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = new[] { "30", "60" },
        };

        var error = new HttpError(HttpStatusCode.TooManyRequests, headers);

        Assert.Equal(TimeSpan.FromSeconds(30), error.GetRetryAfter(s_clock));
    }

    [Fact]
    public void WithoutATimeProvider_UsesTheSystemClock()
    {
        var inAnHour = DateTimeOffset.UtcNow.AddHours(1).ToString("r", CultureInfo.InvariantCulture);

        var delay = WithRetryAfter(inAnHour).GetRetryAfter();

        Assert.NotNull(delay);
        Assert.InRange(delay.Value, TimeSpan.FromMinutes(58), TimeSpan.FromMinutes(61));
    }

    private static HttpError WithRetryAfter(string value)
        => new(HttpStatusCode.TooManyRequests, new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = new[] { value },
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
