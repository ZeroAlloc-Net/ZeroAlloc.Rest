using System.Buffers;

namespace ZeroAlloc.Rest;

public static partial class GeneratedRestClient
{
    // Caps up to this size rent one buffer that holds the whole capped body, plus the one byte
    // past the cap that detects truncation, so the body is copied once. A larger cap starts at
    // this size and grows, so a huge cap never rents a huge buffer up front.
    private const int MaxInitialErrorBodyRent = 1024 * 1024;

    /// <summary>
    /// Reads the body of a non-success response for an <see cref="HttpError"/>. Generated clients
    /// call this before the response is disposed.
    /// </summary>
    /// <param name="content">The response content.</param>
    /// <param name="maxBytes">The most bytes to keep. A value of 0 or less reads nothing.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// The body, cut to <paramref name="maxBytes"/>, and whether it was cut. The body is empty when
    /// the response has none, and when reading it fails: the status is the real failure, and a
    /// broken body must not hide it.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled during the read.
    /// </exception>
    public static async ValueTask<(ReadOnlyMemory<byte> Body, bool Truncated)> ReadErrorBodyAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var length = content.Headers.ContentLength;
        if (maxBytes <= 0 || length == 0)
            return (ReadOnlyMemory<byte>.Empty, false);

        try
        {
            // The content owns this stream and disposes it with the response.
            var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            // A Content-Length above Array.MaxLength cannot back a single array: new byte[known]
            // would throw OutOfMemoryException, which is not one of the failures this method
            // absorbs. The pooled, growable path handles it instead. Both paths share one limit, the
            // cap clamped to Array.MaxLength - 1, so no body escapes the documented clamp.
            if (length is { } known && known <= Math.Min(maxBytes, Array.MaxLength - 1))
                return (await ReadExactAsync(stream, (int)known, cancellationToken).ConfigureAwait(false), false);
            return await ReadCappedAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked to stop. Cancelling can surface mid-read as an I/O error, so it is
            // reported as the cancellation it is.
            if (ex is OperationCanceledException)
                throw;
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A timeout the caller did not ask for.
            return (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (IOException)
        {
            // The connection broke mid-body.
            return (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (HttpRequestException)
        {
            return (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (InvalidOperationException)
        {
            // The content was already consumed or disposed; ObjectDisposedException derives from this.
            return (ReadOnlyMemory<byte>.Empty, false);
        }
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadExactAsync(
        Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var read = await stream.ReadAtLeastAsync(buffer, length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        if (read == 0)
            return ReadOnlyMemory<byte>.Empty;
        // A stream shorter than the declared Content-Length leaves the rest of `buffer` unused;
        // copy down to an exact-size array so the body's backing array matches its real length.
        return read == length ? buffer : buffer.AsSpan(0, read).ToArray();
    }

    private static async ValueTask<(ReadOnlyMemory<byte> Body, bool Truncated)> ReadCappedAsync(
        Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        // Array.MaxLength is the largest array the runtime allows. The cap is clamped below it so
        // the one byte past the cap, used to detect truncation, always fits, and so a huge cap
        // still detects truncation instead of silently reading up to the array limit and stopping.
        var cap = Math.Min(maxBytes, Array.MaxLength - 1);
        // One byte past the cap tells a body of exactly `cap` bytes from a longer one.
        var limit = cap + 1;
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(limit, MaxInitialErrorBodyRent + 1));
        var total = 0;
        try
        {
            while (total < limit)
            {
                if (total == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent((int)Math.Min((long)buffer.Length * 2, limit));
                    buffer.AsSpan(0, total).CopyTo(larger);
                    // Error bodies can hold tokens or PII; do not leave them in the returned buffer.
                    buffer.AsSpan(0, total).Clear();
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var window = Math.Min(buffer.Length, limit) - total;
                var read = await stream.ReadAsync(buffer.AsMemory(total, window), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
            }

            var truncated = total > cap;
            var kept = truncated ? cap : total;
            return kept == 0
                ? (ReadOnlyMemory<byte>.Empty, false)
                : (new ReadOnlyMemory<byte>(buffer.AsSpan(0, kept).ToArray()), truncated);
        }
        finally
        {
            // Error bodies can hold tokens or PII; do not leave them in the returned buffer.
            buffer.AsSpan(0, total).Clear();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
