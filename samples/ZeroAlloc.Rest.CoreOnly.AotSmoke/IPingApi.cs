using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

// The generator emits only PingApiClient's class and constructor without the DI package: see #336.
[ZeroAllocRestClient]
public interface IPingApi
{
    [Get("/ping")]
    Task<PingResponse> GetAsync(CancellationToken ct = default);
}
