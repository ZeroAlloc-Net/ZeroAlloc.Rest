using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

// Issue #318: [Post] and the other HTTP method attributes can be used without a path, sending
// the request to the HttpClient's BaseAddress itself.
public record EvaluateRequest(string Expression);

[ZeroAllocRestClient]
public interface IEvalApi
{
    [Post]
    Task EvaluateAsync([Body] EvaluateRequest body, CancellationToken ct = default);

    [Post]
    Task EvaluateWithQueryAsync([Query] int x, CancellationToken ct = default);
}
