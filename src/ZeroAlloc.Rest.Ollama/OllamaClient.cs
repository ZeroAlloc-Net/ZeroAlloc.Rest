using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>
/// The Ollama embed client: <c>POST /api/embed</c>. Build it with <see cref="Create"/>, or register it with
/// <see cref="OllamaServiceCollectionExtensions.AddOllamaClient"/>.
/// </summary>
public sealed class OllamaClient
{
    private readonly IOllamaApi _api;

    internal OllamaClient(IOllamaApi api) => _api = api;

    // The context's own options come along with its metadata; this constructor carries no trim annotation.
    internal static SystemTextJsonSerializer Serializer { get; } = new(OllamaJson.Context);

    /// <summary>Builds a client over <paramref name="httpClient"/>, which needs its base address set.</summary>
    /// <param name="httpClient">The HTTP client. The caller keeps ownership of it.</param>
    /// <returns>The client.</returns>
    public static OllamaClient Create(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return new OllamaClient(new OllamaApiClient(httpClient, Serializer, new OllamaErrorMapper()));
    }

    /// <summary>Embeds the texts of <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call; cancelling throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The embeddings, or the error.</returns>
    public Task<Result<EmbedResponse, OllamaApiError>> EmbedAsync(EmbedRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _api.EmbedAsync(request, ct);
    }
}
