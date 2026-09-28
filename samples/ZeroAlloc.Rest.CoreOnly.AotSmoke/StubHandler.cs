using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

// Answers every request with a 200 and a PingResponse body, camelCase as
// JsonSerializerDefaults.Web writes it, so PingApiClient has something real to deserialize.
internal sealed class StubHandler : HttpMessageHandler
{
    internal const string ExpectedMessage = "pong";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"message":"{{ExpectedMessage}}"}""", Encoding.UTF8, "application/json"),
        });
}
