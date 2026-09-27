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

## No path

Every method attribute also has a parameterless form. A missing route means an empty path, so
the request goes to the `HttpClient`'s BaseAddress itself — useful when the base address already
names the resource:

```csharp
services.AddIClient(options => options.BaseAddress = new Uri("https://api.example.com/v2/eval"));

[ZeroAllocRestClient]
public interface IClient
{
    [Post]
    Task<EvaluateResponse> EvaluateAsync([Body] EvaluateRequest body, CancellationToken ct = default);
}
```

`[Post]` sends the request to `https://api.example.com/v2/eval` unchanged — `HttpClient` treats an
empty relative request URI as the base address itself, trailing path segment included.

`[Query]` parameters still work on a pathless method; they are appended after the base address
(`https://api.example.com/v2/eval?x=1`). A route (or implicit path) parameter has nothing to bind
to on a pathless method — there is no `{name}` token for it to replace — so it is silently unused
in the URL, exactly as an unmatched `{name}` token behaves today. Give the method a route if you
need the parameter to appear in the request.

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
