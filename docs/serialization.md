---
id: serialization
title: Serialization
slug: /serialization
sidebar_position: 4
description: IRestSerializer, built-in adapters, and per-method serializer overrides.
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
options.UseSerializer<MemoryPackSerializer>();
```

Content-Type: `application/x-memorypack`. Both endpoints must understand MemoryPack encoding.

### MessagePack

```sh
dotnet add package ZeroAlloc.Rest.MessagePack
```

```csharp
options.UseSerializer<MessagePackSerializer>();
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

Register it:

```csharp
options.UseSerializer<MySerializer>();
```

## Per-method serializer override

When one endpoint speaks a different protocol, annotate that specific method:

```csharp
[ZeroAllocRestClient]
public interface IUploadApi
{
    [Post("/upload")]
    [Serializer(typeof(MemoryPackSerializer))]
    Task UploadAsync([Body] byte[] data, CancellationToken ct = default);

    [Get("/status")]
    Task<StatusDto> GetStatusAsync(CancellationToken ct = default);  // uses default serializer
}
```

The generator injects `MemoryPackSerializer` as a constructor parameter and uses it only for `UploadAsync`. The DI emitter also registers `TryAddSingleton<MemoryPackSerializer>` automatically.
