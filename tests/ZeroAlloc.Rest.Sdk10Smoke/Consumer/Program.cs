using System.Net;
using ZeroAlloc.Rest;
using ZeroAlloc.Rest.Attributes;

// PingApiClient exists only if the compiler loaded the generator.
var handler = new RecordingHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
IPingApi client = new PingApiClient(http, new UnusedSerializer());

await client.PingAsync().ConfigureAwait(false);
if (handler.Path != "/ping")
{
    Console.Error.WriteLine($"FAIL: the generated client requested '{handler.Path}'");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded, generated client sent the request.");
return 0;

[ZeroAllocRestClient]
public interface IPingApi
{
    [Get("/ping")]
    Task PingAsync(CancellationToken ct = default);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    public string? Path { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Path = request.RequestUri?.AbsolutePath;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

// The request has no body and the response is not read, so the serializer is never called.
internal sealed class UnusedSerializer : IRestSerializer
{
    public string ContentType => "application/json";

    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => throw new NotSupportedException();
}
