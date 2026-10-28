namespace ZeroAlloc.Rest.Ollama;

/// <summary>Settings for the registered Ollama client.</summary>
public sealed class OllamaClientOptions
{
    /// <summary>The Ollama server's base address. Default: <c>http://localhost:11434/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("http://localhost:11434/");
}
