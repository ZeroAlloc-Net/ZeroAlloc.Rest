---
id: advanced
title: Advanced
slug: /advanced
sidebar_position: 10
description: ApiResponse<T>, multiple serializers, CancellationToken, and edge cases.
---

# Advanced

## ApiResponse&lt;T&gt;

When you need the HTTP status code or response headers alongside the body, change the return type from `Task<T>` to `Task<ApiResponse<T>>`:

```csharp
[Get("/users/{id}")]
Task<ApiResponse<UserDto>> GetUserAsync(int id, CancellationToken ct = default);
```

`ApiResponse<T>` carries:

```csharp
public sealed class ApiResponse<T>
{
    public T? Content { get; }
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
}
```

`ApiResponse<T>` does **not** call `EnsureSuccessStatusCode`. You must check `StatusCode` yourself.

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
