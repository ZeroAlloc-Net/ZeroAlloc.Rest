using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #333: a route parameter with no matching {name} token in the route template, and a {token}
// with no matching route parameter, are both reported as ZRA005 instead of being silently dropped
// from, or sent literally in, the request URL.
public class RouteTemplateAnalyzerTests
{
    private static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Collections.HeapPooledList<>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    private const string Header = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public sealed record Payload(string Value);

        """;

    private static string Api(params string[] members)
        => Header + "[ZeroAllocRestClient]\npublic interface IApi\n{\n"
            + string.Concat(members.Select(m => "    " + m + "\n")) + "}\n";

    [Fact]
    public void RouteParameterWithoutToken_ReportsZra005_AtTheParameter()
    {
        var source = Api("[Get(\"users/{usrId}\")] Task<string> GetAsync(int userId, int usrId, CancellationToken ct = default);");

        var diagnostic = Assert.Single(Run(source).Diagnostics);

        Assert.Equal("ZRA005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("userId", At(source, diagnostic));
        Assert.Equal(
            "Parameter 'userId' of method 'GetAsync' is a route parameter, but route 'users/{usrId}' has no "
                + "'{userId}' token, so its value is never sent; add the token, or bind the parameter with "
                + "[Query], [Header] or [Body]",
            Message(diagnostic));
    }

    [Fact]
    public void RouteParameterOnPathlessMethod_ReportsZra005_AtTheParameter()
    {
        var source = Api("[Post] Task<string> EvaluateAsync(int id, [Body] Payload body, CancellationToken ct = default);");

        var diagnostic = Assert.Single(Run(source).Diagnostics);

        Assert.Equal("ZRA005", diagnostic.Id);
        Assert.Equal("id", At(source, diagnostic));
    }

    [Fact]
    public void TokenWithoutRouteParameter_ReportsZra005_AtTheAttribute()
    {
        var source = Api("[Get(\"users/{usrId}\")] Task<string> GetAsync(CancellationToken ct = default);");

        var diagnostic = Assert.Single(Run(source).Diagnostics);

        Assert.Equal("ZRA005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Get(\"users/{usrId}\")", At(source, diagnostic));
        Assert.Equal(
            "Route 'users/{usrId}' of method 'GetAsync' has a '{usrId}' token that no route parameter "
                + "matches, so it is sent as literal text",
            Message(diagnostic));
    }

    [Fact]
    public void Typo_ReportsBothDirections_AndTheClientStillCompiles()
    {
        var source = Api("[Get(\"users/{usrId}\")] Task<string> GetAsync(int userId, CancellationToken ct = default);");

        var run = Run(source);

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, d => Assert.Equal("ZRA005", d.Id));
        Assert.Contains(run.Diagnostics, d => At(source, d) == "userId");
        Assert.Contains(run.Diagnostics, d => At(source, d) == "Get(\"users/{usrId}\")");
        // Before #333 the unmatched token became an interpolation hole naming an undefined variable,
        // a CS0103 in generated code on top of the ZRA005.
        Assert.Empty(run.CompileErrors);
        Assert.Contains("var __url = $\"users/{{usrId}}\";", run.Sources["IApi.g.cs"]);
    }

    [Fact]
    public void TokenMatchingOnlyAQueryParameter_IsReported_AndNotInterpolated()
    {
        var source = Api("[Get(\"users/{id}\")] Task<string> GetAsync([Query] int id, CancellationToken ct = default);");

        var run = Run(source);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("ZRA005", diagnostic.Id);
        Assert.Equal("Get(\"users/{id}\")", At(source, diagnostic));
        Assert.Empty(run.CompileErrors);
        Assert.Contains("var __urlBase = $\"users/{{id}}\";", run.Sources["IApi.g.cs"]);
    }

    [Fact]
    public void TokenMatchIsCaseSensitive_ReportsBothDirections()
    {
        var source = Api("[Delete(\"users/{Id}\")] Task DeleteAsync(int id);");

        var run = Run(source);

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.Contains(run.Diagnostics, d => At(source, d) == "id");
        Assert.Contains(run.Diagnostics, d => At(source, d) == "Delete(\"users/{Id}\")");
        Assert.Empty(run.CompileErrors);
    }

    [Fact]
    public void EveryTokenIsReportedOnce_EvenWhenRepeated()
    {
        var source = Api("[Get(\"a/{x}/b/{y}/c/{x}\")] Task<string> GetAsync();");

        var run = Run(source);

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.Contains(run.Diagnostics, d => Message(d).Contains("'{x}' token", StringComparison.Ordinal));
        Assert.Contains(run.Diagnostics, d => Message(d).Contains("'{y}' token", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundParameters_AndMatchedTokens_ReportNothing()
    {
        var source = Api(
            "[Get(\"orgs/{orgId}/users/{userId}\")] Task<string> GetAsync(int orgId, string userId, CancellationToken ct = default);",
            "[Get(\"items/{id}/{id}\")] Task<string> RepeatedAsync(int id);",
            "[Post] Task<string> EvaluateAsync([Body] Payload body, CancellationToken ct = default);",
            "[Post] Task<string> EvaluateWithQueryAsync([Query] int x, [Query(Name = \"y\")] int[] ys);",
            "[Get] Task<string> RootAsync();",
            "[Post(\"form\")] Task<string> FormAsync([FormBody] Dictionary<string, string> form);",
            "[Get(\"headers\")] Task<string> HeaderAsync([Header(\"X-Id\")] string id, [Header(\"X-Other\")] string? other);",
            "[Get(\"users/{class}\")] Task<string> KeywordAsync(int @class);",
            "[Put(\"users/{id}\")] Task PutAsync(int id, [Body] Payload payload, CancellationToken cancellationToken);");

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.CompileErrors);
    }

    [Fact]
    public void QuoteAndBackslashInRoute_AreEscaped_AndTheClientCompiles()
    {
        // Each route is the C# literal "a\\b/\"c\"", whose value is a\b/"c". Before #333 the route
        // was pasted into the generated string literal unescaped, which did not compile.
        var source = Api(
            """[Get("a\\b/\"c\"")] Task<string> PlainAsync();""",
            """[Get("a\\b/\"c\"/{id}")] Task<string> WithRouteAsync(int id);""",
            """[Get("a\\b/\"c\"")] Task<string> WithQueryAsync([Query] int q);""");

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.CompileErrors);
        var client = run.Sources["IApi.g.cs"];
        Assert.Contains("""var __url = "a\\b/\"c\"";""", client);
        Assert.Contains("""var __url = $"a\\b/\"c\"/{Uri.EscapeDataString(id.ToString())}";""", client);
        Assert.Contains("""var __urlBase = $"a\\b/\"c\"";""", client);
    }

    [Fact]
    public void ParametersNamedLikeGeneratedLocalsOrKeywords_Compile()
    {
        // Before #333 the generated method declared locals named url, request, response and so on,
        // so a parameter with one of those names did not compile, and neither did one named @class.
        var source = Api(
            """[Post("x/{url}")] Task<string> PostAsync(int url, [Query] int hasQuery, [Query] string? urlBase, [Body] Payload request, [Header("X-A")] string response, [Header("X-B")] string content, [Header("X-C")] string responseStream, CancellationToken urlBuilder);""",
            """[Put("y/{class}")] Task PutAsync(int @class, [Query] int @event, [Header("X-D")] string @string, [Body] Payload @object, CancellationToken @int);""",
            """[Post("z")] Task<string> FormAsync([FormBody] Dictionary<string, string> bodyStream);""");

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.CompileErrors);
    }

    [Fact]
    public void PragmaWarningDisable_SuppressesZra005()
    {
        var source = Header + """
            [ZeroAllocRestClient]
            public interface IApi
            {
            #pragma warning disable ZRA005
                [Get("users/{usrId}")]
                Task<string> GetAsync(int userId);
            #pragma warning restore ZRA005
            }
            """;

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void PragmaWarningDisable_OnlySuppressesItsOwnRegion()
    {
        var source = Header + """
            [ZeroAllocRestClient]
            public interface IApi
            {
            #pragma warning disable ZRA005
                [Get("users/{usrId}")]
                Task<string> GetAsync(int userId);
            #pragma warning restore ZRA005
                [Get("orders/{ordId}")]
                Task<string> GetOrderAsync(int orderId);
            }
            """;

        var run = Run(source);

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.Contains(run.Diagnostics, d => At(source, d) == "orderId");
        Assert.Contains(run.Diagnostics, d => At(source, d) == "Get(\"orders/{ordId}\")");
    }

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    private static string At(string source, Diagnostic diagnostic)
        => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static GeneratorRun Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "Api.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        // ZRA005 comes from RouteTemplateAnalyzer, which the compiler runs over the compilation the
        // generator produced. Without reportSuppressedDiagnostics, a diagnostic that #pragma
        // warning disable suppresses is left out, as the compiler leaves it out of the build.
        var analyzerDiagnostics = output
            .WithAnalyzers([new RouteTemplateAnalyzer()])
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();

        var sources = driver.GetRunResult().Results[0].GeneratedSources
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString().Replace("\r\n", "\n"), StringComparer.Ordinal);
        var compileErrors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return new GeneratorRun(sources, generatorDiagnostics.AddRange(analyzerDiagnostics), compileErrors);
    }

    private sealed record GeneratorRun(
        Dictionary<string, string> Sources,
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<Diagnostic> CompileErrors);
}
