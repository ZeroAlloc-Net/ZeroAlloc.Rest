using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Benchmarks;

// ── Benchmark: response body cost per call ────────────────────────────────────
//
// The handler answers with a body that, like a connection's, has a Content-Length and a stream that
// cannot seek. Buffered is the default: HttpClient copies the body into its own buffer before the
// serializer reads it. Streamed sets StreamResponses, so the serializer reads the stream itself.
//
// Users sets the body size: 1 user is about 25 bytes of JSON, 1000 users about 30 KB.

[MemoryDiagnoser]
[SimpleJob]
public class ResponseBodyBenchmarks
{
    private static readonly Uri s_baseUri = new("http://localhost/");

    private IZeroAllocResponseApi _client = null!;
    private HttpClient _httpClient = null!;

    [Params(1, 1000)]
    public int Users { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var users = new UserDto[Users];
        for (var i = 0; i < users.Length; i++)
            users[i] = new UserDto { Id = i, Name = "User " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        var body = JsonSerializer.SerializeToUtf8Bytes(users, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _httpClient = new HttpClient(new ConnectionLikeHandler(body)) { BaseAddress = s_baseUri };
        _client = new ZeroAllocResponseApiClient(_httpClient, new SystemTextJsonSerializer());
    }

    [GlobalCleanup]
    public void Cleanup() => _httpClient.Dispose();

    [Benchmark(Baseline = true)]
    public Task<UserDto[]> Buffered() => _client.GetUsersAsync();

    [Benchmark]
    public Task<UserDto[]> Streamed() => _client.GetUsersStreamedAsync();

    private sealed class ConnectionLikeHandler(byte[] body) : HttpMessageHandler
    {
        private static readonly MediaTypeHeaderValue s_json = new("application/json");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ConnectionLikeContent(body);
            content.Headers.ContentType = s_json;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class ConnectionLikeContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(body, 0, body.Length);

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new UnseekableStream(body));

        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return true;
        }
    }

    private sealed class UnseekableStream(byte[] body) : MemoryStream(body, writable: false)
    {
        public override bool CanSeek => false;
    }
}
