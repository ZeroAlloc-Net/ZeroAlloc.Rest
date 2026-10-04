using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #358: a [Body] Stream is sent as it is, with the media type [Body(ContentType)] declares,
// and a Stream success type is handed to the caller as a stream that owns the response.
public class GeneratorStreamBodyTests
{
    private const string HeadersRead = "global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead";

    private const string Source = """
        using System.IO;
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
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        public interface IFileApi
        {
            [Put("/files/{name}")]
            Task UploadAsync(string name, [Body(ContentType = "image/png")] Stream body, CancellationToken ct = default);

            [Put("/files/raw")]
            Task<UnitResult<HttpError>> UploadRawAsync([Body] FileStream body, CancellationToken ct = default);

            [Patch("/things/1")]
            Task PatchAsync([Body(ContentType = "application/merge-patch+json")] Thing body, CancellationToken ct = default);

            [Post("/things")]
            Task PostAsync([Body] Thing body, CancellationToken ct = default);

            [Get("/files/{name}")]
            Task<Stream> DownloadAsync(string name, CancellationToken ct = default);

            [Get("/files/{name}/result")]
            Task<Result<Stream, HttpError>> TryDownloadAsync(string name, CancellationToken ct = default);

            [Get("/files/{name}/mapped")]
            Task<Result<Stream, JevError>> MappedDownloadAsync(string name, CancellationToken ct = default);

            [Get("/files/{name}/memory")]
            Task<MemoryStream> DeserializedAsync(string name, CancellationToken ct = default);

            [Get("/things/1")]
            Task<Thing> GetAsync(CancellationToken ct = default);
        }
        """;

    [Fact]
    public void GeneratedClient_Compiles()
    {
        var run = Run();

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void StreamBody_IsSentAsIs_WithItsDeclaredMediaType()
    {
        var body = MethodBody(Run().GeneratedSource, "UploadAsync");

        Assert.Contains("if (body is not null) __request.Content = global::ZeroAlloc.Rest.GeneratedRestClient.CreateStreamBodyContent(body, \"image/png\");", body);
        Assert.DoesNotContain("CreateBodyContentAsync", body);
    }

    [Fact]
    public void DerivedStreamBody_IsSentAsIs_AsOctetStream()
    {
        var body = MethodBody(Run().GeneratedSource, "UploadRawAsync");

        Assert.Contains("CreateStreamBodyContent(body, null);", body);
        Assert.DoesNotContain("CreateBodyContentAsync", body);
    }

    [Fact]
    public void SerializedBody_WithAMediaType_ReplacesTheSerializers()
    {
        var client = Run().GeneratedSource;

        Assert.Contains(
            "__request.Content = global::ZeroAlloc.Rest.GeneratedRestClient.WithMediaType(await global::ZeroAlloc.Rest.GeneratedRestClient.CreateBodyContentAsync(_serializer, body, ct).ConfigureAwait(false), \"application/merge-patch+json\");",
            MethodBody(client, "PatchAsync"));
        Assert.Contains(
            "__request.Content = await global::ZeroAlloc.Rest.GeneratedRestClient.CreateBodyContentAsync(_serializer, body, ct).ConfigureAwait(false);",
            MethodBody(client, "PostAsync"));
        Assert.DoesNotContain("WithMediaType", MethodBody(client, "PostAsync"));
    }

    [Theory]
    [InlineData("DownloadAsync")]
    [InlineData("TryDownloadAsync")]
    [InlineData("MappedDownloadAsync")]
    public void StreamResponse_IsReadUnbuffered_AndHandedToTheCaller(string methodName)
    {
        var body = MethodBody(Run().GeneratedSource, methodName);

        Assert.Contains($"__response = await _httpClient.SendAsync(__request, {HeadersRead}, ct).ConfigureAwait(false);", body);
        Assert.DoesNotContain("using var __response", body);
        Assert.Contains("var __responseBody = await global::ZeroAlloc.Rest.GeneratedRestClient.OpenResponseBodyAsync(__response, ct).ConfigureAwait(false);", body);
        Assert.Contains("__responseHandedOver = true;", body);
        Assert.Contains("if (!__responseHandedOver)", body);
        Assert.DoesNotContain("DeserializeAsync", body);
        Assert.DoesNotContain("\"Accept\"", body);
    }

    [Fact]
    public void DerivedStreamResponse_IsDeserialized_AsBefore()
    {
        var body = MethodBody(Run().GeneratedSource, "DeserializedAsync");

        Assert.Contains("using var __response = await _httpClient.SendAsync(__request, ct).ConfigureAwait(false);", body);
        Assert.Contains("DeserializeAsync<System.IO.MemoryStream>", body);
        Assert.Contains("Headers.TryAddWithoutValidation(\"Accept\", _serializer.ContentType)", body);
    }

    [Fact]
    public void OtherMethods_AreUnchanged()
    {
        var body = MethodBody(Run().GeneratedSource, "GetAsync");

        Assert.Contains("using var __response = await _httpClient.SendAsync(__request, ct).ConfigureAwait(false);", body);
        Assert.DoesNotContain("__responseHandedOver", body);
        Assert.Contains("Headers.TryAddWithoutValidation(\"Accept\", _serializer.ContentType)", body);
    }

    private static GeneratorHarness.GeneratorHarnessRun Run()
        => GeneratorHarness.Run(Source, "MyApp.IFileApi.g.cs");

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
}
