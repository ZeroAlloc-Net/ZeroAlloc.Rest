using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// Answers every request with a 422 and a problem+json body, so the smoke can check under ILC that
// a Result method reads the error body.
internal sealed class UnprocessableHandler : HttpMessageHandler
{
    internal const string Body = """{"code":"field_required","field":"name"}""";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/problem+json"),
        });
}
