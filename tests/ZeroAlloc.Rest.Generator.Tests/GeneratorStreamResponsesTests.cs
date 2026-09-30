using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #362: StreamResponses, on [ZeroAllocRestClient] or on a method's HTTP attribute, makes the
// generated call read the response with ResponseHeadersRead and deserialize from the response
// stream. A method-level value overrides the interface's. Without it, the emission is unchanged.
public class GeneratorStreamResponsesTests
{
    private const string HeadersRead = "global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead";
    private const string OpenStream = "global::ZeroAlloc.Rest.GeneratedRestClient.ReadResponseStreamAsync(__response.Content, ct)";

    private static string Api(string clientAttribute, string getAttribute = "[Get(\"/things/{id}\")]",
        string resultAttribute = "[Get(\"/things/{id}/result\")]", string deleteAttribute = "[Delete(\"/things/{id}\")]")
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            using ZeroAlloc.Results;
            namespace MyApp;
            public sealed record Thing(string Name);
            public sealed record JevError(string Code);
            public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
            {
                public JevError Map(HttpError error) => new(error.Kind.ToString());
            }
            {{clientAttribute}}
            [ErrorMapper(typeof(JevErrorMapper))]
            public interface IThingApi
            {
                {{getAttribute}}
                Task<Thing> GetAsync(int id, CancellationToken ct = default);

                {{resultAttribute}}
                Task<Result<Thing, HttpError>> GetResultAsync(int id, CancellationToken ct = default);

                [Get("/things/{id}/mapped")]
                Task<Result<int, JevError>> GetMappedAsync(int id, CancellationToken ct = default);

                {{deleteAttribute}}
                Task DeleteAsync(int id, CancellationToken ct = default);

                [Delete("/things/{id}/unit")]
                Task<UnitResult<HttpError>> DeleteUnitAsync(int id, CancellationToken ct = default);
            }
            """;

    [Theory]
    [InlineData("[ZeroAllocRestClient]")]
    [InlineData("[ZeroAllocRestClient(StreamResponses = false)]")]
    public void WithoutTheFlag_EveryMethodBuffersTheResponse_AsBefore(string clientAttribute)
    {
        var run = Run(Api(clientAttribute));

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        Assert.DoesNotContain("HttpCompletionOption", client);
        Assert.DoesNotContain("ReadResponseStreamAsync", client);
        Assert.Equal(5, Count(client, "await _httpClient.SendAsync(__request, ct).ConfigureAwait(false);"));
        Assert.Contains("var __responseStream = await __response.Content.ReadAsStreamAsync().ConfigureAwait(false);", client);
        Assert.Equal(2, Count(client, "var __responseStream = await __response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);"));
    }

    [Fact]
    public void InterfaceFlag_StreamsEveryMethod()
    {
        var run = Run(Api("[ZeroAllocRestClient(StreamResponses = true)]"));

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        Assert.Equal(5, Count(client, $"await _httpClient.SendAsync(__request, {HeadersRead}, ct).ConfigureAwait(false);"));
        Assert.DoesNotContain("__response.Content.ReadAsStreamAsync", client);
        // The three methods that read a success body open it through the helper.
        Assert.Equal(3, Count(client, $"var __responseStream = await {OpenStream}.ConfigureAwait(false);"));
    }

    [Fact]
    public void InterfaceFlag_ReadsTheErrorBodyThroughTheSameHelper()
    {
        var client = Run(Api("[ZeroAllocRestClient(StreamResponses = true)]")).GeneratedSource;

        var method = MethodBody(client, "GetResultAsync");
        Assert.Contains("GeneratedRestClient.ReadErrorBodyAsync(__response.Content, 65536, ct)", method);
    }

    [Theory]
    [InlineData("GetResultAsync")]
    [InlineData("GetMappedAsync")]
    public void Streamed_ResultMethod_OpensTheStreamInsideTheDeserializationTry(string methodName)
    {
        // Opening a streamed body reads from the connection, so it can fail like the rest of the
        // body read: that is a Deserialization failure, not an exception escaping a Result method.
        var method = MethodBody(Run(Api("[ZeroAllocRestClient(StreamResponses = true)]")).GeneratedSource, methodName);

        var open = method.IndexOf("var __responseStream = await " + OpenStream, StringComparison.Ordinal);
        var tryStart = method.IndexOf("try\n", method.IndexOf("if (__response.IsSuccessStatusCode)", StringComparison.Ordinal), StringComparison.Ordinal);
        var deserializationCatch = method.IndexOf("catch (global::System.Exception __ex) when (__ex is not global::System.OperationCanceledException)", StringComparison.Ordinal);
        Assert.True(open >= 0, "The stream is not opened through the helper.");
        Assert.True(tryStart >= 0 && tryStart < open, "The stream must be opened inside the try.");
        Assert.True(open < deserializationCatch, "The stream must be opened before the Deserialization catch.");
    }

    [Fact]
    public void Streamed_ValueTypeMethod_KeepsTheEmptyBodyCheck()
    {
        var method = MethodBody(Run(Api("[ZeroAllocRestClient(StreamResponses = true)]")).GeneratedSource, "GetMappedAsync");

        Assert.Contains("if (__responseStream.CanSeek && __responseStream.Position >= __responseStream.Length)", method);
    }

    [Fact]
    public void MethodFlag_StreamsOnlyThatMethod()
    {
        var run = Run(Api("[ZeroAllocRestClient]", getAttribute: "[Get(\"/things/{id}\", StreamResponses = true)]"));

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        var streamed = MethodBody(client, "GetAsync");
        Assert.Contains($"SendAsync(__request, {HeadersRead}, ct)", streamed);
        Assert.Contains($"var __responseStream = await {OpenStream}.ConfigureAwait(false);", streamed);
        Assert.Equal(1, Count(client, "HttpCompletionOption"));
        Assert.Equal(1, Count(client, "ReadResponseStreamAsync"));
    }

    [Fact]
    public void MethodFlag_OnAMethodWithoutABody_StreamsItsSend()
    {
        var run = Run(Api("[ZeroAllocRestClient]", deleteAttribute: "[Delete(\"/things/{id}\", StreamResponses = true)]"));

        Assert.Empty(run.Problems);
        var method = MethodBody(run.GeneratedSource, "DeleteAsync");
        Assert.Contains($"SendAsync(__request, {HeadersRead}, ct)", method);
        Assert.Equal(1, Count(run.GeneratedSource, "HttpCompletionOption"));
    }

    [Fact]
    public void MethodFlagFalse_OverridesTheInterfaceFlag()
    {
        var run = Run(Api("[ZeroAllocRestClient(StreamResponses = true)]",
            resultAttribute: "[Get(\"/things/{id}/result\", StreamResponses = false)]"));

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        var buffered = MethodBody(client, "GetResultAsync");
        Assert.Contains("await _httpClient.SendAsync(__request, ct).ConfigureAwait(false);", buffered);
        Assert.Contains("var __responseStream = await __response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);", buffered);
        Assert.DoesNotContain("HttpCompletionOption", buffered);
        Assert.Equal(4, Count(client, HeadersRead));
    }

    [Fact]
    public void MethodFlagTrue_UnderAnInterfaceFlagTrue_StillStreams()
    {
        var run = Run(Api("[ZeroAllocRestClient(StreamResponses = true)]",
            getAttribute: "[Get(\"/things/{id}\", StreamResponses = true)]"));

        Assert.Empty(run.Problems);
        Assert.Equal(5, Count(run.GeneratedSource, HeadersRead));
    }

    [Fact]
    public void StreamedMethod_WithoutACancellationToken_Compiles()
    {
        const string source = """
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            using ZeroAlloc.Results;
            namespace MyApp;
            [ZeroAllocRestClient(StreamResponses = true)]
            public interface IThingApi
            {
                [Get("/count")]
                Task<int> CountAsync();

                [Get("/count/result")]
                Task<Result<int, HttpError>> CountResultAsync();
            }
            """;

        var run = Run(source);

        Assert.Empty(run.Problems);
        Assert.Equal(2, Count(run.GeneratedSource, "ReadResponseStreamAsync(__response.Content, default)"));
        Assert.Equal(2, Count(run.GeneratedSource, $"SendAsync(__request, {HeadersRead}, default)"));
    }

    private static GeneratorHarness.GeneratorHarnessRun Run(string source)
        => GeneratorHarness.Run(source, "MyApp.IThingApi.g.cs");

    // The text of one generated method, from its signature to the next member.
    private static string MethodBody(string client, string methodName)
    {
        var start = client.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{methodName} is not generated.");
        var end = client.IndexOf("\n    public ", start, StringComparison.Ordinal);
        var helpers = client.IndexOf("\n    private static ", start, StringComparison.Ordinal);
        if (end < 0 || (helpers >= 0 && helpers < end))
            end = helpers;
        return end < 0 ? client[start..] : client[start..end];
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
