---
id: parameters
title: Parameters
slug: /parameters
sidebar_position: 3
description: Query strings, request bodies, headers, and path parameters in ZeroAlloc.Rest.
---

# Parameters

## Path parameters

Named in the route template with `{name}`. The method parameter with the same name is substituted.
A parameter with no attribute is a path parameter; if the route has no `{name}` token for it, it is
never sent and [ZRA005](advanced.md#zra005-route-template-and-route-parameters-do-not-match) warns:

```csharp
[Get("/users/{id}")]
Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);
```

Strongly-typed identifiers from [`ZeroAlloc.ValueObjects`](https://www.nuget.org/packages/ZeroAlloc.ValueObjects) `[TypedId]` are also supported as path or query parameters — the generator formats the typed wrapper the same as any other [route, query or header value](#value-formatting); a `[TypedId]` wrapper implements no `IFormattable`, so that is its own `ToString()`, which produces the strategy-specific string format (ULID base32 by default). See the [`Strongly-typed IDs` cookbook entry](cookbook/05-typed-ids.md) for a worked example.

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

The serializer writes the body into a buffer rented from `ArrayPool<byte>.Shared`, and the request sends it with its `Content-Length`. Disposing the request clears the buffer and returns it to the pool, also when the send fails or is cancelled. A send that is still copying the body at that moment, such as an HTTP/2 upload the server answered early, keeps the buffer until the copy ends. The serializer gets a seekable stream, so a serializer that seeks or reads back what it wrote works as it did with a `MemoryStream`.

`ContentType` on `[Body]` sets the media type the body is sent with, in place of the serializer's:

```csharp
[Patch("/users/{id}")]
Task PatchUserAsync(int id, [Body(ContentType = "application/merge-patch+json")] UserPatch body, CancellationToken ct = default);
```

A `Stream` body, or one of a type derived from `Stream` such as `FileStream`, is not serialized: it
is sent as it is, as `application/octet-stream` unless `ContentType` says otherwise. See
[Raw Stream bodies](advanced.md#raw-stream-bodies).

## Header parameters

Decorated with `[Header("Header-Name")]`. The value is added to the request headers:

```csharp
[Get("/secure/resource")]
Task<ResourceDto> GetSecureAsync([Header("X-Api-Key")] string apiKey, CancellationToken ct = default);
```

The header name in the attribute is the exact HTTP header name sent over the wire.

A header parameter that is `null` is left out of the request, the way a nullable `[Query]` parameter is. Use a nullable type such as `string?` or `int?` for an optional header:

```csharp
[Post("/events")]
Task PublishAsync([Body] Event body, [Header("X-Retry-Count")] int? retryCount, CancellationToken ct = default);
```

Passing `null` sends no `X-Retry-Count` header at all, not an empty one. An empty string is a value, so `""` still sends the header with an empty value.

## Value formatting

Every route, query and header value is written with `CultureInfo.InvariantCulture`, not the current culture:

| Type | Written as |
|---|---|
| `bool` | `true` or `false` |
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` | ISO 8601, the `"O"` format |
| An enum member | its `[JsonStringEnumMemberName]`, or its C# name |
| A `[Flags]` combination | the member names System.Text.Json writes for it, joined with `, ` |
| An enum value no member or combination names | its underlying number |
| A type implementing `IFormattable`, including explicitly | `value.ToString(null, CultureInfo.InvariantCulture)` |
| Any other type | `value.ToString()` |

A `null` nullable `[Header]` parameter sends no header at all, as described above; a `null` nullable `[Query]` parameter is omitted the same way.

## CancellationToken

Every method should end with `CancellationToken ct = default`. The generator recognises this type by its well-known name and passes it to `HttpClient.SendAsync`.

## Summary table

| Annotation | Where | Notes |
|---|---|---|
| `{name}` in route | URL path segment | URL-encoded automatically |
| `[Query]` | Query string | Nullable → omitted when null |
| `[Body]` | Request body | Serialized by `IRestSerializer`; a `Stream` is sent as it is. `ContentType` sets its media type |
| `[Header("Name")]` | Request header | Exact header name required; omitted when null |
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

`[FormBody]` and `[Body]` are mutually exclusive on the same method. Using both produces a [`ZRA001`](advanced.md#zra001-conflicting-body-attributes) compile-time error.
