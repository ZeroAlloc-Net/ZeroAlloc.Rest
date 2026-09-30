using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient]
public interface IFileApi
{
    [Put("/files/{name}")]
    Task UploadAsync(string name, [Body(ContentType = "image/png")] Stream body, CancellationToken ct = default);

    [Put("/files/raw")]
    Task UploadRawAsync([Body] Stream? body, CancellationToken ct = default);

    [Put("/files/raw")]
    Task<UnitResult<HttpError>> TryUploadAsync([Body] Stream body, CancellationToken ct = default);

    [Patch("/things/1")]
    Task PatchThingAsync([Body(ContentType = "application/merge-patch+json")] Thing body, CancellationToken ct = default);

    [Get("/files/{name}")]
    Task<Stream> DownloadAsync(string name, CancellationToken ct = default);

    [Get("/files/{name}")]
    Task<Result<Stream, HttpError>> TryDownloadAsync(string name, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IMappedFileApi
{
    [Get("/files/{name}")]
    Task<Result<Stream, DomainError>> DownloadAsync(string name, CancellationToken ct = default);
}

// Issue #358: a [Body] Stream is sent as it is, with its declared media type, and a Stream success
// type is handed to the caller as a stream that owns the response and reads it unbuffered.
public sealed class StreamBodyTests
{
    private const int LargeBody = 1024 * 1024;

    [Fact]
    public async Task StreamBody_IsSentWithItsDeclaredMediaTypeAndLength()
    {
        var bytes = Encoding.UTF8.GetBytes("png bytes");
        using var body = new MemoryStream(bytes);
        HttpRequestMessage? sent = null;
        string? mediaType = null;
        long? length = null;
        byte[]? received = null;
        using var http = Http(async (request, _) =>
        {
            sent = request;
            mediaType = request.Content!.Headers.ContentType!.MediaType;
            length = request.Content.Headers.ContentLength;
            received = await request.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await Api(http).UploadAsync("cat.png", body);

        Assert.Equal(HttpMethod.Put, sent!.Method);
        Assert.Equal("/files/cat.png", sent.RequestUri!.AbsolutePath);
        Assert.Equal("image/png", mediaType);
        Assert.Equal(bytes.Length, length);
        Assert.Equal(bytes, received);
        // The caller owns the stream: the request did not dispose it.
        Assert.True(body.CanRead);
    }

    [Fact]
    public async Task StreamBody_WithoutAMediaType_IsOctetStream_AndAnUnseekableOneHasNoLength()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var body = new UnseekableStream(bytes);
        string? mediaType = null;
        long? length = 0;
        byte[]? received = null;
        using var http = Http(async (request, _) =>
        {
            mediaType = request.Content!.Headers.ContentType!.MediaType;
            length = request.Content.Headers.ContentLength;
            received = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await Api(http).UploadRawAsync(body);

        Assert.Equal("application/octet-stream", mediaType);
        Assert.Null(length);
        Assert.Equal(bytes, received);
        Assert.False(body.Disposed);
    }

    [Fact]
    public async Task NullStreamBody_SendsNoBody()
    {
        HttpContent? content = new StringContent("placeholder");
        using var http = Http((request, _) =>
        {
            content = request.Content;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        await Api(http).UploadRawAsync(null);

        Assert.Null(content);
    }

    [Fact]
    public async Task SerializedBody_IsSentWithItsDeclaredMediaType()
    {
        string? mediaType = null;
        string? json = null;
        using var http = Http(async (request, _) =>
        {
            mediaType = request.Content!.Headers.ContentType!.MediaType;
            json = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await Api(http).PatchThingAsync(new Thing("Rex"));

        Assert.Equal("application/merge-patch+json", mediaType);
        Assert.Equal("{\"name\":\"Rex\"}", json);
    }

    [Fact]
    public async Task StreamBody_ReadFailure_IsATransportFailure_FromAResultMethod()
    {
        // The body is read while the request is sent, so a failing stream fails the send. The
        // handler wraps the failure as SocketsHttpHandler does.
        var body = new UnseekableStream([1, 2, 3]) { Fail = true };
        using var http = Http(async (request, ct) =>
        {
            try
            {
                await request.Content!.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new HttpRequestException("Error while copying content to a stream.", ex);
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await Api(http).TryUploadAsync(body);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
        Assert.IsType<IOException>(result.Error.Exception!.InnerException);
    }

    [Fact]
    public async Task LargeUpload_IsNotCopied()
    {
        var bytes = new byte[LargeBody];
        new Random(358).NextBytes(bytes);
        long sentLength = 0;
        using var http = Http(async (request, ct) =>
        {
            // As a connection does: the content is copied to the transport, not buffered.
            var counter = new CountingStream();
            await request.Content!.CopyToAsync(counter, ct).ConfigureAwait(false);
            sentLength = counter.Count;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var api = Api(http);
        using var warmUp = new MemoryStream(bytes, writable: false);
        await api.UploadAsync("warm", warmUp);

        using var body = new UnseekableStream(bytes);
        var allocated = await AllocatedByAsync(() => api.UploadAsync("big", body));

        Assert.Equal(LargeBody, sentLength);
        Assert.True(allocated < LargeBody / 16, $"Allocated {allocated} bytes to upload a {LargeBody}-byte body.");
    }

    [Fact]
    public async Task Download_ReturnsTheBodyUnbuffered_AndTheStreamOwnsTheResponse()
    {
        var content = new TrackingContent("file contents");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        using var http = Http((_, _) => Task.FromResult(response));

        var stream = await Api(http).DownloadAsync("a.txt");

        Assert.Equal(0, content.BufferedCount);
        Assert.False(content.Disposed);
        using (var reader = new StreamReader(stream))
            Assert.Equal("file contents", await reader.ReadToEndAsync());
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Download_DoesNotSendTheSerializersAcceptHeader()
    {
        HttpRequestMessage? sent = null;
        using var http = Http((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new TrackingContent("x") });
        });

        await using var stream = await Api(http).DownloadAsync("a.txt");

        Assert.Empty(sent!.Headers.Accept);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheResponse()
    {
        var content = new TrackingContent("file contents");
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var stream = await Api(http).DownloadAsync("a.txt");
        await stream.DisposeAsync();

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task LargeDownload_IsNotCopied()
    {
        var bytes = new byte[LargeBody];
        new Random(358).NextBytes(bytes);
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new TrackingContent(bytes) }));
        var api = Api(http);
        await using (var warmUp = await api.DownloadAsync("warm"))
            await warmUp.CopyToAsync(Stream.Null);

        var counter = new CountingStream();
        var allocated = await AllocatedByAsync(async () =>
        {
            var stream = await api.DownloadAsync("big").ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
                await stream.CopyToAsync(counter).ConfigureAwait(false);
        });

        Assert.Equal(LargeBody, counter.Count);
        Assert.True(allocated < LargeBody / 16, $"Allocated {allocated} bytes to download a {LargeBody}-byte body.");
    }

    [Fact]
    public async Task Download_ErrorStatus_ThrowsAndDisposes()
    {
        var content = new TrackingContent("missing");
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = content }));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => Api(http).DownloadAsync("a.txt"));

        Assert.Equal(HttpStatusCode.NotFound, thrown.StatusCode);
        Assert.Equal(0, content.StreamedCount);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task TryDownload_Success_IsTheBodyStream()
    {
        var content = new TrackingContent("file contents");
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var result = await Api(http).TryDownloadAsync("a.txt");

        Assert.True(result.IsSuccess);
        Assert.False(content.Disposed);
        using (var reader = new StreamReader(result.Value))
            Assert.Equal("file contents", await reader.ReadToEndAsync());
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task TryDownload_EmptyBody_IsAnEmptyStream()
    {
        var content = new TrackingContent(string.Empty);
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var result = await Api(http).TryDownloadAsync("a.txt");

        await using var stream = result.Value;
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public async Task TryDownload_ErrorStatus_IsAStatusFailureWithItsBody_AndDisposes()
    {
        var content = new TrackingContent("{\"code\":\"missing\"}");
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = content }));

        var result = await Api(http).TryDownloadAsync("a.txt");

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
        Assert.Equal("{\"code\":\"missing\"}", Encoding.UTF8.GetString(result.Error.Body.Span));
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task TryDownload_BodyThatCannotBeOpened_IsADeserializationFailure_AndDisposes()
    {
        var content = new TrackingContent("x") { FailToOpen = true };
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var result = await Api(http).TryDownloadAsync("a.txt");

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.IsType<IOException>(result.Error.Exception);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Download_BodyThatCannotBeOpened_Throws_AndDisposes()
    {
        var content = new TrackingContent("x") { FailToOpen = true };
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        await Assert.ThrowsAsync<IOException>(() => Api(http).DownloadAsync("a.txt"));

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task TryDownload_TransportFailure_IsATransportFailure()
    {
        using var http = Http((_, _) => throw new HttpRequestException("refused"));

        var result = await Api(http).TryDownloadAsync("a.txt");

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
    }

    [Fact]
    public async Task TryDownload_HttpClientTimeout_BeforeTheHeaders_IsATimeout()
    {
        using var http = Http(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        http.Timeout = TimeSpan.FromMilliseconds(100);

        var result = await Api(http).TryDownloadAsync("a.txt");

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellation_BeforeTheHeaders_Throws(bool result)
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Http(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var api = Api(http);

        var call = result ? (Task)api.TryDownloadAsync("a.txt", cts.Token) : api.DownloadAsync("a.txt", cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task CallerCancellation_WhileReadingTheReturnedStream_StopsTheRead()
    {
        // The call has returned, so only the token passed to each read applies to the body.
        var content = new TrackingContent("file contents") { BlockReads = true };
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var stream = await Api(http).DownloadAsync("a.txt");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[16], cts.Token).AsTask());
        await stream.DisposeAsync();

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task MappedDownload_ErrorStatus_IsMapped_AndDisposes()
    {
        var content = new TrackingContent("{\"code\":\"missing\"}");
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = content }));
        IMappedFileApi api = new MappedFileApiClient(http, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await api.DownloadAsync("a.txt");

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.Status);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task MappedDownload_Success_IsTheBodyStream()
    {
        var content = new TrackingContent("file contents");
        using var http = Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        IMappedFileApi api = new MappedFileApiClient(http, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await api.DownloadAsync("a.txt");

        Assert.True(result.IsSuccess);
        await result.Value.DisposeAsync();
        Assert.True(content.Disposed);
    }

    private static IFileApi Api(HttpClient http) => new FileApiClient(http, new SystemTextJsonSerializer());

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = ResultErrorMapperTests.BaseAddress };

    // Every step of these calls completes synchronously, so the whole call runs on this thread.
    private static async Task<long> AllocatedByAsync(Func<Task> call)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = call();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(task.IsCompletedSuccessfully, "The call did not complete synchronously, so its allocations were not all on this thread.");
        await task.ConfigureAwait(false);
        return allocated;
    }

    // Counts the bytes written to it and keeps none, as a connection's transport does.
    private sealed class CountingStream : Stream
    {
        public long Count { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Count += count;

        public override void Write(ReadOnlySpan<byte> buffer) => Count += buffer.Length;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Count += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Count += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }

    // A body that cannot seek, as a connection's cannot, and that can fail or block its reads.
    private sealed class UnseekableStream(byte[] body) : Stream
    {
        private int _position;

        public bool Fail { get; init; }
        public bool BlockReads { get; init; }
        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (Fail)
                throw new IOException("The file could not be read.");
            var count = Math.Min(buffer.Length, body.Length - _position);
            body.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (BlockReads)
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Read(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void CopyTo(Stream destination, int bufferSize)
        {
            if (Fail)
                throw new IOException("The file could not be read.");
            destination.Write(body, _position, body.Length - _position);
            _position = body.Length;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            // Writes straight from the body, so the copy allocates no buffer of its own.
            if (Fail)
                return Task.FromException(new IOException("The file could not be read."));
            var write = destination.WriteAsync(body.AsMemory(_position), cancellationToken);
            _position = body.Length;
            return write.AsTask();
        }

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

    // Response content that records whether it was buffered, streamed or disposed.
    private sealed class TrackingContent : HttpContent
    {
        private readonly byte[] _body;

        public TrackingContent(string body)
            : this(Encoding.UTF8.GetBytes(body))
        {
        }

        public TrackingContent(byte[] body) => _body = body;

        public bool FailToOpen { get; init; }
        public bool BlockReads { get; init; }
        public int BufferedCount { get; private set; }
        public int StreamedCount { get; private set; }
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            BufferedCount++;
            return stream.WriteAsync(_body, 0, _body.Length);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            if (FailToOpen)
                return Task.FromException<Stream>(new IOException("The connection was closed."));
            StreamedCount++;
            return Task.FromResult<Stream>(new UnseekableStream(_body) { BlockReads = BlockReads });
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
