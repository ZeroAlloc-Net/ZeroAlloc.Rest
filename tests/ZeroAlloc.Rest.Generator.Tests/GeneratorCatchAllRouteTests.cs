using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #422: {**name} keeps the '/' separators of the value it binds, {*name} and {name} escape
// them. Only a client with a {**name} token carries the __EscapePath helper.
public class GeneratorCatchAllRouteTests
{
    private static string Source(string route, string parameters = "string path") => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IFilesApi
        {
            [Get("{{route}}")]
            Task<string> GetAsync({{parameters}}, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void DoubleStarToken_EmitsTheHelper_AndCallsItInTheHole()
    {
        var run = Generate(Source("files/{**path}"));

        Assert.Contains("var __url = $\"files/{(__EscapePath(__FormatValue(path)))}\";", run.GeneratedSource);
        Assert.Contains("private static string __EscapePath(string value)", run.GeneratedSource);
        Assert.Equal(1, Occurrences(run.GeneratedSource, "private static string __EscapePath("));
        Assert.DoesNotContain("Uri.EscapeDataString(__FormatValue(path))", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    // Issue #425: a value of unreserved characters and '/' is returned as it is, before the builder.
    [Fact]
    public void Helper_ReturnsACleanValueUnchanged_BeforeAnythingAllocates()
    {
        var run = Generate(Source("files/{**path}"));
        var source = run.GeneratedSource;

        var scan = source.IndexOf("if (__ch != '/' && !__IsUnreserved(__ch))", System.StringComparison.Ordinal);
        var unchanged = source.IndexOf("return value;", System.StringComparison.Ordinal);
        var builder = source.IndexOf("new global::System.Text.StringBuilder", System.StringComparison.Ordinal);
        Assert.True(scan >= 0 && scan < unchanged && unchanged < builder, "The clean-value scan and return must come before the builder.");
        Assert.Equal(1, Occurrences(source, "private static bool __IsUnreserved(char c)"));
        Assert.Equal(1, Occurrences(source, "c >= 'a' && c <= 'z'"));
        Assert.Equal(2, Occurrences(source, "__IsUnreserved(__c"));
        Assert.Empty(run.Problems);
    }

    [Theory]
    [InlineData("files/{*path}")]
    [InlineData("files/{path}")]
    public void SingleStarAndPlainTokens_EscapeTheWholeValue_AndEmitNoHelper(string route)
    {
        var run = Generate(Source(route));

        Assert.Contains("var __url = $\"files/{(global::System.Uri.EscapeDataString(__FormatValue(path)))}\";", run.GeneratedSource);
        Assert.DoesNotContain("__EscapePath", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void MixedRoute_KeepsItsLiterals_AroundTheCatchAllHole()
    {
        var run = Generate(Source("api/{**path}/end"));

        Assert.Contains("var __url = $\"api/{(__EscapePath(__FormatValue(path)))}/end\";", run.GeneratedSource);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void CatchAllAndPlainTokensTogether_EachUseTheirOwnEscape()
    {
        var run = Generate(Source("orgs/{id}/files/{**path}", "int id, string path"));

        Assert.Contains(
            "var __url = $\"orgs/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}/files/{(__EscapePath(__FormatValue(path)))}\";",
            run.GeneratedSource);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void CatchAllTokenWithNoParameter_StaysLiteral_AndNeedsNoHelper()
    {
        var run = Generate(Source("files/{**typo}"));

        Assert.Contains("{{**typo}}", run.GeneratedSource);
        Assert.DoesNotContain("__EscapePath", run.GeneratedSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public void CatchAllOnAMethodWithoutAnErrorMapper_IsAStub_AndNeedsNoHelper()
    {
        const string Stubbed = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            using ZeroAlloc.Results;
            namespace MyApp;
            public sealed record MissingError(string Code);
            [ZeroAllocRestClient]
            public interface IFilesApi
            {
                [Get("files/{**path}")]
                Task<Result<string, MissingError>> GetAsync(string path, CancellationToken ct = default);
            }
            """;

        var run = Generate(Stubbed);

        Assert.Contains(run.GeneratorDiagnostics, d => d.Id == "ZRA002");
        Assert.DoesNotContain("__EscapePath", run.GeneratedSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Helper_AppendsCleanSegmentsDirectly_AndEscapesOnlyTheOthers()
    {
        var run = Generate(Source("files/{**path}"));

        Assert.Contains("__result.Append(value, __start, __end - __start);", run.GeneratedSource);
        Assert.Contains("__result.Append(global::System.Uri.EscapeDataString(value.Substring(__start, __end - __start)));", run.GeneratedSource);
    }

    private static GeneratorHarness.GeneratorHarnessRun Generate(string source)
        => GeneratorHarness.Run(source, "MyApp.IFilesApi.g.cs");

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, System.StringComparison.Ordinal); i >= 0;
            i = text.IndexOf(value, i + value.Length, System.StringComparison.Ordinal))
            count++;
        return count;
    }
}
