using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.AotSmoke;

[ZeroAllocRestClient]
public interface IUserApi
{
    // The generator-emitted UserApiClient.GetUserAsync carries
    // [Requires{Dynamic,Unreferenced}Code] — the interface must match or
    // ILC raises IL3051/IL2046. Tracked as #42: the generator should emit
    // [UnconditionalSuppressMessage] instead so consumers don't have to
    // propagate the Requires annotations to every caller.
    [Get("/users/{id}")]
    [RequiresDynamicCode("Generated client may deserialise via reflection.")]
    [RequiresUnreferencedCode("Generated client may deserialise via reflection.")]
    Task<string?> GetUserAsync(int id, CancellationToken ct = default);
}
