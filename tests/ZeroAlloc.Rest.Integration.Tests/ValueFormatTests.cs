using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;
using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

public enum Mood
{
    [JsonStringEnumMemberName("very-happy")]
    VeryHappy,
    Sad,
}

[Flags]
public enum Access
{
    [JsonStringEnumMemberName("none")] None = 0,
    [JsonStringEnumMemberName("exec")] Exec = 4,
    [JsonStringEnumMemberName("r")] Read = 1,
    Write = 2,
    [JsonStringEnumMemberName("wx")] WriteExec = 6,
}

public struct PlainPoint
{
    public int X;

    public override readonly string ToString() => "pt" + X.ToString(CultureInfo.InvariantCulture);
}

public readonly struct ExplicitCode : IFormattable
{
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
        => 1.5.ToString(formatProvider);
}

[ZeroAllocRestClient]
public interface IFormatMixApi
{
    [Get("/n/{when}")]
    Task<Result<string, HttpError>> NullableAsync(DateTimeOffset? when, [Query] bool? flag, CancellationToken ct = default);

    [Get("/f")]
    Task<Result<string, HttpError>> FlagsAsync([Query] Access access, CancellationToken ct = default);

    [Get("/s/{point}")]
    Task<Result<string, HttpError>> StructAsync(PlainPoint point, [Query] ExplicitCode code, CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface IFormatApi
{
    [Get("/at/{when}")]
    Task<Result<string, HttpError>> AtAsync(
        DateTimeOffset when,
        [Query] double ratio,
        [Query] bool active,
        [Query] Mood mood,
        [Query] Mood other,
        [Query] int? limit,
        [Query] List<DateOnly> days,
        [Header("X-Retry")] int retry,
        [Header("X-Trace")] string? trace,
        CancellationToken ct = default);
}

// Design decision 3 of the OpenAPI models plan: values are written the same in every culture,
// in the form an OpenAPI server expects.
public sealed class ValueFormatTests
{
    [Fact]
    public async Task Values_AreWrittenInvariantly_WithIsoDatesLowerCaseBooleansAndWireEnumNames()
    {
        HttpRequestMessage? sent = null;
        using var http = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("\"ok\"", Encoding.UTF8, "application/json"),
            });
        }))
        { BaseAddress = ResultErrorMapperTests.BaseAddress };
        IFormatApi api = new FormatApiClient(http, new SystemTextJsonSerializer());
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await api.AtAsync(
                new DateTimeOffset(2026, 9, 27, 10, 30, 0, TimeSpan.FromHours(2)),
                ratio: 1.5,
                active: true,
                mood: Mood.VeryHappy,
                other: Mood.Sad,
                limit: null,
                days: [new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28)],
                retry: 3,
                trace: null);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.NotNull(sent);
        Assert.Equal(
            "/at/2026-09-27T10%3A30%3A00.0000000%2B02%3A00?ratio=1.5&active=true&mood=very-happy&other=Sad&days=2026-09-27&days=2026-09-28",
            sent.RequestUri?.PathAndQuery);
        Assert.Equal("3", Assert.Single(sent.Headers.GetValues("X-Retry")));
        Assert.False(sent.Headers.Contains("X-Trace"));
    }

    [Fact]
    public async Task NullableRouteAndQueryValues_AreWrittenInvariantly()
    {
        var (api, sent) = CreateMixClient();
        using var culture = new CultureScope("de-DE");

        await api.NullableAsync(new DateTimeOffset(2026, 9, 27, 10, 30, 0, TimeSpan.Zero), flag: false);
        Assert.Equal("/n/2026-09-27T10%3A30%3A00.0000000%2B00%3A00?flag=false", sent()?.RequestUri?.PathAndQuery);

        await api.NullableAsync(when: null, flag: null);
        Assert.Equal("/n/", sent()?.RequestUri?.PathAndQuery);
    }

    // Every value from 0 to 15, named members, combinations and values no member combination covers,
    // is written exactly as System.Text.Json's JsonStringEnumConverter writes it.
    [Fact]
    public async Task FlagsValues_AreWrittenAsSystemTextJsonWritesThem()
    {
        var (api, sent) = CreateMixClient();
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        for (var i = 0; i < 16; i++)
        {
            var access = (Access)i;
            await api.FlagsAsync(access);

            var expected = System.Text.Json.JsonSerializer.Serialize(access, options).Trim('"');
            Assert.Equal("/f?access=" + Uri.EscapeDataString(expected), sent()?.RequestUri?.PathAndQuery);
        }
    }

    [Fact]
    public async Task PlainStruct_UsesItsToString_AndExplicitFormattable_IsInvariant()
    {
        var (api, sent) = CreateMixClient();
        using var culture = new CultureScope("de-DE");

        await api.StructAsync(new PlainPoint { X = 7 }, default);

        Assert.Equal("/s/pt7?code=1.5", sent()?.RequestUri?.PathAndQuery);
    }

    private static (IFormatMixApi Api, Func<HttpRequestMessage?> Sent) CreateMixClient()
    {
        HttpRequestMessage? sent = null;
        var http = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("\"ok\"", Encoding.UTF8, "application/json"),
            });
        }))
        { BaseAddress = ResultErrorMapperTests.BaseAddress };
        return (new FormatMixApiClient(http, new SystemTextJsonSerializer()), () => sent);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
