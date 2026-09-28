---
id: getting-started
title: Getting Started
slug: /
sidebar_position: 1
description: Install ZeroAlloc.Rest, register the DI extension, and make your first HTTP call.
---

# Getting Started

## Installation

Install the core packages. `ZeroAlloc.Rest` bundles the source generator, so no separate analyzer
reference is needed:

```sh
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.DependencyInjection
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

Or via `<PackageReference>`:

```xml
<PackageReference Include="ZeroAlloc.Rest" Version="x.y.z" />
<PackageReference Include="ZeroAlloc.Rest.DependencyInjection" Version="x.y.z" />
<PackageReference Include="ZeroAlloc.Rest.SystemTextJson" Version="x.y.z" />
```

`ZeroAlloc.Rest.DependencyInjection` is what generates the `AddI{Interface}` extension used below and
adds `Microsoft.Extensions.Http`. Skip it if you build the client by hand instead; see
[Dependency Injection: Without dependency injection](dependency-injection.md#without-dependency-injection).

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
- `IUserApi.DI.g.cs` — `AddIUserApi(IServiceCollection, Action<ZeroAllocClientOptions>)` extension,
  emitted only when `ZeroAlloc.Rest.DependencyInjection` is referenced

You can inspect the generated code in Visual Studio via **Analyzers → ZeroAlloc.Rest.Generator → Generated files**.

## Next steps

- [Routing](routing.md) — path parameters and route templates
- [Parameters](parameters.md) — query strings, request bodies, and headers
- [Serialization](serialization.md) — plug in System.Text.Json, MemoryPack, or your own serializer
