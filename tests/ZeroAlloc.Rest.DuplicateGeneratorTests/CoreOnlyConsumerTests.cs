using System.IO;
using System.Text.Json;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

// Issue #336: a consumer that only references ZeroAlloc.Rest gets a working generated client with
// no Microsoft.Extensions.* anywhere in its restore graph, and one that also references
// ZeroAlloc.Rest.DependencyInjection gets the generated Add{I} registration on top. A unit test
// cannot see what a real restore pulls in, so these build and run a throwaway console project
// against the packages in artifacts/local, the same way MSBuildTaskPackageTests does.
public sealed class CoreOnlyConsumerTests
{
    private const string CorePackageId = "ZeroAlloc.Rest";
    private const string DiPackageId = "ZeroAlloc.Rest.DependencyInjection";

    [Fact]
    public async Task ConsumerWithCoreOnly_BuildsAndRuns_WithoutMicrosoftExtensions()
    {
        var (feed, version) = LocateFeedAndVersion();

        var workDir = Path.Combine(Path.GetTempPath(), "za-rest-core-only-" + Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        try
        {
            ConsumerProcess.WriteNuGetConfig(workDir, feed);
            WritePingApiFiles(workDir);

            File.WriteAllText(Path.Combine(workDir, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <RestorePackagesPath>$(MSBuildProjectDirectory)/packages</RestorePackagesPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{CorePackageId}" Version="{version}" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(workDir, "Program.cs"),
                """
                using System;
                using System.Net.Http;
                using Consumer;

                using var httpClient = new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost/") };
                var client = new PingApiClient(httpClient, new PingSerializer());
                var result = await client.PingAsync(System.Threading.CancellationToken.None);
                Console.WriteLine(result);
                """);

            var (buildExit, buildOut, buildErr) = await ConsumerProcess.RunDotnetAsync(workDir, "build", "-c", "Release");
            Assert.True(buildExit == 0, "Consumer build failed:\n" + buildOut + "\n" + buildErr);

            AssertNoMicrosoftExtensionsInRestoreGraph(workDir);

            var (runExit, runOut, runErr) = await ConsumerProcess.RunDotnetAsync(workDir, "run", "-c", "Release", "--no-build");
            Assert.True(runExit == 0, "Consumer run failed:\n" + runOut + "\n" + runErr);
            Assert.Contains("pong", runOut, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    [Fact]
    public async Task ConsumerWithDependencyInjection_GetsAddMethod()
    {
        var (feed, version) = LocateFeedAndVersion();
        _ = ConsumerProcess.FindPackage(feed, DiPackageId);

        var workDir = Path.Combine(Path.GetTempPath(), "za-rest-core-di-" + Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        try
        {
            ConsumerProcess.WriteNuGetConfig(workDir, feed);
            WritePingApiFiles(workDir);

            File.WriteAllText(Path.Combine(workDir, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <RestorePackagesPath>$(MSBuildProjectDirectory)/packages</RestorePackagesPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{CorePackageId}" Version="{version}" />
                    <PackageReference Include="{DiPackageId}" Version="{version}" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(workDir, "Program.cs"),
                """
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Consumer;
                using ZeroAlloc.Rest;

                var services = new ServiceCollection();
                services.AddIPingApi(o =>
                {
                    o.BaseAddress = new Uri("http://localhost/");
                    o.UseSerializer(new PingSerializer());
                }).ConfigurePrimaryHttpMessageHandler(() => new StubHandler());

                using var provider = services.BuildServiceProvider();
                var api = provider.GetRequiredService<IPingApi>();
                var result = await api.PingAsync();
                Console.WriteLine(result);
                """);

            var (buildExit, buildOut, buildErr) = await ConsumerProcess.RunDotnetAsync(workDir, "build", "-c", "Release");
            Assert.True(buildExit == 0, "Consumer build failed:\n" + buildOut + "\n" + buildErr);

            var (runExit, runOut, runErr) = await ConsumerProcess.RunDotnetAsync(workDir, "run", "-c", "Release", "--no-build");
            Assert.True(runExit == 0, "Consumer run failed:\n" + runOut + "\n" + runErr);
            Assert.Contains("pong", runOut, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    // The interface, client-side handler and serializer shared by both consumers: one [Get] method
    // over a StubHandler that returns a JSON string, and a source-generated System.Text.Json
    // serializer for that string, so no reflection-based serializer is needed. Split one file per
    // method to stay under the analyzer's per-method line limit.
    private static void WritePingApiFiles(string workDir)
    {
        WriteIPingApi(workDir);
        WriteStubHandler(workDir);
        WritePingSerializer(workDir);
    }

    private static void WriteIPingApi(string workDir)
    {
        File.WriteAllText(Path.Combine(workDir, "IPingApi.cs"),
            """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;

            namespace Consumer;

            [ZeroAllocRestClient]
            public interface IPingApi
            {
                [Get("/ping")]
                Task<string> PingAsync(CancellationToken ct = default);
            }
            """);
    }

    private static void WriteStubHandler(string workDir)
    {
        File.WriteAllText(Path.Combine(workDir, "StubHandler.cs"),
            """
            using System.Net;
            using System.Net.Http;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Consumer;

            internal sealed class StubHandler : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("\"pong\"", Encoding.UTF8, "application/json"),
                    };
                    return Task.FromResult(response);
                }
            }
            """);
    }

    private static void WritePingSerializer(string workDir)
    {
        File.WriteAllText(Path.Combine(workDir, "PingSerializer.cs"),
            """
            using System.IO;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;

            namespace Consumer;

            internal sealed class PingSerializer : IRestSerializer
            {
                private static readonly JsonSerializerOptions Options = new() { TypeInfoResolver = PingJsonContext.Default };

                public string ContentType => "application/json";

                public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                    => await JsonSerializer.DeserializeAsync<T>(stream, Options, ct);

                public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                    => await JsonSerializer.SerializeAsync(stream, value, Options, ct);
            }

            [JsonSerializable(typeof(string))]
            internal sealed partial class PingJsonContext : JsonSerializerContext;
            """);
    }

    // The consumer's own project.assets.json after restore must have no Microsoft.Extensions.*
    // library, proving the core-only restore graph is what docs/plans/2026-09-27-split-di.md,
    // Global Constraints, promises: only ZeroAlloc.Results and ZeroAlloc.Collections underneath
    // ZeroAlloc.Rest.
    private static void AssertNoMicrosoftExtensionsInRestoreGraph(string workDir)
    {
        var assetsPath = Path.Combine(workDir, "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"{assetsPath} not found; restore did not run.");

        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        Assert.True(doc.RootElement.TryGetProperty("libraries", out var libraries),
            "project.assets.json has no \"libraries\" key.");

        var offenders = new List<string>();
        foreach (var library in libraries.EnumerateObject())
        {
            if (library.Name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
                offenders.Add(library.Name);
        }

        Assert.True(offenders.Count == 0,
            "project.assets.json restored Microsoft.Extensions.* libraries for a core-only consumer: "
            + string.Join(", ", offenders));
    }

    private static (string Feed, string Version) LocateFeedAndVersion()
    {
        var feed = ConsumerProcess.LocateFeed();
        var corePath = ConsumerProcess.FindPackage(feed, CorePackageId);
        var version = ConsumerProcess.GetPackageVersion(corePath, CorePackageId);
        return (feed, version);
    }

    private static void TryDelete(string workDir)
    {
        try { Directory.Delete(workDir, recursive: true); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
