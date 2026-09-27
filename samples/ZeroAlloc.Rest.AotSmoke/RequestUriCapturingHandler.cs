using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// Answers every request with 200 OK and records the resolved RequestUri, so the smoke can check
// that a pathless method (issue #318) sends the request to the HttpClient's BaseAddress itself.
internal sealed class RequestUriCapturingHandler : HttpMessageHandler
{
    public Uri? CapturedUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
