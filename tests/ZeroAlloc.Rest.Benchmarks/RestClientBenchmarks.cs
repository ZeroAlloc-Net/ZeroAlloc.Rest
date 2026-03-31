using BenchmarkDotNet.Attributes;
using Refit;
using System.Text.Json;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Benchmarks;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// ── ZeroAlloc.Rest interface — source generator emits ZeroAllocUserApiClient ──
// Use fully-qualified attribute names to avoid ambiguity with Refit's attributes.

[ZeroAlloc.Rest.Attributes.ZeroAllocRestClient]
public interface IZeroAllocUserApi
{
    [ZeroAlloc.Rest.Attributes.Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [ZeroAlloc.Rest.Attributes.Post("/users")]
    Task<UserDto> CreateUserAsync([ZeroAlloc.Rest.Attributes.Body] UserDto body, CancellationToken ct = default);
}

// ── Refit interface — reflection-based client ─────────────────────────────────

public interface IRefitUserApi
{
    [Refit.Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id);

    [Refit.Post("/users")]
    Task<UserDto> CreateUserAsync([Refit.Body] UserDto body);
}

// ── Benchmarks ────────────────────────────────────────────────────────────────

[MemoryDiagnoser]
[SimpleJob]
public class RestClientBenchmarks
{
    private static readonly Uri s_baseUri = new("http://localhost/");
    private static readonly UserDto s_testUser = new() { Id = 1, Name = "Alice" };
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private IZeroAllocUserApi _zeroAlloc = null!;
    private IRefitUserApi _refit = null!;
    private HttpClient _rawHttp = null!;

    [GlobalSetup]
    public void Setup()
    {
        var handler = new InMemoryHandler(s_testUser);

        // ZeroAlloc.Rest: instantiate the source-generated client directly
        _zeroAlloc = new ZeroAllocUserApiClient(
            new HttpClient(handler) { BaseAddress = s_baseUri },
            new SystemTextJsonSerializer());

        // Refit: reflection-based client
        _refit = RestService.For<IRefitUserApi>(
            new HttpClient(handler) { BaseAddress = s_baseUri });

        // Raw HttpClient: manual JSON deserialization (true baseline)
        _rawHttp = new HttpClient(handler) { BaseAddress = s_baseUri };
    }

    [GlobalCleanup]
    public void Cleanup() => _rawHttp.Dispose();

    // ── GET benchmarks ────────────────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public async Task<UserDto?> RawHttpClient_Get()
    {
        using var response = await _rawHttp.GetAsync("/users/1").ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<UserDto>(stream, s_jsonOptions)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public Task<UserDto> ZeroAlloc_Get() => _zeroAlloc.GetUserAsync(1);

    [Benchmark]
    public Task<UserDto> Refit_Get() => _refit.GetUserAsync(1);

    // ── POST benchmarks ───────────────────────────────────────────────────────

    [Benchmark]
    public async Task<UserDto?> RawHttpClient_Post()
    {
        var json = JsonSerializer.Serialize(s_testUser, s_jsonOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _rawHttp.PostAsync("/users", content).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<UserDto>(stream, s_jsonOptions)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public Task<UserDto> ZeroAlloc_Post() => _zeroAlloc.CreateUserAsync(s_testUser);

    [Benchmark]
    public Task<UserDto> Refit_Post() => _refit.CreateUserAsync(s_testUser);
}
