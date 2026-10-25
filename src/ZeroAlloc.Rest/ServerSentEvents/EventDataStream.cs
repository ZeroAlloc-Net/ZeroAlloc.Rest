namespace ZeroAlloc.Rest;

// A read-only, seekable stream over one event's data. ServerSentEventReader keeps one and points it
// at each event's data in turn, so a serializer reads the data without a copy or a new stream. It
// owns no memory: the reader's pooled buffer holds the bytes, and they are valid only until the next
// read. Disposing it does nothing, so a serializer that disposes the stream it is given cannot break
// the next event.
internal sealed class EventDataStream : Stream
{
    private ReadOnlyMemory<byte> _data;
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _data.Length;

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = (int)Math.Min(value, _data.Length);
        }
    }

    internal void Reset(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _data.Length - _position);
        _data.Span.Slice(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override int ReadByte() => _position < _data.Length ? _data.Span[_position++] : -1;

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<int>(cancellationToken)
            : Task.FromResult(Read(buffer.AsSpan(offset, count)));
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<int>(cancellationToken)
            : ValueTask.FromResult(Read(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _data.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        Position = target;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Owns nothing, so there is nothing to release, and the reader reuses it for the next event.
    protected override void Dispose(bool disposing) => base.Dispose(disposing);
}
