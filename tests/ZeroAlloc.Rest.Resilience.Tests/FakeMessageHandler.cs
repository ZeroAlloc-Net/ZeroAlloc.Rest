using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.Resilience.Tests;

/// <summary>
/// Minimal fake HttpMessageHandler for unit/integration testing.
/// Supports a fixed response or a dynamic per-request factory.
/// </summary>
internal sealed class FakeMessageHandler : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];
    private Func<HttpRequestMessage, HttpResponseMessage>? _dynamicFactory;
    private HttpResponseMessage? _fixedResponse;

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public void SetResponse(HttpStatusCode statusCode, string? jsonBody)
    {
        _dynamicFactory = null;
        _fixedResponse = new HttpResponseMessage(statusCode);
        if (jsonBody is not null)
            _fixedResponse.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
    }

    public void SetDynamicResponse(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _dynamicFactory = factory;
        _fixedResponse = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Add(request);

        HttpResponseMessage response;
        if (_dynamicFactory is not null)
        {
            response = _dynamicFactory(request);
        }
        else if (_fixedResponse is not null)
        {
            // Clone the response so each caller gets a fresh message.
            response = CloneResponse(_fixedResponse);
        }
        else
        {
            response = new HttpResponseMessage(HttpStatusCode.OK);
        }

        return Task.FromResult(response);
    }

    private static HttpResponseMessage CloneResponse(HttpResponseMessage original)
    {
        var clone = new HttpResponseMessage(original.StatusCode);
        if (original.Content is StringContent sc)
        {
            // Re-read the string synchronously (only safe in tests).
            var body = original.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var mediaType = original.Content.Headers.ContentType?.MediaType ?? "application/json";
            clone.Content = new StringContent(body, Encoding.UTF8, mediaType);
        }
        return clone;
    }
}
