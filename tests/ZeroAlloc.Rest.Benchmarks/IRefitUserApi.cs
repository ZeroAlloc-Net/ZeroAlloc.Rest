namespace ZeroAlloc.Rest.Benchmarks;

// Refit interface, a reflection-based client.

public interface IRefitUserApi
{
    [Refit.Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id);

    [Refit.Post("/users")]
    Task<UserDto> CreateUserAsync([Refit.Body] UserDto body);

    [Refit.Get("/users/{id}")]
    Task<UserDto> GetUserWithTagAsync(int id, string? tag = null);

    [Refit.Delete("/users/{id}")]
    Task DeleteUserAsync(int id);
}
