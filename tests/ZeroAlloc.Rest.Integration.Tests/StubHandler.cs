using System.Net.Http;

namespace ZeroAlloc.Rest.Integration.Tests;

// Answers every request with whatever the test's delegate returns or throws.
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => send(request, cancellationToken);
}
