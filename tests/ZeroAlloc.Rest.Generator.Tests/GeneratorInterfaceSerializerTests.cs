using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Regression coverage for #301: an interface-level [Serializer] used to be read and then
// ignored, so the client silently took the container-wide IRestSerializer instead.
public class GeneratorInterfaceSerializerTests
{
    private static readonly MetadataReference[] RuntimeReferences =
    [
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.IRestSerializer).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    private const string SerializerSource = """
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public sealed class JevSerializer : IRestSerializer
        {
            public string ContentType => "application/x-jev";
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                => ValueTask.FromResult<T?>(default);
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                => ValueTask.CompletedTask;
        }
        public sealed class UploadSerializer : IRestSerializer
        {
            public string ContentType => "application/octet-stream";
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                => ValueTask.FromResult<T?>(default);
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                => ValueTask.CompletedTask;
        }
        """;

    private const string JevApiSource = SerializerSource + """

        [ZeroAllocRestClient]
        [Serializer(typeof(JevSerializer))]
        public interface IJevApi
        {
            [Get("/answers")]
            Task<string> GetAnswerAsync(CancellationToken ct = default);
        }
        """;

    // The public constructor keeps IRestSerializer, so an internal serializer type on a public
    // interface does not leak into public API; Create resolves the concrete type.
    [Fact]
    public void InterfaceLevelSerializer_ConstructorKeepsIRestSerializer_CreateResolvesConcreteType()
    {
        var (sources, _, _) = Run(JevApiSource);
        var client = sources["IJevApi.g.cs"];

        Assert.Contains("public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer)", client);
        Assert.Contains("ServiceProviderServiceExtensions.GetRequiredService<global::MyApp.JevSerializer>(services)", client);
    }

    [Fact]
    public void PublicInterface_WithInternalSerializer_Compiles()
    {
        var source = SerializerSource.Replace("public sealed class JevSerializer", "internal sealed class JevSerializer", StringComparison.Ordinal) + """

            [ZeroAllocRestClient]
            [Serializer(typeof(JevSerializer))]
            public interface IJevApi
            {
                [Get("/answers")]
                [Serializer(typeof(JevSerializer))]
                Task<string> GetAnswerAsync(CancellationToken ct = default);
            }
            """;

        var (_, _, errors) = Run(source);

        Assert.Empty(errors);
    }

    // Method-level override serializers are IRestSerializer constructor parameters too, so an
    // internal override type on a public interface does not leak into public API.
    [Fact]
    public void PublicInterface_WithInternalMethodLevelSerializer_Compiles()
    {
        var source = SerializerSource.Replace("public sealed class UploadSerializer", "internal sealed class UploadSerializer", StringComparison.Ordinal) + """

            [ZeroAllocRestClient]
            public interface IUploadApi
            {
                [Post("/upload")]
                [Serializer(typeof(UploadSerializer))]
                Task UploadAsync([Body] string data, CancellationToken ct = default);
            }
            """;

        var (sources, _, errors) = Run(source);

        Assert.Empty(errors);
        Assert.Contains("public UploadApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer, ZeroAlloc.Rest.IRestSerializer uploadSerializer)", sources["IUploadApi.g.cs"]);
        Assert.Contains("GetRequiredService<global::MyApp.UploadSerializer>(services)", sources["IUploadApi.g.cs"]);
    }

    // Serializer type names are emitted global::-qualified, so a namespace that shadows the
    // serializer's namespace from the interface's namespace cannot capture the name.
    [Fact]
    public void SerializerTypeNames_AreGlobalQualified()
    {
        var source = """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            namespace Lib
            {
                public sealed class JevSerializer : IRestSerializer
                {
                    public string ContentType => "application/x-jev";
                    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
                    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
                    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                        => ValueTask.FromResult<T?>(default);
                    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
                    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
                    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                        => ValueTask.CompletedTask;
                }
            }
            namespace App.Lib
            {
                public sealed class Unrelated { }
            }
            namespace App.Api
            {
                [ZeroAllocRestClient]
                [Serializer(typeof(global::Lib.JevSerializer))]
                public interface IJevApi
                {
                    [Get("/answers")]
                    Task<string> GetAnswerAsync(CancellationToken ct = default);
                }

                [ZeroAllocRestClient]
                public interface IUploadApi
                {
                    [Post("/upload")]
                    [Serializer(typeof(global::Lib.JevSerializer))]
                    Task UploadAsync([Body] string data, CancellationToken ct = default);
                }
            }
            """;

        var (sources, _, errors) = Run(source);

        Assert.Empty(errors);
        Assert.Contains("GetRequiredService<global::Lib.JevSerializer>(services)", sources["IJevApi.g.cs"]);
        Assert.Contains("GetRequiredService<global::Lib.JevSerializer>(services)", sources["IUploadApi.g.cs"]);
    }

    [Fact]
    public void InterfaceLevelSerializer_RegistersAndResolvesConcreteType()
    {
        var (sources, _, _) = Run(JevApiSource);
        var client = sources["IJevApi.g.cs"];

        Assert.Contains("ServiceCollectionDescriptorExtensions.TryAddSingleton<global::MyApp.JevSerializer>(services);", client);
        Assert.Contains("ServiceProviderServiceExtensions.GetRequiredService<global::MyApp.JevSerializer>(services)", client);
        Assert.DoesNotContain("GetRequiredRestSerializer", client);
    }

    [Fact]
    public void EveryClient_ImplementsGeneratedRestClient()
    {
        var source = JevApiSource + """

            [ZeroAllocRestClient]
            public interface IHostApi
            {
                [Get("/ping")]
                Task<string> PingAsync(CancellationToken ct = default);
            }
            """;

        var (_, compilation, errors) = Run(source);

        Assert.Empty(errors);
        foreach (var name in new[] { "JevApiClient", "HostApiClient" })
        {
            var client = compilation.GetTypeByMetadataName("MyApp." + name)!;
            Assert.Contains(client.AllInterfaces, i => i.ToDisplayString() == $"ZeroAlloc.Rest.IGeneratedRestClient<MyApp.{name}>");
        }
    }

    // The IGeneratedRestClient members are implemented explicitly, so an interface may declare
    // methods with the same names and parameter types without clashing.
    [Fact]
    public void InterfaceWithOwnCreateAndAddSerializers_Compiles()
    {
        var source = SerializerSource + """

            public sealed record Foo(int Id);
            [ZeroAllocRestClient]
            public interface IClashApi
            {
                [Get("/foo")]
                Task<Foo> Create(System.Net.Http.HttpClient httpClient, System.IServiceProvider services);

                [Post("/serializers")]
                Task AddSerializers(Microsoft.Extensions.DependencyInjection.IServiceCollection services, ZeroAllocClientOptions options);
            }
            """;

        var (sources, _, errors) = Run(source);

        Assert.Empty(errors);
        Assert.DoesNotContain("public static ClashApiClient Create(", sources["IClashApi.g.cs"]);
        Assert.DoesNotContain("public static void AddSerializers(", sources["IClashApi.g.cs"]);
    }

    [Fact]
    public void Add_DelegatesToTheClientsStaticMembers()
    {
        var (sources, _, _) = Run(JevApiSource);
        var di = sources["IJevApi.DI.g.cs"];

        Assert.Contains("global::ZeroAlloc.Rest.GeneratedRestClient.AddSerializers<JevApiClient>(services, options);", di);
        Assert.Contains("global::ZeroAlloc.Rest.GeneratedRestClient.Create<JevApiClient>(httpClient, sp)", di);
    }

    [Fact]
    public void InterfaceLevelSerializer_AddRejectsUseSerializer()
    {
        var (sources, _, _) = Run(JevApiSource);
        var client = sources["IJevApi.g.cs"];

        Assert.Contains("if (options.SerializerType is not null || options.SerializerInstance is not null)", client);
        Assert.Contains("throw new global::System.InvalidOperationException(", client);
        Assert.Contains("MyApp.IJevApi", client);
        Assert.Contains("[Serializer(typeof(MyApp.JevSerializer))]", client);
    }

    [Fact]
    public void InterfaceLevelSerializer_SameMethodLevelType_IsInjectedOnce()
    {
        var source = SerializerSource + """

            [ZeroAllocRestClient]
            [Serializer(typeof(JevSerializer))]
            public interface IJevApi
            {
                [Get("/answers")]
                [Serializer(typeof(JevSerializer))]
                Task<string> GetAnswerAsync(CancellationToken ct = default);

                [Post("/upload")]
                [Serializer(typeof(UploadSerializer))]
                Task UploadAsync([Body] string data, CancellationToken ct = default);
            }
            """;

        var (sources, _, errors) = Run(source);

        Assert.Empty(errors);
        Assert.Contains(
            "public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer, ZeroAlloc.Rest.IRestSerializer uploadSerializer)",
            sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void NoInterfaceLevelSerializer_ClientKeepsIRestSerializer_AndDiResolvesPerClient()
    {
        var source = SerializerSource + """

            [ZeroAllocRestClient]
            public interface IHostApi
            {
                [Get("/ping")]
                Task<string> PingAsync(CancellationToken ct = default);
            }
            """;

        var (sources, compilation, errors) = Run(source);

        Assert.Empty(errors);
        Assert.Contains("public HostApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer)", sources["IHostApi.g.cs"]);
        Assert.Contains("RestSerializerServiceProviderExtensions.GetRequiredRestSerializer<IHostApi>(services)", sources["IHostApi.g.cs"]);
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.HostApiClient"));
        Assert.DoesNotContain("InvalidOperationException", sources["IHostApi.g.cs"]);
    }

    private static (Dictionary<string, string> Sources, Compilation Compilation, ImmutableArray<Diagnostic> Errors) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            Basic.Reference.Assemblies.Net100.References.All.Concat(RuntimeReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var sources = driver.GetRunResult().Results[0].GeneratedSources
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (sources, output, errors);
    }
}
