using System.IO;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

public sealed class DuplicateGeneratorDiagnosticTests
{
    [Fact]
    public async Task Build_Fails_With_ZR9001_When_Both_Packages_Referenced()
    {
        var feed = ConsumerProcess.LocateFeed();

        // ConsumerProcess.FindPackage keeps only the file whose id ends where the version
        // begins, with a digit, so "ZeroAlloc.Rest" does not also match the sibling packages
        // "ZeroAlloc.Rest.Generator.<version>" and "ZeroAlloc.Rest.Tools.MSBuild.<version>" that
        // the plain "ZeroAlloc.Rest.*.nupkg" glob would otherwise catch.
        var restNupkgPath = ConsumerProcess.FindPackage(feed, "ZeroAlloc.Rest");
        _ = ConsumerProcess.FindPackage(feed, "ZeroAlloc.Rest.Generator");

        var version = ConsumerProcess.GetPackageVersion(restNupkgPath, "ZeroAlloc.Rest");

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
        ConsumerProcess.WriteNuGetConfig(workDir, feed);

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
