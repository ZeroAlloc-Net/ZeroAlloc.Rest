using System.Net;
using System.Net.Http;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient]
public interface ICatchAllApi
{
    [Get("files/{**path}")]
    Task<string> DoubleStarAsync(string path, CancellationToken ct = default);

    [Get("{**path}")]
    Task<string> RootAsync(string path, CancellationToken ct = default);

    [Get("files/{*path}")]
    Task<string> SingleStarAsync(string path, CancellationToken ct = default);

    [Get("files/{path}")]
    Task<string> PlainAsync(string path, CancellationToken ct = default);

    [Get("orgs/{id}/files/{**path}/end")]
    Task<string> MixedAsync(int id, string path, [Query] string? tag = null, CancellationToken ct = default);
}

// Issue #422: the URL actually sent for {**name}, {*name} and {name}.
public sealed class CatchAllRouteTests
{
    [Theory]
    [InlineData("v1/systemone", "/files/v1/systemone")]
    [InlineData("v1/system one", "/files/v1/system%20one")]
    [InlineData("a?b#c/d", "/files/a%3Fb%23c/d")]
    [InlineData("100%/x", "/files/100%25/x")]
    [InlineData("/v1/x", "/files//v1/x")]
    [InlineData("a//b", "/files/a//b")]
    [InlineData("", "/files/")]
    public async Task DoubleStar_KeepsTheSeparators_AndEscapesEachSegment(string value, string expected)
    {
        var (api, sent) = Create();

        await api.DoubleStarAsync(value);

        Assert.Equal(expected, sent().RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task DoubleStar_AsTheWholeRoute_KeepsALeadingSlash()
    {
        var (api, sent) = Create();

        await api.RootAsync("/v1/x");

        Assert.Equal("/v1/x", sent().RequestUri?.PathAndQuery);
    }

    [Theory]
    [InlineData("v1/x", "/files/v1%2Fx")]
    [InlineData("v1/system one", "/files/v1%2Fsystem%20one")]
    public async Task SingleStar_EscapesTheSeparators_LikeAPlainToken(string value, string expected)
    {
        var (api, sent) = Create();

        await api.SingleStarAsync(value);
        Assert.Equal(expected, sent().RequestUri?.PathAndQuery);

        await api.PlainAsync(value);
        Assert.Equal(expected, sent().RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task MixedRoute_KeepsItsLiterals_AndTheQuery()
    {
        var (api, sent) = Create();

        await api.MixedAsync(7, "a b/c", tag: "x y");

        Assert.Equal("/orgs/7/files/a%20b/c/end?tag=x%20y", sent().RequestUri?.PathAndQuery);
    }

    private static (ICatchAllApi Api, Func<HttpRequestMessage> Sent) Create()
    {
        HttpRequestMessage? sent = null;
        var http = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("\"ok\"", System.Text.Encoding.UTF8, "application/json"),
            });
        }))
        { BaseAddress = ResultErrorMapperTests.BaseAddress };
        return (new CatchAllApiClient(http, new SystemTextJsonSerializer()), () => sent!);
    }
}
