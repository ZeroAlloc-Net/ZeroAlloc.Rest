using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace ZeroAlloc.Rest;

/// <summary>
/// Reads a <c>text/event-stream</c> one event at a time, following the WHATWG event-stream rules.
/// It scans bytes, not characters, and keeps its working memory in pooled buffers that
/// <see cref="DisposeAsync"/> returns. The reader disposes only the <see cref="Stream"/> it was given;
/// the caller owns and disposes the <c>HttpResponseMessage</c> that the stream came from.
/// </summary>
/// <remarks>
/// <para>
/// One deviation from the browser rule: the last event is dispatched when the stream ends without a
/// blank line after it, where a browser would drop it. That includes an unterminated partial line at
/// the end of the stream, which is read as a field line and, if it is a <c>data</c> line, delivered as
/// an event. A server that closes the connection right after its last event is common, and losing
/// that event is worse.
/// </para>
/// <para>
/// Line and event size are unbounded, as with the BCL <c>SseParser</c>: a server can make the pooled
/// buffers grow as large as it sends. Read only from servers you trust to bound their events.
/// </para>
/// <para>
/// The reader is not thread-safe. Call <see cref="ReadAsync"/> one at a time, and do not let
/// <see cref="DisposeAsync"/> overlap a <see cref="ReadAsync"/>.
/// </para>
/// </remarks>
public sealed class ServerSentEventReader : IAsyncDisposable
{
    private const int InitialBufferSize = 4096;
    private static readonly byte[] s_bom = [0xEF, 0xBB, 0xBF];

    private readonly Stream _stream;

    private byte[] _buf;
    private int _start;
    private int _end;
    private int _scanned; // bytes after _start already searched for a line end
    private bool _skipLf; // the previous line ended in CR, so a leading LF belongs to it
    private bool _atStart = true;
    private bool _eof;
    private bool _disposed;

    private byte[] _data;
    private int _dataLen;
    private int _dataLines;

    private byte[] _name;
    private int _nameLen;

    private byte[] _id;
    private int _idLen;
    private bool _idSet;

    /// <summary>Creates a reader over <paramref name="stream"/>, which the reader disposes with itself.</summary>
    /// <param name="stream">The response body.</param>
    public ServerSentEventReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buf = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        _data = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        _name = ArrayPool<byte>.Shared.Rent(64);
        _id = ArrayPool<byte>.Shared.Rent(64);
    }

    /// <summary>
    /// The data of the current event, with multi-line data joined by <c>\n</c>. It is valid until
    /// the next <see cref="ReadAsync"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data.AsMemory(0, Math.Max(_dataLen - 1, 0));

    /// <summary>The name of the current event. It is decoded on each read; <see langword="null"/> when there is none.</summary>
    public string? EventName => _nameLen == 0 ? null : Encoding.UTF8.GetString(_name, 0, _nameLen);

    /// <summary>
    /// The last <c>id:</c> the stream set. Like the browser's last event id it persists across
    /// events. It is <see langword="null"/> before any id.
    /// </summary>
    public string? LastEventId => _idSet ? Encoding.UTF8.GetString(_id, 0, _idLen) : null;

    /// <summary>The last valid <c>retry:</c> the stream set, in milliseconds; <see langword="null"/> before any.</summary>
    public int? RetryMilliseconds { get; private set; }

    /// <summary>Compares the current event name with <paramref name="utf8"/> without allocating.</summary>
    /// <param name="utf8">The UTF-8 bytes of the name to test for.</param>
    /// <returns><see langword="true"/> when the current event has exactly that name.</returns>
    public bool EventNameEquals(ReadOnlySpan<byte> utf8) =>
        _name.AsSpan(0, _nameLen).SequenceEqual(utf8);

    /// <summary>Reads the next event.</summary>
    /// <param name="ct">Cancels the read; the stream read throws <see cref="OperationCanceledException"/>.</param>
    /// <returns><see langword="true"/> when an event is available; <see langword="false"/> at the end of the stream.</returns>
    public async ValueTask<bool> ReadAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ResetEvent();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (TryTakeLine(out var line))
            {
                if (line.Length == 0)
                {
                    if (_dataLines > 0)
                    {
                        return true;
                    }

                    ResetEvent();
                }
                else
                {
                    ProcessLine(line);
                }

                continue;
            }

            if (_eof)
            {
                // A last event with no blank line after it is still delivered.
                if (_dataLines > 0)
                {
                    return true;
                }

                ResetEvent();
                return false;
            }

            await FillAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Returns the pooled buffers and disposes the stream. It must not overlap a <see cref="ReadAsync"/>.</summary>
    /// <returns>A task that completes when the stream is disposed.</returns>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buf);
        ArrayPool<byte>.Shared.Return(_data);
        ArrayPool<byte>.Shared.Return(_name);
        ArrayPool<byte>.Shared.Return(_id);
        return _stream.DisposeAsync();
    }

    private void ResetEvent()
    {
        _dataLen = 0;
        _dataLines = 0;
        _nameLen = 0;
    }

    // Splits the next complete line off the buffer. A pending partial line, or an undecided BOM,
    // returns false so the caller reads more. At the end of the stream the partial line is the line.
    private bool TryTakeLine(out ReadOnlySpan<byte> line)
    {
        line = default;

        if (_atStart)
        {
            var avail = _end - _start;
            var check = Math.Min(avail, s_bom.Length);
            if (!_buf.AsSpan(_start, check).SequenceEqual(s_bom.AsSpan(0, check)))
            {
                _atStart = false;
            }
            else if (check == s_bom.Length)
            {
                _start += s_bom.Length;
                _atStart = false;
            }
            else if (!_eof)
            {
                return false;
            }
            else
            {
                _atStart = false;
            }
        }

        if (_skipLf && _start < _end)
        {
            if (_buf[_start] == (byte)'\n')
            {
                _start++;
            }

            _skipLf = false;
        }

        var window = _buf.AsSpan(_start + _scanned, _end - _start - _scanned);
        var i = window.IndexOfAny((byte)'\r', (byte)'\n');
        if (i < 0)
        {
            _scanned = _end - _start;
            if (!_eof || _scanned == 0)
            {
                return false;
            }

            line = _buf.AsSpan(_start, _scanned);
            _start = _end;
            _scanned = 0;
            return true;
        }

        var length = _scanned + i;
        line = _buf.AsSpan(_start, length);
        _skipLf = _buf[_start + length] == (byte)'\r';
        _start += length + 1;
        _scanned = 0;
        return true;
    }

    private async ValueTask FillAsync(CancellationToken ct)
    {
        if (_start == _end)
        {
            _start = _end = 0;
        }
        else if (_end == _buf.Length)
        {
            if (_start > 0)
            {
                Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            else
            {
                Grow(ref _buf, _end, _end + 1);
            }
        }

        var read = await _stream.ReadAsync(_buf.AsMemory(_end), ct).ConfigureAwait(false);
        if (read == 0)
        {
            _eof = true;
        }
        else
        {
            _end += read;
        }
    }

    private void ProcessLine(ReadOnlySpan<byte> line)
    {
        var colon = line.IndexOf((byte)':');
        if (colon == 0)
        {
            return; // comment
        }

        ReadOnlySpan<byte> field;
        ReadOnlySpan<byte> value;
        if (colon < 0)
        {
            field = line;
            value = default;
        }
        else
        {
            field = line[..colon];
            value = line[(colon + 1)..];
            if (value.Length > 0 && value[0] == (byte)' ')
            {
                value = value[1..];
            }
        }

        if (field.SequenceEqual("data"u8))
        {
            Grow(ref _data, _dataLen, _dataLen + value.Length + 1);
            value.CopyTo(_data.AsSpan(_dataLen));
            _dataLen += value.Length;
            _data[_dataLen++] = (byte)'\n';
            _dataLines++;
        }
        else if (field.SequenceEqual("event"u8))
        {
            Grow(ref _name, 0, value.Length);
            value.CopyTo(_name);
            _nameLen = value.Length;
        }
        else if (field.SequenceEqual("id"u8))
        {
            if (!value.Contains((byte)0))
            {
                Grow(ref _id, 0, value.Length);
                value.CopyTo(_id);
                _idLen = value.Length;
                _idSet = true;
            }
        }
        else if (field.SequenceEqual("retry"u8))
        {
            if (IsAsciiDigits(value)
                && Utf8Parser.TryParse(value, out int ms, out _))
            {
                RetryMilliseconds = ms;
            }
        }
    }

    private static bool IsAsciiDigits(ReadOnlySpan<byte> value)
    {
        foreach (var b in value)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    // Makes buffer hold at least `needed` bytes, keeping the first `used` bytes.
    private static void Grow(ref byte[] buffer, int used, int needed)
    {
        if (needed <= buffer.Length)
        {
            return;
        }

        var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(needed, buffer.Length * 2));
        Buffer.BlockCopy(buffer, 0, bigger, 0, used);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = bigger;
    }
}
