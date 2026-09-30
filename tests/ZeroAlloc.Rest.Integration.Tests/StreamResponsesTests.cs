using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient(StreamResponses = true)]
public interface IStreamedThingApi
{
    [Get("/thing")]
    Task<Thing> GetAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Thing?> GetMaybeAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Result<Thing, HttpError>> GetResultAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Result<Thing?, HttpError>> GetMaybeResultAsync(CancellationToken ct = default);

    [Get("/count")]
    Task<int> GetCountAsync(CancellationToken ct = default);

    [Get("/count")]
    Task<Result<int, HttpError>> GetCountResultAsync(CancellationToken ct = default);

    [Get("/count")]
    Task<Result<int?, HttpError>> GetMaybeCountResultAsync(CancellationToken ct = default);

    [Delete("/thing")]
    Task DeleteAsync(CancellationToken ct = default);

    [Delete("/thing")]
    Task<UnitResult<HttpError>> DeleteResultAsync(CancellationToken ct = default);

    [Get("/thing", StreamResponses = false)]
    Task<Result<Thing, HttpError>> GetBufferedResultAsync(CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface IPartlyStreamedThingApi
{
    [Get("/thing", StreamResponses = true)]
    Task<Result<Thing, HttpError>> GetStreamedResultAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Result<Thing, HttpError>> GetResultAsync(CancellationToken ct = default);
}

[ZeroAllocRestClient(StreamResponses = true)]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IStreamedMappedThingApi
{
    [Get("/thing")]
    Task<Result<Thing, DomainError>> GetAsync(CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface INumbersApi
{
    [Get("/numbers")]
    Task<int[]> GetBufferedAsync(CancellationToken ct = default);

    [Get("/numbers", StreamResponses = true)]
    Task<int[]> GetStreamedAsync(CancellationToken ct = default);
}

// Issue #362: with StreamResponses, the serializer reads the body from the response stream and
// HttpClient never buffers it. The response and its stream are disposed on every path.
public sealed class StreamResponsesTests
{
    private const string Rex = "{\"name\":\"Rex\"}";

    [Fact]
    public async Task Success_IsReadFromTheStream_WithoutBuffering()
    {
        var content = new TrackingContent(Rex);
        var api = Streamed(content);

        var thing = await api.GetAsync();

        Assert.Equal("Rex", thing.Name);
        Assert.Equal(0, content.BufferedCount);
        Assert.Equal(1, content.StreamedCount);
        Assert.True(content.Stream.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResultSuccess_IsReadFromTheStream_WithoutBuffering(bool lengthKnown)
    {
        var content = new TrackingContent(Rex, lengthKnown);
        var api = Streamed(content);

        var result = await api.GetResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Rex", result.Value.Name);
        Assert.Equal(0, content.BufferedCount);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task WithoutTheFlag_TheResponseIsBuffered()
    {
        var content = new TrackingContent(Rex);
        var api = Streamed(content);

        var result = await api.GetBufferedResultAsync();

        Assert.Equal("Rex", result.Value.Name);
        Assert.Equal(1, content.BufferedCount);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task MethodFlag_StreamsOnlyThatMethod()
    {
        var streamed = new TrackingContent(Rex);
        var buffered = new TrackingContent(Rex);
        var responses = new Queue<TrackingContent>([streamed, buffered]);
        using var http = Http((_, _) => Task.FromResult(Response(HttpStatusCode.OK, responses.Dequeue())));
        IPartlyStreamedThingApi api = new PartlyStreamedThingApiClient(http, new SystemTextJsonSerializer());

        Assert.Equal("Rex", (await api.GetStreamedResultAsync()).Value.Name);
        Assert.Equal("Rex", (await api.GetResultAsync()).Value.Name);

        Assert.Equal(0, streamed.BufferedCount);
        Assert.Equal(1, buffered.BufferedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ErrorBody_IsReadFromTheStream(bool lengthKnown)
    {
        const string Problem = "{\"code\":\"missing\"}";
        var content = new TrackingContent(Problem, lengthKnown);
        var api = Streamed(content, HttpStatusCode.NotFound);

        var result = await api.GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
        Assert.Equal(Problem, Encoding.UTF8.GetString(result.Error.Body.Span));
        Assert.False(result.Error.BodyTruncated);
        Assert.Equal(0, content.BufferedCount);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task UnitResultErrorBody_IsReadFromTheStream()
    {
        var content = new TrackingContent("gone", lengthKnown: false);
        var api = Streamed(content, HttpStatusCode.Gone);

        var result = await api.DeleteResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("gone", Encoding.UTF8.GetString(result.Error.Body.Span));
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task VoidMethod_DisposesTheUnreadBody()
    {
        var content = new TrackingContent(Rex);
        var api = Streamed(content);

        await api.DeleteAsync();

        Assert.Equal(0, content.BufferedCount);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task VoidMethod_ErrorStatus_ThrowsAndDisposes()
    {
        var content = new TrackingContent(Rex);
        var api = Streamed(content, HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() => api.DeleteAsync());

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task DeserializationFailure_IsADeserializationError_AndDisposes()
    {
        var content = new TrackingContent("not json", lengthKnown: false);
        var api = Streamed(content);

        var result = await api.GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.IsType<JsonException>(result.Error.Exception, exactMatch: false);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task DeserializationFailure_ThrowsFromATaskMethod_AndDisposes()
    {
        var content = new TrackingContent("not json");
        var api = Streamed(content);

        await Assert.ThrowsAnyAsync<JsonException>(() => api.GetAsync());

        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task MappedDeserializationFailure_IsMapped_AndDisposes()
    {
        var content = new TrackingContent("not json");
        using var http = Http((_, _) => Task.FromResult(Response(HttpStatusCode.OK, content)));
        IStreamedMappedThingApi api = new StreamedMappedThingApiClient(http, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await api.GetAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task ConnectionDropMidBody_IsADeserializationError_WhenStreamed()
    {
        var content = new TrackingContent(Rex) { FailAfter = 4 };
        var api = Streamed(content);

        var result = await api.GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.IsType<IOException>(result.Error.Exception);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task ConnectionDropMidBody_IsATransportError_WhenBuffered()
    {
        // The contrast the StreamResponses docs describe: buffering reads the body inside SendAsync.
        var content = new TrackingContent(Rex) { FailAfter = 4 };
        var api = Streamed(content);

        var result = await api.GetBufferedResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
    }

    [Fact]
    public async Task ConnectionDropMidBody_ThrowsIOExceptionFromATaskMethod()
    {
        var content = new TrackingContent(Rex) { FailAfter = 4 };
        var api = Streamed(content);

        await Assert.ThrowsAsync<IOException>(() => api.GetAsync());

        Assert.True(content.Stream.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationMidBody_Throws_AndDisposes(bool result)
    {
        var content = new TrackingContent(Rex, lengthKnown: false) { BlockAfter = 4 };
        var api = Streamed(content);
        using var cts = new CancellationTokenSource();

        var call = result ? (Task)api.GetResultAsync(cts.Token) : api.GetAsync(cts.Token);
        await content.Stream.Blocked.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(call.IsCompleted);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task HttpClientTimeout_DoesNotCoverTheBody()
    {
        // The body read outlives HttpClient.Timeout; only the caller's token bounds it.
        var content = new TrackingContent(Rex, lengthKnown: false) { DelayAfter = (4, TimeSpan.FromMilliseconds(300)) };
        using var http = Http((_, _) => Task.FromResult(Response(HttpStatusCode.OK, content)));
        http.Timeout = TimeSpan.FromMilliseconds(100);
        IStreamedThingApi api = new StreamedThingApiClient(http, new SystemTextJsonSerializer());

        var result = await api.GetResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Rex", result.Value.Name);
    }

    [Fact]
    public async Task HttpClientTimeout_StillCoversTheHeaders()
    {
        // Streaming moves only the body out of HttpClient.Timeout; a server slow to answer at all
        // is still a Timeout.
        using var http = Http(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Response(HttpStatusCode.OK, new TrackingContent(Rex));
        });
        http.Timeout = TimeSpan.FromMilliseconds(100);
        IStreamedThingApi api = new StreamedThingApiClient(http, new SystemTextJsonSerializer());

        var result = await api.GetResultAsync();
        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetAsync());
        Assert.IsType<TimeoutException>(thrown.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerDeadline_BoundsTheBody_AndDisposes(bool result)
    {
        // The documented way to bound a streamed body: a token with a deadline. It is the caller's
        // token, so the call throws, from a Result method too, as for any caller cancellation.
        var content = new TrackingContent(Rex, lengthKnown: false) { DelayAfter = (4, TimeSpan.FromSeconds(30)) };
        var api = Streamed(content);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var call = result ? (Task)api.GetResultAsync(deadline.Token) : api.GetAsync(deadline.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(content.Stream.Disposed);
    }

    [Fact]
    public async Task ErrorStatus_ThrowsFromATaskMethod_WithoutOpeningTheBody()
    {
        var content = new TrackingContent(Rex);
        var api = Streamed(content, HttpStatusCode.NotFound);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetAsync());

        Assert.Equal(HttpStatusCode.NotFound, thrown.StatusCode);
        Assert.Equal(0, content.BufferedCount);
        Assert.Equal(0, content.StreamedCount);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Streamed_DoesNotAllocateACopyOfTheBody()
    {
        // Buffered, HttpClient copies the body into an array of its own before the serializer
        // reads it. Streamed, the serializer reads the stream through its own pooled buffer, so
        // the call allocates at least a body's worth less. Both calls allocate the int[] result.
        var numbers = new int[16 * 1024];
        for (var i = 0; i < numbers.Length; i++)
            numbers[i] = 1_000_000 + i;
        var body = JsonSerializer.SerializeToUtf8Bytes(numbers);
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ConnectionLikeContent(body) }));
        INumbersApi api = new NumbersApiClient(http, new SystemTextJsonSerializer());

        // Warm up both paths: JIT, the serializer's metadata and the pool's arrays.
        Assert.Equal(numbers, await api.GetBufferedAsync());
        Assert.Equal(numbers, await api.GetStreamedAsync());

        var buffered = await AllocatedByAsync(() => api.GetBufferedAsync());
        var streamed = await AllocatedByAsync(() => api.GetStreamedAsync());

        Assert.True(streamed >= numbers.Length * sizeof(int), $"Streamed allocated {streamed} bytes: less than its own result, so the measurement missed the call.");
        Assert.True(buffered - streamed >= body.Length, $"Buffered allocated {buffered} bytes and streamed {streamed}, for a {body.Length}-byte body.");
    }

    // Every step of these calls completes synchronously, so the whole call runs on this thread.
    private static async Task<long> AllocatedByAsync(Func<Task<int[]>> call)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = call();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(task.IsCompletedSuccessfully, "The call did not complete synchronously, so its allocations were not all on this thread.");
        await task.ConfigureAwait(false);
        return allocated;
    }

    // A body with a Content-Length whose stream cannot seek, as a connection's is, and that
    // completes every read synchronously.
    private sealed class ConnectionLikeContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            stream.Write(body);
            return Task.CompletedTask;
        }

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

    // An empty body behaves as it does when buffered, whether its length is sent or not.
    public static TheoryData<string?, bool> EmptyBodies => new()
    {
        { null, true },
        { null, false },
        { "null", false },
    };

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task EmptyBody_NonNullableReference_IsADeserializationFailure(string? body, bool lengthKnown)
    {
        var result = await Streamed(new TrackingContent(body, lengthKnown)).GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Contains("empty or null", result.Error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(result.Error.Exception);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task EmptyBody_NullableReference_IsASuccessWithNull(string? body, bool lengthKnown)
    {
        var result = await Streamed(new TrackingContent(body, lengthKnown)).GetMaybeResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Null(await Streamed(new TrackingContent(body, lengthKnown)).GetMaybeAsync());
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task EmptyBody_NonNullableReference_ThrowsFromATaskMethod(string? body, bool lengthKnown)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Streamed(new TrackingContent(body, lengthKnown)).GetAsync());

        Assert.Contains("Thing?", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyBody_NonNullableValue_IsADeserializationFailure(bool lengthKnown)
    {
        var result = await Streamed(new TrackingContent(null, lengthKnown)).GetCountResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Contains("empty or null", result.Error.Message, StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Streamed(new TrackingContent(null, lengthKnown)).GetCountAsync());
        Assert.Contains("GetCountAsync", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task EmptyBody_NullableValue_IsASuccessWithNull(string? body, bool lengthKnown)
    {
        var result = await Streamed(new TrackingContent(body, lengthKnown)).GetMaybeCountResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NoContent_WithoutALength_IsEmpty()
    {
        // A 204 carries no Content-Length; the client reads its first byte to find it empty.
        var content = new TrackingContent(null, lengthKnown: false);
        var result = await Streamed(content, HttpStatusCode.NoContent).GetMaybeResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.True(content.Stream.Disposed);
    }

    private static IStreamedThingApi Streamed(TrackingContent content, HttpStatusCode status = HttpStatusCode.OK)
        => new StreamedThingApiClient(
            Http((_, _) => Task.FromResult(Response(status, content))),
            new SystemTextJsonSerializer());

    private static HttpResponseMessage Response(HttpStatusCode status, TrackingContent content)
        => new(status) { Content = content };

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = ResultErrorMapperTests.BaseAddress };

    // Response content that records whether HttpClient buffered it or the client read it as a
    // stream. Its stream is not seekable, as a connection's is not.
    private sealed class TrackingContent : HttpContent
    {
        private readonly bool _lengthKnown;

        public TrackingContent(string? body, bool lengthKnown = true)
        {
            _lengthKnown = lengthKnown;
            Stream = new TrackingStream(body is null ? [] : Encoding.UTF8.GetBytes(body));
            if (body is not null)
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        public TrackingStream Stream { get; }

        public int FailAfter { init => Stream.FailAfter = value; }

        public int BlockAfter { init => Stream.BlockAfter = value; }

        public (int Position, TimeSpan Delay) DelayAfter { init => Stream.DelayAfter = value; }

        public int BufferedCount { get; private set; }

        public int StreamedCount { get; private set; }

        public bool Disposed { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            BufferedCount++;
            await Stream.CopyToAsync(stream).ConfigureAwait(false);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            StreamedCount++;
            return Task.FromResult<Stream>(Stream);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Stream.Size;
            return _lengthKnown;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing)
                Stream.Dispose();
            base.Dispose(disposing);
        }
    }

    // A non-seekable body that can fail, block until cancelled, or stall at a position.
    internal sealed class TrackingStream(byte[] body) : Stream
    {
        private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public int Size => body.Length;
        public int FailAfter { get; set; } = -1;
        public int BlockAfter { get; set; } = -1;
        public (int Position, TimeSpan Delay) DelayAfter { get; set; } = (-1, TimeSpan.Zero);
        public bool Disposed { get; private set; }
        public Task Blocked => _blocked.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (_position == FailAfter)
                throw new IOException("The connection was closed mid-body.");
            if (_position == BlockAfter)
            {
                _blocked.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            if (_position == DelayAfter.Position)
            {
                var delay = DelayAfter.Delay;
                DelayAfter = (-1, TimeSpan.Zero);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            // Stop at the next position a test acts on, so each is reached by its own read.
            var end = body.Length;
            foreach (var mark in new[] { FailAfter, BlockAfter, DelayAfter.Position })
            {
                if (mark > _position && mark < end)
                    end = mark;
            }
            var count = Math.Min(buffer.Length, end - _position);
            body.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
