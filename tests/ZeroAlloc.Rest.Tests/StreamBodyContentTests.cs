using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

// Issue #358: the helpers generated clients call for a [Body] Stream and a Stream success type.
public sealed class StreamBodyContentTests
{
    [Fact]
    public async Task CreateStreamBodyContent_SendsFromTheCurrentPosition_WithTheRemainingLength()
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("skip:payload"));
        body.Position = 5;

        using var content = GeneratedRestClient.CreateStreamBodyContent(body, "text/plain; charset=utf-8");

        Assert.Equal(7, content.Headers.ContentLength);
        Assert.Equal("text/plain", content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", content.Headers.ContentType.CharSet);
        Assert.Equal("payload", await content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateStreamBodyContent_SentTwice_RewindsASeekableBody()
    {
        // A handler that resends the request, such as one that retries, sends the whole body again.
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        using var content = GeneratedRestClient.CreateStreamBodyContent(body, null);

        using var first = new MemoryStream();
        await content.CopyToAsync(first);
        using var second = new MemoryStream();
        await content.CopyToAsync(second);

        Assert.Equal("payload", Encoding.UTF8.GetString(first.ToArray()));
        Assert.Equal("payload", Encoding.UTF8.GetString(second.ToArray()));
    }

    [Fact]
    public async Task CreateStreamBodyContent_SentTwice_ThrowsForABodyThatCannotSeek()
    {
        using var body = new UnseekableStream(Encoding.UTF8.GetBytes("payload"));
        using var content = GeneratedRestClient.CreateStreamBodyContent(body, null);

        await content.CopyToAsync(Stream.Null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => content.CopyToAsync(Stream.Null));
    }

    [Fact]
    public void CreateStreamBodyContent_DefaultsToOctetStream_AndDoesNotOwnTheBody()
    {
        var body = new MemoryStream([1, 2, 3]);

        var content = GeneratedRestClient.CreateStreamBodyContent(body, null);
        content.Dispose();

        Assert.Equal("application/octet-stream", content.Headers.ContentType!.MediaType);
        Assert.True(body.CanRead);
    }

    [Fact]
    public void CreateStreamBodyContent_RejectsAnInvalidMediaType()
        => Assert.Throws<FormatException>(() => GeneratedRestClient.CreateStreamBodyContent(new MemoryStream(), "not a media type"));

    [Fact]
    public void CreateStreamBodyContent_RejectsANullBody()
        => Assert.Throws<ArgumentNullException>(() => GeneratedRestClient.CreateStreamBodyContent(null!, null));

    [Fact]
    public void WithMediaType_ReplacesTheContentType()
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var result = GeneratedRestClient.WithMediaType(content, "application/merge-patch+json");

        Assert.Same(content, result);
        Assert.Equal("application/merge-patch+json", content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task OpenResponseBodyAsync_ReadsTheBody_AndDisposingItDisposesTheResponse()
    {
        var content = new DisposalTrackingContent("body");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        var stream = await GeneratedRestClient.OpenResponseBodyAsync(response, CancellationToken.None);
        using (var reader = new StreamReader(stream, leaveOpen: true))
            Assert.Equal("body", await reader.ReadToEndAsync());
        Assert.False(content.Disposed);
        Assert.False(stream.CanWrite);

        stream.Dispose();
        stream.Dispose();

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task OpenResponseBodyAsync_WhenOpeningFails_LeavesTheResponseUndisposed()
    {
        var content = new DisposalTrackingContent("body") { FailToOpen = true };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        await Assert.ThrowsAsync<IOException>(() => GeneratedRestClient.OpenResponseBodyAsync(response, CancellationToken.None).AsTask());

        Assert.False(content.Disposed);
    }

    private sealed class DisposalTrackingContent(string body) : HttpContent
    {
        public bool FailToOpen { get; init; }
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => FailToOpen
                ? Task.FromException<Stream>(new IOException("The connection was closed."))
                : Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class UnseekableStream(byte[] body) : MemoryStream(body, writable: false)
    {
        public override bool CanSeek => false;
    }
}
