using System.Diagnostics;
using System.IO;

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
}
