using System.Net;
using System.Net.Http;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #318: [Post] and the other HTTP method attributes can be used without a path. A missing
// path means an empty request URI, and HttpClient treats an empty relative URI as its
// BaseAddress — trailing path segment included.
public sealed class PathlessMethodTests
{
    private static readonly Uri s_baseAddressWithTrailingSegment = new("https://host/api/");

    [Fact]
    public async Task PathlessPost_RequestUri_EqualsBaseAddress()
    {
        Uri? capturedUri = null;
        using var httpClient = CreateHttpClient(s_baseAddressWithTrailingSegment, (request, _) =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        IEvalApi client = new EvalApiClient(httpClient, new SystemTextJsonSerializer());

        await client.EvaluateAsync(new EvaluateRequest("1+1"));

        Assert.Equal(s_baseAddressWithTrailingSegment, capturedUri);
    }

    [Fact]
    public async Task PathlessPost_WithQueryParam_AppendsQueryToBaseAddress()
    {
        Uri? capturedUri = null;
        using var httpClient = CreateHttpClient(s_baseAddressWithTrailingSegment, (request, _) =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        IEvalApi client = new EvalApiClient(httpClient, new SystemTextJsonSerializer());

        await client.EvaluateWithQueryAsync(1);

        Assert.Equal("https://host/api/?x=1", capturedUri!.ToString());
    }

    private static HttpClient CreateHttpClient(
        Uri baseAddress, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = baseAddress };
}
