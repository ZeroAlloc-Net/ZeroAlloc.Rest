using Xunit;
using Xunit.Abstractions;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §10.4: public specs generate a client that compiles with no diagnostics. ZRT002 warnings are
// allowed: they are the tool reporting what it could not type, not a broken build.
public class RealWorldSpecTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("petstore.yaml", "IPetstoreApi")]
    [InlineData("petstore-expanded.yaml", "IPetstoreExpandedApi")]
    [InlineData("github-issues.yaml", "IGitHubIssuesApi")]
    public void Spec_GeneratesAClientThatCompilesClean(string file, string interfaceName)
    {
        var warnings = new List<OpenApiWarning>();
        var code = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Specs", file)), "RealWorld", interfaceName, warnings, GenerationOptions.Default);

        foreach (var warning in warnings)
            output.WriteLine($"{warning.Code}: {warning.Message}");
        Assert.All(warnings, w => Assert.Contains(w.Code, new[] { "ZRT001", "ZRT002" }, StringComparer.Ordinal));
        GeneratedCode.Compile(code).AssertClean();
    }

    [Fact]
    public void GitHubTimeline_IsAUnionOfItsEventTypes()
    {
        var code = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Specs", "github-issues.yaml")), "RealWorld", "IGitHubIssuesApi");

        Assert.Contains("public sealed record TimelineIssueEvents", code);
        Assert.Contains("internal sealed class TimelineIssueEventsConverter", code);
    }
}
