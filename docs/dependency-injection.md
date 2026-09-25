---
id: dependency-injection
title: Dependency Injection
slug: /dependency-injection
sidebar_position: 5
description: Generated AddI* DI extension, IHttpClientFactory integration, and ClientOptions.
---

# Dependency Injection

## Generated extension method

For every interface annotated with `[ZeroAllocRestClient]`, the generator emits an `AddI{InterfaceName}` extension on `IServiceCollection`:

```csharp
// Generated for IUserApi in namespace MyApp:
public static IHttpClientBuilder AddIUserApi(
    this IServiceCollection services,
    Action<ZeroAllocClientOptions>? configure = null)
```

Usage:

```csharp
builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
});
```

## ZeroAllocClientOptions

| Property / Method | Description |
|---|---|
| `BaseAddress` | Base URI for the `HttpClient` |
| `UseSerializer<T>()` | Use `T` as this client's serializer. It is registered as a keyed singleton under the client interface |
| `UseSerializer(instance)` | Use this exact instance as this client's serializer |

`UseSerializer` applies to one client only. To share one serializer across clients, register an app-wide default with `services.AddRestSerializer<T>()` or `services.AddRestSerializer(instance)`. See [Serialization](serialization.md#choosing-the-serializer-for-a-client) for the full precedence rules.

## IHttpClientFactory integration

Under the hood, the generated extension registers a named `HttpClient` called `IUserApi`, plus a typed-client factory that calls `UserApiClient`'s static `IGeneratedRestClient<UserApiClient>.Create`. That method passes in the client's serializer, with no reflection or `ActivatorUtilities`. This means:
- The `HttpClient` is managed by `IHttpClientFactory` with proper handler lifetime rotation
- You can further configure the named client via the returned `IHttpClientBuilder`:

```csharp
builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
})
.AddHttpMessageHandler<LoggingHandler>()
.AddPolicyHandler(retryPolicy);
```

## Serializer overrides in DI

When the interface or a method carries `[Serializer(typeof(T))]`, `T` is registered as a singleton automatically. The generated client's explicit `IGeneratedRestClient<TSelf>.AddSerializers` does it, and both `AddI{Interface}()` and `AddRestResilience` call that method:

```csharp
// Generated in UploadApiClient for IUploadApi:
static void global::ZeroAlloc.Rest.IGeneratedRestClient<UploadApiClient>.AddSerializers(
    IServiceCollection services, ZeroAllocClientOptions options)
{
    global::ZeroAlloc.Rest.GeneratedRestClient.AddPerClientSerializer<IUploadApi>(services, options);
    ServiceCollectionDescriptorExtensions.TryAddSingleton<global::ZeroAlloc.Rest.MemoryPack.MemoryPackRestSerializer>(services);
}
```

No manual registration is needed. The real output spells every type name out in full.
