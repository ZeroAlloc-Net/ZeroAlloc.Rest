using MemoryPack;
using Xunit;
using ZeroAlloc.Rest.MemoryPack;

namespace ZeroAlloc.Rest.Tests.Serializers;

// Issue #362: MemoryPackRestSerializer reads and writes a body through pooled buffers, so a call
// allocates no copy of the payload of its own.
public sealed class MemoryPackSerializerBufferTests
{
    private const int PayloadInts = 8 * 1024;
    private const int PayloadBytes = PayloadInts * sizeof(int);

    private readonly MemoryPackRestSerializer _sut = new(types => types.Add<MemPackTestDto>());

    [Fact]
    public async Task NonSeekableStream_LargePayload_RoundTrips_AcrossShortReads()
    {
        var payload = Payload();
        var bytes = MemoryPackSerializer.Serialize(payload);

        var result = await _sut.DeserializeAsync<int[]>(new TrickleStream(bytes, chunk: 1000));

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task NonSeekableStream_Dto_RoundTrips()
    {
        var bytes = MemoryPackSerializer.Serialize(new MemPackTestDto("Alice", 30));

        var result = await _sut.DeserializeAsync<MemPackTestDto>(new TrickleStream(bytes, chunk: 3));

        Assert.Equal(new MemPackTestDto("Alice", 30), result);
    }

    [Fact]
    public async Task SeekableStream_ReadsFromItsPosition()
    {
        var bytes = MemoryPackSerializer.Serialize(new MemPackTestDto("Bob", 7));
        using var stream = new MemoryStream();
        stream.Write([9, 9, 9]);
        stream.Write(bytes);
        stream.Position = 3;

        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);

        Assert.Equal(new MemPackTestDto("Bob", 7), result);
    }

    [Fact]
    public async Task NonSeekableStream_Empty_ThrowsAsBefore()
        => await Assert.ThrowsAnyAsync<MemoryPackSerializationException>(
            () => _sut.DeserializeAsync<int>(new TrickleStream([], chunk: 1)).AsTask());

    [Fact]
    public async Task NonSeekableStream_DoesNotCopyThePayload()
    {
        var bytes = MemoryPackSerializer.Serialize(Payload());
        // Warm up: the first call rents the pool's arrays and JITs the path.
        await _sut.DeserializeAsync<int[]>(new TrickleStream(bytes, chunk: 4096));

        var stream = new TrickleStream(bytes, chunk: 4096);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await _sut.DeserializeAsync<int[]>(stream);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(PayloadInts, result!.Length);
        // The result array itself is PayloadBytes. A copy of the body, as a MemoryStream and its
        // ToArray made, would add at least as much again.
        Assert.True(allocated < PayloadBytes + 4096, $"Allocated {allocated} bytes for a {PayloadBytes}-byte payload.");
    }

    [Fact]
    public async Task SeekableStream_DoesNotCopyThePayload()
    {
        var bytes = MemoryPackSerializer.Serialize(Payload());
        using var warmUp = new MemoryStream(bytes, writable: false);
        await _sut.DeserializeAsync<int[]>(warmUp);

        using var stream = new MemoryStream(bytes, writable: false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await _sut.DeserializeAsync<int[]>(stream);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(PayloadInts, result!.Length);
        Assert.True(allocated < PayloadBytes + 4096, $"Allocated {allocated} bytes for a {PayloadBytes}-byte payload.");
    }

    [Fact]
    public async Task Serialize_DoesNotCopyThePayload()
    {
        var payload = Payload();
        using var destination = new MemoryStream(capacity: PayloadBytes * 2);
        await _sut.SerializeAsync(destination, payload);
        var expected = destination.ToArray();
        destination.SetLength(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        await _sut.SerializeAsync(destination, payload);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, destination.ToArray());
        Assert.Equal(MemoryPackSerializer.Serialize(payload), expected);
        Assert.True(allocated < 4096, $"Allocated {allocated} bytes to serialize a {PayloadBytes}-byte payload.");
    }

    [Fact]
    public async Task Deserialize_ClearsTheBodyBeforeReturningThePooledArray()
    {
        var bytes = MemoryPackSerializer.Serialize(new MemPackTestDto("Secret", 42));
        var stream = new CapturingStream(bytes, failAfterWrite: false);

        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);

        Assert.Equal(new MemPackTestDto("Secret", 42), result);
        Assert.NotEmpty(stream.Buffers);
        Assert.All(stream.Buffers, buffer => Assert.DoesNotContain(buffer, b => b != 0));
    }

    [Fact]
    public async Task Deserialize_ReadThatFailsPartWay_ClearsWhatItWrote()
    {
        // A connection that drops mid-read may already have written into the rented span without
        // the read returning a count, so the bytes it wrote were never advanced over.
        var stream = new CapturingStream([0xAB, 0xAB, 0xAB, 0xAB], failAfterWrite: true);

        await Assert.ThrowsAsync<IOException>(() => _sut.DeserializeAsync<MemPackTestDto>(stream).AsTask());

        var buffer = Assert.Single(stream.Buffers);
        Assert.DoesNotContain(buffer, b => b != 0);
    }

    private static int[] Payload()
    {
        var payload = new int[PayloadInts];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = i * 31;
        return payload;
    }

    // A non-seekable stream that writes `body` into the buffer it is handed and records that
    // buffer's array, so a test can check what is left in it after the array goes back to the pool.
    // With `failAfterWrite`, it throws after writing, as a connection that drops mid-read can.
    private sealed class CapturingStream(byte[] body, bool failAfterWrite) : Stream
    {
        private int _position;

        public List<byte[]> Buffers { get; } = [];

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray<byte>(buffer, out var segment));
            if (!Buffers.Contains(segment.Array!))
                Buffers.Add(segment.Array!);
            var count = Math.Min(buffer.Length, body.Length - _position);
            body.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            if (failAfterWrite)
                return ValueTask.FromException<int>(new IOException("The connection dropped."));
            return ValueTask.FromResult(count);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A non-seekable stream that returns at most `chunk` bytes per read, as a connection does.
    private sealed class TrickleStream(byte[] body, int chunk) : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, chunk), body.Length - _position);
            body.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
