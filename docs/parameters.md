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

Strongly-typed identifiers from [`ZeroAlloc.ValueObjects`](https://www.nuget.org/packages/ZeroAlloc.ValueObjects) `[TypedId]` are also supported as path or query parameters — the generator calls `id.ToString()` on the typed wrapper, which produces the strategy-specific string format (ULID base32 by default). See the [`Strongly-typed IDs` cookbook entry](cookbook/05-typed-ids.md) for a worked example.

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
| `[Header("Name", Value = "...")]` on method | Static request header | Compile-time constant; silently ignored when `Value` is omitted |
| `[Query]` on `IEnumerable<T>` | Repeated query keys | Null items skipped; null collection emits nothing |
| `[FormBody]` | Form-encoded body | `IEnumerable<KeyValuePair<string,string>>`; no serializer used |

### Static headers on methods

Use `[Header("Name", Value = "literal")]` on a method to emit a compile-time header. The header is added to every request for that method, independent of the serializer.

```csharp
[Get("/files/{id}")]
[Header("Accept", Value = "application/octet-stream")]
Task<byte[]> GetFileAsync(int id, CancellationToken ct = default);
```

> **Note:** Static headers are *additive*. If you set `Accept` via `[Header]` and the serializer also sets `Accept`, both values appear in the outgoing header. Use `ConfigureHttpClient` if you need exclusive control over `Accept`.

If `Value` is omitted on a method-level `[Header]`, the attribute is silently ignored.

### Multi-value query parameters

Annotate an `IEnumerable<T>` parameter with `[Query]` to emit repeated query string keys (`?tags=a&tags=b`).

```csharp
[Get("/items")]
Task<List<ItemDto>> SearchAsync([Query] IEnumerable<string>? tags, CancellationToken ct = default);
// ?tags=admin&tags=active
```

Null items inside the collection are skipped. A `null` collection emits no query keys. The parameter type must implement `IEnumerable<T>` (e.g. `List<T>`, `string[]`, `IReadOnlyList<T>`). Passing `string` is treated as a scalar (strings are `IEnumerable<char>` but that case is excluded).

### Form-encoded body

Use `[FormBody]` to send `application/x-www-form-urlencoded` content without a serializer. The parameter type must be `IEnumerable<KeyValuePair<string, string>>` (e.g. `Dictionary<string, string>`).

```csharp
[Post("/oauth/token")]
Task<TokenResponse> GetTokenAsync([FormBody] Dictionary<string, string> form, CancellationToken ct = default);
```

```csharp
var token = await api.GetTokenAsync(new Dictionary<string, string>
{
    ["grant_type"] = "client_credentials",
    ["client_id"] = "my-app"
});
```

`[FormBody]` and `[Body]` are mutually exclusive on the same method. Using both produces a `ZRA001` compile-time error.
