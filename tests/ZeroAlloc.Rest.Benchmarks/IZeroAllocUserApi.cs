using ZeroAlloc.Rest;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Benchmarks;

// Source generator emits ZeroAllocUserApiClient.
// Use fully-qualified attribute names to avoid ambiguity with Refit's attributes.

[ZeroAlloc.Rest.Attributes.ZeroAllocRestClient]
public interface IZeroAllocUserApi
{
    [ZeroAlloc.Rest.Attributes.Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [ZeroAlloc.Rest.Attributes.Post("/users")]
    Task<UserDto> CreateUserAsync([ZeroAlloc.Rest.Attributes.Body] UserDto body, CancellationToken ct = default);

    [ZeroAlloc.Rest.Attributes.Get("/users/{id}")]
    Task<UserDto> GetUserWithTagAsync(int id, [ZeroAlloc.Rest.Attributes.Query] string? tag = null, CancellationToken ct = default);

    [ZeroAlloc.Rest.Attributes.Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);

    [ZeroAlloc.Rest.Attributes.Get("/users/{id}/result")]
    Task<Result<UserDto, HttpError>> GetUserResultAsync(int id, CancellationToken ct = default);
}
