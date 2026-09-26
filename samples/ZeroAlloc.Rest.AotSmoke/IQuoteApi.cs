using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.AotSmoke;

// A Result method with a user-defined error type: the generated client takes the mapper, and the
// generated Add registers and resolves it, all under ILC.
[ZeroAllocRestClient]
[ErrorMapper(typeof(QuoteErrorMapper))]
public interface IQuoteApi
{
    [Get("/quotes/{id}")]
    Task<Result<string, QuoteError>> TryGetQuoteAsync(int id, CancellationToken ct = default);
}
