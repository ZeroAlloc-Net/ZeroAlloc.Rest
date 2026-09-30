using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// The harness drops location-less warnings, such as CS1701 for its own reference set, but must
// never drop an error, whether or not it has a source location.
public class GeneratorHarnessTests
{
    private const string Source = """
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IThingApi
        {
            [Get("/things")] Task<string> GetAsync([Query] int page);
        }
        """;

    [Fact]
    public void LocationLessError_IsReported()
    {
        // Bytes that are not a PE image: the compiler reports CS0009 with no source location.
        var broken = MetadataReference.CreateFromImage(new byte[] { 1, 2, 3, 4 }, filePath: "Broken.dll");

        var run = GeneratorHarness.Run(Source, "MyApp.IThingApi.g.cs", extraReferences: [broken]);

        var error = Assert.Single(run.Problems, d => d.Id == "CS0009");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.False(error.Location.IsInSource);
    }

    [Fact]
    public void LocationLessWarning_FromTheReferenceSet_IsNotReported()
    {
        var run = GeneratorHarness.Run(Source, "MyApp.IThingApi.g.cs");

        // [Query] needs ZeroAlloc.Collections, whose net9.0 asset reports CS1701 here.
        Assert.Empty(run.Problems);
    }
}
