namespace ZeroAlloc.Rest.Ollama;

/// <summary>A failed Ollama call.</summary>
/// <param name="Status">The HTTP status of the failed response; 0 when no response describes the failure.</param>
/// <param name="Message">Ollama's <c>error</c> text, or a description of the failure.</param>
public sealed record OllamaApiError(int Status, string Message);
