using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.AotSmoke;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<string?> GetUserAsync(int id, CancellationToken ct = default);
}
