using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Spec §6 of the OpenAPI models design: a method with no success body returns UnitResult<E>.
// Failures map exactly as for Result<T, E>; success reads no body.
public class GeneratorUnitResultTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record JevError(string Code);
        public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new(error.Kind.ToString());
        }

        """;

    private const string HttpErrorApi = Usings + """
        [ZeroAllocRestClient]
        public interface IThingApi
        {
            [Delete("/things/{id}")]
            Task<UnitResult<HttpError>> DeleteAsync(int id, CancellationToken ct = default);
        }
        """;

    private const string MappedApi = Usings + """
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        public interface IThingApi
        {
            [Delete("/things/{id}")]
            ValueTask<UnitResult<JevError>> DeleteAsync(int id, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void UnitResultMethod_Compiles_AndReturnsSuccessWithoutReadingTheBody()
    {
        var run = Run(HttpErrorApi);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        Assert.Contains("return ZeroAlloc.Results.UnitResult<ZeroAlloc.Rest.HttpError>.Success();", client);
        Assert.DoesNotContain("DeserializeAsync<", client);
        Assert.DoesNotContain("HttpErrorKind.Deserialization", client);
    }

    [Fact]
    public void UnitResultMethod_ReturnsStatusTimeoutAndTransportFailures()
    {
        var client = Run(HttpErrorApi).GeneratedSource;

        Assert.Contains("return ZeroAlloc.Results.UnitResult<ZeroAlloc.Rest.HttpError>.Failure(", client);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, __response, null, __errorBody.Body, __errorBody.Truncated)", client);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex)", client);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex)", client);
    }

    [Fact]
    public void MappedUnitResultMethod_MapsOnceThroughTheErrorMapper()
    {
        var run = Run(MappedApi);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        Assert.Contains("return ZeroAlloc.Results.UnitResult<global::MyApp.JevError>.Success();", run.GeneratedSource);
        Assert.Contains(
            "return ZeroAlloc.Results.UnitResult<global::MyApp.JevError>.Failure(_jevErrorMapper.Map(__httpError));",
            run.GeneratedSource);
    }

    [Fact]
    public void UnitResultWithUnmappedErrorType_ReportsZra002()
    {
        var run = Run(MappedApi.Replace("[ErrorMapper(typeof(JevErrorMapper))]", "", System.StringComparison.Ordinal));

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
    }

    private static GeneratorHarness.GeneratorHarnessRun Run(string source) => GeneratorHarness.Run(source, "MyApp.IThingApi.g.cs");
}
