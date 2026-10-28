using System.Net;
using Microsoft.Extensions.DependencyInjection;
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

        // Red: return Result<EmbedResponse, OllamaApiError>.Failure from EmbedAsync whatever the answer; IsSuccess is then false.
        Assert.True(result.IsSuccess);
        var response = result.Value;
        // Red: rename [JsonPropertyName("model")] on EmbedResponse to "name"; the model is then null.
        Assert.Equal("all-minilm", response.Model);
        // Red: rename [JsonPropertyName("embeddings")] on EmbedResponse to "vectors"; Embeddings stays null, so this line throws NullReferenceException.
        Assert.Equal(2, response.Embeddings.Count);
        // Red: reverse the Embeddings list in EmbedAsync; the first vector is then the second.
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

        // Red: have EmbedAsync turn every failure into Result.Success of an empty response; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: build the error with status 0 in Map; the status then differs.
        Assert.Equal(404, result.Error.Status);
        // Red: compare ValueTextEquals to "message" in ReadMessage; the text is then the HTTP 404 fallback.
        Assert.Equal("model \"x\" not found", result.Error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":""")]
    [InlineData(HttpStatusCode.InternalServerError, """{"detail":"no error member"}""")]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":{"code":1}}""")]
    [InlineData(HttpStatusCode.InternalServerError, "[1,2]")]
    public async Task A_body_without_an_error_text_falls_back_to_the_status(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);

        var result = await Client(handler).EmbedAsync(new EmbedRequest("m", ["a"]));

        // Red: have EmbedAsync turn every failure into Result.Success of an empty response; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: let ReadMessage return string.Empty for a body that is not an object, or stop catching JsonException;
        // the "[1,2]" row then gets an empty message and the malformed row throws.
        // Red: build the fallback error with status 0 in Map; the status then differs.
        Assert.Equal((int)status, result.Error.Status);
        // Red: change the fallback text in Map to "failed"; the message then differs.
        Assert.Equal($"Ollama answered HTTP {(int)status} without an error body.", result.Error.Message);
    }

    [Fact]
    public async Task A_transport_failure_maps_to_its_message()
    {
        var http = new HttpClient(new FailingHandler(new HttpRequestException("connection refused")))
        {
            BaseAddress = new Uri("http://ollama.test/"),
        };

        var result = await OllamaClient.Create(http).EmbedAsync(new EmbedRequest("m", ["a"]));

        // Red: have EmbedAsync turn every failure into Result.Success of an empty response; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: drop the Transport arm of Map so it falls to the body arm; the message is then the fallback text.
        Assert.Equal("connection refused", result.Error.Message);
    }

    [Fact]
    public async Task A_timeout_maps_to_status_zero()
    {
        var http = new HttpClient(new FailingHandler(new TaskCanceledException("timed out", new TimeoutException())))
        {
            BaseAddress = new Uri("http://ollama.test/"),
        };

        var result = await OllamaClient.Create(http).EmbedAsync(new EmbedRequest("m", ["a"]));

        // Red: have EmbedAsync turn every failure into Result.Success of an empty response; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: build the Timeout error with status 408 instead of 0; the status then differs.
        Assert.Equal(0, result.Error.Status);
        // Red: drop the Timeout arm of Map so it falls to the body arm; the message is then the fallback text.
        Assert.Equal("timed out", result.Error.Message);
    }

    [Fact]
    public async Task A_success_body_that_is_not_json_maps_to_a_deserialization_error()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "not json");

        var result = await Client(handler).EmbedAsync(new EmbedRequest("m", ["a"]));

        // Red: have EmbedAsync turn every failure into Result.Success of an empty response; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: build the Deserialization error with status 0; the status is then not 200.
        Assert.Equal(200, result.Error.Status);
        // Red: drop the Deserialization arm of Map so it falls to the body arm; the message is then the fallback text.
        Assert.DoesNotContain("without an error body", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"model":"all-minilm"}""")]
    [InlineData("""{"embeddings":[[0.1]]}""")]
    [InlineData("{}")]
    public async Task A_success_body_missing_a_required_member_is_a_deserialization_error(string body)
    {
        var handler = new StubHandler(HttpStatusCode.OK, body);

        var result = await Client(handler).EmbedAsync(new EmbedRequest("all-minilm", ["a"]));

        // Red: set RespectRequiredConstructorParameters = false in OllamaJsonContext; the body then reads as a success
        // with a null Embeddings or Model, and IsFailure is false.
        Assert.True(result.IsFailure);
        // Red: build the Deserialization error with status 0 in Map; the status then differs.
        Assert.Equal(200, result.Error.Status);
    }

    [Fact]
    public async Task The_optional_durations_and_count_may_be_absent()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"model":"m","embeddings":[[0.5]]}""");

        var result = await Client(handler).EmbedAsync(new EmbedRequest("m", ["a"]));

        // Red: add JsonRequired to the total_duration property of EmbedResponse; IsSuccess is then false.
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.TotalDuration);
        Assert.Null(result.Value.LoadDuration);
        Assert.Null(result.Value.PromptEvalCount);
    }

    [Fact]
    public async Task AddOllamaClient_resolves_a_client_that_sends_to_the_configured_address()
    {
        var handler = new StubHandler(HttpStatusCode.OK, DocumentedResponse);
        var services = new ServiceCollection();
        services.AddOllamaClient(o => o.BaseAddress = new Uri("http://configured.test:1234/"))
            .ConfigurePrimaryHttpMessageHandler(_ => handler);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<OllamaClient>().EmbedAsync(new EmbedRequest("all-minilm", ["a", "b"]));

        // Red: leave out the TryAddTransient of OllamaClient; GetRequiredService above then throws.
        Assert.True(result.IsSuccess);
        // Red: drop "o.BaseAddress = options.BaseAddress" in AddOllamaClient; the host is then not configured.test.
        Assert.Equal(new Uri("http://configured.test:1234/api/embed"), handler.Uri);
        // Red: drop "o.UseSerializer(OllamaClient.Serializer)"; the body is then not written with the context.
        Assert.Equal("""{"model":"all-minilm","input":["a","b"]}""", handler.Body);
    }
}
