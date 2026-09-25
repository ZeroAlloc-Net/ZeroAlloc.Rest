---
id: serialization
title: Serialization
slug: /serialization
sidebar_position: 4
description: IRestSerializer, built-in adapters, per-client and app-wide serializers, and interface- and method-level overrides.
---

# Serialization

## IRestSerializer

All serialization goes through `IRestSerializer`:

```csharp
public interface IRestSerializer
{
    string ContentType { get; }

    [RequiresDynamicCode("...")]
    [RequiresUnreferencedCode("...")]
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);

    [RequiresDynamicCode("...")]
    [RequiresUnreferencedCode("...")]
    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default);
}
```

The `ContentType` property controls both the `Content-Type` header on requests and the `Accept` header.

## Choosing the serializer for a client

Each generated client picks its serializer in this order. The first match wins:

1. **Interface-level `[Serializer(typeof(T))]`.** It is fixed at compile time. The generated client resolves `T` from DI, and the generated `Add{I}` registers `T` with `TryAddSingleton<T>()`. The app-wide default does not apply to such a client, and calling `UseSerializer` for it throws an `InvalidOperationException` at registration, naming the interface and the attribute's type.
2. **Per-client `UseSerializer`** in that client's `Add{I}` options. It is registered as a keyed singleton under the client interface, so it applies to that client only. This needs a container whose `IServiceProvider` implements `IKeyedServiceProvider`; see [Migrating to 2.0](migrating-to-v2.md#per-client-serializers-need-keyed-services).
3. **The app-wide `IRestSerializer`**, which you register with `services.AddRestSerializer<T>()`, `services.AddRestSerializer(instance)`, or your own `IRestSerializer` registration.

A client with none of these fails when it is resolved, with an `InvalidOperationException` that names the client interface. Hand-written clients and registrations can apply the same rules with `services.GetRequiredRestSerializer<TClient>()`:

```text
No IRestSerializer is configured for the REST client 'MyApp.IUserApi'. Call options.UseSerializer<T>()
or options.UseSerializer(instance) when registering this client, or register an app-wide default with
services.AddRestSerializer<T>().
```

A method-level `[Serializer]` overrides the choice above for that method only; see [Per-method serializer override](#per-method-serializer-override).

### Per-client serializer

```csharp
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
});
```

`UseSerializer` affects only `IUserApi`. It never registers the app-wide `IRestSerializer`, so another client, or the host application, cannot pick it up or replace it, whatever the registration order. This is what a library that ships a ZeroAlloc.Rest client should use.

For a serializer that needs constructor arguments, pass an instance. The client uses that exact instance:

```csharp
var serializer = new SystemTextJsonSerializer(JevJson.Options);
services.AddIJevApi(options =>
{
    options.BaseAddress = new Uri("https://api.typesafe.ai/");
    options.UseSerializer(serializer);
});
```

The last `UseSerializer` call on an options object wins.

### App-wide default

For an application with several clients that share one serializer, register it once:

```csharp
services.AddRestSerializer<SystemTextJsonSerializer>();
// or, for a configured instance:
services.AddRestSerializer(new SystemTextJsonSerializer(MyJson.Options));

services.AddIUserApi(o => o.BaseAddress = new Uri("https://users.example.com"));
services.AddIOrderApi(o => o.BaseAddress = new Uri("https://orders.example.com"));
```

The last app-wide registration wins. Before 2.0, `UseSerializer` on any client also became the app-wide default; see [Migrating to 2.0](migrating-to-v2.md).

### Interface-level serializer

```csharp
[ZeroAllocRestClient]
[Serializer(typeof(JevSerializer))]
public interface IJevApi
{
    [Get("/answers/{id}")]
    Task<Answer> GetAnswerAsync(int id, CancellationToken ct = default);
}
```

`AddIJevApi` registers `JevSerializer` as a singleton, and the generated client resolves it from DI and uses it. DI activates it, so it needs a public constructor that DI can satisfy. The generated constructor still takes `IRestSerializer`, so `JevSerializer` may be `internal` while `IJevApi` is public. For a serializer that needs configuration, drop the attribute and use a per-client `UseSerializer(instance)` instead. Using both is rejected at registration.

> **Library authors: use a dedicated serializer type.** The attribute's type is registered with `TryAddSingleton<T>()`, keyed only by the type. If the host or another library registers the same concrete type, for example `SystemTextJsonSerializer`, every client resolves that one instance, with whatever options it was built with. Declare your own type, such as an internal `sealed class JevSerializer : IRestSerializer`, so no one else can register or configure it.

### With the Resilience bridge

`AddRestResilience` applies the same order. See [Resilience: serializer selection](resilience.md#serializer-selection).

## Built-in serializers

### System.Text.Json

```sh
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

```csharp
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
});
```

Uses `JsonSerializerDefaults.Web` (camelCase, case-insensitive). Content-Type: `application/json`.

### MemoryPack

```sh
dotnet add package ZeroAlloc.Rest.MemoryPack
```

```csharp
options.UseSerializer<MemoryPackRestSerializer>();
```

Content-Type: `application/x-memorypack`. Both endpoints must understand MemoryPack encoding.

### MessagePack

```sh
dotnet add package ZeroAlloc.Rest.MessagePack
```

```csharp
options.UseSerializer<MessagePackRestSerializer>();
```

Content-Type: `application/x-msgpack`.

## Custom serializer

Implement `IRestSerializer`:

```csharp
public sealed class MySerializer : IRestSerializer
{
    public string ContentType => "application/json";

    [RequiresDynamicCode("Serialization may require dynamic code.")]
    [RequiresUnreferencedCode("Serialization may require unreferenced code.")]
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
    }

    [RequiresDynamicCode("Serialization may require dynamic code.")]
    [RequiresUnreferencedCode("Serialization may require unreferenced code.")]
    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: ct);
    }
}
```

Register it for one client, or as the app-wide default:

```csharp
options.UseSerializer<MySerializer>();       // this client only
services.AddRestSerializer<MySerializer>();  // every client without its own
```

## Per-method serializer override

When one endpoint speaks a different protocol, annotate that specific method:

```csharp
[ZeroAllocRestClient]
public interface IUploadApi
{
    [Post("/upload")]
    [Serializer(typeof(MemoryPackRestSerializer))]
    Task UploadAsync([Body] byte[] data, CancellationToken ct = default);

    [Get("/status")]
    Task<StatusDto> GetStatusAsync(CancellationToken ct = default);  // uses the client's serializer
}
```

The generated client resolves `MemoryPackRestSerializer` from DI and uses it only for `UploadAsync`. Registration also runs `TryAddSingleton<MemoryPackRestSerializer>` automatically. The override type may be `internal` even when the interface is public.
