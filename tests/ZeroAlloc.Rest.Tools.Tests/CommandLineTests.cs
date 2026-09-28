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

    internal const string ModelSpec = """
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /pets/{id}:
            get:
              operationId: getPet
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: integer
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              properties:
                name:
                  type: string
        """;

    [Fact]
    public void CookieParameter_IsReportedAsZrt001()
    {
        var (exitCode, stderr, _) = Run(CookieSpec);

        Assert.Equal(0, exitCode);
        Assert.Contains("warning ZRT001: Operation 'listUsers': cookie parameter 'session' is not emitted", stderr);
    }

    [Fact]
    public void NoWarn_SuppressesZrt001()
    {
        var (exitCode, stderr, _) = Run(CookieSpec, "--nowarn", "ZRT001");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("ZRT001", stderr);
    }

    // Issue #360: a union whose variants cannot be told apart. With oneOf, ZRT003 is an error: it is
    // printed as one, --nowarn does not suppress it, the tool fails, and no file is written.
    internal static string AmbiguousUnionSpec(string keyword) => $$"""
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /events:
            get:
              operationId: getEvent
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Timeline'
        components:
          schemas:
            Labeled:
              type: object
              required: [event]
              properties:
                event:
                  type: string
            Unlabeled:
              type: object
              required: [event]
              properties:
                event:
                  type: string
            Timeline:
              {{keyword}}:
                - $ref: '#/components/schemas/Labeled'
                - $ref: '#/components/schemas/Unlabeled'
        """;

    [Fact]
    public void OneOfAmbiguousUnion_IsAZrt003Error_ThatFailsTheTool()
    {
        var (exitCode, stderr, output) = Run(AmbiguousUnionSpec("oneOf"), "--nowarn", "ZRT003");

        Assert.Equal(1, exitCode);
        Assert.Contains("error ZRT003: Schema '#/components/schemas/Timeline': oneOf variants 'Labeled' and 'Unlabeled'", stderr);
        Assert.Equal("", output);
    }

    [Fact]
    public void AnyOfAmbiguousUnion_IsAZrt003Warning_ThatNoWarnSuppresses()
    {
        var (exitCode, stderr, output) = Run(AmbiguousUnionSpec("anyOf"));
        var (suppressedExitCode, suppressedStderr, _) = Run(AmbiguousUnionSpec("anyOf"), "--nowarn", "ZRT003");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning ZRT003: Schema '#/components/schemas/Timeline': anyOf variants 'Labeled' and 'Unlabeled'", stderr);
        Assert.Contains("public sealed record Timeline", output);
        Assert.Equal(0, suppressedExitCode);
        Assert.DoesNotContain("ZRT003", suppressedStderr);
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--models", "true" }, true)]
    [InlineData(new[] { "--models", "false" }, false)]
    public void Models_AreGeneratedUnlessTurnedOff(string[] extraArgs, bool expected)
    {
        var (exitCode, _, output) = Run(ModelSpec, extraArgs);

        Assert.Equal(0, exitCode);
        Assert.Equal(expected, output.Contains("public sealed record Pet", StringComparison.Ordinal));
    }

    private static (int ExitCode, string Stderr, string Output) Run(string spec, params string[] extraArgs)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var originalError = Console.Error;
        var originalOut = Console.Out;
        try
        {
            var specFile = Path.Combine(dir, "openapi.yaml");
            File.WriteAllText(specFile, spec);
            var outputFile = Path.Combine(dir, "IMyApi.g.cs");
            string[] args =
            [
                "generate", "--spec", specFile, "--namespace", "MyApp",
                "--output", outputFile, .. extraArgs,
            ];
            using var stderr = new StringWriter();
            Console.SetError(stderr);
            Console.SetOut(TextWriter.Null);
            var entryPoint = typeof(OpenApiInterfaceGenerator).Assembly.EntryPoint
                ?? throw new InvalidOperationException("ZeroAlloc.Rest.Tools has no entry point.");
            var result = entryPoint.Invoke(null, [args])
                ?? throw new InvalidOperationException("ZeroAlloc.Rest.Tools entry point returned no exit code.");
            var exitCode = (int)result;
            var output = File.Exists(outputFile) ? File.ReadAllText(outputFile) : "";
            return (exitCode, stderr.ToString(), output);
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOut);
            Directory.Delete(dir, recursive: true);
        }
    }
}
