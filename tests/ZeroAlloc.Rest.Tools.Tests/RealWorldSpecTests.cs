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
    [InlineData("duplicate-inline-unions.yaml", "IDuplicateUnionsApi")]
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

    // Issue #359: the GitHub subset declares the owner union inline twice; it used to generate a
    // byte-identical SimpleUserOrEnterprise2.
    [Fact]
    public void GitHub_IdenticalInlineUnions_AreGeneratedOnce()
    {
        var code = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Specs", "github-issues.yaml")), "RealWorld", "IGitHubIssuesApi");

        Assert.Contains("public sealed record SimpleUserOrEnterprise\n", code.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("SimpleUserOrEnterprise2", code, StringComparison.Ordinal);
    }

    // Issue #359: two operations whose schemas declare the same unions inline share one type each,
    // so a value read from one is passed to the other, and both round-trip.
    [Fact]
    public void DuplicateInlineUnions_GenerateOneTypeEach_AndRoundTrip()
    {
        var warnings = new List<OpenApiWarning>();
        var code = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Specs", "duplicate-inline-unions.yaml")),
            "MyApp", "IDuplicateUnionsApi", warnings, GenerationOptions.Default);

        Assert.Empty(warnings);
        var lines = code.ReplaceLineEndings("\n").Split('\n');
        Assert.Single(lines, l => l.StartsWith("public sealed record UserOrEnterprise", StringComparison.Ordinal));
        Assert.Single(lines, l => l.StartsWith("public sealed record AppValue", StringComparison.Ordinal));
        Assert.DoesNotContain("InstallationValue", code, StringComparison.Ordinal);

        var compiled = GeneratedCode.Compile(code, """
            using System;
            using System.Net;
            using System.Net.Http;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.SystemTextJson;

            public sealed class Stub : HttpMessageHandler
            {
                public string? LastBody { get; private set; }

                protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var json = "{\"owner\":{\"slug\":\"acme\"},\"value\":7}";
                    if (request.Content is not null)
                        json = LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    var stub = new Stub();
                    MyApp.IDuplicateUnionsApi api = new MyApp.DuplicateUnionsApiClient(
                        new HttpClient(stub) { BaseAddress = new Uri("http://stub/") },
                        new SystemTextJsonSerializer(MyApp.DuplicateUnionsApiJsonContext.Default));
                    var app = api.GetAppAsync(1).GetAwaiter().GetResult().Value;
                    MyApp.UserOrEnterprise owner = app.Owner;
                    MyApp.AppValue? value = app.Value;
                    var installation = api.CreateInstallationAsync(new MyApp.Installation { Account = owner, Value = value })
                        .GetAwaiter().GetResult().Value;
                    return string.Join("|", installation.Account.AsEnterprise?.Slug, installation.Value?.AsInt32, stub.LastBody);
                }
            }
            """);

        Assert.Equal("acme|7|{\"account\":{\"slug\":\"acme\"},\"value\":7}", compiled.RunProbe());
    }
}
