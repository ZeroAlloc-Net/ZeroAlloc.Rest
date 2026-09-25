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

The generated client returns `Result<T, HttpError>.Success(value)` on a 2xx response whose body it could read. It returns `Result<T, HttpError>.Failure(error)`, and **throws no exception**, when:

| Failure | `Kind` | `StatusCode` | `Headers` | `Exception` |
|---|---|---|---|---|
| The server answers with a non-2xx status | `Status` | The response status | The response headers | `null` |
| The request fails before a response arrives: DNS, connection refused, TLS | `Transport` | `0`, or the status an `HttpRequestException` carries | Empty | The `HttpRequestException` |
| The request times out, for example through `HttpClient.Timeout` | `Timeout` | `0` | Empty | The `OperationCanceledException` or `TaskCanceledException` |
| The body of a 2xx response cannot be deserialized | `Deserialization` | The response status | The response headers | Whatever the serializer threw: a `JsonException`, a `MemoryPackSerializationException`, a `MessagePackSerializationException` and so on |

`HttpError` exposes:

| Property | Type | Description |
|---|---|---|
| `Kind` | `HttpErrorKind` | What went wrong: `Status`, `Transport`, `Timeout` or `Deserialization`. Defaults to `Status`. |
| `StatusCode` | `HttpStatusCode` | Status code of the response, or `0` when none arrived |
| `Headers` | `IReadOnlyDictionary<string, IReadOnlyList<string>>` | Response headers, or empty when no response arrived. Lookups ignore case. |
| `Message` | `string?` | The exception message, or `null` for a `Status` failure |
| `Exception` | `Exception?` | The exception behind a `Transport`, `Timeout` or `Deserialization` failure, or `null` for a `Status` failure |

### What still throws

Only transport, timeout and response-deserialization failures become an `HttpError`. These still throw from a `Result<T, HttpError>` method:

- **Cancellation you asked for.** When the method's `CancellationToken` is cancelled, the `OperationCanceledException` propagates. Cancellation is not an error. Any other cancellation, such as `HttpClient.Timeout`, counts as a `Timeout`. A method without a `CancellationToken` parameter has no caller cancellation, so every cancellation it sees is a `Timeout`.
- **Serializing the request body.** A `[Body]` value the serializer cannot write is a bug in the call, not a failure of the server.
- **Everything else**, such as an argument the client cannot put in the URL, or an exception from your own `DelegatingHandler` that is not an `HttpRequestException`.

Methods that do not return a `Result` throw in every case, as before.

Failures that become an `HttpError` are still traced as failures: the span status is set to `Error` and the request duration is recorded, as for an exception that propagates.

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

Use `[Serializer(typeof(T))]` at the method level. Each override becomes a separate `IRestSerializer` constructor parameter, named after its type, and the generated `Create` resolves the concrete type from DI:

```csharp
[ZeroAllocRestClient]
public interface IMixedApi
{
    [Get("/json-endpoint")]
    Task<DataDto> GetDataAsync(CancellationToken ct = default);  // uses default serializer

    [Post("/binary-upload")]
    [Serializer(typeof(MemoryPackRestSerializer))]
    Task UploadAsync([Body] byte[] payload, CancellationToken ct = default);  // uses MemoryPack

    [Get("/proto-endpoint")]
    [Serializer(typeof(ProtobufSerializer))]
    Task<ProtoDto> GetProtoAsync(CancellationToken ct = default);  // uses Protobuf
}
```

The generated `AddSerializers`, called by `Add{I}` and `AddRestResilience`, registers each override serializer type as a singleton via `TryAddSingleton<T>()`. The override types may be `internal` even when the interface is public, because they never appear in the client's public constructor.

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

The generated `UserApiClient` constructor takes `HttpClient` and `IRestSerializer` directly, plus one `IRestSerializer` parameter per method-level override type, so it works without a DI container. When the interface carries `[Serializer(typeof(T))]`, pass a `T` as the main `IRestSerializer`. Pass each override parameter an instance of the type the method's `[Serializer]` names.

Every generated client also implements `IGeneratedRestClient<TSelf>`, explicitly, so the client's own surface is unchanged. Its static `Create(HttpClient, IServiceProvider)` builds the client with the serializer chosen by the rules in [Serialization](serialization.md#choosing-the-serializer-for-a-client), and its static `AddSerializers(IServiceCollection, ZeroAllocClientOptions)` registers the serializers the client needs. The generated `Add{I}` and `AddRestResilience` both use them. You rarely need to call them yourself.
