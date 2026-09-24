using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Regression coverage for #295: the generated client and DI extensions must take the
// interface's accessibility instead of always being public.
public class GeneratorAccessibilityTests
{
    private static readonly MetadataReference[] RuntimeReferences =
    [
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.IRestSerializer).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    [Fact]
    public void InternalInterface_WithInternalDto_Compiles()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace Repro;
            internal sealed record UserDto(int Id, string Name);
            [ZeroAllocRestClient]
            internal interface IUserApi
            {
                [Get("/users/{id}")]
                Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);
            }
            """;

        var (_, errors) = RunAndCompile(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void InternalInterface_EmitsInternalClientAndExtensions()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace Repro;
            [ZeroAllocRestClient]
            internal interface IUserApi
            {
                [Get("/users")]
                Task<string> ListAsync(CancellationToken ct = default);
            }
            """;

        var (compilation, _) = RunAndCompile(source);

        var client = compilation.GetTypeByMetadataName("Repro.UserApiClient");
        Assert.NotNull(client);
        Assert.Equal(Accessibility.Internal, client.DeclaredAccessibility);

        var extensions = FindAddMethod(compilation, "AddIUserApi").ContainingType;
        Assert.Equal(Accessibility.Internal, extensions.DeclaredAccessibility);
    }

    [Fact]
    public void PublicInterface_KeepsPublicClientAndExtensions()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace Repro;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users")]
                Task<string> ListAsync(CancellationToken ct = default);
            }
            """;

        var (compilation, errors) = RunAndCompile(source);

        Assert.Empty(errors);
        Assert.Equal(Accessibility.Public, compilation.GetTypeByMetadataName("Repro.UserApiClient")!.DeclaredAccessibility);
        var extensions = FindAddMethod(compilation, "AddIUserApi").ContainingType;
        Assert.Equal("GeneratedRestClientExtensions", extensions.Name);
        Assert.Equal(Accessibility.Public, extensions.DeclaredAccessibility);
    }

    [Fact]
    public void MixedPublicAndInternalInterfaces_InOneNamespace_Compile()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace Repro;
            internal sealed record SecretDto(int Id);
            [ZeroAllocRestClient]
            public interface IPublicApi
            {
                [Get("/public")]
                Task<string> GetAsync(CancellationToken ct = default);
            }
            [ZeroAllocRestClient]
            internal interface ISecretApi
            {
                [Get("/secret")]
                Task<SecretDto> GetAsync(CancellationToken ct = default);
            }
            """;

        var (compilation, errors) = RunAndCompile(source);

        Assert.Empty(errors);
        Assert.Equal(Accessibility.Public, FindAddMethod(compilation, "AddIPublicApi").ContainingType.DeclaredAccessibility);
        Assert.Equal(Accessibility.Internal, FindAddMethod(compilation, "AddISecretApi").ContainingType.DeclaredAccessibility);
    }

    private static IMethodSymbol FindAddMethod(Compilation compilation, string name) =>
        compilation.GetSymbolsWithName(name, SymbolFilter.Member).OfType<IMethodSymbol>().Single();

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Errors) RunAndCompile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            Basic.Reference.Assemblies.Net100.References.All.Concat(RuntimeReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (output, errors);
    }
}
