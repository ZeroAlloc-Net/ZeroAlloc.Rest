using System.Text;

namespace ZeroAlloc.Rest.Anthropic.Tests.Support;

/// <summary>Reads its bytes once, then throws the IOException of a dropped connection.</summary>
internal sealed class DroppingStream(string first) : Stream
{
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes(first);
    private int _position;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _bytes.Length)
            throw new IOException("The connection was closed mid-stream.");
        var n = Math.Min(buffer.Length, _bytes.Length - _position);
        _bytes.AsMemory(_position, n).CopyTo(buffer);
        _position += n;
        return ValueTask.FromResult(n);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
