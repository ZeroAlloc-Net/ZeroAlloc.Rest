---
id: cookbook-crud-web-api
title: "01 — CRUD API Client"
slug: /cookbook/crud-web-api
sidebar_position: 101
description: Build a complete CRUD REST client from scratch with ZeroAlloc.Rest.
---

# Recipe 01 — CRUD API Client

**Goal:** Build a complete Create / Read / Update / Delete client for a users API.

## 1. Install packages

```sh
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.Generator
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

Add the generator as an analyzer in your `.csproj`:

```xml
<PackageReference Include="ZeroAlloc.Rest.Generator"
                  Version="x.y.z"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## 2. Define DTOs

```csharp
public record UserDto(int Id, string Name, string Email);
public record CreateUserRequest(string Name, string Email);
public record UpdateUserRequest(string? Name, string? Email);
```

## 3. Define the interface

```csharp
using ZeroAlloc.Rest.Attributes;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users")]
    Task<List<UserDto>> ListUsersAsync([Query] int? page = null, CancellationToken ct = default);

    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest request, CancellationToken ct = default);

    [Put("/users/{id}")]
    Task<UserDto> UpdateUserAsync(int id, [Body] UpdateUserRequest request, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}
```

## 4. Register in DI

```csharp
// Program.cs
builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri(builder.Configuration["UserApi:BaseUrl"]!);
    options.UseSerializer<SystemTextJsonSerializer>();
});
```

## 5. Inject and use

```csharp
public class UserService(IUserApi api, ILogger<UserService> logger)
{
    public async Task<UserDto?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            return await api.GetUserAsync(id, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("User {Id} not found", id);
            return null;
        }
    }

    public Task<UserDto> CreateAsync(string name, string email, CancellationToken ct = default)
        => api.CreateUserAsync(new CreateUserRequest(name, email), ct);
}
```

## 6. Test with WireMock.Net

See [Testing](../testing.md) for the full pattern. Quick test for the list endpoint:

```csharp
[Fact]
public async Task ListUsers_WithPage_AppendsQueryParam()
{
    _server.Given(Request.Create().WithPath("/users").WithParam("page", "2").UsingGet())
           .RespondWith(Response.Create()
               .WithStatusCode(200)
               .WithHeader("Content-Type", "application/json")
               .WithBody("[]"));

    var result = await _client.ListUsersAsync(page: 2);
    Assert.Empty(result);
}
```
