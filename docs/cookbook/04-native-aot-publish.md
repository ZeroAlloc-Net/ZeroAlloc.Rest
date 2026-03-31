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
dotnet add package ZeroAlloc.Rest.SystemTextJson
dotnet add package Microsoft.Extensions.Http
```

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

## 4. Wire everything up

```csharp
// Program.cs
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);
}

var services = new ServiceCollection();
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://jsonplaceholder.typicode.com");
    options.UseSerializer<SystemTextJsonSerializer>();
});

var provider = services.BuildServiceProvider();
var api = provider.GetRequiredService<IUserApi>();
var user = await api.GetUserAsync(1);
Console.WriteLine($"User: {user.Id} — {user.Name}");
```

## 5. Suppress AOT warnings

After verifying your serializer is AOT-safe, suppress the warnings in your project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);IL2026;IL3050</NoWarn>
</PropertyGroup>
```

> Only suppress after you are confident all serialization paths are reachable by the AOT linker.

## 6. Publish

```sh
dotnet publish -c Release -r linux-x64
# or
dotnet publish -c Release -r win-x64
# or
dotnet publish -c Release -r osx-arm64
```

The output is in `bin/Release/net10.0/linux-x64/publish/`. It is a single native binary.

## 7. Verify

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
