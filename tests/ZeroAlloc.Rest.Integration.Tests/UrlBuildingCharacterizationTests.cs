using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

public enum Shade
{
    [JsonStringEnumMemberName("dark red")]
    DarkRed,
    Blue,
}

// Formats differently through ISpanFormattable.TryFormat than through ToString, so a test sees
// which of the two a URL was built with. A type outside the BCL is written with its ToString.
public readonly struct TwoFacedCode(int value) : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider)
        => "tostring:" + value.ToString(formatProvider);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        const string text = "tryformat";
        charsWritten = 0;
        if (!text.AsSpan().TryCopyTo(destination))
            return false;
        charsWritten = text.Length;
        return true;
    }
}

[ZeroAllocRestClient]
public interface IUrlShapeApi
{
    [Get("p/{i}/{l}/{d}/{m}/{f}/{g}/{ts}/{dt}/{dto}/{date}/{time}/{h}/{big}/{b}/{e}/{s}/{n}")]
    Task PathAsync(int i, long l, double d, decimal m, float f, Guid g, TimeSpan ts, DateTime dt,
        DateTimeOffset dto, DateOnly date, TimeOnly time, Half h, Int128 big, bool b, Shade e, string s, int? n,
        CancellationToken ct = default);

    [Get("c/{**when}/x/{**count}")]
    Task CatchAllFormattedAsync(DateTimeOffset when, int? count, CancellationToken ct = default);

    [Get("q")]
    Task QueryAsync([Query] string text, [Query] string? opt, [Query] int? page, [Query] double ratio,
        [Query(Name = "from")] DateTime since, [Query] Shade shade, [Query] Guid? id, [Query] TwoFacedCode code,
        CancellationToken ct = default);

    [Get("list?fixed=1")]
    Task ListAsync([Query] List<string?>? tags, [Query] int[] ids, [Query] IEnumerable<double?> ratios,
        [Query] List<Shade> shades, CancellationToken ct = default);

    [Get("mixed/{id}/{**path}")]
    Task MixedAsync(Guid id, string path, [Query] decimal? amount, [Query] string? note, [Query] TimeOnly? at,
        CancellationToken ct = default);
}

/// <summary>
/// The URL a generated client sends, for every shape of route and query value, captured before
/// #428 changed how the URL is built: path and query escaping, the {**name} catch-all, invariant
/// formatting under a culture that would write the values differently, nullable and optional query
/// values, and collections. Each expected URL is what the generator at 6e79d98 sent, so a change to
/// how the URL is built cannot change a byte of it.
/// </summary>
public sealed class UrlBuildingCharacterizationTests
{
    private const string Awkward = "a b/c?d&e=f+g#h%i~j-k.l_m/é/😀/";
    private const string LoneSurrogate = "x\uD800y";

    public static TheoryData<string> Cultures => new() { "de-DE", "fa-IR", "sv-SE" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task PathValues_KeepTheirUrl(string culture)
    {
        var (api, sent) = Create();
        using var scope = new CultureScope(culture);

        await api.PathAsync(
            -42, long.MinValue, -1.5e20, 1234.5600m, float.MaxValue,
            new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), new TimeSpan(-1, -2, -3, -4, -5),
            new DateTime(2026, 10, 11, 12, 13, 14, 15, DateTimeKind.Utc),
            new DateTimeOffset(2026, 10, 11, 12, 13, 14, TimeSpan.FromHours(-5.5)),
            new DateOnly(2026, 10, 11), new TimeOnly(1, 2, 3, 4), (Half)0.5, Int128.MinValue, false,
            Shade.DarkRed, Awkward, n: 7);
        Assert.Equal(
            "http://stub.local/p/-42/-9223372036854775808/-1.5E%2B20/1234.5600/3.4028235E%2B38/0f8fad5b-d9cb-469f-a165-70867728950e/-1.02%3A03%3A04.0050000/2026-10-11T12%3A13%3A14.0150000Z/2026-10-11T12%3A13%3A14.0000000-05%3A30/2026-10-11/01%3A02%3A03.0040000/0.5/-170141183460469231731687303715884105728/false/dark%20red/a%20b%2Fc%3Fd%26e%3Df%2Bg%23h%25i~j-k.l_m%2F%C3%A9%2F%F0%9F%98%80%2F/7",
            sent());

        await api.PathAsync(
            0, 0, double.NaN, 0m, float.NegativeInfinity, Guid.Empty, TimeSpan.Zero,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), DateTimeOffset.MinValue,
            DateOnly.MinValue, TimeOnly.MaxValue, Half.NegativeZero, Int128.MaxValue, true,
            (Shade)17, LoneSurrogate, n: null);
        Assert.Equal(
            "http://stub.local/p/0/0/NaN/0/-Infinity/00000000-0000-0000-0000-000000000000/00%3A00%3A00/2026-01-02T03%3A04%3A05.0000000/0001-01-01T00%3A00%3A00.0000000%2B00%3A00/0001-01-01/23%3A59%3A59.9999999/-0/170141183460469231731687303715884105727/true/17/x%EF%BF%BDy/",
            sent());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task CatchAllFormattedValues_KeepTheirUrl(string culture)
    {
        var (api, sent) = Create();
        using var scope = new CultureScope(culture);

        await api.CatchAllFormattedAsync(new DateTimeOffset(2026, 10, 11, 1, 2, 3, TimeSpan.FromHours(2)), -3);
        Assert.Equal("http://stub.local/c/2026-10-11T01%3A02%3A03.0000000%2B02%3A00/x/-3", sent());

        await api.CatchAllFormattedAsync(DateTimeOffset.MaxValue, null);
        Assert.Equal("http://stub.local/c/9999-12-31T23%3A59%3A59.9999999%2B00%3A00/x/", sent());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task QueryValues_KeepTheirUrl(string culture)
    {
        var (api, sent) = Create();
        using var scope = new CultureScope(culture);

        await api.QueryAsync(Awkward, "", 3, -0.25, new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
            Shade.Blue, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), new TwoFacedCode(-5));
        Assert.Equal(
            "http://stub.local/q?text=a%20b%2Fc%3Fd%26e%3Df%2Bg%23h%25i~j-k.l_m%2F%C3%A9%2F%F0%9F%98%80%2F&opt=&page=3&ratio=-0.25&from=2026-10-11T00%3A00%3A00.0000000Z&shade=Blue&id=0f8fad5b-d9cb-469f-a165-70867728950e&code=tostring%3A-5",
            sent());

        await api.QueryAsync(LoneSurrogate, null, null, double.PositiveInfinity, DateTime.MinValue,
            Shade.DarkRed, null, default);
        Assert.Equal(
            "http://stub.local/q?text=x%EF%BF%BDy&ratio=Infinity&from=0001-01-01T00%3A00%3A00.0000000&shade=dark%20red&code=tostring%3A0",
            sent());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task CollectionQueryValues_KeepTheirUrl(string culture)
    {
        var (api, sent) = Create();
        using var scope = new CultureScope(culture);

        await api.ListAsync(["a b", null, "", "ü/&"], [1, -2], [1.5, null, -0.125], [Shade.DarkRed, Shade.Blue]);
        Assert.Equal(
            "http://stub.local/list?fixed=1?tags=a%20b&tags=&tags=%C3%BC%2F%26&ids=1&ids=-2&ratios=1.5&ratios=-0.125&shades=dark%20red&shades=Blue",
            sent());

        await api.ListAsync(null, [], [], []);
        Assert.Equal("http://stub.local/list?fixed=1", sent());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task MixedPathCatchAllAndQuery_KeepTheirUrl(string culture)
    {
        var (api, sent) = Create();
        using var scope = new CultureScope(culture);

        await api.MixedAsync(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), Awkward, -1234.5m, "n/o te", new TimeOnly(23, 0));
        Assert.Equal(
            "http://stub.local/mixed/0f8fad5b-d9cb-469f-a165-70867728950e/a%20b/c%3Fd%26e%3Df%2Bg%23h%25i~j-k.l_m/%C3%A9/%F0%9F%98%80/?amount=-1234.5&note=n%2Fo%20te&at=23%3A00%3A00.0000000",
            sent());

        await api.MixedAsync(Guid.Empty, "", null, null, null);
        Assert.Equal("http://stub.local/mixed/00000000-0000-0000-0000-000000000000/", sent());
    }

    private static (IUrlShapeApi Api, Func<string?> Sent) Create()
    {
        string? sent = null;
        var http = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request.RequestUri?.OriginalString;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }))
        { BaseAddress = ResultErrorMapperTests.BaseAddress };
        return (new UrlShapeApiClient(http, new SystemTextJsonSerializer()), () => sent);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
