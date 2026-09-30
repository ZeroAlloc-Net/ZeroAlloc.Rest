using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Add{I} registers a named HttpClient called after the interface's name within its namespace.
// Only when two clients in one compilation would share that name do both get the name qualified
// with their namespace; every other client keeps its name and its generated code (#395).
public class GeneratorHttpClientNameTests
{
    private const string Members = """
        [ZeroAlloc.Rest.Attributes.Get("/users")]
        System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
        """;

    private const string NameMember = "HttpClientName";

    [Fact]
    public void SingleInterface_KeepsItsName_AndDeclaresNoName()
    {
        var source = $$"""
            namespace MyApp { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        Assert.Contains("services.AddHttpClient(nameof(IUserApi), client =>", Source(run, "MyApp.IUserApi.DI.g.cs"));
        Assert.DoesNotContain(NameMember, Source(run, "MyApp.IUserApi.g.cs"));
    }

    [Fact]
    public void SameNamedInterfaces_InDifferentNamespaces_GetQualifiedNames()
    {
        var source = $$"""
            namespace N1 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace N2.Inner { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace N1 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IOtherApi { {{Members}} } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        Assert.Contains("services.AddHttpClient(\"N1.IUserApi\", client =>", Source(run, "N1.IUserApi.DI.g.cs"));
        Assert.Contains("services.AddHttpClient(\"N2.Inner.IUserApi\", client =>", Source(run, "N2.Inner.IUserApi.DI.g.cs"));
        Assert.Contains(
            "static string? global::ZeroAlloc.Rest.IGeneratedRestClient<UserApiClient>.HttpClientName => \"N1.IUserApi\";",
            Source(run, "N1.IUserApi.g.cs"));
        Assert.Contains(
            "static string? global::ZeroAlloc.Rest.IGeneratedRestClient<UserApiClient>.HttpClientName => \"N2.Inner.IUserApi\";",
            Source(run, "N2.Inner.IUserApi.g.cs"));

        // A client that collides with nothing keeps its name and its code.
        Assert.Contains("services.AddHttpClient(nameof(IOtherApi), client =>", Source(run, "N1.IOtherApi.DI.g.cs"));
        Assert.DoesNotContain(NameMember, Source(run, "N1.IOtherApi.g.cs"));
    }

    [Fact]
    public void SameNamedNestedInterfaces_InDifferentNamespaces_GetQualifiedNames()
    {
        var source = $$"""
            namespace N1 { public class Orders { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } } }
            namespace N2 { public class Orders { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.Problems);
        Assert.Contains("services.AddHttpClient(\"N1.Orders.IApi\", client =>", Source(run, "N1.Orders+IApi.DI.g.cs"));
        Assert.Contains("services.AddHttpClient(\"N2.Orders.IApi\", client =>", Source(run, "N2.Orders+IApi.DI.g.cs"));
    }

    [Fact]
    public void QualifiedName_ThatMatchesAnotherClientsName_QualifiesThatClientToo()
    {
        // N.IUserApi and M.IUserApi collide on "IUserApi" and become "N.IUserApi" and
        // "M.IUserApi". X.N.IUserApi, nested in type N, is named "N.IUserApi" too, so it is
        // qualified as well, and all three end up distinct.
        var source = $$"""
            namespace N { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace M { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace X { public class N { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.Problems);
        Assert.Contains("services.AddHttpClient(\"N.IUserApi\", client =>", Source(run, "N.IUserApi.DI.g.cs"));
        Assert.Contains("services.AddHttpClient(\"M.IUserApi\", client =>", Source(run, "M.IUserApi.DI.g.cs"));
        Assert.Contains("services.AddHttpClient(\"X.N.IUserApi\", client =>", Source(run, "X.N+IUserApi.DI.g.cs"));
    }

    [Fact]
    public void InterfaceInGlobalNamespace_ThatCollides_KeepsItsNameAndQualifiesTheOther()
    {
        var source = $$"""
            [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} }
            namespace @class { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.Problems);
        Assert.Contains("services.AddHttpClient(\"IUserApi\", client =>", Source(run, "IUserApi.DI.g.cs"));
        Assert.Contains("services.AddHttpClient(\"class.IUserApi\", client =>", Source(run, "class.IUserApi.DI.g.cs"));
    }

    [Fact]
    public void UnsupportedInterface_DoesNotCollide()
    {
        // A generic IUserApi<T> gets ZRA006 and no client, so it takes no name.
        var source = $$"""
            namespace N1 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace N2 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi<T> { {{Members}} } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.Problems);
        Assert.Contains("services.AddHttpClient(nameof(IUserApi), client =>", Source(run, "N1.IUserApi.DI.g.cs"));
    }

    [Fact]
    public void DependencyInjectionPackageWithoutTheNameMember_GetsQualifiedAddOnly()
    {
        // A ZeroAlloc.Rest.DependencyInjection older than the generator has no HttpClientName to
        // implement; the client must not declare it there, or it would not compile.
        var source = $$"""
            namespace ZeroAlloc.Rest.DependencyInjection { public sealed class DependencyInjectionMarker { } }
            namespace ZeroAlloc.Rest { public interface IGeneratedRestClient<TSelf> where TSelf : class, IGeneratedRestClient<TSelf> { } }
            namespace N1 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace N2 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            """;
        MetadataReference[] references =
        [
            .. Basic.Reference.Assemblies.Net100.References.All,
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        ];

        var run = GeneratorHarness.RunAll(source, references);

        Assert.Contains("services.AddHttpClient(\"N1.IUserApi\", client =>", Source(run, "N1.IUserApi.DI.g.cs"));
        Assert.DoesNotContain(NameMember, Source(run, "N1.IUserApi.g.cs"));
    }

    [Fact]
    public void UnrelatedFileEdited_HttpClientNameStepIsCached()
    {
        var api = $$"""
            namespace N1 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace N2 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            """;
        const string Unrelated = "namespace N1; public static class Unrelated { public static int Value => 1; }";
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(api, path: "Api.cs"), CSharpSyntaxTree.ParseText(Unrelated, path: "Unrelated.cs")],
            GeneratorHarness.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new RestClientGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(t => t.FilePath == "Unrelated.cs"),
            CSharpSyntaxTree.ParseText(Unrelated.Replace("=> 1;", "=> 2;", System.StringComparison.Ordinal), path: "Unrelated.cs"));
        driver = driver.RunGenerators(edited);

        var result = driver.GetRunResult().Results[0];
        var steps = result.TrackedSteps["QualifiedHttpClientNames"].SelectMany(step => step.Outputs).ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected Cached or Unchanged, got {output.Reason}."));
        var outputs = result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(step => step.Outputs).ToList();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected Cached or Unchanged, got {output.Reason}."));
    }

    private static string Source(GeneratorHarness.GeneratorHarnessRunAll run, string hintName) =>
        run.GeneratedSources
            .Single(s => string.Equals(s.HintName, hintName, System.StringComparison.Ordinal))
            .SourceText.ToString();
}
