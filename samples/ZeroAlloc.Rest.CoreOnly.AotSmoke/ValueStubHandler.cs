using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

// Answers every request with the status and body set last. A null body is an empty one.
internal sealed class ValueStubHandler : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string? Body { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(Status)
        {
            Content = Body is null ? new ByteArrayContent([]) : new StringContent(Body, Encoding.UTF8, "application/json"),
        });
}
