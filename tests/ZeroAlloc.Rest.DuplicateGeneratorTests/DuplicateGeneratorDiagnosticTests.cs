using System.IO;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

public sealed class DuplicateGeneratorDiagnosticTests
{
    [Fact]
    public async Task Build_Fails_With_ZR9001_When_Both_Packages_Referenced()
    {
        var repoRoot = ConsumerProcess.LocateRepoRoot();
        var feed = Path.Combine(repoRoot, "artifacts", "local");
        Assert.True(Directory.Exists(feed),
            $"Local nupkg feed not found at {feed}. Run `dotnet pack -c Release -p:Version=0.0.0-dev -o artifacts/local` on src/ZeroAlloc.Rest and src/ZeroAlloc.Rest.Generator first.");

        // The "ZeroAlloc.Rest.*.nupkg" glob also matches "ZeroAlloc.Rest.Generator.*.nupkg"
        // because `*` greedily eats "Generator.<version>". Filter it out explicitly — relying
        // on enumeration order is unreliable across file systems (worked on Windows NTFS,
        // returned Generator first on Linux ext4 in CI).
        var restNupkg = Directory.GetFiles(feed, "ZeroAlloc.Rest.*.nupkg")
            .Where(f => !Path.GetFileName(f).StartsWith("ZeroAlloc.Rest.Generator.", StringComparison.Ordinal))
            .ToArray();
        var genNupkg = Directory.GetFiles(feed, "ZeroAlloc.Rest.Generator.*.nupkg");
        Assert.NotEmpty(restNupkg);
        Assert.NotEmpty(genNupkg);

        var version = Path.GetFileNameWithoutExtension(restNupkg[0])
            .Substring("ZeroAlloc.Rest.".Length);

        var workDir = Path.Combine(Path.GetTempPath(), "za-rest-dup-gen-" + Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        try
        {
            ScaffoldConsumer(workDir, feed, version);
            var (exitCode, stdout, stderr) = await ConsumerProcess.RunDotnetAsync(workDir, "build", "-c", "Release");
            Assert.NotEqual(0, exitCode);
            var combined = stdout + "\n" + stderr;
            Assert.Contains("ZR9001", combined, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }

    private static void ScaffoldConsumer(string workDir, string feed, string version)
    {
        File.WriteAllText(Path.Combine(workDir, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        File.WriteAllText(Path.Combine(workDir, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Rest" Version="{version}" />
                <PackageReference Include="ZeroAlloc.Rest.Generator" Version="{version}" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(workDir, "Program.cs"), "// empty consumer\n");
    }
}
