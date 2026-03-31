namespace ZeroAlloc.Rest;

public interface IRestSerializer
{
    string ContentType { get; }
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);
    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default);
}
