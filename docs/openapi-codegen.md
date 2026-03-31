---
id: openapi-codegen
title: OpenAPI Code Generation
slug: /openapi-codegen
sidebar_position: 7
description: Generate ZeroAllocRestClient interfaces from OpenAPI 3.x specs via API or MSBuild task.
---

# OpenAPI Code Generation

`ZeroAlloc.Rest.Tools` provides two ways to generate a `[ZeroAllocRestClient]` interface from an OpenAPI 3.x specification.

## Installation

```sh
dotnet add package ZeroAlloc.Rest.Tools
```

## C# API

Use `OpenApiInterfaceGenerator` directly in code or in a build script:

```csharp
using ZeroAlloc.Rest.Tools;

// From a YAML/JSON string
string code = OpenApiInterfaceGenerator.Generate(yamlOrJson, "MyApp", "IMyApi");

// From a file
string code = OpenApiInterfaceGenerator.GenerateFromFile("openapi.yaml", "MyApp", "IMyApi");

// From a URL
string code = await OpenApiInterfaceGenerator.GenerateFromUrlAsync(
    "https://api.example.com/openapi.json", "MyApp", "IMyApi");
```

The generator maps:
- `operationId` → method name (PascalCase, suffixed with `Async`)
- `parameters[in=query]` → `[Query] T name`
- `parameters[in=header]` → `[Header("Name")] string name`
- `parameters[in=path]` → plain `T name` (matched by route template)
- `requestBody` → `[Body] object body`
- `responses[2xx]` → `Task<ReturnType>` (resolves `$ref`, maps arrays to `List<T>`)

## MSBuild task

For automatic generation as part of your build, add `<ZeroAllocApiSpec>` items to your project:

```xml
<ItemGroup>
  <ZeroAllocApiSpec
      Include="openapi.yaml"
      Namespace="MyApp"
      InterfaceName="IMyApi"
      Output="$(MSBuildProjectDirectory)/Generated/IMyApi.g.cs" />
</ItemGroup>
```

The `GenerateZeroAllocRestClients` target runs before `BeforeBuild`. Supported properties:

| Property | Required | Description |
|---|---|---|
| `Include` | Yes | Path to a `.yaml`/`.json` file, or an `http(s)://` URL |
| `Namespace` | Yes | C# namespace for the generated interface |
| `Output` | Yes | Path to write the generated `.cs` file |
| `InterfaceName` | No | Interface name (default: `IApiClient`) |

The targets file is automatically imported via NuGet's MSBuild integration when you reference `ZeroAlloc.Rest.Tools`.

## Workflow recommendation

1. Add the OpenAPI spec to your repo as `openapi.yaml`
2. Add `<ZeroAllocApiSpec>` to your project file
3. Add the generated output path to `.gitignore` (it is regenerated on every build)
4. After generation, register the interface in DI and point `UseSerializer<T>()` at your serializer

The Roslyn source generator then picks up the generated interface and emits the `HttpClient` implementation.
