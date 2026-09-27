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

A token binds only the parameter with exactly its name, case included. A route parameter with no
matching token is never sent. A token with no matching route parameter is sent as literal text,
unless it compiles as C#, such as a token named after a `[Query]` parameter, which fills it without
URL escaping. Both get a
[ZRA005](advanced.md#zra005-route-template-and-route-parameters-do-not-match) warning.

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
to on a pathless method — there is no `{name}` token for it to replace — so it is never sent, and
[ZRA005](advanced.md#zra005-route-template-and-route-parameters-do-not-match) warns about it.
Give the method a route, or bind the parameter with `[Query]`, `[Header]` or `[Body]`.

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
