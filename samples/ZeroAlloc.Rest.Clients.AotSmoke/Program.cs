using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest.Clients.AotSmoke;
using ZeroAlloc.Rest.Ollama;

// Verify that the provider clients publish and run under PublishAot=true with no trim or AOT warning.
// Each client is driven over a loopback socket against an in-process stub, through its generated
// client and its source-generated JSON context.

const string OllamaDocumentedResponse =
    """{"model":"all-minilm","embeddings":[[0.010071029,-0.0017594862],[-0.0098027075,0.06042469]],"total_duration":14143917,"load_duration":1019500,"prompt_eval_count":8}""";

using (var stub = new StubServer())
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var serving = stub.ServeAsync(200, OllamaDocumentedResponse, cts.Token);

    using var http = new HttpClient { BaseAddress = stub.BaseAddress };
    var client = OllamaClient.Create(http);
    var result = await client.EmbedAsync(new EmbedRequest("all-minilm", ["a", "b"]), cts.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);

    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"AOT smoke: FAIL — Ollama embed returned {result.Error}");
        return 1;
    }

    var response = result.Value;
    if (!string.Equals(response.Model, "all-minilm", StringComparison.Ordinal) || response.Embeddings.Count != 2
        || response.Embeddings[1][1] != 0.06042469f || response.PromptEvalCount != 8)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — Ollama embed response was not read as documented");
        return 1;
    }

    if (!stub.LastRequest.StartsWith("POST /api/embed ", StringComparison.Ordinal)
        || !stub.LastRequest.EndsWith("""{"model":"all-minilm","input":["a","b"]}""", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"AOT smoke: FAIL — Ollama embed request was not written as documented: {stub.LastRequest}");
        return 1;
    }
}

using (var stub = new StubServer())
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var serving = stub.ServeAsync(404, """{"error":"model \"x\" not found"}""", cts.Token);

    var services = new ServiceCollection();
    services.AddOllamaClient(o => o.BaseAddress = stub.BaseAddress);
    using var provider = services.BuildServiceProvider();
    var client = provider.GetRequiredService<OllamaClient>();
    var result = await client.EmbedAsync(new EmbedRequest("x", ["a"]), cts.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);

    if (!result.IsFailure || result.Error.Status != 404 || !string.Equals(result.Error.Message, "model \"x\" not found", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — Ollama error body was not mapped through the registered client");
        return 1;
    }
}

Console.WriteLine("AOT smoke: OK");
return 0;
