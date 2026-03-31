using BenchmarkDotNet.Attributes;
using MemoryPack;
using MessagePack;
using System.Text.Json;
using ZeroAlloc.Rest.MemoryPack;
using ZeroAlloc.Rest.MessagePack;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Benchmarks;

// ── DTOs annotated for each serializer ────────────────────────────────────────

[MemoryPackable]
public partial class MemoryPackUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[MessagePackObject]
public sealed class MessagePackUserDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

// ── Benchmark: serializer throughput (serialize + deserialize) ────────────────

[MemoryDiagnoser]
[SimpleJob]
public class SerializerBenchmarks
{
    private static readonly UserDto s_stjDto = new() { Id = 42, Name = "Alice" };
    private static readonly MemoryPackUserDto s_mpDto = new() { Id = 42, Name = "Alice" };
    private static readonly MessagePackUserDto s_msgDto = new() { Id = 42, Name = "Alice" };
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SystemTextJsonSerializer _stj = new();
    private readonly MemoryPackRestSerializer _mp = new();
    private readonly MessagePackRestSerializer _msg = new();

    // Pre-serialized payloads — computed once in GlobalSetup so Deserialize_*
    // benchmarks measure deserialization only, not a serialize+deserialize round-trip.
    private byte[] _stjBytes = [];
    private byte[] _mpBytes = [];
    private byte[] _msgBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _stjBytes = JsonSerializer.SerializeToUtf8Bytes(s_stjDto, s_jsonOptions);

        using var mpMs = new MemoryStream();
        _mp.SerializeAsync(mpMs, s_mpDto).AsTask().GetAwaiter().GetResult();
        _mpBytes = mpMs.ToArray();

        using var msgMs = new MemoryStream();
        _msg.SerializeAsync(msgMs, s_msgDto).AsTask().GetAwaiter().GetResult();
        _msgBytes = msgMs.ToArray();
    }

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public async Task<long> Serialize_SystemTextJson()
    {
        using var ms = new MemoryStream();
        await _stj.SerializeAsync(ms, s_stjDto).ConfigureAwait(false);
        return ms.Length;
    }

    [Benchmark]
    public async Task<long> Serialize_MemoryPack()
    {
        using var ms = new MemoryStream();
        await _mp.SerializeAsync(ms, s_mpDto).ConfigureAwait(false);
        return ms.Length;
    }

    [Benchmark]
    public async Task<long> Serialize_MessagePack()
    {
        using var ms = new MemoryStream();
        await _msg.SerializeAsync(ms, s_msgDto).ConfigureAwait(false);
        return ms.Length;
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    [Benchmark]
    public async Task<UserDto?> Deserialize_SystemTextJson()
    {
        using var ms = new MemoryStream(_stjBytes);
        return await _stj.DeserializeAsync<UserDto>(ms).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<MemoryPackUserDto?> Deserialize_MemoryPack()
    {
        using var ms = new MemoryStream(_mpBytes);
        return await _mp.DeserializeAsync<MemoryPackUserDto>(ms).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<MessagePackUserDto?> Deserialize_MessagePack()
    {
        using var ms = new MemoryStream(_msgBytes);
        return await _msg.DeserializeAsync<MessagePackUserDto>(ms).ConfigureAwait(false);
    }
}
