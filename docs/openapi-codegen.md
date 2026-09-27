---
id: openapi-codegen
title: OpenAPI Code Generation
slug: /openapi-codegen
sidebar_position: 7
description: Generate ZeroAllocRestClient interfaces from OpenAPI 3.x specs with the CLI or the MSBuild task.
---

# OpenAPI Code Generation

Two packages generate a `[ZeroAllocRestClient]` interface from an OpenAPI 3.x specification, and share
the same generator:

| Package | Kind | Use it for |
|---|---|---|
| `ZeroAlloc.Rest.Tools` | .NET tool, command `zeroalloc` | Generating from the command line or a script |
| `ZeroAlloc.Rest.Tools.MSBuild` | MSBuild task, development dependency | Generating on every build from `<ZeroAllocApiSpec>` items |

## Installation

```sh
# The CLI
dotnet tool install --global ZeroAlloc.Rest.Tools

# The MSBuild task, in the project that uses the generated interface
dotnet add package ZeroAlloc.Rest.Tools.MSBuild
```

`ZeroAlloc.Rest.Tools` is a .NET tool, so NuGet does not accept it as a `PackageReference`.

## CLI

```sh
zeroalloc generate --spec openapi.yaml --namespace MyApp --interface IMyApi --output Generated/IMyApi.g.cs
```

`--spec` also takes an `http(s)://` URL. `--interface` defaults to `IApiClient`.

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
- a name that is not a valid C# identifier is made into one: `user-id` becomes `userId`, a keyword such as `class` is escaped as `@class`, and a name that clashes with another parameter, `body` or `ct` gets a numeric suffix
- `parameters[in=query]` → `[Query] T name`, or `[Query(Name = "page-size")] T pageSize` when the C# name differs from the wire name
- `parameters[in=header]` → `[Header("Name")] string name`
- `parameters[in=path]` → plain `T name`; the route's `{token}` is rewritten to the same C# name, so `/users/{User-Id}` becomes `/users/{userId}` with `int userId`
- `parameters[in=cookie]` → not emitted. ZeroAlloc.Rest has no cookie binding, so the generated method carries a comment, and the CLI and the MSBuild task report warning [ZRT001](advanced.md#zrt001-cookie-parameter-not-emitted). Send cookies from the `HttpClient`, for example with a `CookieContainer` on its handler.
- parameters declared on the path item apply to each of its operations, unless the operation overrides them
- `requestBody` → `[Body] object body`
- `responses[2xx]` → `Task<ReturnType>` (resolves `$ref`, maps arrays to `List<T>`)

## MSBuild task

For automatic generation as part of your build, reference `ZeroAlloc.Rest.Tools.MSBuild` and add
`<ZeroAllocApiSpec>` items to your project:

```xml
<ItemGroup>
  <PackageReference Include="ZeroAlloc.Rest.Tools.MSBuild" Version="x.y.z" />
</ItemGroup>
<ItemGroup>
  <ZeroAllocApiSpec
      Include="openapi.yaml"
      Namespace="MyApp"
      InterfaceName="IMyApi"
      OutputPath="$(MSBuildProjectDirectory)/Generated/IMyApi.g.cs" />
</ItemGroup>
```

The package imports its targets through NuGet's MSBuild integration. Its
`GenerateZeroAllocRestClients` target runs before `CoreCompile`, writes each interface and adds it to
the compilation, so the ZeroAlloc.Rest source generator emits the client for it in the same build.
The file is rewritten only when its content changes. Supported metadata:

| Metadata | Required | Description |
|---|---|---|
| `Include` | Yes | Path to a `.yaml`/`.json` file, or an `http(s)://` URL |
| `Namespace` | Yes | C# namespace for the generated interface |
| `OutputPath` | Yes | Path to write the generated `.cs` file, relative to the project directory or absolute |
| `InterfaceName` | No | Interface name (default: `IApiClient`) |

The metadata is `OutputPath`, not `Output`: MSBuild reserves `Output` as an item metadata name.

The package is a development dependency, so it does not flow to projects that reference yours. The
task runs under the .NET MSBuild that `dotnet build` uses.

## Warnings

When the spec has something the interface cannot express, generation still succeeds and reports a
warning with a `ZRT` code, listed under [Diagnostics](advanced.md#openapi-code-generation-zrt-diagnostics).
The MSBuild task logs it against the spec file, and `<NoWarn>` in the project suppresses it. The
`zeroalloc generate` CLI writes it to stderr, and `--nowarn ZRT001` suppresses it there.

## Workflow recommendation

1. Add the OpenAPI spec to your repo as `openapi.yaml`
2. Add `<ZeroAllocApiSpec>` to your project file
3. Add the generated output path to `.gitignore` (it is regenerated on every build)
4. After generation, register the interface in DI and point `UseSerializer<T>()` at your serializer

The Roslyn source generator then picks up the generated interface and emits the `HttpClient` implementation.
