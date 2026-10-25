using System.Net;
using System.Text;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

public sealed class ServerSentEventReaderTests
{
    public static TheoryData<int> ChunkSizes => new() { 1, 4096 };

    private static ServerSentEventReader ReaderOver(string text, int chunk) =>
        ReaderOver(Encoding.UTF8.GetBytes(text), chunk);

    private static ServerSentEventReader ReaderOver(byte[] bytes, int chunk) =>
        new(new ChunkedStream(bytes, chunk));

    private static async Task<List<(string? Name, string Data)>> ReadAllAsync(ServerSentEventReader reader)
    {
        var events = new List<(string?, string)>();
        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            events.Add((reader.EventName, Encoding.UTF8.GetString(reader.Data.Span)));
        }

        return events;
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task Named_events_with_json_data_are_read_in_order(int chunk)
    {
        await using var reader = ReaderOver("event: a\ndata: {\"x\":1}\n\nevent: b\ndata: 2\n\n", chunk);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        // Red: make EventNameEquals return false, or compare against the wrong name.
        Assert.True(reader.EventNameEquals("a"u8));
        // Red: make EventNameEquals ignore the length, so "ab" would match "a"; this one fails when it always returns true.
        Assert.False(reader.EventNameEquals("b"u8));
        // Red: make EventName return null, or skip the field.
        Assert.Equal("a", reader.EventName);
        // Red: stop removing the single space after the colon, or keep the trailing newline.
        Assert.Equal("{\"x\":1}", Encoding.UTF8.GetString(reader.Data.Span));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        // Red: make EventName return null, or let the first event's name stay in place of "b".
        Assert.Equal("b", reader.EventName);
        Assert.Equal("2", Encoding.UTF8.GetString(reader.Data.Span));

        // Red: return true at the end of the stream instead of false.
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task Multi_line_data_is_joined_with_newlines(int chunk)
    {
        await using var reader = ReaderOver("data: l1\ndata: l2\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: join the lines with nothing, or with a space, instead of a newline.
        Assert.Equal([(null, "l1\nl2")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task Comment_lines_are_ignored(int chunk)
    {
        await using var reader = ReaderOver(": keep-alive\ndata: 1\n: another\n\n: tail\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: treat a line that starts with a colon as a data line.
        Assert.Equal([(null, "1")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task Retry_and_id_fields_are_exposed(int chunk)
    {
        await using var reader = ReaderOver("retry: 3000\nid: 7\ndata: x\n\ndata: y\n\n", chunk);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        // Red: drop the retry branch in ProcessLine.
        Assert.Equal(3000, reader.RetryMilliseconds);
        // Red: drop the id branch in ProcessLine.
        Assert.Equal("7", reader.LastEventId);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        // Red: clear the id at the start of each event. The id persists across events, like the browser's last event id.
        Assert.Equal("7", reader.LastEventId);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task A_missing_final_blank_line_still_yields_the_last_event(int chunk)
    {
        await using var reader = ReaderOver("data: a\n\ndata: b", chunk);

        var events = await ReadAllAsync(reader);

        // Red: return false at the end of the stream without dispatching the pending event, or drop the partial last line.
        Assert.Equal([(null, "a"), (null, "b")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task CRLF_and_CR_line_endings_are_accepted(int chunk)
    {
        await using var crlf = ReaderOver("event: a\r\ndata: 1\r\ndata: 2\r\n\r\ndata: 3\r\n\r\n", chunk);
        await using var cr = ReaderOver("event: a\rdata: 1\rdata: 2\r\rdata: 3\r\r", chunk);

        var viaCrlf = await ReadAllAsync(crlf);
        var viaCr = await ReadAllAsync(cr);

        // Red: stop skipping the LF after a CR, so CRLF reads as two lines and splits the event after its first data line.
        Assert.Equal([("a", "1\n2"), (null, "3")], viaCrlf);
        // Red: treat a lone CR as ordinary data instead of a line end.
        Assert.Equal([("a", "1\n2"), (null, "3")], viaCr);
    }

    [Fact]
    public async Task A_CR_at_a_read_boundary_before_an_LF_is_one_line_end()
    {
        // The first read ends on the CR, the second begins with the LF.
        await using var reader = new ServerSentEventReader(new ChunkedStream("data: 1\r\ndata: 2\r\n\r\n"u8.ToArray(), 8));

        var events = await ReadAllAsync(reader);

        // Red: decide the CR is a complete line end and read the LF as an empty line, which dispatches "1" alone.
        Assert.Equal([(null, "1\n2")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task A_UTF8_sequence_split_across_reads_is_kept_intact(int chunk)
    {
        await using var reader = ReaderOver("data: é😀\n\n", chunk);

        Assert.True(await reader.ReadAsync(CancellationToken.None));

        // Red: process the partial line at the end of each read instead of waiting for the line end.
        Assert.Equal("é😀", Encoding.UTF8.GetString(reader.Data.Span));
        Assert.Equal(Encoding.UTF8.GetBytes("é😀"), reader.Data.ToArray());
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task A_field_without_a_space_after_the_colon_is_read(int chunk)
    {
        await using var reader = ReaderOver("data:x\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: skip one byte after the colon unconditionally, which loses the x.
        Assert.Equal([(null, "x")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task Only_one_space_after_the_colon_is_removed(int chunk)
    {
        await using var reader = ReaderOver("data:  two\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: trim all leading whitespace from the value.
        Assert.Equal([(null, " two")], events);
    }

    [Fact]
    public async Task Cancellation_mid_stream_throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await using var reader = new ServerSentEventReader(new HangingStream("data: 1\n\n"u8.ToArray()));

        Assert.True(await reader.ReadAsync(cts.Token));
        var pending = reader.ReadAsync(cts.Token).AsTask();
        cts.CancelAfter(50);

        // Red: pass CancellationToken.None to the stream read; the read then hangs and WaitAsync times out instead.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task An_already_cancelled_token_throws_before_reading()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await using var reader = ReaderOver("data: 1\n\n", 4096);

        // Red: remove ct.ThrowIfCancellationRequested from the read loop, then a stream that ignores the token still delivers the event.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token).AsTask());
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task A_line_longer_than_the_initial_buffer_is_read(int chunk)
    {
        var payload = new string('x', 100 * 1024);
        await using var reader = ReaderOver($"data: {payload}\n\ndata: after\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: make Grow return without growing; the read buffer or the data buffer overflows.
        Assert.Equal(2, events.Count);
        Assert.Equal(payload, events[0].Data);
        Assert.Equal("after", events[1].Data);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task A_leading_BOM_is_stripped(int chunk)
    {
        await using var reader = ReaderOver([0xEF, 0xBB, 0xBF, .. "data: a\n\n"u8], chunk);

        var events = await ReadAllAsync(reader);

        // Red: remove the BOM handling; the first line is then "﻿data: a", an unknown field, and no event arrives.
        Assert.Equal([(null, "a")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task A_BOM_lookalike_prefix_is_not_stripped(int chunk)
    {
        // 0xEF 0xBB then "x" is not a BOM, so the first line is an unknown field and no event arrives.
        await using var reader = ReaderOver([0xEF, 0xBB, (byte)'x', .. "data: a\n\n"u8], chunk);

        var events = await ReadAllAsync(reader);

        // Red: skip three bytes at the start without checking that they are the BOM; "data: a" is then an event.
        Assert.Empty(events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task An_event_with_no_data_lines_is_not_dispatched(int chunk)
    {
        await using var reader = ReaderOver("event: ping\n\ndata: a\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: dispatch on every blank line; a nameless-data "ping" event then appears first.
        // Also red: skip the name reset on a dropped event, then "ping" leaks into the next event.
        Assert.Equal([(null, "a")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task An_empty_data_line_still_dispatches_an_event(int chunk)
    {
        await using var reader = ReaderOver("data:\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: count dispatchable events by data length instead of data lines.
        Assert.Equal([(null, "")], events);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task An_id_containing_NUL_is_ignored(int chunk)
    {
        await using var reader = ReaderOver("id: 1\ndata: a\n\nid: 2\0x\ndata: b\n\n", chunk);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(await reader.ReadAsync(CancellationToken.None));

        // Red: remove the NUL check, then the id becomes "2\0x".
        Assert.Equal("1", reader.LastEventId);
    }

    [Theory]
    [InlineData("retry: 12x\n", null)]
    [InlineData("retry: -5\n", null)]
    [InlineData("retry:\n", null)]
    [InlineData("retry: 99999999999\n", null)]
    [InlineData("retry: 250\n", 250)]
    public async Task Retry_is_used_only_when_it_is_ASCII_digits(string line, int? expected)
    {
        await using var reader = ReaderOver(line + "data: a\n\n", 4096);

        Assert.True(await reader.ReadAsync(CancellationToken.None));

        // Red: drop IsAsciiDigits, then "-5" parses to -5; drop the length check, then "12x" parses to 12.
        Assert.Equal(expected, reader.RetryMilliseconds);
    }

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task Unknown_fields_and_a_field_without_a_colon_are_ignored(int chunk)
    {
        await using var reader = ReaderOver("foo: bar\ndata\ndata: a\n\n", chunk);

        var events = await ReadAllAsync(reader);

        // Red: treat an unknown field as data; the bare "data" line is a data field with an empty value per spec, so it adds a leading newline.
        Assert.Equal([(null, "\na")], events);
    }

    [Fact]
    public async Task EventName_is_null_without_an_event_field_and_EventNameEquals_is_false_for_a_name()
    {
        await using var reader = ReaderOver("data: a\n\n", 4096);

        Assert.True(await reader.ReadAsync(CancellationToken.None));

        // Red: return an empty string instead of null when there is no event field.
        Assert.Null(reader.EventName);
        // Red: make EventNameEquals ignore the length check, so an absent name equals anything.
        Assert.False(reader.EventNameEquals("a"u8));
    }

    [Fact]
    public async Task EventNameEquals_needs_the_whole_name()
    {
        await using var reader = ReaderOver("event: abc\ndata: a\n\n", 4096);

        Assert.True(await reader.ReadAsync(CancellationToken.None));

        // Red: compare only the shorter length, so the prefix "ab" matches.
        Assert.False(reader.EventNameEquals("ab"u8));
        Assert.False(reader.EventNameEquals("abcd"u8));
        Assert.True(reader.EventNameEquals("abc"u8));
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_stream_and_is_idempotent_and_blocks_reads()
    {
        var stream = new ChunkedStream("data: a\n\n"u8.ToArray(), 4096);
        var reader = new ServerSentEventReader(stream);

        await reader.DisposeAsync();
        await reader.DisposeAsync();

        // Red: do not dispose the stream in DisposeAsync.
        Assert.True(stream.IsDisposed);
        // Red: remove the ObjectDisposedException guard; the read would use buffers already returned to the pool.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ReadAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public void A_null_stream_is_rejected()
    {
        // Red: remove the null check.
        Assert.Throws<ArgumentNullException>(() => new ServerSentEventReader(null!));
    }

    [Fact]
    public void HttpErrorException_carries_the_error_and_sets_the_status_code()
    {
        var error = new HttpError(HttpStatusCode.TooManyRequests, new Dictionary<string, IReadOnlyList<string>>());

        var ex = new HttpErrorException(error);

        // Red: pass null instead of error.StatusCode to the base constructor.
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        // Red: do not store the error.
        Assert.Same(error, ex.Error);
        // Red: drop the default message; the base class then uses a generic one without the status.
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public void HttpErrorException_uses_the_error_message_and_exception_when_present()
    {
        var inner = new InvalidOperationException("boom");
        var error = new HttpError(HttpStatusCode.BadGateway, new Dictionary<string, IReadOnlyList<string>>(), "upstream down")
        {
            Exception = inner,
        };

        var ex = new HttpErrorException(error);

        // Red: ignore error.Message in favour of the default text.
        Assert.Equal("upstream down", ex.Message);
        // Red: do not pass error.Exception as the inner exception.
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void HttpErrorException_has_no_status_code_when_the_error_has_none()
    {
        var error = new HttpError(default, new Dictionary<string, IReadOnlyList<string>>(), "no response");

        var ex = new HttpErrorException(error);

        // Red: pass error.StatusCode unconditionally; StatusCode then becomes 0 instead of null.
        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public void HttpErrorException_of_TError_carries_the_mapped_error()
    {
        var error = new HttpError(HttpStatusCode.BadRequest, new Dictionary<string, IReadOnlyList<string>>());

        var ex = new HttpErrorException<string>(error, "mapped");

        // Red: store a default instead of mappedError.
        Assert.Equal("mapped", ex.MappedError);
        // Red: do not chain the base constructor, or pass a different error.
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.IsAssignableFrom<HttpErrorException>(ex);
    }

    [Fact]
    public void HttpErrorException_rejects_a_null_error()
    {
        // Red: remove the null guard; the base call then throws NullReferenceException instead.
        Assert.Throws<ArgumentNullException>(() => new HttpErrorException(null!));
    }

    [Fact]
    public void ServerSentEvent_is_a_value_record()
    {
        var e = new ServerSentEvent<int>("a", "1", 5);

        // Red: swap two of the positional parameters.
        Assert.Equal(("a", "1", 5), (e.Event, e.Id, e.Data));
        Assert.Equal(new ServerSentEvent<int>("a", "1", 5), e);
    }

    // Hands out at most `max` bytes per read.
    private sealed class ChunkedStream(byte[] data, int max) : Stream
    {
        private int _pos;

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = Math.Min(Math.Min(max, buffer.Length), data.Length - _pos);
            data.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return new ValueTask<int>(n);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    // Returns its bytes once, then waits for cancellation, like a live connection with nothing to say.
    private sealed class HangingStream(byte[] first) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                first.CopyTo(buffer);
                return first.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
