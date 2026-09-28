---
id: cookbook-native-aot-publish
title: "04 — Native AOT Publish"
slug: /cookbook/native-aot-publish
sidebar_position: 104
description: Publish a ZeroAlloc.Rest client application as a Native AOT binary.
---

# Recipe 04 — Native AOT Publish

**Goal:** Build a console application that uses `IUserApi` and publish it as a self-contained Native AOT binary with no .NET runtime dependency.

## Prerequisites

- .NET 10 SDK
- Native AOT toolchain:
  - **Linux:** `build-essential` (gcc, binutils)
  - **macOS:** Xcode Command Line Tools
  - **Windows:** Visual Studio Build Tools with C++ desktop workload

## 1. Create the project

```sh
dotnet new console -n AotDemo
cd AotDemo
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.Generator
dotnet add package ZeroAlloc.Rest.DependencyInjection
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

`ZeroAlloc.Rest.DependencyInjection` brings in `Microsoft.Extensions.Http` for `AddIUserApi` and
`IHttpClientFactory`. A console app needs it explicitly — an ASP.NET Core host already references it.

## 2. Configure the project for AOT

In `AotDemo.csproj`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

`InvariantGlobalization=true` reduces binary size by removing ICU data.

## 3. Use a source-generated JSON context

AOT requires a `JsonSerializerContext` instead of reflection-based serialization:

```csharp
using System.Text.Json.Serialization;

[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(CreateUserRequest))]
internal partial class AotJsonContext : JsonSerializerContext { }
```

Pass it to the serializer in the next step. A client generated from an OpenAPI spec already has one;
see [Recipe 02](02-openapi-import.md).

## 4. Wire everything up

```csharp
// Program.cs
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Results;
using ZeroAlloc.Rest;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<Result<UserDto, HttpError>> GetUserAsync(int id, CancellationToken ct = default);
}

var services = new ServiceCollection();
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://jsonplaceholder.typicode.com");
    options.UseSerializer(new SystemTextJsonSerializer(AotJsonContext.Default));
});

var provider = services.BuildServiceProvider();
var api = provider.GetRequiredService<IUserApi>();
var result = await api.GetUserAsync(1);
Console.WriteLine(result.IsSuccess ? $"User: {result.Value.Id} — {result.Value.Name}" : $"Error: {result.Error.Kind}");
```

## 5. Publish

```sh
dotnet publish -c Release -r linux-x64
# or
dotnet publish -c Release -r win-x64
# or
dotnet publish -c Release -r osx-arm64
```

The output is in `bin/Release/net10.0/linux-x64/publish/`. It is a single native binary. The publish
should report no IL2026 or IL3050 warning. If one appears, a serializer is using reflection:
construct it with the context.

## 6. Verify

```sh
./AotDemo
# User: 1 — Leanne Graham
```

Check no .NET runtime is present:

```sh
file AotDemo
# AotDemo: ELF 64-bit LSB pie executable, ...
ldd AotDemo
# libz.so.1, libstdc++.so.6, libm.so.6, libc.so.6 — no libcoreclr
```
