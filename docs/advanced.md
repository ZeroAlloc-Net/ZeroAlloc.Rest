---
id: advanced
title: Advanced
slug: /advanced
sidebar_position: 10
description: Result<T, HttpError>, multiple serializers, CancellationToken, and edge cases.
---

# Advanced

## Result&lt;T, HttpError&gt;

ZeroAlloc.Rest integrates with [`ZeroAlloc.Results`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) to provide typed error returns without exceptions.

Declare a method with a `Result<T, HttpError>` return type:

```csharp
using ZeroAlloc.Results;
using ZeroAlloc.Rest;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<Result<UserDto, HttpError>> GetUserAsync(int id, CancellationToken ct = default);
}
```

The generated client will:
- Return `Result<T, HttpError>.Success(value)` on a 2xx response
- Return `Result<T, HttpError>.Failure(error)` on any non-2xx response — **no exception is thrown**

`HttpError` exposes:

| Property | Type | Description |
|---|---|---|
| `StatusCode` | `HttpStatusCode` | HTTP status code of the failed response |
| `Headers` | `IReadOnlyDictionary<string, IReadOnlyList<string>>` | Response headers |
| `Message` | `string?` | Optional error message (null by default for HTTP failures) |

Consuming the result:

```csharp
var result = await api.GetUserAsync(42);
if (result.IsSuccess)
{
    Console.WriteLine($"User: {result.Value.Name}");
}
else
{
    Console.WriteLine($"Error: {result.Error.StatusCode}");
}
```

## CancellationToken

Always add `CancellationToken ct = default` as the last parameter. The generator recognises the type by its well-known fully qualified name `System.Threading.CancellationToken` and passes it to `HttpClient.SendAsync`. No attribute is required.

## Multiple serializers on one interface

Use `[Serializer(typeof(T))]` at the method level. Each override is injected as a separate constructor parameter:

```csharp
[ZeroAllocRestClient]
public interface IMixedApi
{
    [Get("/json-endpoint")]
    Task<DataDto> GetDataAsync(CancellationToken ct = default);  // uses default serializer

    [Post("/binary-upload")]
    [Serializer(typeof(MemoryPackSerializer))]
    Task UploadAsync([Body] byte[] payload, CancellationToken ct = default);  // uses MemoryPack

    [Get("/proto-endpoint")]
    [Serializer(typeof(ProtobufSerializer))]
    Task<ProtoDto> GetProtoAsync(CancellationToken ct = default);  // uses Protobuf
}
```

The DI emitter registers each override serializer type as a singleton via `TryAddSingleton<T>()`.

## Void methods (no response body)

Return `Task` (not `Task<T>`) for methods where you only care about success/failure:

```csharp
[Delete("/users/{id}")]
Task DeleteUserAsync(int id, CancellationToken ct = default);
```

The generated code calls `EnsureSuccessStatusCode()` and returns.

## Long-running clients outside DI

If you need a client outside of dependency injection (e.g., in a CLI tool):

```csharp
var httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
var serializer = new SystemTextJsonSerializer();
var client = new UserApiClient(httpClient, serializer);
var user = await client.GetUserAsync(1);
```

The generated `UserApiClient` constructor always takes `HttpClient` and `IRestSerializer` (plus any method-level override serializers) directly, so it works without a DI container.
