using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Rest.Resilience.Tests;

// Annotated with both [ZeroAllocRestClient] (Rest generator) and [Retry] (Resilience generator).
// The Rest generator emits: TestApiClient : ITestApi
// The Resilience generator emits: ITestApiResilienceProxy : ITestApi
[ZeroAllocRestClient]
[Retry(MaxAttempts = 3, BackoffMs = 10)]
public interface ITestApi
{
    [Get("/items/{id}")]
    Task<string> GetItemAsync(int id, CancellationToken ct = default);

    [Post("/items")]
    Task<string> CreateItemAsync([Body] string name, CancellationToken ct = default);
}
