using System.Collections.Generic;
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

    // Issue #356: a collection-typed [Header] parameter sends one header value per element.
    [Get("/items")]
    Task SendListsAsync(
        [Header("X-Tags")] IEnumerable<string?>? tags,
        [Header("X-Ids")] int[] ids,
        CancellationToken ct = default);
}
