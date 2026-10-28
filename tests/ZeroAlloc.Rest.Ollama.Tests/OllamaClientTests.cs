using System.Net;
using Xunit;

namespace ZeroAlloc.Rest.Ollama.Tests;

public sealed class OllamaClientTests
{
    private const string DocumentedResponse =
        """{"model":"all-minilm","embeddings":[[0.010071029,-0.0017594862],[-0.0098027075,0.06042469]],"total_duration":14143917,"load_duration":1019500,"prompt_eval_count":8}""";

    private static OllamaClient Client(StubHandler handler) =>
        OllamaClient.Create(new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test/") });

    [Fact]
    public async Task A_single_input_writes_an_array_of_one()
    {
        var handler = new StubHandler(HttpStatusCode.OK, DocumentedResponse);

        await Client(handler).EmbedAsync(new EmbedRequest("nomic-embed-text", ["a"]));

        // Red: rename [JsonPropertyName("input")] to "inputs"; the body then has "inputs".
        Assert.Equal("""{"model":"nomic-embed-text","input":["a"]}""", handler.Body);
        // Red: change [Post("api/embed")] to [Put("api/embed")]; the method is then PUT.
        Assert.Equal(HttpMethod.Post, handler.Method);
        // Red: change the route to "api/embeddings"; the path then differs.
        Assert.Equal(new Uri("http://ollama.test/api/embed"), handler.Uri);
    }

    [Fact]
    public async Task A_batch_writes_every_input_in_order()
    {
        var handler = new StubHandler(HttpStatusCode.OK, DocumentedResponse);

        await Client(handler).EmbedAsync(new EmbedRequest("m", ["b", "a", "c"]));

        // Red: send request with { Input = [.. request.Input.Reverse()] } from EmbedAsync; the order is then c, a, b.
        Assert.Equal("""{"model":"m","input":["b","a","c"]}""", handler.Body);
    }

    [Fact]
    public async Task The_documented_response_parses()
    {
        var handler = new StubHandler(HttpStatusCode.OK, DocumentedResponse);

        var result = await Client(handler).EmbedAsync(new EmbedRequest("all-minilm", ["a", "b"]));

        // Red: rename [JsonPropertyName("embeddings")] on EmbedResponse to "vectors"; Embeddings is then null and IsSuccess false.
        Assert.True(result.IsSuccess);
        var response = result.Value;
        // Red: rename [JsonPropertyName("model")] on EmbedResponse to "name"; the model is then null.
        Assert.Equal("all-minilm", response.Model);
        // Red: change the stub body to hold one vector; the count is then 1.
        Assert.Equal(2, response.Embeddings.Count);
        // Red: swap the two vectors in the stub body; the first values then differ.
        Assert.Equal([0.010071029f, -0.0017594862f], response.Embeddings[0]);
        Assert.Equal([-0.0098027075f, 0.06042469f], response.Embeddings[1]);
        // Red: rename [JsonPropertyName("total_duration")] to "totalDuration"; the value is then null.
        Assert.Equal(14143917L, response.TotalDuration);
        // Red: rename [JsonPropertyName("load_duration")] to "load"; the value is then null.
        Assert.Equal(1019500L, response.LoadDuration);
        // Red: rename [JsonPropertyName("prompt_eval_count")] to "count"; the value is then null.
        Assert.Equal(8, response.PromptEvalCount);
    }

    [Fact]
    public async Task An_error_body_maps_to_status_and_message()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"error":"model \"x\" not found"}""");

        var result = await Client(handler).EmbedAsync(new EmbedRequest("x", ["a"]));

        // Red: stub a 200 with the documented body; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: build the error with status 0 in Map; the status then differs.
        Assert.Equal(404, result.Error.Status);
        // Red: compare ValueTextEquals to "message" in ReadMessage; the text is then the HTTP 404 fallback.
        Assert.Equal("model \"x\" not found", result.Error.Message);
    }
}
