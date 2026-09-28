using BenchmarkDotNet.Attributes;
using MemoryPack;
using ZeroAlloc.Rest.MemoryPack;

namespace ZeroAlloc.Rest.Benchmarks;

// ── Benchmark: MemoryPackRestSerializer body cost ─────────────────────────────
//
// Deserialize reads from a stream that cannot seek, as a streamed response's does, and from a
// seekable one, as a buffered response's does. Serialize writes to a reused stream, so only the
// serializer's own allocations are measured.
//
// Users sets the body size: 1 user is about 20 bytes, 1000 users about 20 KB.

[MemoryDiagnoser]
[SimpleJob]
public class MemoryPackBodyBenchmarks
{
    private readonly MemoryPackRestSerializer _serializer = new(types => types.Add<MemoryPackUserDto>());
    private readonly MemoryStream _destination = new();
    private MemoryPackUserDto[] _users = [];
    private byte[] _body = [];

    [Params(1, 1000)]
    public int Users { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _users = new MemoryPackUserDto[Users];
        for (var i = 0; i < _users.Length; i++)
            _users[i] = new MemoryPackUserDto { Id = i, Name = "User " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        _body = MemoryPackSerializer.Serialize(_users);
    }

    [Benchmark]
    public ValueTask<MemoryPackUserDto[]?> Deserialize_Unseekable()
        => _serializer.DeserializeAsync<MemoryPackUserDto[]>(new UnseekableStream(_body));

    [Benchmark]
    public ValueTask<MemoryPackUserDto[]?> Deserialize_Seekable()
        => _serializer.DeserializeAsync<MemoryPackUserDto[]>(new MemoryStream(_body, writable: false));

    [Benchmark]
    public ValueTask Serialize()
    {
        _destination.Position = 0;
        return _serializer.SerializeAsync(_destination, _users);
    }

    private sealed class UnseekableStream(byte[] body) : MemoryStream(body, writable: false)
    {
        public override bool CanSeek => false;
    }
}
