using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// A generated file is named after its interface's namespace and containing types, each with its
// generic arity, so two interfaces with the same simple name never produce the same hint name. A
// duplicate hint name made the generator throw CS8785, and then no client in the project was
// generated.
public class GeneratorHintNameTests
{
    private const string Members = """
        [ZeroAlloc.Rest.Attributes.Get("/users")]
        System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
        """;

    [Fact]
    public void SameNamedInterfaces_InDifferentNamespaces_AreBothGenerated()
    {
        var source = $$"""
            namespace N1 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            namespace N2 { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(
            ["N1.IUserApi.DI.g.cs", "N1.IUserApi.g.cs", "N2.IUserApi.DI.g.cs", "N2.IUserApi.g.cs"],
            HintNames(run));
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void SameNamedInterfaces_InDifferentContainingTypes_GetDistinctHintNames()
    {
        var source = $$"""
            namespace App
            {
                public partial class Orders { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } }
                public partial class Customers
                {
                    public partial class Inner { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } }
                }
                [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} }
            }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.DoesNotContain(run.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            [
                "App.Customers+Inner+IApi.DI.g.cs", "App.Customers+Inner+IApi.g.cs",
                "App.IApi.DI.g.cs", "App.IApi.g.cs",
                "App.Orders+IApi.DI.g.cs", "App.Orders+IApi.g.cs",
            ],
            HintNames(run));
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void GenericInterfaces_GetNoFiles_NextToASameNamedInterface()
    {
        var source = $$"""
            namespace App
            {
                [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} }
                [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi<T> { {{Members}} }
                public partial class Outer<T> { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } }
            }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        // A generic interface, and one inside a generic type, get ZRA006 and no client (#394).
        Assert.Equal(["ZRA006", "ZRA006"], run.GeneratorDiagnostics.Select(d => d.Id), System.StringComparer.Ordinal);
        Assert.Equal(["App.IApi.DI.g.cs", "App.IApi.g.cs"], HintNames(run));
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void InterfaceInGlobalNamespace_IsNamedWithoutANamespacePrefix()
    {
        var source = $$"""
            [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IUserApi { {{Members}} }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(["IUserApi.DI.g.cs", "IUserApi.g.cs"], HintNames(run));
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void VerbatimAndNonAsciiNames_AreNamedByTheirIdentifier()
    {
        // A verbatim identifier is named without its '@', and a letter outside ASCII is kept.
        var source = $$"""
            namespace @class.Café { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IΩmegaApi { {{Members}} } }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(["class.Café.IΩmegaApi.DI.g.cs", "class.Café.IΩmegaApi.g.cs"], HintNames(run));
        Assert.Empty(run.Problems);
    }

    private static string[] HintNames(GeneratorHarness.GeneratorHarnessRunAll run) =>
        [.. run.GeneratedSources.Select(s => s.HintName).Order(System.StringComparer.Ordinal)];
}
