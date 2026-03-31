using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

public record UserDto(int Id, string Name);
public record CreateUserRequest(string Name);

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> ListUsersAsync([Query] string? name = null, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}
