using System.Collections.Generic;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

public record UserDto(int Id, string Name);
public record CreateUserRequest(string Name);

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Get("/users/{id}/result")]
    Task<Result<UserDto, ZeroAlloc.Rest.HttpError>> GetUserResultAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> ListUsersAsync([Query] string? name = null, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> ListUsersByTagsAsync([Query] IEnumerable<string> tags, CancellationToken ct = default);

    [Get("/users/{id}/raw")]
    [Header("Accept", Value = "application/octet-stream")]
    Task<string> GetUserRawAsync(int id, CancellationToken ct = default);
}
