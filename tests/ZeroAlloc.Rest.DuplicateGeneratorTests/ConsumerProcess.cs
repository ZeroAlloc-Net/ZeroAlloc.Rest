using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

// Shared by the tests that scaffold a consumer project against the packages PackedFeed packs.
internal static class ConsumerProcess
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunDotnetAsync(
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

    public static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find repo root (Directory.Build.props)");
    }

    // The single "<packageId>.<version>.nupkg" in feed. The remainder after "<packageId>." must
    // start with a digit, so "ZeroAlloc.Rest" does not match a sibling such as
    // "ZeroAlloc.Rest.DependencyInjection.<version>.nupkg" or "ZeroAlloc.Rest.Generator.<version>.nupkg".
    // Fails the calling test, naming the files, if the feed holds none or more than one version
    // of the package: docs/plans/2026-09-27-split-di.md, Global Constraints, promises exactly one
    // version per package in the feed, and a second one, left over from an earlier run at a
    // different version, would otherwise be picked silently by whatever order the file system
    // happens to enumerate in.
    public static string FindPackage(string feed, string packageId)
    {
        var matches = Directory.GetFiles(feed, packageId + ".*.nupkg")
            .Where(path => IsVersionedMatch(path, packageId))
            .ToArray();

        Assert.True(matches.Length > 0, $"No {packageId}.<version>.nupkg found in {feed}.");
        Assert.True(matches.Length == 1,
            $"Found {matches.Length} versions of {packageId} in {feed}, expected exactly one: "
            + string.Join(", ", matches.Select(Path.GetFileName)));

        return matches[0];
    }

    private static bool IsVersionedMatch(string path, string packageId)
    {
        var remainder = Path.GetFileNameWithoutExtension(path).Substring(packageId.Length + 1);
        return remainder.Length > 0 && char.IsDigit(remainder[0]);
    }

    // The NuGet.config every scaffolded consumer needs: the packed feed first, then nuget.org for
    // the transitive dependencies core and the DI package still pull in for real (Results,
    // Collections, Microsoft.Extensions.Http, ...), restored into the feed's private packages
    // folder rather than the machine-wide cache.
    public static void WriteNuGetConfig(string workDir, PackedFeed feed)
    {
        File.WriteAllText(Path.Combine(workDir, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{feed.PackagesFolder}" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{feed.FeedDirectory}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }
}
