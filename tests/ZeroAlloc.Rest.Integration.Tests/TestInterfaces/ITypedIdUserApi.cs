using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.ValueObjects;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

[TypedId]
public readonly partial record struct UserId;

[ZeroAllocRestClient]
public interface ITypedIdUserApi
{
    [Get("/users/{id}")]
    Task<string> GetUserAsync(UserId id, CancellationToken ct = default);

    [Get("/users")]
    Task<string> FindUserAsync([Query] UserId? id = null, CancellationToken ct = default);
}
