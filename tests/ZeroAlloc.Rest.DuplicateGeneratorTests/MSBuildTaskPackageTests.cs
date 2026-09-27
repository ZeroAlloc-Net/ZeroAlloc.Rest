using System.IO;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

// Issue #347: the <ZeroAllocApiSpec> integration ships in ZeroAlloc.Rest.Tools.MSBuild. This restores
// the packed package into a throwaway consumer and builds it, so it covers what a unit test of the task
// cannot: that the package layout, the targets and the UsingTask path load the task, that the generated
// file is compiled together with the source-generated client, and that ZRT001 reaches the build output.
public sealed class MSBuildTaskPackageTests
{
    private const string PackageId = "ZeroAlloc.Rest.Tools.MSBuild";

    [Fact]
    public async Task ConsumerBuild_GeneratesAndCompilesClients_AndReportsZRT001()
    {
        var repoRoot = ConsumerProcess.LocateRepoRoot();
        var feed = Path.Combine(repoRoot, "artifacts", "local");
        Assert.True(Directory.Exists(feed),
            $"Local nupkg feed not found at {feed}. Run `dotnet pack -c Release -o artifacts/local` first.");

        var taskNupkg = Directory.GetFiles(feed, PackageId + ".*.nupkg");
        Assert.NotEmpty(taskNupkg);
        // ZeroAlloc.Rest is packed into the same feed at the same version.
        var version = Path.GetFileNameWithoutExtension(taskNupkg[0]).Substring(PackageId.Length + 1);

        var workDir = Path.Combine(Path.GetTempPath(), "za-rest-msbuild-task-" + Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        try
        {
            ScaffoldConsumer(workDir, feed, version);

            var (exitCode, stdout, stderr) = await ConsumerProcess.RunDotnetAsync(workDir, "build", "-c", "Release");
            var combined = stdout + "\n" + stderr;
            Assert.True(exitCode == 0, "Consumer build failed:\n" + combined);

            // Reported against the spec that declares the cookie parameter.
            Assert.Contains("pets.yaml : warning ZRT001", combined, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(workDir, "Generated", "IPetsApi.g.cs")));
            Assert.True(File.Exists(Path.Combine(workDir, "Generated", "IApiClient.g.cs")));

            // Program.cs reads the generated client types by name, so running it proves they compiled.
            var (runExit, runOut, runErr) = await ConsumerProcess.RunDotnetAsync(workDir, "run", "-c", "Release", "--no-build");
            Assert.True(runExit == 0, "Consumer run failed:\n" + runOut + "\n" + runErr);
            Assert.Contains("Consumer.PetsApiClient", runOut, StringComparison.Ordinal);
            Assert.Contains("Consumer.Status.ApiClientClient", runOut, StringComparison.Ordinal);

            // A rebuild sees the generated files on disk when the project is evaluated. They must still be
            // compiled once, not twice: a duplicate Compile item fails the build with CS2002. NoWarn, as
            // docs/openapi-codegen.md says, suppresses ZRT001.
            var (rebuildExit, rebuildOut, rebuildErr) = await ConsumerProcess.RunDotnetAsync(
                workDir, "build", "-c", "Release", "-warnaserror:CS2002", "-p:NoWarn=ZRT001");
            Assert.True(rebuildExit == 0, "Consumer rebuild failed:\n" + rebuildOut + "\n" + rebuildErr);
            Assert.DoesNotContain("ZRT001", rebuildOut + rebuildErr, StringComparison.Ordinal);
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

        // RestorePackagesPath keeps the restore out of the global packages folder, which would otherwise
        // serve a package cached by an earlier run at the same version instead of the one just packed.
        File.WriteAllText(Path.Combine(workDir, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <RestorePackagesPath>$(MSBuildProjectDirectory)/packages</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Rest" Version="{version}" />
                <PackageReference Include="{PackageId}" Version="{version}" />
              </ItemGroup>
              <ItemGroup>
                <ZeroAllocApiSpec Include="pets.yaml"
                                  Namespace="Consumer"
                                  InterfaceName="IPetsApi"
                                  OutputPath="$(MSBuildProjectDirectory)/Generated/IPetsApi.g.cs" />
                <!-- No InterfaceName: the task's default, IApiClient, applies. -->
                <ZeroAllocApiSpec Include="status.yaml"
                                  Namespace="Consumer.Status"
                                  OutputPath="Generated/IApiClient.g.cs" />
              </ItemGroup>
            </Project>
            """);

        WriteSpecs(workDir);

        File.WriteAllText(Path.Combine(workDir, "Program.cs"),
            """
            System.Console.WriteLine(typeof(Consumer.PetsApiClient).FullName);
            System.Console.WriteLine(typeof(Consumer.Status.ApiClientClient).FullName);
            """);
    }

    // pets.yaml has a cookie parameter, which the generated interface leaves out with ZRT001.
    private static void WriteSpecs(string workDir)
    {
        File.WriteAllText(Path.Combine(workDir, "pets.yaml"),
            """
            openapi: 3.0.0
            info:
              title: Pets
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
                    - name: session
                      in: cookie
                      schema:
                        type: string
                  responses:
                    '200':
                      description: OK
            """);

        File.WriteAllText(Path.Combine(workDir, "status.yaml"),
            """
            openapi: 3.0.0
            info:
              title: Status
              version: "1"
            paths:
              /status:
                get:
                  operationId: getStatus
                  responses:
                    '200':
                      description: OK
            """);
    }
}
