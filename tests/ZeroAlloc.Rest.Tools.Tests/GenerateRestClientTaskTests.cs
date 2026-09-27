extern alias MSBuildTask;

using System.Collections;
using Microsoft.Build.Framework;
using Xunit;
using MSBuildTask::ZeroAlloc.Rest.Tools.MSBuild;

namespace ZeroAlloc.Rest.Tools.Tests;

public class GenerateRestClientTaskTests
{
    // Issue #338: a cookie parameter is left out of the emitted interface, and the build says so
    // with ZRT001, which a project can suppress by its code.
    [Fact]
    public void CookieParameter_IsReportedAsABuildWarning()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var spec = Path.Combine(dir, "openapi.yaml");
            File.WriteAllText(spec, """
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
                """);
            var engine = new RecordingBuildEngine();
            var task = new GenerateRestClientTask
            {
                BuildEngine = engine,
                Spec = spec,
                OutputPath = Path.Combine(dir, "IMyApi.g.cs"),
                Namespace = "MyApp",
                InterfaceName = "IMyApi",
            };

            Assert.True(task.Execute());

            var warning = Assert.Single(engine.Warnings);
            Assert.Equal("ZRT001", warning.Code);
            Assert.Contains("cookie parameter 'session'", warning.Message);
            Assert.Equal(spec, warning.File);
            Assert.Empty(engine.Errors);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Issue #347: the task runs before every compile. Rewriting an unchanged file would bump its
    // timestamp and make CoreCompile rerun on every build, so an up-to-date output is left alone.
    [Fact]
    public void UnchangedOutput_IsNotRewritten()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var spec = Path.Combine(dir, "openapi.yaml");
            File.WriteAllText(spec, """
                openapi: 3.0.0
                info:
                  title: Test
                  version: "1"
                paths:
                  /status:
                    get:
                      operationId: getStatus
                      responses:
                        '200':
                          description: OK
                """);
            var output = Path.Combine(dir, "IMyApi.g.cs");
            GenerateRestClientTask NewTask() => new()
            {
                BuildEngine = new RecordingBuildEngine(),
                Spec = spec,
                OutputPath = output,
                Namespace = "MyApp",
                InterfaceName = "IMyApi",
            };

            Assert.True(NewTask().Execute());
            var earlier = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(output, earlier);

            Assert.True(NewTask().Execute());
            Assert.Equal(earlier, File.GetLastWriteTimeUtc(output));

            // A changed output is still rewritten.
            File.WriteAllText(output, "// stale");
            File.SetLastWriteTimeUtc(output, earlier);
            Assert.True(NewTask().Execute());
            Assert.Contains("public interface IMyApi", File.ReadAllText(output));
            Assert.NotEqual(earlier, File.GetLastWriteTimeUtc(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildWarningEventArgs> Warnings { get; } = new();
        public List<BuildErrorEventArgs> Errors { get; } = new();

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs)
            => throw new NotSupportedException();
    }
}
