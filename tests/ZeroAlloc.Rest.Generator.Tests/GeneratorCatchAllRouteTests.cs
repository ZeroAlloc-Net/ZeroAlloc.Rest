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
