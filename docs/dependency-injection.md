---
id: dependency-injection
title: Dependency Injection
slug: /dependency-injection
sidebar_position: 5
description: Generated AddI* DI extension, IHttpClientFactory integration, and ClientOptions.
---

# Dependency Injection

```sh
dotnet add package ZeroAlloc.Rest.DependencyInjection
```

`ZeroAlloc.Rest` generates the client and its constructor on its own. `ZeroAlloc.Rest.DependencyInjection`
adds everything on this page: the generated `Add{I}` extension, `AddRestSerializer`, `UseSerializer`
and keyed per-client serializers. The generator emits `Add{I}` and the client's
`IGeneratedRestClient<TSelf>` members only when this package is referenced; without it, use the
client's constructor directly — see [Without dependency injection](#without-dependency-injection).

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

`MemoryPackRestSerializer` needs one: it serves only the `[MemoryPackable]` types registered with it, plus MemoryPack's built-in types such as `int`, `string` or `byte[]`. The instance `TryAddSingleton` activates has no registered types, so the first call with a `[MemoryPackable]` type throws `InvalidOperationException` naming the type and the fix. Register a configured instance before `AddI{Interface}`, and `TryAddSingleton` leaves it in place:

```csharp
services.AddSingleton(new MemoryPackRestSerializer(types => types.Add<UploadDto>()));
services.AddIUploadApi(options => options.BaseAddress = new Uri("https://api.example.com"));
```

## Without dependency injection

Without a reference to `ZeroAlloc.Rest.DependencyInjection`, the generator emits only the client
class and its constructor — no `Add{I}`, no `IGeneratedRestClient<TSelf>` members. Build the client
directly:

```csharp
var httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
var serializer = new SystemTextJsonSerializer(AppJsonContext.Default);
var client = new UserApiClient(httpClient, serializer);
```

The constructor takes, in order: the `HttpClient`, the `IRestSerializer`, one `IRestSerializer` per
distinct method-level `[Serializer]` type, then one `IHttpErrorMapper<E>` per mapped error type. This
is the same constructor the DI package's generated `Add{I}` calls internally, so a library that ships
a client this way pulls in nothing beyond `ZeroAlloc.Rest`, `ZeroAlloc.Results` and
`ZeroAlloc.Collections` — no Microsoft.Extensions.Http. See
[Long-running clients outside DI](advanced.md#long-running-clients-outside-di).
