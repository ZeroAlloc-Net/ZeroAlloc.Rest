using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

// The generator emits only PingApiClient's class and constructor without the DI package: see #336.
[ZeroAllocRestClient]
public interface IPingApi
{
    [Get("/ping")]
    Task<PingResponse> GetAsync(CancellationToken ct = default);

    // Value-type responses: the generated read is generic over each of these, and a nullable one
    // takes a different branch from a non-nullable one.
    [Get("/count")]
    Task<int?> GetCountAsync(CancellationToken ct = default);

    [Get("/total")]
    Task<Result<long, HttpError>> TryGetTotalAsync(CancellationToken ct = default);

    [Get("/level")]
    Task<PingLevel> GetLevelAsync(CancellationToken ct = default);

    [Get("/origin")]
    Task<Result<PingPoint?, HttpError>> TryGetOriginAsync(CancellationToken ct = default);
}
