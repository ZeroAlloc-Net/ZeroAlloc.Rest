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

    // Issue #318: no path — the request goes to the HttpClient's BaseAddress itself.
    [Get]
    Task<string?> PingAsync(CancellationToken ct = default);
}
