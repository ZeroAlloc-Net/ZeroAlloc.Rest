namespace ZeroAlloc.Rest;

/// <summary>
/// Reads and writes request and response bodies for a generated client.
/// </summary>
/// <remarks>
/// The contract carries no trim or AOT annotations, so generated clients call it without a
/// suppression. An implementation that needs reflection says so where reflection is chosen, on its
/// constructor, as the reflection-based <c>SystemTextJsonSerializer</c> constructors do.
/// </remarks>
public interface IRestSerializer
{
    string ContentType { get; }

    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);

    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default);
}
