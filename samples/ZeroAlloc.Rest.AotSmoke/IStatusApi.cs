using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.AotSmoke;

// No [Serializer]: registered through the Resilience bridge, it uses the app-wide default.
[ZeroAllocRestClient]
[Retry(MaxAttempts = 2)]
public interface IStatusApi
{
    [Get("/status")]
    Task<string> GetStatusAsync(CancellationToken ct = default);
}
