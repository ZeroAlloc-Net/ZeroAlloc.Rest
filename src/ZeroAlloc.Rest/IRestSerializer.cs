using System.Diagnostics.CodeAnalysis;

namespace ZeroAlloc.Rest;

public interface IRestSerializer
{
    string ContentType { get; }

    [RequiresDynamicCode("Serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("Serialization of arbitrary types may require unreferenced code.")]
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);

    [RequiresDynamicCode("Serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("Serialization of arbitrary types may require unreferenced code.")]
    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default);
}
