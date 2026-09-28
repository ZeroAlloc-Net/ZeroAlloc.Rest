using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace ZeroAlloc.Rest;

public static partial class GeneratedRestClient
{
    /// <summary>
    /// Serializes a request body into a pooled buffer and returns content that sends it. Generated
    /// clients call this for a <c>[Body]</c> parameter and assign the result to the request, which
    /// then owns it.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="serializer">The serializer that writes the body and names its content type.</param>
    /// <param name="value">The body.</param>
    /// <param name="cancellationToken">The caller's token, passed to the serializer.</param>
    /// <returns>
    /// Content that reports the body's length and can be sent more than once. Disposing it clears
    /// the buffer and returns it to the pool; a send still copying from the buffer at that moment
    /// keeps it until the copy ends.
    /// </returns>
    /// <remarks>
    /// When the serializer throws, including on cancellation, the buffer is cleared and returned
    /// before the exception propagates, and no content is created.
    /// </remarks>
    public static ValueTask<HttpContent> CreateBodyContentAsync<T>(
        IRestSerializer serializer, T value, CancellationToken cancellationToken)
        => CreateBodyContentAsync(serializer, value, ArrayPool<byte>.Shared, cancellationToken);

    internal static async ValueTask<HttpContent> CreateBodyContentAsync<T>(
        IRestSerializer serializer, T value, ArrayPool<byte> pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        var stream = new PooledBufferStream(pool);
        try
        {
            await serializer.SerializeAsync(stream, value, cancellationToken).ConfigureAwait(false);
            var content = new PooledBodyContent(pool, stream.DetachBuffer(), (int)stream.Length);
            try
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(serializer.ContentType);
            }
            catch
            {
                // A content type that is not a valid media type: the content owns the buffer now.
                content.Dispose();
                throw;
            }
            return content;
        }
        finally
        {
            // Returns the buffer unless the content took it. Request bodies can hold tokens or PII,
            // so the returned buffer is cleared first.
            stream.ReturnBuffer();
        }
    }

    /// <summary>
    /// A seekable, growable stream over a buffer rented from a pool, which a serializer writes the
    /// request body into. It behaves like a <see cref="MemoryStream"/>, so a serializer that seeks
    /// or reads back what it wrote keeps working.
    /// </summary>
    /// <remarks>
    /// Disposing the stream does not return the buffer, so a serializer that disposes the stream it
    /// was given cannot hand the buffer back to the pool while the content still needs it. The
    /// owner returns it with <see cref="ReturnBuffer"/> or takes it with <see cref="DetachBuffer"/>.
    /// </remarks>
    private sealed class PooledBufferStream(ArrayPool<byte> pool) : Stream
    {
        private const int InitialSize = 4096;

        private byte[] _buffer = [];
        private int _length;
        private int _position;
        private bool _detached;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Array.MaxLength);
                _position = (int)value;
            }
        }

        public byte[] DetachBuffer()
        {
            _detached = true;
            return _buffer;
        }

        public void ReturnBuffer()
        {
            if (_detached)
                return;
            _detached = true;
            if (_buffer.Length == 0)
                return;
            _buffer.AsSpan(0, _length).Clear();
            pool.Return(_buffer);
            _buffer = [];
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
            => cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var available = _length - _position;
            if (available <= 0)
                return 0;
            var read = Math.Min(available, buffer.Length);
            _buffer.AsSpan(_position, read).CopyTo(buffer);
            _position += read;
            return read;
        }

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
                : new ValueTask<int>(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0)
                throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            Position = target;
            return _position;
        }

        public override void SetLength(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Array.MaxLength);
            var length = (int)value;
            if (length > _length)
            {
                EnsureCapacity(length);
                _buffer.AsSpan(_length, length - _length).Clear();
            }
            else
            {
                // Bytes cut off the end are cleared, as they are when the buffer goes back to the pool.
                _buffer.AsSpan(length, _length - length).Clear();
            }
            _length = length;
            if (_position > length)
                _position = length;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            var end = (long)_position + buffer.Length;
            if (end > Array.MaxLength)
                throw new IOException("The request body is larger than the largest supported buffer.");
            EnsureCapacity((int)end);
            // Writing past the end, after a seek there, leaves a gap that reads back as zeros.
            if (_position > _length)
                _buffer.AsSpan(_length, _position - _length).Clear();
            buffer.CopyTo(_buffer.AsSpan(_position));
            _position = (int)end;
            if (_position > _length)
                _length = _position;
        }

        public override void WriteByte(byte value) => Write([value]);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
                return;
            var size = Math.Max(required, _buffer.Length == 0
                ? InitialSize
                : (int)Math.Min((long)_buffer.Length * 2, Array.MaxLength));
            var larger = pool.Rent(size);
            if (_buffer.Length != 0)
            {
                _buffer.AsSpan(0, _length).CopyTo(larger);
                _buffer.AsSpan(0, _length).Clear();
                pool.Return(_buffer);
            }
            _buffer = larger;
        }
    }

    /// <summary>
    /// Sends a request body held in a pooled buffer. The buffer goes back to the pool once the
    /// content is disposed and no send is copying from it, so a send that outlives the request,
    /// such as an HTTP/2 upload the server answered early, never reads a returned buffer.
    /// </summary>
    private sealed class PooledBodyContent : HttpContent
    {
        private readonly ArrayPool<byte> _pool;
        private readonly int _length;
        private byte[] _buffer;

        // One lease for the content itself, released by Dispose, plus one per send in progress.
        // The buffer is returned when the count reaches zero, and a send cannot start after that.
        private int _leases = 1;
        private int _disposed;

        public PooledBodyContent(ArrayPool<byte> pool, byte[] buffer, int length)
        {
            _pool = pool;
            _buffer = buffer;
            _length = length;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = Acquire();
            try
            {
                await stream.WriteAsync(buffer.AsMemory(0, _length), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Release();
            }
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = Acquire();
            try
            {
                stream.Write(buffer, 0, _length);
            }
            finally
            {
                Release();
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
                Release();
            base.Dispose(disposing);
        }

        private byte[] Acquire()
        {
            while (true)
            {
                var leases = Volatile.Read(ref _leases);
                ObjectDisposedException.ThrowIf(leases == 0, this);
                if (Interlocked.CompareExchange(ref _leases, leases + 1, leases) == leases)
                    return _buffer;
            }
        }

        private void Release()
        {
            if (Interlocked.Decrement(ref _leases) != 0)
                return;
            var buffer = _buffer;
            _buffer = [];
            if (buffer.Length == 0)
                return;
            // Request bodies can hold tokens or PII; do not leave them in the returned buffer.
            buffer.AsSpan(0, _length).Clear();
            _pool.Return(buffer);
        }
    }
}
