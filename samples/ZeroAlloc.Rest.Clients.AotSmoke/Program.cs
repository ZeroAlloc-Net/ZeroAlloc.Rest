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

    // Red: return Result.Failure from OllamaClient.EmbedAsync whatever the answer.
    if (!result.IsSuccess)
        return Fail($"Ollama embed returned {result.Error}");

    var response = result.Value;
    // Red: build the response with Model = "x" in EmbedAsync.
    if (!string.Equals(response.Model, "all-minilm", StringComparison.Ordinal))
        return Fail("Ollama embed read the wrong model");

    // Red: keep only the first embedding in EmbedAsync.
    if (response.Embeddings.Count != 2)
        return Fail("Ollama embed read the wrong number of embeddings");

    // Red: reverse the Embeddings list in EmbedAsync.
    if (response.Embeddings[1][1] != 0.06042469f)
        return Fail("Ollama embed read the wrong vector values");

    // Red: build the response with PromptEvalCount = 0 in EmbedAsync.
    if (response.PromptEvalCount != 8)
        return Fail("Ollama embed read the wrong prompt_eval_count");

    // Red: change [Post("api/embed")] to [Post("api/embeddings")] in IOllamaApi.
    if (!stub.LastRequest.StartsWith("POST /api/embed ", StringComparison.Ordinal))
        return Fail($"Ollama embed sent the wrong request line: {stub.LastRequest}");

    // Red: rename [JsonPropertyName("input")] to "inputs" in EmbedRequest.
    if (!stub.LastRequest.EndsWith("""{"model":"all-minilm","input":["a","b"]}""", StringComparison.Ordinal))
        return Fail($"Ollama embed wrote the wrong body: {stub.LastRequest}");
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

    // Red: have EmbedAsync turn every failure into Result.Success of an empty response.
    if (!result.IsFailure)
        return Fail("Ollama error answer was not a failure");

    // Red: build the fallback error with status 0 in OllamaErrorMapper.Map.
    if (result.Error.Status != 404)
        return Fail($"Ollama error status was {result.Error.Status}");

    // Red: compare ValueTextEquals to "message" in OllamaErrorMapper.ReadMessage.
    if (!string.Equals(result.Error.Message, "model \"x\" not found", StringComparison.Ordinal))
        return Fail($"Ollama error message was {result.Error.Message}");
}

Console.WriteLine("AOT smoke: OK");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
