using BenchmarkDotNet.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Benchmarks;

// ── Benchmark: request body cost per call ─────────────────────────────────────
//
// The handler reads the request body to the end, as a real transport does, and answers 204, so
// these measure what the generated client spends to serialize and send a body: the bytes a
// serializer writes, and any copy the client makes of them on the way to the wire.
//
// Users sets the body size: 1 user is about 25 bytes of JSON, 1000 users about 30 KB.

[MemoryDiagnoser]
[SimpleJob]
public class RequestBodyBenchmarks
{
    private static readonly Uri s_baseUri = new("http://localhost/");

    private IZeroAllocBodyApi _client = null!;
    private HttpClient _httpClient = null!;
    private UserDto[] _users = [];

    [Params(1, 1000)]
    public int Users { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _users = new UserDto[Users];
        for (var i = 0; i < _users.Length; i++)
            _users[i] = new UserDto { Id = i, Name = "User " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        _httpClient = new HttpClient(new DrainingHandler()) { BaseAddress = s_baseUri };
        _client = new ZeroAllocBodyApiClient(_httpClient, new SystemTextJsonSerializer());
    }

    [GlobalCleanup]
    public void Cleanup() => _httpClient.Dispose();

    [Benchmark]
    public Task ZeroAlloc_PostBody() => _client.CreateUsersAsync(_users);

    // Copies the request body to Stream.Null, the way a transport copies it to the socket.
    private sealed class DrainingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is { } content)
                await content.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }
}
