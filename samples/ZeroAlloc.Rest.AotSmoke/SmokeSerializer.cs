using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// A do-nothing serializer: the smoke checks DI wiring under ILC, not serialization.
public sealed class SmokeSerializer : IRestSerializer
{
    public string ContentType => "application/x-smoke";

    [RequiresDynamicCode("Smoke serializer.")]
    [RequiresUnreferencedCode("Smoke serializer.")]
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => ValueTask.FromResult<T?>(default);

    [RequiresDynamicCode("Smoke serializer.")]
    [RequiresUnreferencedCode("Smoke serializer.")]
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
