---
id: routing
title: Routing
slug: /routing
sidebar_position: 2
description: Route templates, path parameters, and how ZeroAlloc.Rest builds request URLs.
---

# Routing

## Basic routes

Each method attribute specifies the HTTP method and route:

```csharp
[Get("/users")]
Task<List<UserDto>> ListUsersAsync(CancellationToken ct = default);

[Post("/users")]
Task<UserDto> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);
```

## Path parameters

Wrap the parameter name in `{` `}` in the route. The method parameter with the same name is automatically bound:

```csharp
[Get("/users/{id}")]
Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

[Delete("/organizations/{orgId}/members/{userId}")]
Task RemoveMemberAsync(int orgId, int userId, CancellationToken ct = default);
```

Path parameters are URL-encoded with `Uri.EscapeDataString` before substitution.

## Supported HTTP methods

| Attribute | HTTP verb |
|---|---|
| `[Get]` | GET |
| `[Post]` | POST |
| `[Put]` | PUT |
| `[Patch]` | PATCH |
| `[Delete]` | DELETE |

## Base address

The base address is set when registering the client in DI:

```csharp
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com/v2");
});
```

The route from the attribute is appended to the base address by the underlying `HttpClient`.
