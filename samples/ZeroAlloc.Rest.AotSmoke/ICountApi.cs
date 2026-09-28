using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.AotSmoke;

// Value-type responses read through CountJsonContext: the generated read is generic over each
// response type, and a nullable value type takes a different branch from a non-nullable one.
[ZeroAllocRestClient]
public interface ICountApi
{
    [Get("/count")]
    Task<Result<int?, HttpError>> TryGetCountAsync(CancellationToken ct = default);

    [Get("/total")]
    Task<long> GetTotalAsync(CancellationToken ct = default);

    // Issue #362: the same reads with StreamResponses, which send with ResponseHeadersRead and
    // deserialize from the connection's stream.
    [Get("/count", StreamResponses = true)]
    Task<Result<int?, HttpError>> TryGetCountStreamedAsync(CancellationToken ct = default);

    [Get("/total", StreamResponses = true)]
    Task<long> GetTotalStreamedAsync(CancellationToken ct = default);
}
