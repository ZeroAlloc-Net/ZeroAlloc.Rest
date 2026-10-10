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

### Catch-all tokens

A `{name}` token escapes its whole value, so a `/` in it becomes `%2F`. Two prefixes change that.
Each binds the route parameter called `name`, whatever the prefix:

| Token | Value `"v1/system one"` is sent as | Use it for |
|-------|-----------------------------------|------------|
| `{name}` | `v1%2Fsystem%20one` | One path segment |
| `{*name}` | `v1%2Fsystem%20one` | The same as `{name}`, as ASP.NET Core link generation does it |
| `{**name}` | `v1/system%20one` | A multi-segment path that keeps its `/` separators |

```csharp
[Get("/files/{**path}")]
Task<Stream> DownloadAsync(string path, CancellationToken ct = default);

// DownloadAsync("v1/system one")  ->  GET /files/v1/system%20one
```

`{**name}` splits the value on `/`, escapes each segment with `Uri.EscapeDataString`, and joins them
with `/` again. A `?`, `#` or `%` inside a segment is escaped, so the value cannot reach the query
string. The separators are kept as written: a leading `/` and an empty segment, as in `a//b`, are
sent unchanged. So with the route `{**path}` alone, the value `/v1/x` makes the request URI `/v1/x`,
which replaces any path in the client's `BaseAddress`, while `v1/x` is appended to it. With
`files/{**path}`, the value `/v1/x` is sent as `files//v1/x`: leave the leading `/` out of the value.

A `{**name}` or `{*name}` token with no matching route parameter is sent as literal text, with the
same [ZRA005](advanced.md#zra005-route-template-and-route-parameters-do-not-match) warning as any
other unmatched token.

## No path

Every method attribute also has a parameterless form. A missing route means an empty path, so
the request goes to the `HttpClient`'s BaseAddress itself — useful when the base address already
names the resource. `AddIClient` below comes from `ZeroAlloc.Rest.DependencyInjection`; see
[Dependency Injection](dependency-injection.md):

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

The base address is set when registering the client in DI, through the generated `Add{I}` extension:

```csharp
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com/v2");
});
```

The route from the attribute is appended to the base address by the underlying `HttpClient`.
