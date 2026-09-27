using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// The AOT smoke compiles a client generated from its petstore.yaml, checked in so the CI job needs
// no tool run. This fails when the checked-in file is not what the tool generates now.
public class AotSmokeClientTests
{
    [Fact]
    public void CheckedInClient_MatchesTheGenerator()
    {
        var sample = Path.Combine(RepositoryRoot(), "samples", "ZeroAlloc.Rest.AotSmoke");
        var expected = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(sample, "petstore.yaml")), "ZeroAlloc.Rest.AotSmoke.PetStore", "IPetStoreClient");
        var generatedPath = Path.Combine(sample, "Generated", "IPetStoreClient.g.cs");

        Assert.True(File.Exists(generatedPath), "Generate it: " + Command);
        Assert.True(
            string.Equals(Normalize(expected), Normalize(File.ReadAllText(generatedPath)), StringComparison.Ordinal),
            "The AOT smoke's generated client is stale. Regenerate it: " + Command);
    }

    private const string Command =
        "dotnet run --project src/ZeroAlloc.Rest.Tools -- generate --spec samples/ZeroAlloc.Rest.AotSmoke/petstore.yaml "
        + "--namespace ZeroAlloc.Rest.AotSmoke.PetStore --interface IPetStoreClient "
        + "--output samples/ZeroAlloc.Rest.AotSmoke/Generated/IPetStoreClient.g.cs";

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Rest.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("ZeroAlloc.Rest.slnx not found above " + AppContext.BaseDirectory);
    }
}
