using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Resilience;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Rest.Resilience.Tests;

public sealed record StreamTick(int N);

// ZeroAlloc.Resilience 3.4.0 retries an IAsyncEnumerable method while the failure comes before its
// first item. The Rest client throws HttpErrorException for a non-2xx status before the stream
// starts, so RetryOnException selects it; RetryWhen is not allowed on a stream.
[ZeroAllocRestClient]
[Retry(MaxAttempts = 3, BackoffMs = 1, Jitter = false, RetryOnException = nameof(IsTransient))]
public interface IStreamingApi
{
    [Get("/ticks")]
    [EventStream]
    IAsyncEnumerable<ServerSentEvent<StreamTick>> FollowAsync(CancellationToken ct = default);

    // The predicate accepts the IOException too, so only the rule that nothing is retried after the
    // first item keeps the second test at one request.
    static bool IsTransient(Exception e) =>
        e is IOException or HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable };
}

public sealed class RestResilienceEventStreamTests : IDisposable
{
    private readonly FakeMessageHandler _handler = new();
    private readonly ServiceProvider _provider;

    public RestResilienceEventStreamTests()
    {
        var services = new ServiceCollection();
        services.AddStreamingApiResiliencePolicies();
        services.AddRestResilience<IStreamingApi, StreamingApiClient, IStreamingApiResilienceProxy>(
            (inner, sp) => new IStreamingApiResilienceProxy(inner, sp.GetRequiredService<StreamingApiResiliencePolicies>()),
            options =>
            {
                options.BaseAddress = new Uri("http://fake.local/");
                options.UseSerializer<SystemTextJsonSerializer>();
            })
            .ConfigurePrimaryHttpMessageHandler(() => _handler);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task An_event_stream_that_gets_503_before_its_first_event_is_retried()
    {
        var calls = 0;
        _handler.SetDynamicResponse(_ => ++calls < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : EventResponse("data: {\"N\":1}\n\ndata: {\"N\":2}\n\n"));
        var api = _provider.GetRequiredService<IStreamingApi>();

        var seen = new List<int>();
        await foreach (var item in api.FollowAsync(CancellationToken.None).ConfigureAwait(false))
            seen.Add(item.Data.N);

        // Red: set MaxAttempts = 1 on IStreamingApi [dropping [Retry] fails to compile, no proxy]; one request.
        Assert.Equal(3, _handler.Requests.Count);
        // Red: MaxAttempts = 1 as above; the enumeration throws before any event is yielded.
        Assert.Equal(new[] { 1, 2 }, seen);
    }

    [Fact]
    public async Task An_event_stream_that_fails_after_its_first_event_is_not_retried()
    {
        _handler.SetDynamicResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DroppingContent("data: {\"N\":1}\n\n"),
        });
        var api = _provider.GetRequiredService<IStreamingApi>();

        var seen = new List<int>();
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var item in api.FollowAsync(CancellationToken.None).ConfigureAwait(false))
                seen.Add(item.Data.N);
        });

        // Red: make DroppingContent fail before its first event, with new DroppingContent(""); the
        // IOException is then retried and the count is 3, not 1. Dropping [Retry] does not turn
        // this red: without a retry the count is 1 as well.
        Assert.Single(_handler.Requests);
        // Red: new DroppingContent(""), as above; seen is then empty.
        Assert.Equal(new[] { 1 }, seen);
    }

    private static HttpResponseMessage EventResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    // Sends one chunk, then fails the way a closed connection does.
    private sealed class DroppingContent(string first) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new DroppingStream(first));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class DroppingStream(string first) : Stream
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(first);
        private int _position;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _bytes.Length)
                throw new IOException("The connection was closed mid-stream.");
            var n = Math.Min(buffer.Length, _bytes.Length - _position);
            _bytes.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
