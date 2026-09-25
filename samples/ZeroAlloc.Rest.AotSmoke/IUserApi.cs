using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.AotSmoke;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<string?> GetUserAsync(int id, CancellationToken ct = default);

    [Get("/users/{id}")]
    Task<Result<string, HttpError>> TryGetUserAsync(int id, CancellationToken ct = default);
}
