namespace ZeroAlloc.Rest.Benchmarks;

// Source generator emits ZeroAllocResponseApiClient. Used by ResponseBodyBenchmarks: the same
// request, read buffered and read as a stream.
[ZeroAlloc.Rest.Attributes.ZeroAllocRestClient]
public interface IZeroAllocResponseApi
{
    [ZeroAlloc.Rest.Attributes.Get("/users")]
    Task<UserDto[]> GetUsersAsync(CancellationToken ct = default);

    [ZeroAlloc.Rest.Attributes.Get("/users", StreamResponses = true)]
    Task<UserDto[]> GetUsersStreamedAsync(CancellationToken ct = default);
}
