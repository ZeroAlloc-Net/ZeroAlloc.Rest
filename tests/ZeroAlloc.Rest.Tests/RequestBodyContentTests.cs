using System.Buffers;
using System.Net.Http;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests;

// GeneratedRestClient.CreateBodyContentAsync serializes a [Body] parameter into a pooled buffer
// (issue #362). These tests check that every rented buffer goes back to the pool, cleared, on
// every path, and never while a send is still copying from it.
public sealed class RequestBodyContentTests
{
    private static readonly byte[] s_body = Encoding.UTF8.GetBytes("""{"name":"Bob"}""");

    [Fact]
    public async Task Content_HoldsTheSerializedBody_WithItsLengthAndContentType()
    {
        var pool = new TrackingPool();
        using var content = await CreateAsync(new BytesSerializer(s_body), pool);

        Assert.Equal(s_body, await CopyAsync(content));
        Assert.Equal(s_body.Length, content.Headers.ContentLength);
        Assert.Equal("application/test", content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Content_WithTheSystemTextJsonSerializer_MatchesItsOutput()
    {
        var serializer = new SystemTextJsonSerializer();
        using var expected = new MemoryStream();
        await serializer.SerializeAsync(expected, new Person("Bob", 42));

        using var content = await GeneratedRestClient.CreateBodyContentAsync(serializer, new Person("Bob", 42), CancellationToken.None);

        Assert.Equal(expected.ToArray(), await content.ReadAsByteArrayAsync());
        Assert.Equal(expected.Length, content.Headers.ContentLength);
    }

    [Fact]
    public async Task Content_CanBeSentMoreThanOnce()
    {
        var pool = new TrackingPool();
        using var content = await CreateAsync(new BytesSerializer(s_body), pool);

        Assert.Equal(s_body, await CopyAsync(content));
        Assert.Equal(s_body, await CopyAsync(content));
        Assert.Equal(s_body, content.ReadAsStream().ReadAllBytes());
    }

    [Fact]
    public async Task Dispose_ClearsAndReturnsTheBuffer()
    {
        var pool = new TrackingPool();
        var content = await CreateAsync(new BytesSerializer(s_body), pool);
        Assert.Equal(1, pool.Outstanding);

        content.Dispose();
        content.Dispose();

        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task SerializerThrows_ReturnsTheBuffer()
    {
        var pool = new TrackingPool();
        var serializer = new BytesSerializer(s_body, failAfterWrite: new FormatException("cannot serialize"));

        var ex = await Assert.ThrowsAsync<FormatException>(() => CreateAsync(serializer, pool));

        Assert.Equal("cannot serialize", ex.Message);
        Assert.True(pool.Rented > 0);
        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task SerializerCancelled_ReturnsTheBuffer()
    {
        var pool = new TrackingPool();
        using var cts = new CancellationTokenSource();
        var serializer = new BytesSerializer(s_body, cancelAfterWrite: cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateAsync(serializer, pool, cts.Token));

        Assert.True(pool.Rented > 0);
        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task InvalidContentType_ReturnsTheBuffer()
    {
        var pool = new TrackingPool();

        await Assert.ThrowsAsync<FormatException>(
            () => CreateAsync(new BytesSerializer(s_body, contentType: "not a media type"), pool));

        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task CancelledBeforeSerializing_RentsNothingOrReturnsIt()
    {
        var pool = new TrackingPool();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GeneratedRestClient.CreateBodyContentAsync(
            new SystemTextJsonSerializer(), new Person("Bob", 42), pool, new CancellationToken(canceled: true)).AsTask());

        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task LargeBody_GrowsAcrossRents_AndReturnsEveryBuffer()
    {
        var pool = new TrackingPool();
        var large = new byte[100_000];
        Random.Shared.NextBytes(large);
        // Written in small pieces, so the buffer grows several times.
        var content = await CreateAsync(new BytesSerializer(large, chunk: 1000), pool);

        Assert.Equal(large, await CopyAsync(content));
        Assert.Equal(large.Length, content.Headers.ContentLength);
        Assert.True(pool.Rented > 1);
        Assert.Equal(1, pool.Outstanding);

        content.Dispose();
        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task DisposeDuringASend_KeepsTheBufferUntilTheSendEnds()
    {
        var pool = new TrackingPool();
        var content = await CreateAsync(new BytesSerializer(s_body), pool);
        var wire = new GatedStream();

        // A transport can still be writing the body when the request is disposed, for example an
        // HTTP/2 upload the server answered early.
        var send = content.CopyToAsync(wire);
        await wire.WriteStarted;
        content.Dispose();
        Assert.Equal(1, pool.Outstanding);

        wire.Release();
        await send;

        Assert.Equal(s_body, wire.ToArray());
        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task SendAfterDispose_Throws_AndDoesNotReadTheReturnedBuffer()
    {
        var pool = new TrackingPool();
        var content = await CreateAsync(new BytesSerializer(s_body), pool);
        content.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.CopyToAsync(new MemoryStream()));
        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task SerializerThatDisposesItsStream_StillProducesTheBody()
    {
        var pool = new TrackingPool();
        using (var content = await CreateAsync(new BytesSerializer(s_body, disposeStream: true), pool))
            Assert.Equal(s_body, await CopyAsync(content));

        pool.AssertAllReturnedCleared();
    }

    [Fact]
    public async Task SerializerThatSeeksBack_WritesLikeAMemoryStream()
    {
        var pool = new TrackingPool();
        using var expected = new MemoryStream();
        await new SeekingSerializer().SerializeAsync(expected, 0);

        using (var content = await GeneratedRestClient.CreateBodyContentAsync(new SeekingSerializer(), 0, pool, CancellationToken.None))
            Assert.Equal(expected.ToArray(), await CopyAsync(content));

        pool.AssertAllReturnedCleared();
    }

    private static async Task<HttpContent> CreateAsync(
        IRestSerializer serializer, TrackingPool pool, CancellationToken ct = default)
        => await GeneratedRestClient.CreateBodyContentAsync(serializer, 0, pool, ct).ConfigureAwait(false);

    private static async Task<byte[]> CopyAsync(HttpContent content)
    {
        using var wire = new MemoryStream();
        await content.CopyToAsync(wire).ConfigureAwait(false);
        return wire.ToArray();
    }

    public sealed record Person(string Name, int Age);

    // Writes fixed bytes, optionally in chunks, then optionally fails, cancels or disposes the stream.
    private sealed class BytesSerializer(
        byte[] bytes,
        int? chunk = null,
        Exception? failAfterWrite = null,
        CancellationTokenSource? cancelAfterWrite = null,
        bool disposeStream = false,
        string contentType = "application/test") : IRestSerializer
    {
        public string ContentType => contentType;

        public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        {
            var size = chunk ?? bytes.Length;
            for (var offset = 0; offset < bytes.Length; offset += size)
                await stream.WriteAsync(bytes.AsMemory(offset, Math.Min(size, bytes.Length - offset)), ct).ConfigureAwait(false);
            if (failAfterWrite != null)
                throw failAfterWrite;
            if (cancelAfterWrite != null)
            {
                await cancelAfterWrite.CancelAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            if (disposeStream)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Writes a placeholder, then seeks back to fill in the length after it, as a length-prefixed
    // format would. Also reads back what it wrote.
    private sealed class SeekingSerializer : IRestSerializer
    {
        public string ContentType => "application/test";

        public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        {
            stream.Write([0, 0, 0, 0]);
            stream.Write("payload"u8);
            var end = stream.Position;
            stream.Position = 0;
            stream.Write(BitConverter.GetBytes((int)(end - 4)));
            stream.Position = 0;
            var header = new byte[4];
            stream.ReadExactly(header);
            stream.Seek(0, SeekOrigin.End);
            stream.WriteByte(header[0]);
            return ValueTask.CompletedTask;
        }
    }

    // Rents fresh, zeroed arrays, so a returned array that is not all zeros was not cleared.
    private sealed class TrackingPool : ArrayPool<byte>
    {
        private readonly HashSet<byte[]> _outstanding = new(ReferenceEqualityComparer.Instance);
        private readonly List<byte[]> _notCleared = [];

        public int Rented { get; private set; }

        public int Outstanding
        {
            get
            {
                lock (_outstanding)
                    return _outstanding.Count;
            }
        }

        public override byte[] Rent(int minimumLength)
        {
            var array = new byte[minimumLength];
            lock (_outstanding)
            {
                Rented++;
                _outstanding.Add(array);
            }
            return array;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            lock (_outstanding)
            {
                Assert.True(_outstanding.Remove(array), "A buffer was returned twice, or was not rented from this pool.");
                if (!clearArray && array.AsSpan().ContainsAnyExcept((byte)0))
                    _notCleared.Add(array);
            }
        }

        public void AssertAllReturnedCleared()
        {
            lock (_outstanding)
            {
                Assert.Empty(_outstanding);
                Assert.Empty(_notCleared);
            }
        }
    }

    // A wire whose first write blocks until the test releases it.
    private sealed class GatedStream : MemoryStream
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WriteStarted => _started.Task;

        public void Release() => _release.SetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}

file static class StreamExtensions
{
    public static byte[] ReadAllBytes(this Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
