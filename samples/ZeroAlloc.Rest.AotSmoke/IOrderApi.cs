using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.AotSmoke;

// Interface-level [Serializer]: the generated client takes SmokeSerializer directly. Registered
// through the Resilience bridge, which registers SmokeSerializer itself.
[ZeroAllocRestClient]
[Serializer(typeof(SmokeSerializer))]
[Retry(MaxAttempts = 2)]
public interface IOrderApi
{
    [Get("/orders/{id}")]
    Task<string> GetOrderAsync(int id, CancellationToken ct = default);
}
