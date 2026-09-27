using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Runs the zeroalloc tool's entry point in process. It writes to the process-wide console, so
// the tests in this collection do not run in parallel with each other.
[Collection(nameof(CommandLineTests))]
[CollectionDefinition(nameof(CommandLineTests), DisableParallelization = true)]
public class CommandLineTests
{
    private const string CookieSpec = """
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /users:
            get:
              operationId: listUsers
              parameters:
                - name: session
                  in: cookie
                  schema:
                    type: string
              responses:
                '200':
                  description: OK
        """;

    [Fact]
    public void CookieParameter_IsReportedAsZrt001()
    {
        var (exitCode, stderr) = Run();

        Assert.Equal(0, exitCode);
        Assert.Contains("warning ZRT001: Operation 'listUsers': cookie parameter 'session' is not emitted", stderr);
    }

    [Fact]
    public void NoWarn_SuppressesZrt001()
    {
        var (exitCode, stderr) = Run("--nowarn", "ZRT001");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("ZRT001", stderr);
    }

    private static (int ExitCode, string Stderr) Run(params string[] extraArgs)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var originalError = Console.Error;
        var originalOut = Console.Out;
        try
        {
            var spec = Path.Combine(dir, "openapi.yaml");
            File.WriteAllText(spec, CookieSpec);
            string[] args =
            [
                "generate", "--spec", spec, "--namespace", "MyApp",
                "--output", Path.Combine(dir, "IMyApi.g.cs"), .. extraArgs,
            ];
            using var stderr = new StringWriter();
            Console.SetError(stderr);
            Console.SetOut(TextWriter.Null);
            var entryPoint = typeof(OpenApiInterfaceGenerator).Assembly.EntryPoint!;
            var exitCode = (int)entryPoint.Invoke(null, [args])!;
            return (exitCode, stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOut);
            Directory.Delete(dir, recursive: true);
        }
    }
}
