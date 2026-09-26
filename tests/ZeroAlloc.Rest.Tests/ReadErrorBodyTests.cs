using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

// GeneratedRestClient.ReadErrorBodyAsync reads the body of a non-success response for an HttpError.
// These tests use unbuffered content: HttpClient buffers every response before the generated
// client sees it, so only direct calls reach the unknown-length and failing-read paths.
public sealed class ReadErrorBodyTests
{
    private static readonly byte[] s_json = Encoding.UTF8.GetBytes("""{"code":"field_required","field":"name"}""");

    [Fact]
    public async Task KnownLength_UnderTheCap_ReadsIntoAnExactBuffer()
    {
        using var content = new ByteArrayContent(s_json);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
        Assert.True(MemoryMarshal.TryGetArray(body, out var segment));
        Assert.Equal(s_json.Length, segment.Array!.Length);
    }

    [Fact]
    public async Task KnownLength_AtTheCap_IsNotTruncated()
    {
        using var content = new ByteArrayContent(s_json);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, s_json.Length, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
    }

    [Fact]
    public async Task KnownLength_OverTheCap_KeepsTheFirstMaxBytes_AndIsTruncated()
    {
        using var content = new ByteArrayContent(s_json);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 10, CancellationToken.None);

        Assert.Equal(s_json[..10], body.ToArray());
        Assert.True(truncated);
    }

    [Fact]
    public async Task KnownLength_ShorterThanDeclared_ReturnsAnExactSizeCopy()
    {
        var shortBody = s_json[..10];
        using var content = new StreamContent(new TestStream(shortBody));
        content.Headers.ContentLength = s_json.Length; // declares more than the stream actually has

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.Equal(shortBody, body.ToArray());
        Assert.False(truncated);
        Assert.True(MemoryMarshal.TryGetArray(body, out var segment));
        Assert.Equal(shortBody.Length, segment.Array!.Length);
    }

    [Fact]
    public async Task KnownLength_IOFailure_GivesAnEmptyBody()
    {
        using var content = new StreamContent(
            new TestStream(s_json, failure: () => new IOException("connection reset"), failAt: 7));
        content.Headers.ContentLength = s_json.Length;

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.True(body.IsEmpty);
        Assert.False(truncated);
    }

    [Fact]
    public async Task KnownLength_CallerCancellationDuringTheRead_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var content = new StreamContent(new TestStream(
            s_json, failure: () => new OperationCanceledException(cts.Token), failAt: 7, cancelFirst: cts));
        content.Headers.ContentLength = s_json.Length;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, cts.Token).AsTask());
    }

    [Fact]
    public async Task KnownEmptyBody_IsEmptyAndAllocatesNoArray()
    {
        using var content = new ByteArrayContent([]);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        // ReadOnlyMemory<byte>.Empty is default: equal only when no array backs the memory.
        Assert.True(body.Equals(ReadOnlyMemory<byte>.Empty));
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_EmptyBody_IsEmptyAndAllocatesNoArray()
    {
        using var content = new StreamContent(new TestStream([]));
        Assert.Null(content.Headers.ContentLength);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.True(body.Equals(ReadOnlyMemory<byte>.Empty));
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_UnderTheCap_IsCopiedOnceIntoAnExactArray()
    {
        using var content = new StreamContent(new TestStream(s_json));
        Assert.Null(content.Headers.ContentLength);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
        Assert.True(MemoryMarshal.TryGetArray(body, out var segment));
        Assert.Equal(0, segment.Offset);
        Assert.Equal(s_json.Length, segment.Array!.Length);
    }

    [Fact]
    public async Task UnknownLength_AtTheCap_IsNotTruncated()
    {
        using var content = new StreamContent(new TestStream(s_json));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, s_json.Length, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_OverTheCap_KeepsTheFirstMaxBytes_AndIsTruncated()
    {
        using var content = new StreamContent(new TestStream(s_json));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 10, CancellationToken.None);

        Assert.Equal(s_json[..10], body.ToArray());
        Assert.True(truncated);
    }

    [Fact]
    public async Task UnknownLength_CapAboveOneMebibyte_GrowsTheBuffer()
    {
        var large = new byte[3 * 1024 * 1024];
        new Random(42).NextBytes(large);
        using var content = new StreamContent(new TestStream(large, chunk: 64 * 1024));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 4 * 1024 * 1024, CancellationToken.None);

        Assert.Equal(large, body.ToArray());
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_CapAboveOneMebibyte_NotAPowerOfTwo_TruncatesAtTheCap()
    {
        var large = new byte[4 * 1024 * 1024];
        new Random(7).NextBytes(large);
        using var content = new StreamContent(new TestStream(large, chunk: 64 * 1024));

        const int cap = 3 * 1024 * 1024;
        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, cap, CancellationToken.None);

        Assert.True(truncated);
        Assert.Equal(large[..cap], body.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CapZeroOrLess_ReadsNothing(int maxBytes)
    {
        // Reading would throw an exception the helper does not catch.
        using var content = new StreamContent(new TestStream(s_json, failure: () => new FormatException("unexpected read")));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, maxBytes, CancellationToken.None);

        Assert.True(body.IsEmpty);
        Assert.False(truncated);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("http")]
    [InlineData("disposed")]
    [InlineData("consumed")]
    [InlineData("timeout")]
    public async Task ReadFailure_GivesAnEmptyBody(string kind)
    {
        using var content = new StreamContent(new TestStream(s_json, failure: () => Failure(kind), failAt: 7));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.True(body.IsEmpty);
        Assert.False(truncated);
    }

    [Fact]
    public async Task CallerCancellationDuringTheRead_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var content = new StreamContent(new TestStream(
            s_json, failure: () => new OperationCanceledException(cts.Token), failAt: 7, cancelFirst: cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, cts.Token).AsTask());
    }

    [Fact]
    public async Task CallerCancellationSurfacingAsAnIOError_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        using var content = new StreamContent(new TestStream(
            s_json, failure: () => new IOException("aborted"), failAt: 7, cancelFirst: cts));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, cts.Token).AsTask());

        Assert.IsType<IOException>(ex.InnerException);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task AnUnrelatedException_Propagates()
    {
        using var content = new StreamContent(new TestStream(s_json, failure: () => new FormatException("unexpected read")));

        await Assert.ThrowsAsync<FormatException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None).AsTask());
    }

    private static Exception Failure(string kind) => kind switch
    {
        "io" => new IOException("connection reset"),
        "http" => new HttpRequestException("response ended prematurely"),
        "disposed" => new ObjectDisposedException("content"),
        "consumed" => new InvalidOperationException("The stream was already consumed."),
        "timeout" => new OperationCanceledException("timed out"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // A stream with no length, like a chunked response. It hands out at most `chunk` bytes per
    // read. Once `failAt` bytes have been read, it cancels `cancelFirst`, if given, and throws.
    private sealed class TestStream(
        byte[] data,
        Func<Exception>? failure = null,
        int failAt = 0,
        CancellationTokenSource? cancelFirst = null,
        int chunk = 7) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (failure is not null && _position >= failAt)
            {
                cancelFirst?.Cancel();
                throw failure();
            }

            var count = Math.Min(Math.Min(buffer.Length, chunk), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
