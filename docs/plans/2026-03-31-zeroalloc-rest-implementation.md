# ZeroAlloc.Rest Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a Native AOT-compatible, zero-allocation REST client library for .NET with Refit-style interface attributes and Roslyn source generation.

**Architecture:** A Roslyn incremental source generator reads interfaces decorated with `[ZeroAllocRestClient]` and emits concrete implementation classes at compile time. A separate Tools package generates those interfaces from OpenAPI specs. Three AOT-compatible serializer adapters (System.Text.Json, MessagePack, MemoryPack) plug into a common `IRestSerializer` abstraction.

**Tech Stack:** .NET 10, xUnit, Roslyn incremental generators, WireMock.Net (integration tests), Microsoft.OpenApi.Readers (OpenAPI parsing), System.CommandLine (CLI), System.Text.Json, MessagePack-CSharp, MemoryPack.

---

## Task 1: Solution Scaffold

**Files:**
- Create: `ZeroAlloc.Rest.sln`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj`
- Create: `src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj`
- Create: `src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj`
- Create: `src/ZeroAlloc.Rest.MessagePack/ZeroAlloc.Rest.MessagePack.csproj`
- Create: `src/ZeroAlloc.Rest.MemoryPack/ZeroAlloc.Rest.MemoryPack.csproj`
- Create: `src/ZeroAlloc.Rest.Tools/ZeroAlloc.Rest.Tools.csproj`
- Create: `tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj`
- Create: `tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
- Create: `tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj`

**Step 1: Create solution and project directories**

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet new sln -n ZeroAlloc.Rest
mkdir -p src/ZeroAlloc.Rest src/ZeroAlloc.Rest.Generator src/ZeroAlloc.Rest.SystemTextJson
mkdir -p src/ZeroAlloc.Rest.MessagePack src/ZeroAlloc.Rest.MemoryPack src/ZeroAlloc.Rest.Tools
mkdir -p tests/ZeroAlloc.Rest.Tests tests/ZeroAlloc.Rest.Generator.Tests tests/ZeroAlloc.Rest.Integration.Tests
```

**Step 2: Create Directory.Build.props**

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Authors>ZeroAlloc.Rest Contributors</Authors>
    <RepositoryUrl>https://github.com/TODO/ZeroAlloc.Rest</RepositoryUrl>
  </PropertyGroup>
</Project>
```

**Step 3: Create Directory.Packages.props (central package management)**

```xml
<!-- Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Roslyn -->
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.13.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" />
    <!-- Serializers -->
    <PackageVersion Include="MessagePack" Version="3.1.3" />
    <PackageVersion Include="MessagePack.SourceGenerator" Version="3.1.3" />
    <PackageVersion Include="MemoryPack" Version="1.21.3" />
    <!-- OpenAPI -->
    <PackageVersion Include="Microsoft.OpenApi.Readers" Version="2.0.0" />
    <!-- CLI -->
    <PackageVersion Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
    <!-- Testing -->
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" Version="1.1.2" />
    <PackageVersion Include="WireMock.Net" Version="1.6.9" />
  </ItemGroup>
</Project>
```

**Step 4: Create core library project**

```xml
<!-- src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

**Step 5: Create generator project**

```xml
<!-- src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 6: Create serializer adapter projects**

```xml
<!-- src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- src/ZeroAlloc.Rest.MessagePack/ZeroAlloc.Rest.MessagePack.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
    <PackageReference Include="MessagePack" />
    <PackageReference Include="MessagePack.SourceGenerator" />
  </ItemGroup>
</Project>
```

```xml
<!-- src/ZeroAlloc.Rest.MemoryPack/ZeroAlloc.Rest.MemoryPack.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
    <PackageReference Include="MemoryPack" />
  </ItemGroup>
</Project>
```

**Step 7: Create tools project**

```xml
<!-- src/ZeroAlloc.Rest.Tools/ZeroAlloc.Rest.Tools.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>zeroalloc</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.OpenApi.Readers" />
    <PackageReference Include="System.CommandLine" />
    <ProjectReference Include="../ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
  </ItemGroup>
</Project>
```

**Step 8: Create test projects**

```xml
<!-- tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="WireMock.Net" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj" />
  </ItemGroup>
</Project>
```

**Step 9: Add all projects to solution**

```bash
dotnet sln add src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj
dotnet sln add src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj
dotnet sln add src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj
dotnet sln add src/ZeroAlloc.Rest.MessagePack/ZeroAlloc.Rest.MessagePack.csproj
dotnet sln add src/ZeroAlloc.Rest.MemoryPack/ZeroAlloc.Rest.MemoryPack.csproj
dotnet sln add src/ZeroAlloc.Rest.Tools/ZeroAlloc.Rest.Tools.csproj
dotnet sln add tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj
dotnet sln add tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj
dotnet sln add tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj
```

**Step 10: Verify solution builds**

```bash
dotnet build ZeroAlloc.Rest.sln
```
Expected: Build succeeded, 0 errors.

**Step 11: Commit**

```bash
git add .
git commit -m "feat: scaffold solution structure with all projects"
```

---

## Task 2: Core Attributes

**Files:**
- Create: `src/ZeroAlloc.Rest/Attributes/ZeroAllocRestClientAttribute.cs`
- Create: `src/ZeroAlloc.Rest/Attributes/HttpMethodAttribute.cs`
- Create: `src/ZeroAlloc.Rest/Attributes/ParameterAttributes.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/Attributes/AttributeTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/Attributes/AttributeTests.cs
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Tests.Attributes;

public class AttributeTests
{
    [Fact]
    public void ZeroAllocRestClientAttribute_IsAnAttribute()
    {
        var attr = new ZeroAllocRestClientAttribute();
        Assert.IsAssignableFrom<Attribute>(attr);
    }

    [Fact]
    public void GetAttribute_StoresRoute()
    {
        var attr = new GetAttribute("/users/{id}");
        Assert.Equal("/users/{id}", attr.Route);
        Assert.Equal("GET", attr.Method);
    }

    [Fact]
    public void PostAttribute_StoresRoute()
    {
        var attr = new PostAttribute("/users");
        Assert.Equal("/users", attr.Route);
        Assert.Equal("POST", attr.Method);
    }

    [Fact]
    public void PutAttribute_StoresRoute()
    {
        var attr = new PutAttribute("/users/{id}");
        Assert.Equal("PUT", attr.Method);
    }

    [Fact]
    public void PatchAttribute_StoresRoute()
    {
        var attr = new PatchAttribute("/users/{id}");
        Assert.Equal("PATCH", attr.Method);
    }

    [Fact]
    public void DeleteAttribute_StoresRoute()
    {
        var attr = new DeleteAttribute("/users/{id}");
        Assert.Equal("DELETE", attr.Method);
    }

    [Fact]
    public void HeaderAttribute_StoresName()
    {
        var attr = new HeaderAttribute("X-Api-Key");
        Assert.Equal("X-Api-Key", attr.Name);
    }

    [Fact]
    public void SerializerAttribute_StoresType()
    {
        var attr = new SerializerAttribute(typeof(object));
        Assert.Equal(typeof(object), attr.SerializerType);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "AttributeTests"
```
Expected: FAIL — type not found errors.

**Step 3: Create ZeroAllocRestClientAttribute**

```csharp
// src/ZeroAlloc.Rest/Attributes/ZeroAllocRestClientAttribute.cs
namespace ZeroAlloc.Rest.Attributes;

[AttributeUsage(AttributeTargets.Interface)]
public sealed class ZeroAllocRestClientAttribute : Attribute { }
```

**Step 4: Create HttpMethodAttribute and derived attributes**

```csharp
// src/ZeroAlloc.Rest/Attributes/HttpMethodAttribute.cs
namespace ZeroAlloc.Rest.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public abstract class HttpMethodAttribute(string method, string route) : Attribute
{
    public string Method { get; } = method;
    public string Route { get; } = route;
}

public sealed class GetAttribute(string route) : HttpMethodAttribute("GET", route) { }
public sealed class PostAttribute(string route) : HttpMethodAttribute("POST", route) { }
public sealed class PutAttribute(string route) : HttpMethodAttribute("PUT", route) { }
public sealed class PatchAttribute(string route) : HttpMethodAttribute("PATCH", route) { }
public sealed class DeleteAttribute(string route) : HttpMethodAttribute("DELETE", route) { }
```

**Step 5: Create parameter attributes**

```csharp
// src/ZeroAlloc.Rest/Attributes/ParameterAttributes.cs
namespace ZeroAlloc.Rest.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class QueryAttribute : Attribute
{
    public string? Name { get; init; }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HeaderAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string? Value { get; init; }
}

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public sealed class SerializerAttribute(Type serializerType) : Attribute
{
    public Type SerializerType { get; } = serializerType;
}
```

**Step 6: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "AttributeTests"
```
Expected: All 8 tests PASS.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Rest/Attributes/ tests/ZeroAlloc.Rest.Tests/Attributes/
git commit -m "feat: add core HTTP method and parameter attributes"
```

---

## Task 3: IRestSerializer & ApiResponse

**Files:**
- Create: `src/ZeroAlloc.Rest/IRestSerializer.cs`
- Create: `src/ZeroAlloc.Rest/ApiResponse.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/ApiResponseTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/ApiResponseTests.cs
using ZeroAlloc.Rest;
using System.Net;

namespace ZeroAlloc.Rest.Tests;

public class ApiResponseTests
{
    [Fact]
    public void ApiResponse_ExposesStatusCode()
    {
        var response = new ApiResponse<string>("hello", HttpStatusCode.OK, []);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void ApiResponse_ExposesContent()
    {
        var response = new ApiResponse<int>(42, HttpStatusCode.OK, []);
        Assert.Equal(42, response.Content);
    }

    [Fact]
    public void ApiResponse_NonSuccessStatus_IsSuccessStatusCodeFalse()
    {
        var response = new ApiResponse<string>(null, HttpStatusCode.NotFound, []);
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public void ApiResponse_ExposesHeaders()
    {
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            ["X-Request-Id"] = ["abc123"]
        };
        var response = new ApiResponse<string>("body", HttpStatusCode.OK, headers);
        Assert.Equal("abc123", response.Headers["X-Request-Id"].First());
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "ApiResponseTests"
```
Expected: FAIL — type not found.

**Step 3: Create IRestSerializer**

```csharp
// src/ZeroAlloc.Rest/IRestSerializer.cs
namespace ZeroAlloc.Rest;

public interface IRestSerializer
{
    string ContentType { get; }
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);
    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default);
}
```

**Step 4: Create ApiResponse**

```csharp
// src/ZeroAlloc.Rest/ApiResponse.cs
using System.Net;

namespace ZeroAlloc.Rest;

public sealed class ApiResponse<T>(
    T? content,
    HttpStatusCode statusCode,
    IReadOnlyDictionary<string, IEnumerable<string>> headers)
{
    public T? Content { get; } = content;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool IsSuccessStatusCode { get; } = (int)statusCode >= 200 && (int)statusCode <= 299;
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; } = headers;
}
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "ApiResponseTests"
```
Expected: All 4 tests PASS.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Rest/ tests/ZeroAlloc.Rest.Tests/
git commit -m "feat: add IRestSerializer abstraction and ApiResponse<T>"
```

---

## Task 4: DI Registration Infrastructure

**Files:**
- Create: `src/ZeroAlloc.Rest/ZeroAllocClientOptions.cs`
- Create: `src/ZeroAlloc.Rest/ZeroAllocClientBuilder.cs`
- Create: `src/ZeroAlloc.Rest/ServiceCollectionExtensions.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/ServiceCollectionExtensionsTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/ServiceCollectionExtensionsTests.cs
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.Tests;

// Minimal stub serializer for tests
file sealed class StubSerializer : IRestSerializer
{
    public string ContentType => "application/json";
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct) => ValueTask.FromResult(default(T));
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct) => ValueTask.CompletedTask;
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddZeroAllocClient_RegistersBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddZeroAllocClient(options =>
        {
            options.BaseAddress = new Uri("https://example.com");
            options.UseSerializer<StubSerializer>();
        });
        Assert.NotNull(builder);
    }

    [Fact]
    public void ZeroAllocClientOptions_DefaultsAreReasonable()
    {
        var options = new ZeroAllocClientOptions();
        Assert.Null(options.BaseAddress);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "ServiceCollectionExtensionsTests"
```
Expected: FAIL — types not found.

**Step 3: Create ZeroAllocClientOptions**

```csharp
// src/ZeroAlloc.Rest/ZeroAllocClientOptions.cs
namespace ZeroAlloc.Rest;

public sealed class ZeroAllocClientOptions
{
    public Uri? BaseAddress { get; set; }
    internal Type? SerializerType { get; private set; }

    public void UseSerializer<TSerializer>() where TSerializer : IRestSerializer
        => SerializerType = typeof(TSerializer);
}
```

**Step 4: Create ZeroAllocClientBuilder**

```csharp
// src/ZeroAlloc.Rest/ZeroAllocClientBuilder.cs
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Rest;

public sealed class ZeroAllocClientBuilder(IHttpClientBuilder httpClientBuilder)
{
    public IHttpClientBuilder HttpClientBuilder { get; } = httpClientBuilder;

    public ZeroAllocClientBuilder ConfigureHttpClient(Action<HttpClient> configure)
    {
        HttpClientBuilder.ConfigureHttpClient(configure);
        return this;
    }
}
```

**Step 5: Create ServiceCollectionExtensions**

```csharp
// src/ZeroAlloc.Rest/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Rest;

public static class ServiceCollectionExtensions
{
    public static ZeroAllocClientBuilder AddZeroAllocClient(
        this IServiceCollection services,
        Action<ZeroAllocClientOptions> configure)
    {
        var options = new ZeroAllocClientOptions();
        configure(options);

        if (options.SerializerType is not null)
            services.AddSingleton(typeof(IRestSerializer), options.SerializerType);

        var httpClientBuilder = services.AddHttpClient("ZeroAllocClient", client =>
        {
            if (options.BaseAddress is not null)
                client.BaseAddress = options.BaseAddress;
        });

        return new ZeroAllocClientBuilder(httpClientBuilder);
    }
}
```

**Step 6: Add Microsoft.Extensions.Http to core project**

Add to `src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj`:
```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

Or use the package directly:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
</ItemGroup>
```

Also add to `Directory.Packages.props`:
```xml
<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.0" />
```

**Step 7: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "ServiceCollectionExtensionsTests"
```
Expected: All 2 tests PASS.

**Step 8: Commit**

```bash
git add src/ZeroAlloc.Rest/ tests/ZeroAlloc.Rest.Tests/
git commit -m "feat: add DI registration infrastructure"
```

---

## Task 5: Source Generator — Scaffolding & Interface Discovery

**Files:**
- Create: `src/ZeroAlloc.Rest.Generator/RestClientGenerator.cs`
- Create: `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs`
- Create: `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs`
- Create: `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiscoveryTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiscoveryTests.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Rest.Generator;

namespace ZeroAlloc.Rest.Generator.Tests;

public class GeneratorDiscoveryTests
{
    [Fact]
    public void Generator_DoesNotEmit_WhenNoAttributePresent()
    {
        var source = """
            namespace MyApp;
            public interface INotARestClient
            {
                System.Threading.Tasks.Task<string> GetAsync();
            }
            """;

        var (diagnostics, output) = RunGenerator(source);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output, f => f.HintName.EndsWith(".g.cs") && f.HintName.Contains("Client"));
    }

    [Fact]
    public void Generator_EmitsImplementation_WhenAttributePresent()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users/{id}")]
                System.Threading.Tasks.Task<string> GetUserAsync(int id, System.Threading.CancellationToken ct = default);
            }
            """;

        var (diagnostics, output) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output, f => f.HintName == "IUserApi.g.cs");
    }

    [Fact]
    public void Generator_GeneratedFile_ContainsClassName()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
            }
            """;

        var (_, output) = RunGenerator(source);
        var file = output.Single(f => f.HintName == "IUserApi.g.cs");
        Assert.Contains("UserApiClient", file.SourceText.ToString());
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Output)
        RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            Basic.Reference.Assemblies.Net80.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RestClientGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();
        return (result.Diagnostics, result.Results[0].GeneratedSources);
    }
}
```

Note: Add `Basic.Reference.Assemblies.NetStandard20` NuGet for test references. Add to Directory.Packages.props:
```xml
<PackageVersion Include="Basic.Reference.Assemblies.Net80" Version="1.7.10" />
```
And to generator tests csproj:
```xml
<PackageReference Include="Basic.Reference.Assemblies.Net80" />
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/
```
Expected: FAIL — RestClientGenerator not found.

**Step 3: Create the models**

```csharp
// src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs
namespace ZeroAlloc.Rest.Generator.Models;

internal enum ParameterKind { Path, Query, Body, Header, CancellationToken }

internal record ParameterModel(
    string Name,
    string TypeName,
    ParameterKind Kind,
    string? HeaderName = null,
    string? QueryName = null);
```

```csharp
// src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs
namespace ZeroAlloc.Rest.Generator.Models;

internal record MethodModel(
    string Name,
    string HttpMethod,
    string Route,
    string ReturnTypeName,       // e.g. "Task<User>", "Task", "Task<ApiResponse<User>>"
    string? InnerTypeName,       // e.g. "User" extracted from Task<User>
    bool ReturnsApiResponse,
    bool ReturnsVoid,
    IReadOnlyList<ParameterModel> Parameters,
    string? SerializerTypeName); // null = use client default
```

```csharp
// src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs
namespace ZeroAlloc.Rest.Generator.Models;

internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,            // InterfaceName with leading 'I' stripped + "Client"
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName); // null = use DI-registered default
```

**Step 4: Create the generator scaffold**

```csharp
// src/ZeroAlloc.Rest.Generator/RestClientGenerator.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

[Generator]
public sealed class RestClientGenerator : IIncrementalGenerator
{
    private const string ZeroAllocRestClientAttributeName =
        "ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var clientModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ZeroAllocRestClientAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.Extract(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(clientModels, static (ctx, model) =>
            ClientEmitter.Emit(ctx, model));
    }
}
```

**Step 5: Create ModelExtractor stub (returns null for now)**

```csharp
// src/ZeroAlloc.Rest.Generator/ModelExtractor.cs
using Microsoft.CodeAnalysis;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class ModelExtractor
{
    internal static ClientModel? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct) => null; // implemented in Task 6
}
```

**Step 6: Create ClientEmitter stub**

```csharp
// src/ZeroAlloc.Rest.Generator/ClientEmitter.cs
using Microsoft.CodeAnalysis;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class ClientEmitter
{
    internal static void Emit(SourceProductionContext ctx, ClientModel model)
    {
        // implemented in Task 7
    }
}
```

**Step 7: Run generator discovery test (first test only)**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "DoesNotEmit"
```
Expected: PASS (generator exists, emits nothing — ModelExtractor returns null).

**Step 8: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ tests/ZeroAlloc.Rest.Generator.Tests/
git commit -m "feat: scaffold source generator with incremental provider"
```

---

## Task 6: Source Generator — Model Extraction

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`

**Step 1: Run the "emits implementation" test to confirm it fails**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "EmitsImplementation"
```
Expected: FAIL — ModelExtractor returns null.

**Step 2: Implement ModelExtractor**

```csharp
// src/ZeroAlloc.Rest.Generator/ModelExtractor.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class ModelExtractor
{
    private const string GetAttr    = "ZeroAlloc.Rest.Attributes.GetAttribute";
    private const string PostAttr   = "ZeroAlloc.Rest.Attributes.PostAttribute";
    private const string PutAttr    = "ZeroAlloc.Rest.Attributes.PutAttribute";
    private const string PatchAttr  = "ZeroAlloc.Rest.Attributes.PatchAttribute";
    private const string DeleteAttr = "ZeroAlloc.Rest.Attributes.DeleteAttribute";
    private const string BodyAttr   = "ZeroAlloc.Rest.Attributes.BodyAttribute";
    private const string QueryAttr  = "ZeroAlloc.Rest.Attributes.QueryAttribute";
    private const string HeaderAttr = "ZeroAlloc.Rest.Attributes.HeaderAttribute";
    private const string SerializerAttr = "ZeroAlloc.Rest.Attributes.SerializerAttribute";

    internal static ClientModel? Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
            return null;

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();

        var interfaceName = interfaceSymbol.Name;
        var className = interfaceName.StartsWith("I") && interfaceName.Length > 1
            ? interfaceName[1..] + "Client"
            : interfaceName + "Client";

        var clientSerializer = GetSerializerType(interfaceSymbol);

        var methods = new List<MethodModel>();
        foreach (var member in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();
            var method = ExtractMethod(member);
            if (method is not null) methods.Add(method);
        }

        return new ClientModel(ns, interfaceName, className, methods, clientSerializer);
    }

    private static MethodModel? ExtractMethod(IMethodSymbol method)
    {
        string? httpMethod = null;
        string? route = null;

        foreach (var attr in method.GetAttributes())
        {
            var attrClass = attr.AttributeClass?.ToDisplayString();
            if (attrClass is null) continue;
            if (attrClass == GetAttr)    { httpMethod = "GET";    route = (string?)attr.ConstructorArguments[0].Value; }
            else if (attrClass == PostAttr)   { httpMethod = "POST";   route = (string?)attr.ConstructorArguments[0].Value; }
            else if (attrClass == PutAttr)    { httpMethod = "PUT";    route = (string?)attr.ConstructorArguments[0].Value; }
            else if (attrClass == PatchAttr)  { httpMethod = "PATCH";  route = (string?)attr.ConstructorArguments[0].Value; }
            else if (attrClass == DeleteAttr) { httpMethod = "DELETE"; route = (string?)attr.ConstructorArguments[0].Value; }
        }

        if (httpMethod is null || route is null) return null;

        // Parse return type
        var returnType = method.ReturnType as INamedTypeSymbol;
        if (returnType is null) return null;

        bool returnsVoid = false;
        bool returnsApiResponse = false;
        string? innerTypeName = null;
        string returnTypeName = returnType.ToDisplayString();

        // Unwrap Task<T>
        if (returnType.TypeArguments.Length == 1)
        {
            var inner = returnType.TypeArguments[0] as INamedTypeSymbol;
            innerTypeName = inner?.ToDisplayString();
            returnsApiResponse = inner?.OriginalDefinition.ToDisplayString()
                .StartsWith("ZeroAlloc.Rest.ApiResponse") == true;
            if (returnsApiResponse && inner?.TypeArguments.Length == 1)
                innerTypeName = inner.TypeArguments[0].ToDisplayString();
        }
        else
        {
            returnsVoid = true;
        }

        var methodSerializer = GetSerializerType(method);
        var parameters = ExtractParameters(method);

        return new MethodModel(
            method.Name, httpMethod, route, returnTypeName,
            innerTypeName, returnsApiResponse, returnsVoid,
            parameters, methodSerializer);
    }

    private static List<ParameterModel> ExtractParameters(IMethodSymbol method)
    {
        var result = new List<ParameterModel>();
        foreach (var param in method.Parameters)
        {
            var typeName = param.Type.ToDisplayString();

            if (typeName == "System.Threading.CancellationToken")
            {
                result.Add(new ParameterModel(param.Name, typeName, ParameterKind.CancellationToken));
                continue;
            }

            ParameterKind kind = ParameterKind.Path; // default: path segment
            string? headerName = null;
            string? queryName = null;

            foreach (var attr in param.GetAttributes())
            {
                var attrClass = attr.AttributeClass?.ToDisplayString();
                if (attrClass == BodyAttr)   { kind = ParameterKind.Body; break; }
                if (attrClass == QueryAttr)
                {
                    kind = ParameterKind.Query;
                    queryName = attr.NamedArguments
                        .FirstOrDefault(a => a.Key == "Name").Value.Value as string
                        ?? param.Name;
                    break;
                }
                if (attrClass == HeaderAttr)
                {
                    kind = ParameterKind.Header;
                    headerName = (string?)attr.ConstructorArguments[0].Value ?? param.Name;
                    break;
                }
            }

            // If no attribute but name appears in route template, it's a path param
            result.Add(new ParameterModel(param.Name, typeName, kind, headerName, queryName ?? param.Name));
        }
        return result;
    }

    private static string? GetSerializerType(ISymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SerializerAttr);
        return attr?.ConstructorArguments[0].Value is INamedTypeSymbol t
            ? t.ToDisplayString() : null;
    }
}
```

**Step 3: Run model extraction tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "EmitsImplementation|ContainsClassName"
```
Expected: FAIL — emitter still a stub.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ModelExtractor.cs
git commit -m "feat: implement model extraction from interface symbols"
```

---

## Task 7: Source Generator — Code Emission

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`

**Step 1: Implement ClientEmitter**

```csharp
// src/ZeroAlloc.Rest.Generator/ClientEmitter.cs
using Microsoft.CodeAnalysis;
using System.Text;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class ClientEmitter
{
    internal static void Emit(SourceProductionContext ctx, ClientModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Net.Http;");
        sb.AppendLine("using System.Net.Http.Headers;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using ZeroAlloc.Rest;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"public sealed partial class {model.ClassName} : {model.InterfaceName}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly System.Net.Http.HttpClient _httpClient;");
        sb.AppendLine("    private readonly ZeroAlloc.Rest.IRestSerializer _serializer;");
        sb.AppendLine();
        sb.AppendLine($"    public {model.ClassName}(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer)");
        sb.AppendLine("    {");
        sb.AppendLine("        _httpClient = httpClient;");
        sb.AppendLine("        _serializer = serializer;");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var method in model.Methods)
            EmitMethod(sb, method);

        sb.AppendLine("}");

        ctx.AddSource($"{model.InterfaceName}.g.cs", sb.ToString());
    }

    private static void EmitMethod(StringBuilder sb, MethodModel method)
    {
        var ctParam = method.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.CancellationToken);
        var ctArg   = ctParam?.Name ?? "default";
        var pathParams  = method.Parameters.Where(p => p.Kind == ParameterKind.Path).ToList();
        var queryParams = method.Parameters.Where(p => p.Kind == ParameterKind.Query).ToList();
        var bodyParam   = method.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Body);
        var headerParams = method.Parameters.Where(p => p.Kind == ParameterKind.Header).ToList();

        sb.AppendLine($"    public async {method.ReturnTypeName} {method.Name}({BuildParamList(method.Parameters)})");
        sb.AppendLine("    {");

        // Build URL
        EmitUrlBuilding(sb, method.Route, pathParams, queryParams);

        // Build request
        sb.AppendLine($"        using var request = new System.Net.Http.HttpRequestMessage(");
        sb.AppendLine($"            System.Net.Http.HttpMethod.{Capitalize(method.HttpMethod)},");
        sb.AppendLine($"            url);");

        // Set headers
        foreach (var h in headerParams)
            sb.AppendLine($"        request.Headers.TryAddWithoutValidation(\"{h.HeaderName}\", {h.Name}?.ToString());");

        // Serialize body
        if (bodyParam is not null)
        {
            sb.AppendLine($"        var bodyStream = new System.IO.MemoryStream();");
            sb.AppendLine($"        await _serializer.SerializeAsync(bodyStream, {bodyParam.Name}, {ctArg}).ConfigureAwait(false);");
            sb.AppendLine($"        bodyStream.Position = 0;");
            sb.AppendLine($"        request.Content = new System.Net.Http.StreamContent(bodyStream);");
            sb.AppendLine($"        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_serializer.ContentType);");
        }

        // Send
        sb.AppendLine($"        using var response = await _httpClient.SendAsync(request, {ctArg}).ConfigureAwait(false);");

        // Handle response
        if (method.ReturnsVoid)
        {
            sb.AppendLine("        response.EnsureSuccessStatusCode();");
        }
        else if (method.ReturnsApiResponse)
        {
            sb.AppendLine("        var responseStream = await response.Content.ReadAsStreamAsync({ctArg}).ConfigureAwait(false);");
            sb.AppendLine($"        var content = await _serializer.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false);");
            sb.AppendLine("        var headers = response.Headers.ToDictionary(h => h.Key, h => h.Value);");
            sb.AppendLine($"        return new ZeroAlloc.Rest.ApiResponse<{method.InnerTypeName}>(content, response.StatusCode, headers);");
        }
        else
        {
            sb.AppendLine("        response.EnsureSuccessStatusCode();");
            sb.AppendLine($"        var responseStream = await response.Content.ReadAsStreamAsync({ctArg}).ConfigureAwait(false);");
            sb.AppendLine($"        return (await _serializer.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false))!;");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitUrlBuilding(StringBuilder sb, string route,
        List<ParameterModel> pathParams, List<ParameterModel> queryParams)
    {
        if (pathParams.Count == 0 && queryParams.Count == 0)
        {
            sb.AppendLine($"        var url = \"{route}\";");
            return;
        }

        // Replace path params
        var routeExpr = route;
        foreach (var p in pathParams)
            routeExpr = routeExpr.Replace($"{{{p.Name}}}", $"{{Uri.EscapeDataString({p.Name}.ToString()!)}}");

        if (queryParams.Count == 0)
        {
            sb.AppendLine($"        var url = $\"{routeExpr}\";");
        }
        else
        {
            sb.AppendLine($"        var urlBase = $\"{routeExpr}\";");
            sb.AppendLine("        var queryBuilder = new System.Text.StringBuilder(urlBase).Append('?');");
            foreach (var q in queryParams)
                sb.AppendLine($"        queryBuilder.Append(\"{q.QueryName}=\").Append(Uri.EscapeDataString({q.Name}?.ToString() ?? string.Empty)).Append('&');");
            sb.AppendLine("        if (queryBuilder[^1] == '&') queryBuilder.Length--;");
            sb.AppendLine("        var url = queryBuilder.ToString();");
        }
    }

    private static string BuildParamList(IEnumerable<ParameterModel> parameters)
        => string.Join(", ", parameters.Select(p => $"{p.TypeName} {p.Name}"));

    private static string Capitalize(string s)
        => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
```

**Step 2: Run generator tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/
```
Expected: All 3 tests PASS.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs
git commit -m "feat: implement source generator code emission"
```

---

## Task 8: Source Generator — DI Registration Emission

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/RestClientGenerator.cs`
- Create: `src/ZeroAlloc.Rest.Generator/DiEmitter.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiTests.cs`

**Step 1: Write failing test**

```csharp
// tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiTests.cs
public class GeneratorDiTests
{
    [Fact]
    public void Generator_EmitsDiExtensionMethod()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
            }
            """;

        var (_, output) = RunGenerator(source);
        var diFile = output.FirstOrDefault(f => f.HintName == "IUserApi.DI.g.cs");
        Assert.NotNull(diFile);
        Assert.Contains("AddIUserApi", diFile.SourceText.ToString());
    }
    // RunGenerator helper same as GeneratorDiscoveryTests
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "GeneratorDiTests"
```
Expected: FAIL.

**Step 3: Create DiEmitter**

```csharp
// src/ZeroAlloc.Rest.Generator/DiEmitter.cs
using Microsoft.CodeAnalysis;
using System.Text;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class DiEmitter
{
    internal static void Emit(SourceProductionContext ctx, ClientModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using ZeroAlloc.Rest;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine("public static partial class GeneratedRestClientExtensions");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IHttpClientBuilder Add{model.InterfaceName}(");
        sb.AppendLine($"        this IServiceCollection services,");
        sb.AppendLine($"        System.Action<ZeroAlloc.Rest.ZeroAllocClientOptions>? configure = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        var options = new ZeroAlloc.Rest.ZeroAllocClientOptions();");
        sb.AppendLine("        configure?.Invoke(options);");
        sb.AppendLine("        if (options.SerializerType is not null)");
        sb.AppendLine("            services.AddSingleton(typeof(ZeroAlloc.Rest.IRestSerializer), options.SerializerType);");
        sb.AppendLine($"        services.AddTransient<{model.InterfaceName}, {model.ClassName}>();");
        sb.AppendLine($"        return services.AddHttpClient<{model.ClassName}>(client =>");
        sb.AppendLine("        {");
        sb.AppendLine("            if (options.BaseAddress is not null)");
        sb.AppendLine("                client.BaseAddress = options.BaseAddress;");
        sb.AppendLine("        });");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        ctx.AddSource($"{model.InterfaceName}.DI.g.cs", sb.ToString());
    }
}
```

**Step 4: Wire DiEmitter into RestClientGenerator**

In `RestClientGenerator.Initialize`, register a second output:
```csharp
context.RegisterSourceOutput(clientModels, static (ctx, model) =>
{
    ClientEmitter.Emit(ctx, model);
    DiEmitter.Emit(ctx, model);
});
```

**Step 5: Run all generator tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/
```
Expected: All tests PASS.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ tests/ZeroAlloc.Rest.Generator.Tests/
git commit -m "feat: emit DI extension method from source generator"
```

---

## Task 9: System.Text.Json Serializer Adapter

**Files:**
- Create: `src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/Serializers/SystemTextJsonSerializerTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/Serializers/SystemTextJsonSerializerTests.cs
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests.Serializers;

file record TestDto(string Name, int Age);

public class SystemTextJsonSerializerTests
{
    private readonly SystemTextJsonSerializer _sut = new();

    [Fact]
    public void ContentType_IsApplicationJson()
        => Assert.Equal("application/json", _sut.ContentType);

    [Fact]
    public async Task RoundTrip_SerializeDeserialize()
    {
        var dto = new TestDto("Alice", 30);
        var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task Deserialize_ReturnsNull_ForEmptyStream()
    {
        var stream = new MemoryStream();
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.Null(result);
    }
}
```

Also add `ZeroAlloc.Rest.SystemTextJson` project reference to `ZeroAlloc.Rest.Tests.csproj`.

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "SystemTextJsonSerializerTests"
```
Expected: FAIL.

**Step 3: Implement SystemTextJsonSerializer**

```csharp
// src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.SystemTextJson;

public sealed class SystemTextJsonSerializer : IRestSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonSerializer() : this(new JsonSerializerOptions(JsonSerializerDefaults.Web)) { }

    public SystemTextJsonSerializer(JsonSerializerOptions options)
        => _options = options;

    public string ContentType => "application/json";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.Length == 0) return default;
        return await JsonSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await JsonSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
}
```

**Step 4: Run tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "SystemTextJsonSerializerTests"
```
Expected: All 3 tests PASS.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Rest.SystemTextJson/ tests/ZeroAlloc.Rest.Tests/Serializers/
git commit -m "feat: add System.Text.Json serializer adapter"
```

---

## Task 10: MessagePack Serializer Adapter

**Files:**
- Create: `src/ZeroAlloc.Rest.MessagePack/MessagePackRestSerializer.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/Serializers/MessagePackSerializerTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/Serializers/MessagePackSerializerTests.cs
using MessagePack;
using ZeroAlloc.Rest.MessagePack;

namespace ZeroAlloc.Rest.Tests.Serializers;

[MessagePackObject]
file record TestDto([property: Key(0)] string Name, [property: Key(1)] int Age);

public class MessagePackSerializerTests
{
    private readonly MessagePackRestSerializer _sut = new();

    [Fact]
    public void ContentType_IsMessagePack()
        => Assert.Equal("application/x-msgpack", _sut.ContentType);

    [Fact]
    public async Task RoundTrip_SerializeDeserialize()
    {
        var dto = new TestDto("Bob", 25);
        var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.Equal(dto, result);
    }
}
```

Also add `ZeroAlloc.Rest.MessagePack` project reference to `ZeroAlloc.Rest.Tests.csproj`.

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "MessagePackSerializerTests"
```
Expected: FAIL.

**Step 3: Implement MessagePackRestSerializer**

```csharp
// src/ZeroAlloc.Rest.MessagePack/MessagePackRestSerializer.cs
using MessagePack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MessagePack;

public sealed class MessagePackRestSerializer : IRestSerializer
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackRestSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    public MessagePackRestSerializer(MessagePackSerializerOptions options)
        => _options = options;

    public string ContentType => "application/x-msgpack";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => await MessagePackSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await MessagePackSerializer.SerializeAsync(stream, value, _options, ct).ConfigureAwait(false);
}
```

**Step 4: Run tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "MessagePackSerializerTests"
```
Expected: All 2 tests PASS.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Rest.MessagePack/ tests/ZeroAlloc.Rest.Tests/Serializers/
git commit -m "feat: add MessagePack serializer adapter"
```

---

## Task 11: MemoryPack Serializer Adapter

**Files:**
- Create: `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/Serializers/MemoryPackSerializerTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/Serializers/MemoryPackSerializerTests.cs
using MemoryPack;
using ZeroAlloc.Rest.MemoryPack;

namespace ZeroAlloc.Rest.Tests.Serializers;

[MemoryPackable]
file partial record TestDto(string Name, int Age);

public class MemoryPackSerializerTests
{
    private readonly MemoryPackRestSerializer _sut = new();

    [Fact]
    public void ContentType_IsMemoryPack()
        => Assert.Equal("application/x-memorypack", _sut.ContentType);

    [Fact]
    public async Task RoundTrip_SerializeDeserialize()
    {
        var dto = new TestDto("Carol", 28);
        var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.Equal(dto, result);
    }
}
```

Also add `ZeroAlloc.Rest.MemoryPack` project reference to `ZeroAlloc.Rest.Tests.csproj`.

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "MemoryPackSerializerTests"
```
Expected: FAIL.

**Step 3: Implement MemoryPackRestSerializer**

```csharp
// src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs
using MemoryPack;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.MemoryPack;

public sealed class MemoryPackRestSerializer : IRestSerializer
{
    public string ContentType => "application/x-memorypack";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var bytes = new byte[stream.Length - stream.Position];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return MemoryPackSerializer.Deserialize<T>(bytes);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        var bytes = MemoryPackSerializer.Serialize(value);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }
}
```

**Step 4: Run tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter "MemoryPackSerializerTests"
```
Expected: All 2 tests PASS.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Rest.MemoryPack/ tests/ZeroAlloc.Rest.Tests/Serializers/
git commit -m "feat: add MemoryPack serializer adapter"
```

---

## Task 12: Integration Tests

**Files:**
- Create: `tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs`
- Create: `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs`

**Step 1: Create test interface**

```csharp
// tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

public record UserDto(int Id, string Name);
public record CreateUserRequest(string Name);

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> ListUsersAsync([Query] string? name = null, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}
```

**Step 2: Write integration tests**

```csharp
// tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

public class UserApiTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly IUserApi _client;

    public UserApiTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        services.AddSingleton<ZeroAlloc.Rest.IRestSerializer, SystemTextJsonSerializer>();
        services.AddHttpClient<UserApiClient>(c => c.BaseAddress = new Uri(_server.Url!));
        services.AddTransient<IUserApi, UserApiClient>();
        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<IUserApi>();
    }

    [Fact]
    public async Task GetUser_ReturnsDeserializedUser()
    {
        _server.Given(Request.Create().WithPath("/users/1").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(1, "Alice"))));

        var user = await _client.GetUserAsync(1);
        Assert.Equal(1, user.Id);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task CreateUser_SerializesBody_ReturnsCreatedUser()
    {
        _server.Given(Request.Create().WithPath("/users").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(2, "Bob"))));

        var result = await _client.CreateUserAsync(new CreateUserRequest("Bob"));
        Assert.Equal(2, result.Id);
    }

    [Fact]
    public async Task ListUsers_WithQueryParam_AppendsToUrl()
    {
        _server.Given(Request.Create().WithPath("/users").WithParam("name", "Alice").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new List<UserDto> { new(1, "Alice") })));

        var result = await _client.ListUsersAsync("Alice");
        Assert.Single(result);
    }

    [Fact]
    public async Task DeleteUser_SendsDeleteRequest()
    {
        _server.Given(Request.Create().WithPath("/users/5").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(204));

        await _client.DeleteUserAsync(5); // Should not throw
    }

    public void Dispose() => _server.Dispose();
}
```

**Step 3: Run integration tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Integration.Tests/
```
Expected: All 4 tests PASS. (The source generator will have emitted `UserApiClient` at compile time.)

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Rest.Integration.Tests/
git commit -m "test: add integration tests with WireMock"
```

---

## Task 13: OpenAPI Tools — CLI

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools/Program.cs`
- Create: `src/ZeroAlloc.Rest.Tools/OpenApiInterfaceGenerator.cs`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/OpenApiInterfaceGeneratorTests.cs`
- Create: `tests/ZeroAlloc.Rest.Tools.Tests/ZeroAlloc.Rest.Tools.Tests.csproj`

Add tools tests project to sln:
```bash
mkdir -p tests/ZeroAlloc.Rest.Tools.Tests
dotnet sln add tests/ZeroAlloc.Rest.Tools.Tests/ZeroAlloc.Rest.Tools.Tests.csproj
```

Tools tests csproj:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.OpenApi.Readers" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest.Tools/ZeroAlloc.Rest.Tools.csproj" />
  </ItemGroup>
</Project>
```

**Step 1: Write failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/OpenApiInterfaceGeneratorTests.cs
using ZeroAlloc.Rest.Tools;

namespace ZeroAlloc.Rest.Tools.Tests;

public class OpenApiInterfaceGeneratorTests
{
    private const string MinimalSpec = """
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
        var result = OpenApiInterfaceGenerator.Generate(MinimalSpec, "MyApp", "ITestApi");
        Assert.Contains("[ZeroAllocRestClient]", result);
        Assert.Contains("interface ITestApi", result);
    }

    [Fact]
    public void Generate_ProducesGetMethod()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalSpec, "MyApp", "ITestApi");
        Assert.Contains("[Get(\"/users/{id}\")]", result);
        Assert.Contains("int id", result);
    }

    [Fact]
    public void Generate_ProducesPostMethod()
    {
        var result = OpenApiInterfaceGenerator.Generate(MinimalSpec, "MyApp", "ITestApi");
        Assert.Contains("[Post(\"/users\")]", result);
    }
}
```

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Tools.Tests/
```
Expected: FAIL.

**Step 3: Implement OpenApiInterfaceGenerator**

```csharp
// src/ZeroAlloc.Rest.Tools/OpenApiInterfaceGenerator.cs
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using System.Text;

namespace ZeroAlloc.Rest.Tools;

public static class OpenApiInterfaceGenerator
{
    public static string Generate(string yamlOrJson, string @namespace, string interfaceName)
    {
        var reader = new OpenApiStringReader();
        var document = reader.Read(yamlOrJson, out var diagnostics);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using ZeroAlloc.Rest.Attributes;");
        sb.AppendLine();
        sb.AppendLine($"namespace {@namespace};");
        sb.AppendLine();
        sb.AppendLine("[ZeroAllocRestClient]");
        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                EmitMethod(sb, path, operationType, operation);
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public static async Task<string> GenerateFromUrlAsync(
        string url, string @namespace, string interfaceName,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        var content = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        return Generate(content, @namespace, interfaceName);
    }

    public static async Task<string> GenerateFromFileAsync(
        string filePath, string @namespace, string interfaceName,
        CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return Generate(content, @namespace, interfaceName);
    }

    private static void EmitMethod(StringBuilder sb, string path,
        OperationType operationType, OpenApiOperation operation)
    {
        var httpAttr = operationType switch
        {
            OperationType.Get    => "Get",
            OperationType.Post   => "Post",
            OperationType.Put    => "Put",
            OperationType.Patch  => "Patch",
            OperationType.Delete => "Delete",
            _ => null
        };
        if (httpAttr is null) return;

        sb.AppendLine($"    [{httpAttr}(\"{path}\")]");

        var methodName = ToPascalCase(operation.OperationId ?? $"{httpAttr}{path.Replace("/", "_")}") + "Async";
        var parameters = new List<string>();

        foreach (var param in operation.Parameters ?? [])
        {
            var typeName = MapSchemaType(param.Schema);
            var attr = param.In switch
            {
                ParameterLocation.Query => "[Query] ",
                ParameterLocation.Header => $"[Header(\"{param.Name}\")] ",
                _ => ""
            };
            parameters.Add($"{attr}{typeName} {ToCamelCase(param.Name)}");
        }

        if (operation.RequestBody is not null)
            parameters.Add("[Body] object body");

        parameters.Add("CancellationToken ct = default");

        sb.AppendLine($"    Task<object> {methodName}({string.Join(", ", parameters)});");
        sb.AppendLine();
    }

    private static string MapSchemaType(OpenApiSchema? schema) => schema?.Type switch
    {
        "integer" => "int",
        "number"  => "double",
        "boolean" => "bool",
        _         => "string"
    };

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s[1..];
    }

    private static string ToCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToLower(s[0]) + s[1..];
    }
}
```

**Step 4: Implement CLI Program.cs**

```csharp
// src/ZeroAlloc.Rest.Tools/Program.cs
using System.CommandLine;
using ZeroAlloc.Rest.Tools;

var specOption = new Option<string>("--spec", "Path or URL to OpenAPI spec file") { IsRequired = true };
var namespaceOption = new Option<string>("--namespace", "C# namespace for generated interface") { IsRequired = true };
var outputOption = new Option<string>("--output", "Output .cs file path") { IsRequired = true };
var interfaceOption = new Option<string>("--interface", getDefaultValue: () => "IApiClient", "Interface name");

var generateCommand = new Command("generate", "Generate a ZeroAllocRestClient interface from an OpenAPI spec")
{
    specOption, namespaceOption, outputOption, interfaceOption
};

generateCommand.SetHandler(async (spec, ns, output, iface) =>
{
    string content;
    if (spec.StartsWith("http://") || spec.StartsWith("https://"))
        content = await OpenApiInterfaceGenerator.GenerateFromUrlAsync(spec, ns, iface);
    else
        content = await OpenApiInterfaceGenerator.GenerateFromFileAsync(spec, ns, iface);

    var dir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(output, content);
    Console.WriteLine($"Generated: {output}");
}, specOption, namespaceOption, outputOption, interfaceOption);

var root = new RootCommand("ZeroAlloc.Rest code generation tools") { generateCommand };
return await root.InvokeAsync(args);
```

**Step 5: Run tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Tools.Tests/
```
Expected: All 3 tests PASS.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Rest.Tools/ tests/ZeroAlloc.Rest.Tools.Tests/
git commit -m "feat: add OpenAPI interface generator and dotnet CLI tool"
```

---

## Task 14: MSBuild Task

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools/MSBuild/GenerateRestClientTask.cs`
- Create: `src/ZeroAlloc.Rest.Tools/MSBuild/ZeroAlloc.Rest.Tools.targets`

**Step 1: Create MSBuild task**

```csharp
// src/ZeroAlloc.Rest.Tools/MSBuild/GenerateRestClientTask.cs
using Microsoft.Build.Framework;
using ZeroAlloc.Rest.Tools;
using Task = Microsoft.Build.Utilities.Task;

namespace ZeroAlloc.Rest.Tools.MSBuild;

public sealed class GenerateRestClientTask : Task
{
    [Required] public string Spec    { get; set; } = "";
    [Required] public string Output  { get; set; } = "";
    [Required] public string Namespace { get; set; } = "";
    public string InterfaceName { get; set; } = "IApiClient";

    public override bool Execute()
    {
        try
        {
            string content;
            if (Spec.StartsWith("http://") || Spec.StartsWith("https://"))
                content = OpenApiInterfaceGenerator.GenerateFromUrlAsync(Spec, Namespace, InterfaceName)
                    .GetAwaiter().GetResult();
            else
                content = OpenApiInterfaceGenerator.GenerateFromFileAsync(Spec, Namespace, InterfaceName)
                    .GetAwaiter().GetResult();

            var dir = Path.GetDirectoryName(Output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Output, content);
            Log.LogMessage(MessageImportance.Normal, $"ZeroAlloc.Rest: Generated {Output}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"ZeroAlloc.Rest generation failed: {ex.Message}");
            return false;
        }
    }
}
```

**Step 2: Create .targets file**

```xml
<!-- src/ZeroAlloc.Rest.Tools/MSBuild/ZeroAlloc.Rest.Tools.targets -->
<Project>
  <UsingTask TaskName="ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask"
             AssemblyFile="$(MSBuildThisFileDirectory)../ZeroAlloc.Rest.Tools.dll" />

  <Target Name="GenerateZeroAllocRestClients" BeforeTargets="BeforeBuild">
    <GenerateRestClientTask
        Condition="'%(ZeroAllocApiSpec.Identity)' != ''"
        Spec="%(ZeroAllocApiSpec.Identity)"
        Namespace="%(ZeroAllocApiSpec.Namespace)"
        Output="%(ZeroAllocApiSpec.Output)"
        InterfaceName="%(ZeroAllocApiSpec.InterfaceName)" />
  </Target>
</Project>
```

**Step 3: Add Microsoft.Build.Framework reference to Tools project**

Add to `Directory.Packages.props`:
```xml
<PackageVersion Include="Microsoft.Build.Framework" Version="17.13.0" />
<PackageVersion Include="Microsoft.Build.Utilities.Core" Version="17.13.0" />
```

Add to Tools csproj:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Build.Framework" PrivateAssets="all" />
  <PackageReference Include="Microsoft.Build.Utilities.Core" PrivateAssets="all" />
</ItemGroup>
```

**Step 4: Build and verify everything compiles**

```bash
dotnet build ZeroAlloc.Rest.sln
dotnet test ZeroAlloc.Rest.sln
```
Expected: Build succeeded, all tests PASS.

**Step 5: Final commit**

```bash
git add src/ZeroAlloc.Rest.Tools/MSBuild/
git commit -m "feat: add MSBuild task for OpenAPI code generation"
```

---

## Summary

After all tasks:

```
✅ Task 1:  Solution scaffold (6 src + 3 test projects)
✅ Task 2:  Core attributes
✅ Task 3:  IRestSerializer + ApiResponse<T>
✅ Task 4:  DI registration infrastructure
✅ Task 5:  Source generator scaffold + interface discovery
✅ Task 6:  Source generator model extraction
✅ Task 7:  Source generator code emission
✅ Task 8:  Source generator DI emission
✅ Task 9:  System.Text.Json adapter
✅ Task 10: MessagePack adapter
✅ Task 11: MemoryPack adapter
✅ Task 12: Integration tests
✅ Task 13: OpenAPI CLI tool
✅ Task 14: MSBuild task
```

Run full test suite:
```bash
dotnet test ZeroAlloc.Rest.sln
```
