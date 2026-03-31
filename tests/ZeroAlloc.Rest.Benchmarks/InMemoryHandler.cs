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
    private readonly HttpStatusCode _statusCode;

    public InMemoryHandler(object responseObject, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = JsonSerializer.SerializeToUtf8Bytes(responseObject);
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new ByteArrayContent(_responseBody)
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        return Task.FromResult(response);
    }
}
