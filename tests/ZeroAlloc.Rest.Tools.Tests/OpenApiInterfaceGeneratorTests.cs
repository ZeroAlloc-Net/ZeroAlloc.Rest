using Xunit;
using ZeroAlloc.Rest.Tools;

namespace ZeroAlloc.Rest.Tools.Tests;

public class OpenApiInterfaceGeneratorTests
{
    private const string MinimalYaml = """
        openapi: "3.0.0"
        info:
          title: Test API
          version: "1.0"
        paths:
          /users/{id}:
            get:
              operationId: getUser
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: integer
              responses:
                "200":
                  description: OK
          /users:
            post:
              operationId: createUser
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
              responses:
                "201":
                  description: Created
        """;

    [Fact]
    public void Generate_ProducesInterfaceWithZeroAllocAttribute()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalYaml, "MyApp", "ITestApi");
        Assert.Contains("[ZeroAllocRestClient]", result);
        Assert.Contains("interface ITestApi", result);
    }

    [Fact]
    public void Generate_IncludesNamespace()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalYaml, "MyApp", "ITestApi");
        Assert.Contains("namespace MyApp", result);
    }

    [Fact]
    public void Generate_ProducesGetMethod()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalYaml, "MyApp", "ITestApi");
        Assert.Contains("[Get(\"/users/{id}\")]", result);
    }

    [Fact]
    public void Generate_ProducesPostMethod()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalYaml, "MyApp", "ITestApi");
        Assert.Contains("[Post(\"/users\")]", result);
    }

    [Fact]
    public void Generate_GetMethodHasPathParam()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalYaml, "MyApp", "ITestApi");
        Assert.Contains("int id", result);
    }

    [Fact]
    public void Generate_PostMethodHasBodyParam()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalYaml, "MyApp", "ITestApi");
        Assert.Contains("[Body]", result);
    }

    [Fact]
    public void Generate_FromFile_ReadsLocalFile()
    {
        var file = Path.GetTempFileName() + ".yaml";
        File.WriteAllText(file, MinimalYaml);
        try
        {
            var result = OpenApiInterfaceGenerator.GenerateFromFile(file, "MyApp", "ITestApi");
            Assert.Contains("[ZeroAllocRestClient]", result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Generate_WithObjectResponseRef_MapsTypeName()
    {
        var yaml = """
            openapi: 3.0.0
            info:
              title: Test
              version: "1"
            paths:
              /users/{id}:
                get:
                  operationId: getUser
                  parameters:
                    - in: path
                      name: id
                      required: true
                      schema:
                        type: integer
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/UserDto'
            components:
              schemas:
                UserDto:
                  type: object
            """;
        var result = OpenApiInterfaceGenerator.Generate(yaml, "MyApp", "IMyApi");
        Assert.Contains("Task<UserDto>", result);
        Assert.DoesNotContain("Task<object>", result);
    }

    [Fact]
    public void Generate_WithArrayResponse_MapsListType()
    {
        var yaml = """
            openapi: 3.0.0
            info:
              title: Test
              version: "1"
            paths:
              /users:
                get:
                  operationId: listUsers
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema:
                            type: array
                            items:
                              $ref: '#/components/schemas/UserDto'
            components:
              schemas:
                UserDto:
                  type: object
            """;
        var result = OpenApiInterfaceGenerator.Generate(yaml, "MyApp", "IMyApi");
        Assert.Contains("Task<List<UserDto>>", result);
    }

    [Fact]
    public void Generate_WithSnakeCaseOperationId_ProducesPascalCaseMethodName()
    {
        var yaml = """
            openapi: 3.0.0
            info:
              title: Test
              version: "1"
            paths:
              /users:
                get:
                  operationId: get_all_users
                  responses:
                    '200':
                      description: ok
            """;
        var result = OpenApiInterfaceGenerator.Generate(yaml, "MyApp", "IMyApi");
        Assert.Contains("GetAllUsersAsync", result);
    }
}
