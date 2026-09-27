using System.Collections;
using Microsoft.Build.Framework;
using Xunit;
using ZeroAlloc.Rest.Tools.MSBuild;

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
                Output = Path.Combine(dir, "IMyApi.g.cs"),
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
