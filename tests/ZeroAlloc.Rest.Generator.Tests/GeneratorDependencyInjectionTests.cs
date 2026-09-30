using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #336: the generator emits Add{I} and the IGeneratedRestClient members only when the
// ZeroAlloc.Rest.DependencyInjection marker resolves, so a core-only consumer compiles generated
// clients without anything from Microsoft.Extensions.
public class GeneratorDependencyInjectionTests
{
    private const string Api = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IThingApi
        {
            [Get("/things/{id}")]
            Task<string> GetAsync(int id, CancellationToken ct = default);
        }
        """;

    // Core, Results, Collections and the BCL: exactly what a core-only consumer compiles against.
    private static readonly MetadataReference[] CoreOnly =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Collections.HeapPooledList<>).Assembly.Location),
    ];

    private static readonly MetadataReference[] WithDependencyInjection =
    [
        .. CoreOnly,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.DependencyInjection.DependencyInjectionMarker).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    [Fact]
    public void WithoutMarker_EmitsClientAndConstructorOnly()
    {
        // The core-only reference set is honest only while core itself pulls in nothing from
        // Microsoft.Extensions.
        Assert.DoesNotContain(
            typeof(ZeroAlloc.Rest.HttpError).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("Microsoft.Extensions", System.StringComparison.Ordinal));

        var run = GeneratorHarness.RunAll(Api, CoreOnly);

        Assert.DoesNotContain(run.GeneratedSources, s => s.HintName.EndsWith(".DI.g.cs", System.StringComparison.Ordinal));
        var client = Source(run, "MyApp.IThingApi.g.cs");
        Assert.DoesNotContain("IGeneratedRestClient", client);
        Assert.DoesNotContain("Microsoft.Extensions", client);
        Assert.DoesNotContain(" Create(", client);
        Assert.DoesNotContain("AddSerializers", client);
        Assert.Contains(
            "public ThingApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer)",
            client);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void WithMarker_EmitsGeneratedClientMembersAndAddMethod()
    {
        var run = GeneratorHarness.RunAll(Api, WithDependencyInjection);

        var di = Source(run, "MyApp.IThingApi.DI.g.cs");
        Assert.Contains("AddIThingApi", di);
        Assert.Contains("global::ZeroAlloc.Rest.GeneratedRestClientRegistration.AddSerializers<ThingApiClient>", di);
        Assert.Contains("global::ZeroAlloc.Rest.GeneratedRestClientRegistration.Create<ThingApiClient>", di);
        var client = Source(run, "MyApp.IThingApi.g.cs");
        Assert.Contains("IGeneratedRestClient<ThingApiClient>", client);
        Assert.Contains("global::ZeroAlloc.Rest.GeneratedRestClientRegistration.AddPerClientSerializer<IThingApi>", client);
        Assert.Empty(run.Problems);
    }

    // #336 review: GetTypeByMetadataName returns null when a name resolves in more than one
    // referenced assembly, which would silently turn DI emission off. Two assemblies that each
    // declare the marker type must still enable it.
    [Fact]
    public void MarkerDeclaredInTwoReferencedAssemblies_StillEmitsDiOutput()
    {
        var run = GeneratorHarness.RunAll(Api, [.. WithDependencyInjection, BuildDuplicateMarkerAssembly()]);

        var di = Source(run, "MyApp.IThingApi.DI.g.cs");
        Assert.Contains("AddIThingApi", di);
        Assert.Empty(run.Problems);
    }

    private static MetadataReference BuildDuplicateMarkerAssembly()
    {
        const string source = """
            namespace ZeroAlloc.Rest.DependencyInjection;
            public static class DependencyInjectionMarker;
            """;

        var compilation = CSharpCompilation.Create(
            "DuplicateDependencyInjectionMarkerAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            Basic.Reference.Assemblies.Net100.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new System.IO.MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
            throw new System.InvalidOperationException(string.Join('\n', result.Diagnostics));
        ms.Position = 0;
        return MetadataReference.CreateFromStream(ms);
    }

    [Fact]
    public void WithoutMarker_ErrorMapperAndSerializerStillShapeTheConstructor()
    {
        const string source = """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            using ZeroAlloc.Results;
            namespace MyApp;
            public sealed record ApiError(string Code);
            public sealed class ApiErrorMapper : IHttpErrorMapper<ApiError>
            {
                public ApiError Map(HttpError error) => new(error.StatusCode.ToString());
            }
            public sealed class OverrideSerializer : IRestSerializer
            {
                public string ContentType => "application/octet-stream";
                public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                    => ValueTask.FromResult<T?>(default);
                public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                    => ValueTask.CompletedTask;
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(ApiErrorMapper))]
            public interface IThingApi
            {
                [Get("/things/{id}")]
                Task<Result<string, ApiError>> GetAsync(int id, CancellationToken ct = default);

                [Post("/things")]
                [Serializer(typeof(OverrideSerializer))]
                Task UploadAsync([Body] string data, CancellationToken ct = default);
            }
            """;

        var run = GeneratorHarness.RunAll(source, CoreOnly);

        Assert.DoesNotContain(run.GeneratedSources, s => s.HintName.EndsWith(".DI.g.cs", System.StringComparison.Ordinal));
        var client = Source(run, "MyApp.IThingApi.g.cs");
        Assert.DoesNotContain("IGeneratedRestClient", client);
        Assert.DoesNotContain("Microsoft.Extensions", client);
        var ctor = client.Split('\n').Single(l => l.Contains("public ThingApiClient(", System.StringComparison.Ordinal));
        Assert.Contains("ZeroAlloc.Rest.IRestSerializer serializer, ZeroAlloc.Rest.IRestSerializer ", ctor);
        Assert.Contains("global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.ApiError> ", ctor);
        Assert.Empty(run.Problems);
    }

    private static string Source(GeneratorHarness.GeneratorHarnessRunAll run, string hintName)
        => run.GeneratedSources
            .Single(s => string.Equals(s.HintName, hintName, System.StringComparison.Ordinal))
            .SourceText.ToString().Replace("\r\n", "\n", System.StringComparison.Ordinal);
}
