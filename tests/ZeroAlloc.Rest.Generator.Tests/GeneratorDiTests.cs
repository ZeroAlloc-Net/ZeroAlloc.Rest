using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

public class GeneratorDiTests
{
    private static readonly System.Reflection.Assembly AttributesAssembly =
        typeof(ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute).Assembly;

    [Fact]
    public void Generator_EmitsDiFile()
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

        var output = RunAndGetSources(source);
        Assert.Contains(output, f => f.HintName == "IUserApi.DI.g.cs");
    }

    [Fact]
    public void Generator_DiFile_ContainsAddMethod()
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

        var output = RunAndGetSources(source);
        var diFile = output.Single(f => f.HintName == "IUserApi.DI.g.cs");
        var content = diFile.SourceText.ToString();
        Assert.Contains("AddIUserApi", content);
    }

    [Fact]
    public void Generator_DiFile_RegistersTypedClient()
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

        var output = RunAndGetSources(source);
        var diFile = output.Single(f => f.HintName == "IUserApi.DI.g.cs");
        var content = diFile.SourceText.ToString();
        Assert.Contains("AddHttpClient<", content);
        Assert.Contains("IUserApi", content);
        Assert.Contains("UserApiClient", content);
    }

    [Fact]
    public void Generator_BothFilesEmitted_ForSingleInterface()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IOrderApi
            {
                [Post("/orders")]
                System.Threading.Tasks.Task<string> CreateAsync([Body] string body, System.Threading.CancellationToken ct = default);
            }
            """;

        var output = RunAndGetSources(source);
        Assert.Contains(output, f => f.HintName == "IOrderApi.g.cs");
        Assert.Contains(output, f => f.HintName == "IOrderApi.DI.g.cs");
    }

    [Fact]
    public void MethodLevelSerializer_RegistersOverrideTypeInDI()
    {
        var source = """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            public class OverrideSerializer : IRestSerializer
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
            [ZeroAllocRestClient]
            public interface IUploadApi
            {
                [Post("/upload")]
                [Serializer(typeof(OverrideSerializer))]
                Task UploadAsync([Body] string data, CancellationToken ct = default);
            }
            """;
        var output = RunAndGetSources(source);
        var diFile = output.Single(f => f.HintName == "IUploadApi.DI.g.cs");
        var content = diFile.SourceText.ToString();
        Assert.Contains("TryAddSingleton<MyApp.OverrideSerializer>", content);
    }

    private static ImmutableArray<GeneratedSourceResult> RunAndGetSources(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            Basic.Reference.Assemblies.Net100.References.All
                .Append(MetadataReference.CreateFromFile(AttributesAssembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RestClientGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        return result.Results[0].GeneratedSources;
    }
}
