using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// Fails every request the way a refused connection does, so the smoke can check that a
// Result-returning method returns the failure under ILC without touching the network.
internal sealed class RefusingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
}
