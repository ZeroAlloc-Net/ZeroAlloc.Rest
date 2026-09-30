using System.Net;
using System.Net.Http.Headers;

namespace ZeroAlloc.Rest;

public static partial class GeneratedRestClient
{
    /// <summary>
    /// The media type a <see cref="Stream"/> request body is sent with when its <c>[Body]</c>
    /// declares none.
    /// </summary>
    internal const string DefaultStreamMediaType = "application/octet-stream";

    /// <summary>
    /// Returns content that sends a <see cref="Stream"/> request body as it is, with no copy and no
    /// serializer. Generated clients call this for a <c>[Body] Stream</c> parameter and assign the
    /// result to the request.
    /// </summary>
    /// <param name="body">The body. It is read from its current position to its end.</param>
    /// <param name="mediaType">
    /// The media type the body is sent with, as <c>[Body(ContentType = ...)]</c> declares it, such as
    /// <c>image/png</c> or <c>text/plain; charset=utf-8</c>. <see langword="null"/> sends
    /// <c>application/octet-stream</c>.
    /// </param>
    /// <returns>
    /// Content that copies <paramref name="body"/> to the connection through a pooled buffer. A
    /// seekable body reports its remaining length as <c>Content-Length</c>; any other is sent
    /// chunked. The content does not dispose <paramref name="body"/>: the caller owns it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="mediaType"/> is not a valid media type.</exception>
    public static HttpContent CreateStreamBodyContent(Stream body, string? mediaType)
    {
        ArgumentNullException.ThrowIfNull(body);
        var content = new StreamBodyContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType ?? DefaultStreamMediaType);
        return content;
    }

    /// <summary>
    /// Sets the media type a serialized request body is sent with, replacing the serializer's, as
    /// <c>[Body(ContentType = ...)]</c> declares it. Generated clients call this right after
    /// <see cref="CreateBodyContentAsync{T}(IRestSerializer, T, CancellationToken)"/>.
    /// </summary>
    /// <param name="content">The body's content.</param>
    /// <param name="mediaType">The media type, such as <c>application/merge-patch+json</c>.</param>
    /// <returns><paramref name="content"/>.</returns>
    /// <exception cref="FormatException"><paramref name="mediaType"/> is not a valid media type.</exception>
    public static HttpContent WithMediaType(HttpContent content, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(mediaType);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return content;
    }

    /// <summary>
    /// Hands the body of a success response to the caller as a stream that owns the response.
    /// Generated clients call this for a method that returns <see cref="Stream"/>, after sending
    /// with <see cref="HttpCompletionOption.ResponseHeadersRead"/>.
    /// </summary>
    /// <param name="response">The response. On success the returned stream owns it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// A read-only stream over the body, read from the connection as the caller reads it, with no
    /// buffering. Disposing it disposes the response. An empty body is an empty stream.
    /// </returns>
    /// <remarks>
    /// When opening the body throws, the response is not disposed, and stays the caller's.
    /// </remarks>
    public static async ValueTask<Stream> OpenResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new ResponseOwningStream(response, body);
    }

    // Sends a caller's stream without taking ownership of it. StreamContent would dispose it with
    // the request, which a caller that reuses its stream, or a retry that resends it, cannot have.
    private sealed class StreamBodyContent : HttpContent
    {
        private readonly Stream _body;
        private readonly long _start;
        private bool _sent;

        public StreamBodyContent(Stream body)
        {
            _body = body;
            _start = body.CanSeek ? body.Position : -1;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            // A handler that sends the content again, such as one that retries or follows a 307,
            // sends it from where it started. A stream that cannot seek can be sent once.
            if (_sent)
            {
                if (_start < 0)
                    throw new InvalidOperationException("The request body is a stream that cannot seek, so it cannot be sent again.");
                _body.Position = _start;
            }
            _sent = true;
            // Stream.CopyToAsync copies through a buffer rented from ArrayPool<byte>.Shared.
            await _body.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_start >= 0)
            {
                length = _body.Length - _start;
                return true;
            }
            length = 0;
            return false;
        }
    }

    // A response body that owns its response: disposing the stream disposes the response, which
    // releases the connection. Everything else goes to the content's stream.
    private sealed class ResponseOwningStream(HttpResponseMessage response, Stream inner) : Stream
    {
        private int _disposed;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override bool CanTimeout => inner.CanTimeout;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int ReadTimeout
        {
            get => inner.ReadTimeout;
            set => inner.ReadTimeout = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override int ReadByte() => inner.ReadByte();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override void CopyTo(Stream destination, int bufferSize) => inner.CopyTo(destination, bufferSize);

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            => inner.CopyToAsync(destination, bufferSize, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await inner.DisposeAsync().ConfigureAwait(false);
                response.Dispose();
            }
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
