using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>
/// The typed Ollama API. It is internal so that <see cref="OllamaClient"/> is the only way in. The generated
/// client and its registration are internal with it.
/// </summary>
[ZeroAllocRestClient]
[ErrorMapper(typeof(OllamaErrorMapper))]
internal interface IOllamaApi
{
    /// <summary>Embeds the texts of the request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The embeddings, or the error.</returns>
    [Post("api/embed")]
    Task<Result<EmbedResponse, OllamaApiError>> EmbedAsync([Body] EmbedRequest request, CancellationToken ct = default);
}
