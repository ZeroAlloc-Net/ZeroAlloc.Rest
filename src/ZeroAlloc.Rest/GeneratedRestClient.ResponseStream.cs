using System.Buffers;

namespace ZeroAlloc.Rest;

public static partial class GeneratedRestClient
{
    /// <summary>
    /// Opens the body of a success response for a client that streams responses, as
    /// <c>StreamResponses</c> on <c>[ZeroAllocRestClient]</c> or a method's HTTP attribute asks.
    /// Generated clients call this before deserializing, and dispose the response afterwards, which
    /// disposes the stream.
    /// </summary>
    /// <param name="content">The response content, read with <see cref="HttpCompletionOption.ResponseHeadersRead"/>.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// The body. An empty body is returned as an empty, seekable stream, as a buffered response's
    /// is, so the empty-body checks of the generated client and the serializers behave the same
    /// whether the response is streamed or not. A body with a <c>Content-Length</c> of 0 is empty
    /// without being read. A body whose length is not sent is empty when its first read returns no
    /// bytes; otherwise that byte is read ahead, and the stream returned replays it first.
    /// </returns>
    public static async ValueTask<Stream> ReadResponseStreamAsync(HttpContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var length = content.Headers.ContentLength;
        if (length == 0)
            return Stream.Null;

        // The content owns this stream and disposes it with the response.
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // A known, positive length is a body; a seekable stream already answers the empty checks.
        if (length is not null || stream.CanSeek)
            return stream;

        var peek = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            var read = await stream.ReadAsync(peek.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            return read == 0 ? Stream.Null : new ReadAheadStream(stream, peek[0]);
        }
        finally
        {
            peek[0] = 0;
            ArrayPool<byte>.Shared.Return(peek);
        }
    }

    // A read-only stream that returns one byte already read from `inner`, then the rest of `inner`.
    // It does not own `inner`: the response it came from disposes it.
    private sealed class ReadAheadStream(Stream inner, byte first) : Stream
    {
        private bool _firstPending = true;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;
            if (TakeFirst(buffer))
                return 1;
            return inner.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);
            if (buffer.IsEmpty)
                return ValueTask.FromResult(0);
            if (TakeFirst(buffer.Span))
                return ValueTask.FromResult(1);
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private bool TakeFirst(Span<byte> buffer)
        {
            if (!_firstPending)
                return false;
            _firstPending = false;
            buffer[0] = first;
            return true;
        }
    }
}
