namespace ZeroAlloc.Rest.Ollama.Tests;

/// <summary>Fails every request with the given exception, as a dropped connection or a timeout would.</summary>
internal sealed class FailingHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(exception);
}
