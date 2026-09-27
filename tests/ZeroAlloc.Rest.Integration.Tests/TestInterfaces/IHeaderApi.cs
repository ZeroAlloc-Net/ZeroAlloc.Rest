using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

// Issue #354: a [Header] parameter whose value is null is omitted from the request.
[ZeroAllocRestClient]
public interface IHeaderApi
{
    [Get("/items")]
    Task SendAsync(
        [Header("X-Ref")] string? reference,
        [Header("X-Retry-Count")] int? retryCount,
        [Header("X-Page")] int page,
        CancellationToken ct = default);
}
