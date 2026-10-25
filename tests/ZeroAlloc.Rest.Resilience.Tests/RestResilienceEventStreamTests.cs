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
        // Red: set MaxAttempts = 1 on IStreamingApi, as dropping [Retry] leaves no proxy to compile.
        // The first 503 then escapes this enumeration, so the test fails on this line and
        // the assertions below are not reached.
        await foreach (var item in api.FollowAsync(CancellationToken.None).ConfigureAwait(false))
            seen.Add(item.Data.N);

        // Red: serve the stream on the second request, with ++calls < 2; the count is then 2.
        Assert.Equal(3, _handler.Requests.Count);
        // Red: yield only event 1 from the final response; seen is then { 1 }.
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
        // Red: new DroppingContent(""), so the stream fails before its first event. The IOException is
        // retried, and this line fails with a ResilienceException once the attempts run out.
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var item in api.FollowAsync(CancellationToken.None).ConfigureAwait(false))
                seen.Add(item.Data.N);
        });

        // Guards against a library regression that retries after an item: the count would be 3. It
        // cannot be made red from this repo, since the Resilience library owns the rule.
        Assert.Single(_handler.Requests);
        // Red: DroppingContent("data: {\"N\":2}\n\n") as the first chunk; seen is then { 2 }.
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

public sealed record StreamError(int Status, string Body);

public sealed class StreamErrorMapper : IHttpErrorMapper<StreamError>
{
    public StreamError Map(HttpError error) => new((int)error.StatusCode, Encoding.UTF8.GetString(error.Body.Span));
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(StreamErrorMapper))]
[Retry(MaxAttempts = 2, BackoffMs = 1, Jitter = false, RetryOnException = nameof(IsTransient))]
public interface IMappedStreamingApi
{
    [Get("/ticks")]
    [EventStream(ErrorType = typeof(StreamError))]
    IAsyncEnumerable<ServerSentEvent<StreamTick>> FollowAsync(CancellationToken ct = default);

    static bool IsTransient(Exception e) => e is HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable };
}

// A back-off long enough that only cancellation can end it within the test.
[ZeroAllocRestClient]
[Retry(MaxAttempts = 3, BackoffMs = 600_000, Jitter = false, RetryOnException = nameof(IsTransient))]
public interface ISlowStreamingApi
{
    [Get("/ticks")]
    [EventStream]
    IAsyncEnumerable<ServerSentEvent<StreamTick>> FollowAsync(CancellationToken ct = default);

    static bool IsTransient(Exception e) => e is HttpRequestException;
}

public sealed class RestResilienceEventStreamFailureTests
{
    private readonly FakeMessageHandler _handler = new();

    [Fact]
    public async Task A_mapped_event_stream_whose_retries_run_out_surfaces_the_mapped_error()
    {
        _handler.SetDynamicResponse(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("busy", Encoding.UTF8, "text/plain"),
        });
        using var provider = Build<IMappedStreamingApi, MappedStreamingApiClient, IMappedStreamingApiResilienceProxy>(
            services => services.AddMappedStreamingApiResiliencePolicies(),
            (inner, sp) => new IMappedStreamingApiResilienceProxy(inner, sp.GetRequiredService<MappedStreamingApiResiliencePolicies>()));
        var api = provider.GetRequiredService<IMappedStreamingApi>();

        var failure = await Assert.ThrowsAsync<ResilienceException>(async () =>
        {
            await foreach (var item in api.FollowAsync(CancellationToken.None).ConfigureAwait(false))
                Assert.Fail("No event was expected.");
        });

        // Red: set MaxAttempts = 1 on IMappedStreamingApi; the count is then 1.
        Assert.Equal(2, _handler.Requests.Count);
        // Red: drop ErrorType from [EventStream]; the inner exception is then an HttpErrorException,
        // not the generic one, and this type check fails.
        var inner = Assert.IsType<HttpErrorException<StreamError>>(failure.InnerException);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, inner.StatusCode);
        // Red: expect new StreamError(503, "x"); the mapper carries the response body, "busy".
        Assert.Equal(new StreamError(503, "busy"), inner.MappedError);
    }

    [Fact]
    public async Task Cancelling_during_the_back_off_of_an_event_stream_stops_the_retries()
    {
        using var cts = new CancellationTokenSource();
        _handler.SetDynamicResponse(_ =>
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        using var provider = Build<ISlowStreamingApi, SlowStreamingApiClient, ISlowStreamingApiResilienceProxy>(
            services => services.AddSlowStreamingApiResiliencePolicies(),
            (inner, sp) => new ISlowStreamingApiResilienceProxy(inner, sp.GetRequiredService<SlowStreamingApiResiliencePolicies>()));
        var api = provider.GetRequiredService<ISlowStreamingApi>();

        // Red: pass CancellationToken.None to FollowAsync; the 10 minute back-off is then never
        // cancelled and the test hangs. With BackoffMs = 1 this line fails instead, with a
        // ResilienceException once the attempts run out.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in api.FollowAsync(cts.Token).ConfigureAwait(false))
                Assert.Fail("No event was expected.");
        });

        // Guards against a library regression that keeps retrying after the cancellation, which
        // would show as more than one request. It cannot be made red from this repo.
        Assert.Single(_handler.Requests);
    }

    private ServiceProvider Build<TInterface, TClient, TProxy>(
        Action<IServiceCollection> policies,
        Func<TClient, IServiceProvider, TProxy> factory)
        where TInterface : class
        where TClient : class, TInterface, IGeneratedRestClient<TClient>
        where TProxy : class, TInterface
    {
        var services = new ServiceCollection();
        policies(services);
        services.AddRestResilience<TInterface, TClient, TProxy>(
            factory,
            options =>
            {
                options.BaseAddress = new Uri("http://fake.local/");
                options.UseSerializer<SystemTextJsonSerializer>();
            })
            .ConfigurePrimaryHttpMessageHandler(() => _handler);
        return services.BuildServiceProvider();
    }
}
