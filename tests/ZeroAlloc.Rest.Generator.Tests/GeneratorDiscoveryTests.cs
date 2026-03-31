using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

public class GeneratorDiscoveryTests
{
    [Fact]
    public void Generator_DoesNotEmit_WhenNoAttributePresent()
    {
        var source = """
            namespace MyApp;
            public interface INotARestClient
            {
                System.Threading.Tasks.Task<string> GetAsync();
            }
            """;

        var (diagnostics, output) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(output, f => f.HintName.EndsWith("Client.g.cs"));
    }

    [Fact]
    public void Generator_EmitsImplementation_WhenAttributePresent()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users/{id}")]
                System.Threading.Tasks.Task<string> GetUserAsync(int id, System.Threading.CancellationToken ct = default);
            }
            """;

        var (diagnostics, output) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output, f => f.HintName == "IUserApi.g.cs");
    }

    [Fact]
    public void Generator_GeneratedFile_ContainsClassName()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
            }
            """;

        var (_, output) = RunGenerator(source);
        var file = output.Single(f => f.HintName == "IUserApi.g.cs");
        Assert.Contains("UserApiClient", file.SourceText.ToString());
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Output)
        RunGenerator(string source)
    {
        // We need to add references so the test source can compile — including the ZeroAlloc.Rest attributes assembly
        var attributesAssembly = typeof(ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute).Assembly;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            Basic.Reference.Assemblies.Net100.References.All
                .Append(MetadataReference.CreateFromFile(attributesAssembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RestClientGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        return (result.Diagnostics, result.Results[0].GeneratedSources);
    }
}
