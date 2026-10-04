using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

// Issue #406: the smallest generated call, to compare its allocations with a hand-written one.
[ZeroAllocRestClient]
public interface IPingApi
{
    [Get("/ping")]
    Task PingAsync(CancellationToken ct = default);
}
