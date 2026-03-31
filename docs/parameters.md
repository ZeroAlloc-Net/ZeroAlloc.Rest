---
id: parameters
title: Parameters
slug: /parameters
sidebar_position: 3
description: Query strings, request bodies, headers, and path parameters in ZeroAlloc.Rest.
---

# Parameters

## Path parameters

Named in the route template with `{name}`. The method parameter with the same name is substituted:

```csharp
[Get("/users/{id}")]
Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);
```

## Query parameters

Decorated with `[Query]`. They are appended to the URL as `?name=value`:

```csharp
[Get("/users")]
Task<List<UserDto>> ListUsersAsync([Query] string? name = null, CancellationToken ct = default);
// → GET /users?name=Alice
```

Nullable query parameters (`string?`, `int?`) are omitted from the URL when `null`.

Multiple query parameters:

```csharp
[Get("/products")]
Task<List<ProductDto>> SearchAsync(
    [Query] string? category,
    [Query] int? maxPrice,
    CancellationToken ct = default);
// → GET /products?category=Books&maxPrice=50
```

## Request body

Decorated with `[Body]`. The object is serialized by the configured `IRestSerializer` and sent as the request body with the appropriate `Content-Type`:

```csharp
[Post("/users")]
Task<UserDto> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);
```

Only one `[Body]` parameter per method is supported.

## Header parameters

Decorated with `[Header("Header-Name")]`. The value is added to the request headers:

```csharp
[Get("/secure/resource")]
Task<ResourceDto> GetSecureAsync([Header("X-Api-Key")] string apiKey, CancellationToken ct = default);
```

The header name in the attribute is the exact HTTP header name sent over the wire.

## CancellationToken

Every method should end with `CancellationToken ct = default`. The generator recognises this type by its well-known name and passes it to `HttpClient.SendAsync`.

## Summary table

| Annotation | Where | Notes |
|---|---|---|
| `{name}` in route | URL path segment | URL-encoded automatically |
| `[Query]` | Query string | Nullable → omitted when null |
| `[Body]` | Request body | Serialized by `IRestSerializer` |
| `[Header("Name")]` | Request header | Exact header name required |
| `CancellationToken` | (automatic) | Recognised by type, no attribute needed |
