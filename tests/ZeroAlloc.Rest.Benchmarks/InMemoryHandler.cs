using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ZeroAlloc.Rest.Benchmarks;

/// <summary>
/// Returns a fixed pre-serialized JSON response for every request.
/// Eliminates network latency so benchmarks measure library overhead only.
/// </summary>
internal sealed class InMemoryHandler : HttpMessageHandler
{
    private readonly byte[] _responseBody;

    public InMemoryHandler(object responseObject)
    {
        _responseBody = JsonSerializer.SerializeToUtf8Bytes(responseObject);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_responseBody)
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        return Task.FromResult(response);
    }
}
