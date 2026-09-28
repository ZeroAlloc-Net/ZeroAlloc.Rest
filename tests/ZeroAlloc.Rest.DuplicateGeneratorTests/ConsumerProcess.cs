using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

// Shared by the tests that scaffold a consumer project against the packages in artifacts/local.
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

    // The local nupkg feed every consumer test restores from. Fails the calling test, naming
    // every project a full local pack needs, instead of letting restore fail later with an
    // opaque "package not found".
    public static string LocateFeed()
    {
        var feed = Path.Combine(LocateRepoRoot(), "artifacts", "local");
        Assert.True(Directory.Exists(feed),
            $"Local nupkg feed not found at {feed}. Run `dotnet pack -c Release -p:Version=0.0.0-dev -o artifacts/local` " +
            "on src/ZeroAlloc.Rest, src/ZeroAlloc.Rest.Generator, src/ZeroAlloc.Rest.Tools.MSBuild and " +
            "src/ZeroAlloc.Rest.DependencyInjection first.");
        return feed;
    }

    // The single "<packageId>.<version>.nupkg" in feed. The remainder after "<packageId>." must
    // start with a digit, so "ZeroAlloc.Rest" does not match a sibling such as
    // "ZeroAlloc.Rest.DependencyInjection.<version>.nupkg" or "ZeroAlloc.Rest.Generator.<version>.nupkg".
    // Fails the calling test, naming the files, if the feed holds none or more than one version
    // of the package: constraints.md promises exactly one version per package in the feed, and a
    // second one, left over from an earlier run at a different version, would otherwise be picked
    // silently by whatever order the file system happens to enumerate in.
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

    // The version a packed nupkg carries: its file name with "<packageId>." stripped off the front.
    public static string GetPackageVersion(string nupkgPath, string packageId)
        => Path.GetFileNameWithoutExtension(nupkgPath).Substring(packageId.Length + 1);

    private static bool IsVersionedMatch(string path, string packageId)
    {
        var remainder = Path.GetFileNameWithoutExtension(path).Substring(packageId.Length + 1);
        return remainder.Length > 0 && char.IsDigit(remainder[0]);
    }

    // The NuGet.config every scaffolded consumer needs: the local feed first, then nuget.org for
    // the transitive dependencies core and the DI package still pull in for real (Results,
    // Collections, Microsoft.Extensions.Http, ...).
    public static void WriteNuGetConfig(string workDir, string feed)
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
    }
}
