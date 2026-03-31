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
public static IServiceCollection AddIUserApi(
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
| `UseSerializer<T>()` | Set the default `IRestSerializer` implementation |

## IHttpClientFactory integration

Under the hood, the generated extension calls `AddHttpClient<IUserApi, UserApiClient>()`. This means:
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

## Per-method serializer overrides in DI

When a method carries `[Serializer(typeof(T))]`, the DI emitter registers `T` as a singleton automatically:

```csharp
// Generated for IUploadApi:
services.TryAddSingleton<MemoryPackSerializer>();
```

No manual registration needed.
