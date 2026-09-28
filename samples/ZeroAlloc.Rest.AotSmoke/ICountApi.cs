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
}
