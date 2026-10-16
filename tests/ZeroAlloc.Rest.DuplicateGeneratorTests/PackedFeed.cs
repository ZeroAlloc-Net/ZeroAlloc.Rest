using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

/// <summary>
/// Packs the src projects the consumer tests restore into a private local feed, once per test run.
/// </summary>
/// <remarks>
/// The tests used to read a feed at artifacts/local that only ci.yml packed. Every other workflow
/// that ran the tests had to repeat that step by hand, and the release publish job that lacked it
/// failed its tests, so the release was tagged and nothing reached NuGet. The tests now build the
/// feed they need, so any <c>dotnet test</c> runs them, with nothing to set up first.
/// </remarks>
public sealed class PackedFeed : IDisposable
{
    private static readonly string[] s_packedProjects =
    [
        "src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj",
        "src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj",
        "src/ZeroAlloc.Rest.Tools.MSBuild/ZeroAlloc.Rest.Tools.MSBuild.csproj",
        "src/ZeroAlloc.Rest.DependencyInjection/ZeroAlloc.Rest.DependencyInjection.csproj",
    ];

    private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(10);

    private readonly string _workDir;

    public PackedFeed()
    {
        // A unique version per run, so no NuGet cache can serve an extract of an older pack.
        Version = $"9.9.9-packedfeed-{Guid.NewGuid():N}";
        _workDir = Path.Combine(Path.GetTempPath(), $"za-rest-packedfeed-{Guid.NewGuid():N}");
        FeedDirectory = Path.Combine(_workDir, "feed");
        PackagesFolder = Path.Combine(_workDir, "packages");
        Directory.CreateDirectory(FeedDirectory);

        var repoRoot = ConsumerProcess.LocateRepoRoot();

        // A private artifacts path keeps this rebuild out of the repository's own bin and obj.
        // The release workflow packs with --no-build after the tests have run, so anything a
        // test wrote there would be what ships.
        //
        // -p:Version, not -p:PackageVersion: this pack rebuilds, and PackageVersion alone labels
        // the package without stamping the assembly, which the version guard in
        // Directory.Build.props rejects. See ZeroAlloc-Net/.github#36.
        var artifacts = Path.Combine(_workDir, "artifacts");

        foreach (var project in s_packedProjects)
        {
            var csproj = Path.Combine(repoRoot, project);
            var (exitCode, output) = RunDotnet(
                $"pack \"{csproj}\" -c Release -p:Version={Version} --artifacts-path \"{artifacts}\" -o \"{FeedDirectory}\"",
                repoRoot);
            if (exitCode != 0)
                throw new InvalidOperationException($"Packing {project} failed.\n{output}");
        }
    }

    /// <summary>The version every package in the feed carries.</summary>
    public string Version { get; }

    /// <summary>The local feed directory every consumer restores from.</summary>
    public string FeedDirectory { get; }

    /// <summary>
    /// The global packages folder every consumer restores into, so a scaffolded consumer neither
    /// reads nor fills the machine-wide NuGet cache.
    /// </summary>
    public string PackagesFolder { get; }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch (IOException ex) { Debug.WriteLine($"PackedFeed cleanup of {_workDir} failed: {ex}"); }
        catch (UnauthorizedAccessException ex) { Debug.WriteLine($"PackedFeed cleanup of {_workDir} failed: {ex}"); }
    }

    /// <summary>
    /// Runs a <c>dotnet</c> command. Build servers are disabled so no MSBuild node or compiler
    /// server outlives the command holding its redirected output, and a command that does not
    /// finish in time is killed and reported with its output rather than stalling the test run.
    /// </summary>
    private static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", arguments + " --disable-build-servers")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(s_commandTimeout))
        {
            process.Kill(entireProcessTree: true);
            lock (output)
                return (-1, $"dotnet {arguments} timed out after {s_commandTimeout}.{Environment.NewLine}{output}");
        }

        // The parameterless overload also waits for the redirected output to be drained.
        process.WaitForExit();

        lock (output)
            return (process.ExitCode, output.ToString());
    }
}
