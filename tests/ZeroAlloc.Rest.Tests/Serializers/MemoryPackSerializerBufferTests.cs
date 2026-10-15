using MemoryPack;
using Xunit;
using ZeroAlloc.Rest.MemoryPack;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Rest.Tests.Serializers;

// Issue #362: MemoryPackRestSerializer reads and writes a body through pooled buffers, so a call
// allocates no copy of the payload of its own.
public sealed class MemoryPackSerializerBufferTests
{
    private const int PayloadInts = 8 * 1024;
    private const int PayloadBytes = PayloadInts * sizeof(int);

    private const int Iterations = 1_000;

    // The gated calls use a smaller payload: a 32 KB result is larger than the GC's 8 KB allocation
    // quantum, and on CI one window of such calls read 8,168 B over its exact budget, which no
    // local run reproduced. A 4 KB payload keeps every object within one quantum, and a copy of
    // the body would still break the exact budget.
    private const int GatedPayloadInts = 1024;
    private const int GatedPayloadBytes = GatedPayloadInts * sizeof(int);

    // Measured on .NET 10 x64, Release. The body is gathered in an array rented from the pool, so
    // a call allocates no copy of it, which would add at least GatedPayloadBytes. The buffer that
    // holds the rented array is a struct local since #445, so a write allocates nothing and a read
    // allocates only its int[] result: GatedPayloadBytes plus the 24 B array header.
    private const int DeserializeBytesPerCall = GatedPayloadBytes + 24;
    private const int SerializeBytesPerCall = 0;

    private readonly MemoryPackRestSerializer _sut = new(types => types.Add<MemPackTestDto>());

    [Fact]
    public async Task NonSeekableStream_LargePayload_RoundTrips_AcrossShortReads()
    {
        var payload = Payload(PayloadInts);
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
        var bytes = MemoryPackSerializer.Serialize(Payload(GatedPayloadInts));
        var stream = new TrickleStream(bytes, chunk: 4096);

        AllocationGate.AssertBudgetValueTask(
            DeserializeBytesPerCall,
            Iterations,
            () =>
            {
                stream.Rewind();
                return _sut.DeserializeAsync<int[]>(stream);
            },
            $"MemoryPack read of a {GatedPayloadBytes}-byte payload from a non-seekable stream");

        stream.Rewind();
        Assert.Equal(GatedPayloadInts, (await _sut.DeserializeAsync<int[]>(stream))!.Length);
    }

    [Fact]
    public async Task SeekableStream_DoesNotCopyThePayload()
    {
        var bytes = MemoryPackSerializer.Serialize(Payload(GatedPayloadInts));
        using var stream = new MemoryStream(bytes, writable: false);

        AllocationGate.AssertBudgetValueTask(
            DeserializeBytesPerCall,
            Iterations,
            () =>
            {
                stream.Position = 0;
                return _sut.DeserializeAsync<int[]>(stream);
            },
            $"MemoryPack read of a {GatedPayloadBytes}-byte payload from a seekable stream");

        stream.Position = 0;
        Assert.Equal(GatedPayloadInts, (await _sut.DeserializeAsync<int[]>(stream))!.Length);
    }

    [Fact]
    public async Task Serialize_DoesNotCopyThePayload()
    {
        var payload = Payload(GatedPayloadInts);
        using var destination = new MemoryStream(capacity: GatedPayloadBytes * 2);

        AllocationGate.AssertBudget(
            SerializeBytesPerCall,
            Iterations,
            () =>
            {
                destination.SetLength(0);
                RequireSynchronous(_sut.SerializeAsync(destination, payload));
            },
            $"MemoryPack write of a {GatedPayloadBytes}-byte payload");

        destination.SetLength(0);
        await _sut.SerializeAsync(destination, payload);
        Assert.Equal(MemoryPackSerializer.Serialize(payload), destination.ToArray());
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

    // AllocationGate counts this thread only, so the call must complete synchronously. A plain
    // check rather than an assertion: an assertion object would allocate in the loop.
    private static void RequireSynchronous(ValueTask call)
    {
        if (!call.IsCompletedSuccessfully)
            throw new InvalidOperationException("The call did not complete synchronously, so not all its allocations were on this thread.");
        call.GetAwaiter().GetResult();
    }

    private static int[] Payload(int count)
    {
        var payload = new int[count];
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

        // Starts the body over, so one stream can be read by every call of an allocation gate.
        public void Rewind() => _position = 0;

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
