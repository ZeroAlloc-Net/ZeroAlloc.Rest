using System.Net;
using System.Net.Http;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #354: HttpHeaders.TryAddWithoutValidation sends a null value as an empty header. A
// nullable [Header] parameter whose value is null must be left out of the request entirely.
public sealed class HeaderParameterTests
{
    [Fact]
    public async Task NullHeaderValues_AreOmitted()
    {
        var request = await SendAsync(reference: null, retryCount: null, page: 1);

        Assert.False(request.Headers.Contains("X-Ref"));
        Assert.False(request.Headers.Contains("X-Retry-Count"));
    }

    [Fact]
    public async Task NonNullHeaderValues_AreSent()
    {
        var request = await SendAsync(reference: "abc", retryCount: 2, page: 7);

        Assert.Equal("abc", Assert.Single(request.Headers.GetValues("X-Ref")));
        Assert.Equal("2", Assert.Single(request.Headers.GetValues("X-Retry-Count")));
        Assert.Equal("7", Assert.Single(request.Headers.GetValues("X-Page")));
    }

    [Fact]
    public async Task EmptyStringHeaderValue_IsStillSent()
    {
        // Only null means "absent"; an empty string is a value the caller chose to send.
        var request = await SendAsync(reference: "", retryCount: null, page: 1);

        Assert.True(request.Headers.Contains("X-Ref"));
    }

    private static async Task<HttpRequestMessage> SendAsync(string? reference, int? retryCount, int page)
    {
        HttpRequestMessage? captured = null;
        using var httpClient = new HttpClient(new StubHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }))
        { BaseAddress = new Uri("https://host/") };
        IHeaderApi client = new HeaderApiClient(httpClient, new SystemTextJsonSerializer());

        await client.SendAsync(reference, retryCount, page).ConfigureAwait(false);

        return captured!;
    }
}
