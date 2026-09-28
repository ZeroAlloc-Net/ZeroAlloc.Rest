using System.Net;
using System.Net.Http;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

// Issue #362: a streamed success body is opened through ReadResponseStreamAsync, which hands an
// empty body over as an empty seekable stream, as a buffered one is.
public sealed class ReadResponseStreamTests
{
    [Fact]
    public async Task ZeroContentLength_IsAnEmptySeekableStream_WithoutReading()
    {
        var content = new UnseekableContent([], lengthKnown: true);

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);

        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Length);
        Assert.False(content.Opened);
    }

    [Fact]
    public async Task UnknownLength_EmptyBody_IsAnEmptySeekableStream()
    {
        var content = new UnseekableContent([], lengthKnown: false);

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);

        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task KnownLength_ReturnsTheContentStreamItself()
    {
        var content = new UnseekableContent([1, 2, 3], lengthKnown: true);

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);

        Assert.Same(content.Inner, stream);
    }

    [Fact]
    public async Task SeekableStream_IsReturnedAsIs()
    {
        using var content = new StreamContent(new MemoryStream([1, 2, 3], writable: false));

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);

        Assert.True(stream.CanSeek);
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public async Task UnknownLength_ReplaysTheByteItReadAhead_Async()
    {
        byte[] body = [10, 20, 30, 40, 50];
        var content = new UnseekableContent(body, lengthKnown: false);

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);

        Assert.False(stream.CanSeek);
        Assert.Equal(0, await stream.ReadAsync(Memory<byte>.Empty));
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(body, copy.ToArray());
    }

    [Fact]
    public async Task UnknownLength_ReplaysTheByteItReadAhead_Sync()
    {
        byte[] body = [10, 20, 30];
        var content = new UnseekableContent(body, lengthKnown: false);

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);

        var buffer = new byte[8];
        var total = 0;
        int read;
        while ((read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            total += read;
        Assert.Equal(body, buffer[..total]);
    }

    [Fact]
    public async Task ReadAheadStream_HonoursACancelledToken()
    {
        var content = new UnseekableContent([1, 2], lengthKnown: false);
        var stream = await GeneratedRestClient.ReadResponseStreamAsync(content, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[4], cts.Token).AsTask());
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var content = new UnseekableContent([1], lengthKnown: false);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GeneratedRestClient.ReadResponseStreamAsync(content, cts.Token).AsTask());
    }

    [Fact]
    public async Task NullContent_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(
            () => GeneratedRestClient.ReadResponseStreamAsync(null!, CancellationToken.None).AsTask());

    [Fact]
    public async Task HttpResponse_NoContent_IsEmpty()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        var stream = await GeneratedRestClient.ReadResponseStreamAsync(response.Content, CancellationToken.None);

        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Length);
    }

    private sealed class UnseekableContent(byte[] body, bool lengthKnown) : HttpContent
    {
        public Stream Inner { get; } = new UnseekableStream(body);

        public bool Opened { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Inner.CopyToAsync(stream);

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            Opened = true;
            return Task.FromResult(Inner);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return lengthKnown;
        }
    }

    private sealed class UnseekableStream(byte[] body) : MemoryStream(body, writable: false)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
