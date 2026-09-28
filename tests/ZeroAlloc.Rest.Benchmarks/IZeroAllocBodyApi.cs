namespace ZeroAlloc.Rest.Benchmarks;

// Source generator emits ZeroAllocBodyApiClient. Used by RequestBodyBenchmarks.
[ZeroAlloc.Rest.Attributes.ZeroAllocRestClient]
public interface IZeroAllocBodyApi
{
    [ZeroAlloc.Rest.Attributes.Post("/users/batch")]
    Task CreateUsersAsync([ZeroAlloc.Rest.Attributes.Body] UserDto[] users, CancellationToken ct = default);
}
