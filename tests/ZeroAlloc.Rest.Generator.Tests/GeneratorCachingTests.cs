using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #319: the pipeline models must be value-equatable, or the incremental cache never hits
// and every compilation, including every IDE keystroke, regenerates every client.
public class GeneratorCachingTests
{
    private static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
    ];

    // Covers every collection in the models: methods, parameters, static headers, error mappers
    // with their error types, and a diagnostic with message arguments, ZRA002 on MissingAsync.
    private const string Api = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record JevError(string Code);
        public sealed record OtherError(int Status);
        public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new(error.StatusCode.ToString());
        }
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        public interface IJevApi
        {
            [Get("/users/{id}")]
            [Header("X-Api-Version", Value = "2")]
            Task<Result<string, JevError>> GetAsync(int id, [Query] string? filter, [Header("X-Trace")] string trace, CancellationToken ct = default);

            [Post("/users")]
            Task<Result<string, OtherError>> MissingAsync([Body] string body, CancellationToken ct = default);
        }
        """;

    // The tracking name RestClientGenerator gives the step that produces each ClientModel.
    private const string ClientModelsStep = "ClientModels";

    private const string Unrelated = """
        namespace MyApp;
        public static class Unrelated
        {
            public static int Value => 1;
        }
        """;

    [Fact]
    public void SameCompilation_Cloned_EveryStepIsCached()
    {
        var compilation = CreateCompilation(Api, Unrelated);
        var driver = CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.Clone());

        AssertAllCachedOrUnchanged(driver.GetRunResult());
    }

    [Fact]
    public void UnrelatedFileEdited_EveryStepIsCached()
    {
        var compilation = CreateCompilation(Api, Unrelated);
        var driver = CreateDriver().RunGenerators(compilation);

        var unrelatedTree = compilation.SyntaxTrees.Single(t => t.FilePath == "Unrelated.cs");
        var edited = compilation.ReplaceSyntaxTree(
            unrelatedTree,
            CSharpSyntaxTree.ParseText(Unrelated.Replace("=> 1;", "=> 2;"), path: "Unrelated.cs"));
        driver = driver.RunGenerators(edited);

        AssertAllCachedOrUnchanged(driver.GetRunResult());
    }

    [Fact]
    public void InterfaceEdited_ClientModelIsModified()
    {
        var compilation = CreateCompilation(Api, Unrelated);
        var driver = CreateDriver().RunGenerators(compilation);

        var apiTree = compilation.SyntaxTrees.Single(t => t.FilePath == "Api.cs");
        var edited = compilation.ReplaceSyntaxTree(
            apiTree,
            CSharpSyntaxTree.ParseText(Api.Replace("\"/users\"", "\"/people\""), path: "Api.cs"));
        driver = driver.RunGenerators(edited);

        var result = driver.GetRunResult().Results[0];
        var reasons = result.TrackedSteps[ClientModelsStep]
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason);
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
        var outputReasons = result.TrackedOutputSteps.SelectMany(kv => kv.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason);
        Assert.Contains(IncrementalStepRunReason.Modified, outputReasons);
    }

    private static void AssertAllCachedOrUnchanged(GeneratorDriverRunResult runResult)
    {
        var result = runResult.Results[0];
        Assert.Null(result.Exception);

        // The model step must be tracked, or this test would pass vacuously.
        Assert.True(result.TrackedSteps.ContainsKey(ClientModelsStep));
        Assert.NotEmpty(result.TrackedOutputSteps);

        var modelOutputs = result.TrackedSteps[ClientModelsStep]
            .SelectMany(step => step.Outputs)
            .ToList();
        Assert.NotEmpty(modelOutputs);
        Assert.All(modelOutputs, output => AssertCachedOrUnchanged(output.Reason));

        var sourceOutputs = result.TrackedOutputSteps.SelectMany(kv => kv.Value)
            .SelectMany(step => step.Outputs)
            .ToList();
        Assert.NotEmpty(sourceOutputs);
        Assert.All(sourceOutputs, output => AssertCachedOrUnchanged(output.Reason));
    }

    private static void AssertCachedOrUnchanged(IncrementalStepRunReason reason)
        => Assert.True(
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected Cached or Unchanged, got {reason}.");

    private static GeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            [new RestClientGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation CreateCompilation(string api, string unrelated)
        => CSharpCompilation.Create(
            "TestAssembly",
            [
                CSharpSyntaxTree.ParseText(api, path: "Api.cs"),
                CSharpSyntaxTree.ParseText(unrelated, path: "Unrelated.cs"),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
