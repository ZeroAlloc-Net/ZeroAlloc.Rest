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
}
