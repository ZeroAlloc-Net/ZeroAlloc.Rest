using System.Diagnostics;
using System.IO;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

public sealed class DuplicateGeneratorDiagnosticTests
{
    [Fact]
    public async Task Build_Fails_With_ZR9001_When_Both_Packages_Referenced()
    {
        var repoRoot = LocateRepoRoot();
        var feed = Path.Combine(repoRoot, "artifacts", "local");
        Assert.True(Directory.Exists(feed),
            $"Local nupkg feed not found at {feed}. Run `dotnet pack -c Release -o artifacts/local` first.");

        var restNupkg = Directory.GetFiles(feed, "ZeroAlloc.Rest.*.nupkg");
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
            var (exitCode, stdout, stderr) = await RunDotnetAsync(workDir, "build", "-c", "Release");
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

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunDotnetAsync(
        string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().ConfigureAwait(false);
        return (p.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find repo root (Directory.Build.props)");
    }
}
