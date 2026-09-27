using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Rest.Generator.Tests;

// Shared compile-and-run plumbing for tests that only need one generated file's text plus
// diagnostics. GeneratorErrorMapperTests, GeneratorEmissionTests and the other older test classes
// keep their own verbatim copies; new test classes use this instead of copying it again.
internal static class GeneratorHarness
{
    internal static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Collections.HeapPooledList<>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    // Compiles `source` with the Rest client generator and returns the generated text for
    // `hintName` (e.g. "IThingApi.g.cs"), the generator's own diagnostics, and the problems in the
    // resulting compilation. Consumers build with TreatWarningsAsErrors, so a warning in generated
    // code is a problem worth asserting on. `extraReferences` are added to the reference set.
    internal static GeneratorHarnessRun Run(string source, string hintName, bool nullableEnabled = true,
        MetadataReference[]? extraReferences = null)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "Api.cs")],
            [.. References, .. extraReferences ?? []],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: nullableEnabled ? NullableContextOptions.Enable : NullableContextOptions.Disable));

        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var generatedSource = driver.GetRunResult().Results[0].GeneratedSources
            .First(s => string.Equals(s.HintName, hintName, System.StringComparison.Ordinal))
            .SourceText.ToString().Replace("\r\n", "\n", System.StringComparison.Ordinal);

        // Every error counts, wherever it is. A warning counts when it has a source location: a
        // diagnostic about code, generated code included, always carries one. A location-less warning
        // describes this reference set instead. ZeroAlloc.Collections ships no net10.0 asset, so
        // binding its net9.0 one reports CS1701, which the SDK's default compile hides for consumers too.
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Location.IsInSource))
            .ToImmutableArray();

        return new GeneratorHarnessRun(generatedSource, generatorDiagnostics, problems);
    }

    internal sealed record GeneratorHarnessRun(
        string GeneratedSource,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> Problems);
}
