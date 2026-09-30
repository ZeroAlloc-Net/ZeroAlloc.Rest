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
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.DependencyInjection.DependencyInjectionMarker).Assembly.Location),
    ];

    private const string Header = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public sealed record Payload(string Value);
        public static class ApiInfo { public const string Version = "2"; }

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
        Assert.Contains("var __url = $\"users/{{usrId}}\";", run.Sources["MyApp.IApi.g.cs"]);
    }

    // A token no route parameter matches, which the URL built before ZRA005 filled with a working
    // value, keeps being filled the same way. Each expected line is the URL line the generator
    // emitted at ae0124f, the commit before #339, for the same method, copied byte for byte. The
    // only difference since is the local's name, which #339 prefixed with "__".
    public static TheoryData<string, string, string, string> PreservedTokens => new()
    {
        {
            """[Get("users/{id}")] Task A1([Query] int id);""",
            """        var urlBase = $"users/{id}";""",
            "id",
            "the query parameter 'id'"
        },
        {
            """[Get("users/{id}")] Task A2([Header("X-Id")] string id, [Query] int page);""",
            """        var urlBase = $"users/{id}";""",
            "id",
            "the header parameter 'id'"
        },
        {
            """[Get("items/{id:D4}")] Task A3(int id);""",
            """        var url = $"items/{id:D4}";""",
            "id:D4",
            "the route parameter 'id'"
        },
        {
            """[Get("v{ApiInfo.Version}/users/{id}")] Task A5(int id);""",
            """        var url = $"v{ApiInfo.Version}/users/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}";""",
            "ApiInfo.Version",
            "the C# expression 'ApiInfo.Version'"
        },
        {
            """[Get("y/{DateTime.UtcNow:yyyy}/{id}")] Task A8(int id);""",
            """        var url = $"y/{DateTime.UtcNow:yyyy}/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}";""",
            "DateTime.UtcNow:yyyy",
            "the C# expression 'DateTime.UtcNow'"
        },
        {
            """[Post("b/{id}/{p}")] Task A9(int id, [Body] Payload p);""",
            """        var url = $"b/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}/{p}";""",
            "p",
            "the body parameter 'p'"
        },
        {
            """[Get("x/{ct}/{id}")] Task A10(int id, CancellationToken ct);""",
            """        var url = $"x/{ct}/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}";""",
            "ct",
            "the cancellation token parameter 'ct'"
        },
        {
            """[Get("q/{page + 1}")] Task A11([Query] int page);""",
            """        var urlBase = $"q/{page + 1}";""",
            "page + 1",
            "the query parameter 'page'"
        },
        {
            """[Get("f/{form}")] Task A12([FormBody] Dictionary<string, string> form, [Query] int z);""",
            """        var urlBase = $"f/{form}";""",
            "form",
            "the form body parameter 'form'"
        },
    };

    [Theory]
    [MemberData(nameof(PreservedTokens))]
    public void TokenFilledBeforeZra005_KeepsItsUrl_AndIsReportedAtTheAttribute(
        string member, string lineBefore339, string token, string source)
    {
        var api = Api(member);

        var run = Run(api);

        Assert.Empty(run.CompileErrors);
        var line = lineBefore339.Replace("var url ", "var __url ", StringComparison.Ordinal)
            .Replace("var urlBase ", "var __urlBase ", StringComparison.Ordinal);
        Assert.Contains(line + "\n", run.Sources["MyApp.IApi.g.cs"]);

        // Only the token is reported: every route parameter reaches the URL.
        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("ZRA005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var attribute = member.Substring(1, member.IndexOf(")]", StringComparison.Ordinal));
        Assert.Equal(attribute, At(api, diagnostic));
        var route = attribute.Substring(attribute.IndexOf('"') + 1, attribute.LastIndexOf('"') - attribute.IndexOf('"') - 1);
        var method = member.Split("Task ")[1].Split('(')[0];
        Assert.Equal(
            $"Route '{route}' of method '{method}' has a '{{{token}}}' token that no route parameter matches; "
                + $"it is compiled as C# and takes its value from {source}, without URL escaping; "
                + "make that value a route parameter with a matching token",
            Message(diagnostic));
    }

    // The same, run: the request goes to the URL it went to before #339.
    [Fact]
    public async Task TokenFilledByAQueryParameter_SendsTheSameUrl()
    {
        var source = Api("""[Get("users/{id}")] Task GetAsync([Query] int id, CancellationToken ct = default);""")
            + """
            public sealed class NoSerializer : ZeroAlloc.Rest.IRestSerializer
            {
                public string ContentType => "application/json";
                public System.Threading.Tasks.ValueTask<T?> DeserializeAsync<T>(System.IO.Stream stream, CancellationToken ct = default)
                    => default;
                public System.Threading.Tasks.ValueTask SerializeAsync<T>(System.IO.Stream stream, T value, CancellationToken ct = default)
                    => default;
            }
            """;
        var run = Run(source);
        Assert.Empty(run.CompileErrors);

        using var stream = new System.IO.MemoryStream();
        Assert.True(run.Output.Emit(stream).Success);
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        Uri? sent = null;
        using var httpClient = new System.Net.Http.HttpClient(new CaptureHandler(uri => sent = uri))
        {
            BaseAddress = new Uri("https://host/api/"),
        };
        var serializer = Activator.CreateInstance(assembly.GetType("MyApp.NoSerializer")!);
        var client = Activator.CreateInstance(assembly.GetType("MyApp.ApiClient")!, httpClient, serializer)!;

        await (Task)client.GetType().GetMethod("GetAsync")!.Invoke(client, [5, CancellationToken.None])!;

        Assert.Equal("https://host/api/users/5?id=5", sent!.ToString());
    }

    [Theory]
    [InlineData("""[Get("u/{_httpClient}/{__httpMethod}/{__RestMethodTag}/{id}")] Task A6(int id);""",
        """        var __url = $"u/{{_httpClient}}/{{__httpMethod}}/{{__RestMethodTag}}/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}";""", 3)]
    [InlineData("""[Get("t/{this}/{id}")] Task A13(int id);""",
        """        var __url = $"t/{{this}}/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}";""", 1)]
    [InlineData("""[Get("users/{id}")] Task A4([Header("X-Id")] string id);""",
        """        var __url = "users/{id}";""", 1)]
    public void TokenThatOnlyReachedTheClientsOwnMembers_OrWasNeverInterpolated_IsLiteral(string member, string line, int reports)
    {
        // Before #339 the first two compiled, but sent the client's type name, an Activity, a
        // timestamp or the method's tag: implementation details, not a working URL. The third never
        // was interpolated, because the method has neither route nor query parameters.
        var api = Api(member);

        var run = Run(api);

        Assert.Empty(run.CompileErrors);
        Assert.Contains(line + "\n", run.Sources["MyApp.IApi.g.cs"]);
        Assert.Equal(reports, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, d => Assert.EndsWith("token that no route parameter matches, so it is sent as literal text", Message(d), StringComparison.Ordinal));
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
        var client = run.Sources["MyApp.IApi.g.cs"];
        Assert.Contains("""var __url = "a\\b/\"c\"";""", client);
        Assert.Contains("""var __url = $"a\\b/\"c\"/{(global::System.Uri.EscapeDataString(__FormatValue(id)))}";""", client);
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

    // Issue #346: an interface in generated code, such as the OpenAPI tool's output, gets the same
    // checks as one written by hand, whichever way the compiler knows the code is generated.
    [Theory]
    [InlineData("// <auto-generated/>\n", "Api.cs")]
    [InlineData("// <auto-generated>\n// Generated by a tool.\n// </auto-generated>\n", "Api.cs")]
    [InlineData("", "Api.g.cs")]
    [InlineData("", "Api.designer.cs")]
    public void InterfaceInGeneratedCode_ReportsZra005(string marker, string path)
    {
        var source = marker + Api("[Get(\"users/{usrId}\")] Task<string> GetAsync(int userId, CancellationToken ct = default);");

        var run = Run(source, path);

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, d => Assert.Equal("ZRA005", d.Id));
        Assert.All(run.Diagnostics, d => Assert.Equal(path, d.Location.SourceTree?.FilePath));
        Assert.Contains(run.Diagnostics, d => At(source, d) == "userId");
        Assert.Contains(run.Diagnostics, d => At(source, d) == "Get(\"users/{usrId}\")");
    }

    // The analyzer also runs over the generator's own output. The generated clients, their
    // registration methods and helpers are not [ZeroAllocRestClient] interfaces, so they add
    // nothing: every diagnostic stays on the interface, and a clean interface gets none.
    [Fact]
    public void GeneratorOutput_AddsNoDiagnostics()
    {
        var source = Header + """
            [ZeroAllocRestClient]
            public interface IApi
            {
                [Get("users/{id}")] Task<string> GetAsync(int id, [Query] string? filter, [Header("X-Trace")] string trace, CancellationToken ct = default);
                [Post("users/{id}/items")] Task<string> PostAsync(int id, [Body] Payload body, CancellationToken ct = default);
                [Put] Task PutAsync([Body] Payload body, CancellationToken ct = default);
                [Delete("users/{id}")] Task DeleteAsync(int id, CancellationToken ct = default);
                [Get("items")] Task<string> ListAsync([Query] IEnumerable<int> ids, CancellationToken ct = default);
            }

            public static class Outer
            {
                [ZeroAllocRestClient]
                public interface IApi
                {
                    [Get("orders/{orderId}")] Task<string> GetAsync(int orderId, CancellationToken ct = default);
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.CompileErrors);
        Assert.Contains(run.Output.SyntaxTrees, t => t.FilePath.EndsWith("MyApp.IApi.g.cs", StringComparison.Ordinal));
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void GeneratorOutput_ForAMismatchedInterface_AddsNoDiagnostics()
    {
        var source = Api("[Get(\"users/{usrId}\")] Task<string> GetAsync(int userId, CancellationToken ct = default);");

        var run = Run(source);

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, d => Assert.Equal("Api.cs", d.Location.SourceTree?.FilePath));
    }

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    private static string At(string source, Diagnostic diagnostic)
        => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static GeneratorRun Run(string source, string path = "Api.cs")
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: path)],
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
        return new GeneratorRun(sources, generatorDiagnostics.AddRange(analyzerDiagnostics), compileErrors, output);
    }

    private sealed record GeneratorRun(
        Dictionary<string, string> Sources,
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<Diagnostic> CompileErrors,
        Compilation Output);

    private sealed class CaptureHandler(Action<Uri?> capture) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request.RequestUri);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
