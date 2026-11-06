using Xunit;

namespace ZeroAlloc.Rest.Tests;

public sealed class EventDataStreamTests
{
    private static EventDataStream Over(string text)
    {
        var stream = new EventDataStream();
        stream.Reset(System.Text.Encoding.UTF8.GetBytes(text));
        return stream;
    }

    [Fact]
    public void It_is_a_read_only_seekable_stream_with_the_length_of_the_data()
    {
        using var stream = Over("hello");

        // Red: return false from CanRead, CanSeek or CanWrite.
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        // Red: return the position instead of the data length.
        Assert.Equal(5, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void ReadByte_reads_each_byte_then_minus_one()
    {
        using var stream = Over("ab");

        // Red: do not advance the position in ReadByte; the second read repeats the first byte.
        Assert.Equal('a', stream.ReadByte());
        Assert.Equal('b', stream.ReadByte());
        // Red: return 0 instead of -1 at the end.
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void The_array_Read_returns_at_most_what_is_left()
    {
        using var stream = Over("abcde");
        var buffer = new byte[8];

        // Red: honour count but not the offset, so the bytes land at the start of the array.
        Assert.Equal(3, stream.Read(buffer, 2, 3));
        Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(buffer, 2, 3));
        // Red: ignore the data left, so the read asks for 8 and reads past the end.
        Assert.Equal(2, stream.Read(buffer, 0, 8));
        Assert.Equal(0, stream.Read(buffer, 0, 8));
    }

    [Fact]
    public void The_span_Read_returns_at_most_what_is_left()
    {
        using var stream = Over("abcde");
        Span<byte> buffer = stackalloc byte[4];

        Assert.Equal(4, stream.Read(buffer));
        Assert.Equal("abcd", System.Text.Encoding.UTF8.GetString(buffer));
        Assert.Equal(1, stream.Read(buffer));
        Assert.Equal(0, stream.Read(buffer));
    }

    [Fact]
    public void The_array_Read_validates_its_arguments()
    {
        using var stream = Over("abc");

        // Red: skip ValidateBufferArguments in Read.
        Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[2], 1, 5));
    }

    [Fact]
    public async Task ReadAsync_reads_into_an_array_and_a_memory()
    {
        using var stream = Over("abcdef");
        var buffer = new byte[3];

        Assert.Equal(3, await stream.ReadAsync(buffer, 0, 3, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(buffer));
        Assert.Equal(3, await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("def", System.Text.Encoding.UTF8.GetString(buffer));
        Assert.Equal(0, await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ReadAsync_with_a_cancelled_token_is_cancelled_and_reads_nothing()
    {
        using var stream = Over("abc");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(true);

        // Red: ignore the token in the array overload.
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => stream.ReadAsync(new byte[3], 0, 3, cancelled.Token)).ConfigureAwait(true);
        // Red: ignore the token in the memory overload.
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await stream.ReadAsync(new byte[3].AsMemory(), cancelled.Token).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Position_can_be_set_and_is_clamped_to_the_data()
    {
        using var stream = Over("abcd");

        stream.Position = 2;
        Assert.Equal('c', stream.ReadByte());
        // Red: skip the clamp, so a position past the end makes the next read throw.
        stream.Position = 100;
        Assert.Equal(4, stream.Position);
        Assert.Equal(-1, stream.ReadByte());
        // Red: skip the negative check.
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
    }

    [Fact]
    public void Seek_moves_from_the_begin_the_current_position_and_the_end()
    {
        using var stream = Over("abcdef");

        // Red: treat Begin as Current.
        Assert.Equal(2, stream.Seek(2, SeekOrigin.Begin));
        Assert.Equal(4, stream.Seek(2, SeekOrigin.Current));
        // Red: measure End from the start of the data instead of its end.
        Assert.Equal(4, stream.Seek(-2, SeekOrigin.End));
        Assert.Equal('e', stream.ReadByte());
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-10, SeekOrigin.Begin));
    }

    [Fact]
    public void Reset_points_it_at_new_data_from_the_start()
    {
        using var stream = Over("abc");
        stream.ReadByte();

        stream.Reset("xy"u8.ToArray());

        // Red: keep the position across Reset.
        Assert.Equal(0, stream.Position);
        Assert.Equal(2, stream.Length);
        Assert.Equal('x', stream.ReadByte());
    }

    [Fact]
    public void Writing_and_resizing_are_not_supported_and_Flush_and_Dispose_do_nothing()
    {
        var stream = Over("abc");

        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
        stream.Flush();
        stream.Dispose();
        // Disposing does not break the reader's reuse of the stream for the next event.
        stream.Reset("z"u8.ToArray());
        Assert.Equal('z', stream.ReadByte());
    }
}
