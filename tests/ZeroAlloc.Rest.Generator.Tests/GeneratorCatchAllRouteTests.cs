using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #422: {**name} keeps the '/' separators of the value it binds, {*name} and {name} escape
// them. Only a client with a {**name} token carries an __AppendPathEscaped helper.
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

    // Since #428 the URL is written by RestUrlBuilder, whose AppendPathEscaped escapes each segment
    // as the __EscapePath helper did; RestUrlBuilderTests holds it to that helper's output.
    [Fact]
    public void DoubleStarToken_AppendsThePathEscapedValue()
    {
        var run = Generate(Source("files/{**path}"));

        Assert.Contains(
            Lines(
                "__urlBuilder.AppendLiteral(\"files/\");",
                "__AppendPathEscaped(ref __urlBuilder, path);",
                "return __urlBuilder.ToStringAndClear();"),
            run.GeneratedSource,
            System.StringComparison.Ordinal);
        Assert.Contains(
            "private static void __AppendPathEscaped(ref global::ZeroAlloc.Rest.RestUrlBuilder builder, string value) => builder.AppendPathEscaped(__FormatValue(value));",
            run.GeneratedSource,
            System.StringComparison.Ordinal);
        Assert.DoesNotContain("__AppendEscaped(", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__EscapePath", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    [Theory]
    [InlineData("files/{*path}")]
    [InlineData("files/{path}")]
    public void SingleStarAndPlainTokens_EscapeTheWholeValue(string route)
    {
        var run = Generate(Source(route));

        Assert.Contains("__AppendEscaped(ref __urlBuilder, path);", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__AppendPathEscaped", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void MixedRoute_KeepsItsLiterals_AroundTheCatchAllHole()
    {
        var run = Generate(Source("api/{**path}/end"));

        Assert.Contains(
            Lines(
                "__urlBuilder.AppendLiteral(\"api/\");",
                "__AppendPathEscaped(ref __urlBuilder, path);",
                "__urlBuilder.AppendLiteral(\"/end\");"),
            run.GeneratedSource,
            System.StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void CatchAllAndPlainTokensTogether_EachUseTheirOwnEscape()
    {
        var run = Generate(Source("orgs/{id}/files/{**path}", "int id, string path"));

        Assert.Contains(
            Lines(
                "__urlBuilder.AppendLiteral(\"orgs/\");",
                "__AppendEscaped(ref __urlBuilder, id);",
                "__urlBuilder.AppendLiteral(\"/files/\");",
                "__AppendPathEscaped(ref __urlBuilder, path);"),
            run.GeneratedSource,
            System.StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void CatchAllTokenWithNoParameter_StaysLiteral_AndNeedsNoHelper()
    {
        var run = Generate(Source("files/{**typo}"));

        Assert.Contains("var __url = \"files/{**typo}\";", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__AppendPathEscaped", run.GeneratedSource, System.StringComparison.Ordinal);
        Assert.DoesNotContain("RestUrlBuilder", run.GeneratedSource, System.StringComparison.Ordinal);
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
        Assert.DoesNotContain("__AppendPathEscaped", run.GeneratedSource, System.StringComparison.Ordinal);
    }

    // The lines as the generated method body indents them.
    private static string Lines(params string[] lines)
        => string.Join("\n", System.Array.ConvertAll(lines, l => "            " + l));

    private static GeneratorHarness.GeneratorHarnessRun Generate(string source)
        => GeneratorHarness.Run(source, "MyApp.IFilesApi.g.cs");
}
