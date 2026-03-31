---
id: getting-started
title: Getting Started
slug: /getting-started
sidebar_position: 1
description: Install ZeroAlloc.Rest, register the DI extension, and make your first HTTP call.
---

# Getting Started

## Installation

Install the three core packages:

```sh
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.Generator
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

The generator package must be referenced as an analyzer so that the SDK does not add it as a runtime dependency:

```xml
<PackageReference Include="ZeroAlloc.Rest" Version="x.y.z" />
<PackageReference Include="ZeroAlloc.Rest.Generator"
                  Version="x.y.z"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
<PackageReference Include="ZeroAlloc.Rest.SystemTextJson" Version="x.y.z" />
```

## Define an interface

Decorate your interface with `[ZeroAllocRestClient]`. The source generator picks it up and emits a concrete implementation at compile time.

```csharp
using ZeroAlloc.Rest.Attributes;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest request, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}

public record UserDto(int Id, string Name);
public record CreateUserRequest(string Name);
```

## Register in ASP.NET Core or a generic host

The generator also emits an `AddI{InterfaceName}` extension method:

```csharp
// Program.cs
builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
});
```

This registers `IUserApi` as a typed `HttpClient` via `IHttpClientFactory`. The underlying `HttpClient` is managed by the factory's handler lifetime.

## Use the client

```csharp
public class UserService(IUserApi api)
{
    public async Task<UserDto> GetUserAsync(int id, CancellationToken ct = default)
        => await api.GetUserAsync(id, ct);
}
```

## What the generator produces

Given the interface above, the generator writes two files at compile time:

- `IUserApi.g.cs` — `UserApiClient : IUserApi` with typed HTTP calls
- `IUserApi.DI.g.cs` — `AddIUserApi(IServiceCollection, Action<ZeroAllocClientOptions>)` extension

You can inspect the generated code in Visual Studio via **Analyzers → ZeroAlloc.Rest.Generator → Generated files**.

## Next steps

- [Routing](routing.md) — path parameters and route templates
- [Parameters](parameters.md) — query strings, request bodies, and headers
- [Serialization](serialization.md) — plug in System.Text.Json, MemoryPack, or your own serializer
