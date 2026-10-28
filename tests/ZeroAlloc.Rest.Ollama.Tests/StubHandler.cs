using System.Net;
using System.Text;

namespace ZeroAlloc.Rest.Ollama.Tests;

/// <summary>Answers every request with one canned response and records the request it saw.</summary>
internal sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
{
    public HttpMethod? Method { get; private set; }

    public Uri? Uri { get; private set; }

    public string Body { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Method = request.Method;
        Uri = request.RequestUri;
        Body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
