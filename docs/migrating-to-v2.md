---
id: migrating-to-v2
title: Migrating to 2.0
slug: /migrating-to-v2
sidebar_position: 11
description: ZeroAlloc.Rest 2.0 makes UseSerializer per client, and Result methods return transport, timeout and deserialization failures instead of throwing them.
---

# Migrating to 2.0

## Who is affected

ZeroAlloc.Rest 2.0 changes how serializers are chosen and registered; see [#301](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/301). You are affected if any of these apply:

1. **A client relies on a serializer that another client's `UseSerializer` provided.** It now fails when it is resolved. See [`UseSerializer` is per client](#useserializer-is-per-client).
2. **You use `UseSerializer` with a container that does not support keyed services**, such as Lamar, Simple Injector, some Autofac or DryIoc adapters, or a wrapping `IServiceProvider`. Resolving that client now fails. See [Per-client serializers need keyed services](#per-client-serializers-need-keyed-services).
3. **An interface carries `[Serializer]`.** The attribute now takes effect. Combined with `UseSerializer` for the same client, registration throws. See [Interface-level `[Serializer]` is now honoured](#interface-level-serializer-is-now-honoured).
4. **You pass a hand-written client to `AddRestResilience`.** It no longer compiles. See [`AddRestResilience` requires a generated client](#addrestresilience-requires-a-generated-client).
5. **You construct a generated client by hand.** Every serializer parameter of the generated constructor is now typed `IRestSerializer`, including one per method-level override type. Calls that pass the concrete serializers still compile. See [Generated constructors take `IRestSerializer`](#generated-constructors-take-irestserializer).
6. **You depend on the shape of the generated code**, for example the `Add{I}` registration or the interfaces the client implements. See [Generated clients implement `IGeneratedRestClient<TSelf>`](#generated-clients-implement-igeneratedrestclienttself) and [The generated `Add{I}` uses a typed-client factory](#the-generated-addi-uses-a-typed-client-factory).
7. **A method returns `Result<T, HttpError>` and you catch exceptions around it**, or rely on `[Retry]` to retry its network failures. Transport failures, timeouts and unreadable response bodies now come back as a failed `Result`. See [Result methods return transport failures](#result-methods-return-transport-failures).

## `UseSerializer` is per client

In 1.x, `options.UseSerializer<T>()` on any generated `Add{I}` also registered `T` as the container-wide `IRestSerializer`, with `TryAddSingleton`. Whichever client registered first set the serializer for every client that didn't set its own. A library that shipped a ZeroAlloc.Rest client could therefore lose its serializer to the host, or impose its serializer on the host's clients, depending on registration order.

In 2.0, `UseSerializer` registers the serializer for that client only. It is a keyed singleton under the client interface. It never registers the app-wide `IRestSerializer`. The `AddRestResilience` bridge behaves the same way.

This breaks a client that relied on another client's registration:

```csharp
// 1.x: IOrderApi silently used SystemTextJsonSerializer from IUserApi's registration.
services.AddIUserApi(o =>
{
    o.BaseAddress = new Uri("https://users.example.com");
    o.UseSerializer<SystemTextJsonSerializer>();
});
services.AddIOrderApi(o => o.BaseAddress = new Uri("https://orders.example.com"));
```

In 2.0, resolving `IOrderApi` throws:

```text
System.InvalidOperationException: No IRestSerializer is configured for the REST client 'MyApp.IOrderApi'.
Call options.UseSerializer<T>() or options.UseSerializer(instance) when registering this client,
or register an app-wide default with services.AddRestSerializer<T>().
```

Code that resolves `IRestSerializer` from the container itself, expecting a client's `UseSerializer` to have registered it, now gets `null` or a resolution error in the same way.

### How to migrate

Register the app-wide default explicitly:

```csharp
// 2.0
services.AddRestSerializer<SystemTextJsonSerializer>();

services.AddIUserApi(o => o.BaseAddress = new Uri("https://users.example.com"));
services.AddIOrderApi(o => o.BaseAddress = new Uri("https://orders.example.com"));
```

Alternatively, call `UseSerializer` on every client that needs a serializer. A serializer that needs constructor arguments can be passed as an instance, either to one client or app-wide:

```csharp
services.AddRestSerializer(new SystemTextJsonSerializer(MyJson.Options));
services.AddIJevApi(o => o.UseSerializer(new SystemTextJsonSerializer(JevJson.Options)));
```

Clients that already call `UseSerializer` themselves need no change. `AddZeroAllocClient`, which configures the default named `HttpClient`, still registers its serializer as the app-wide default.

## Per-client serializers need keyed services

A per-client serializer is a keyed service, so a client that calls `UseSerializer` needs a container whose `IServiceProvider` implements `IKeyedServiceProvider`. The Microsoft.Extensions.DependencyInjection container does. Some third-party containers, adapters and wrapping providers do not.

With such a provider, a client on the app-wide default still works. A client that called `UseSerializer` fails when it is resolved, with a message that says the provider does not support keyed services. It never falls back to another serializer. To fix it, use a container with keyed-service support, or register an app-wide default with `AddRestSerializer<T>()` instead of calling `UseSerializer`.

## Interface-level `[Serializer]` is now honoured

In 1.x, `[Serializer(typeof(T))]` on the interface compiled but was ignored. The client used whatever `IRestSerializer` the container held, including one set by `UseSerializer`. In 2.0, the attribute takes effect. `Add{I}` registers `T` with `TryAddSingleton<T>()`, and the client uses `T`. See [Serialization](serialization.md#interface-level-serializer).

In 1.x, `UseSerializer` took effect for such a client and the attribute was ignored. In 2.0, combining an interface-level `[Serializer]` with `UseSerializer` for the same client throws an `InvalidOperationException` at registration, from both `Add{I}` and `AddRestResilience`. Use one or the other.

The generated constructor still takes `IRestSerializer`, so `T` may be internal while the interface is public. If you construct such a client by hand, pass a `T`.

## Generated constructors take `IRestSerializer`

In 1.x, a method-level `[Serializer(typeof(T))]` added a constructor parameter of type `T`, which made the build fail when `T` was `internal` and the interface public. In 2.0, the main serializer and every method-level override are `IRestSerializer` parameters. The parameter names and order are unchanged. The generated `Create` resolves the concrete types from DI, so `Add{I}` and `AddRestResilience` need no change.

If you construct a client by hand, keep passing the same instances. They convert to `IRestSerializer`, so the call still compiles.

## Generated clients implement `IGeneratedRestClient<TSelf>`

Every generated client now implements `ZeroAlloc.Rest.IGeneratedRestClient<TSelf>`, which is new generated surface. The interface is hidden from IntelliSense. It has two static abstract members, which the client implements explicitly:

- `Create(HttpClient, IServiceProvider)` builds the client with its serializer.
- `AddSerializers(IServiceCollection, ZeroAllocClientOptions)` registers the serializers the client needs.

Because the implementations are explicit, the client gains no public `Create` or `AddSerializers` members, and your interface can declare methods with those names. Call them through a type parameter constrained to the interface, or through the hidden `GeneratedRestClient.Create<TClient>` and `GeneratedRestClient.AddSerializers<TClient>` helpers.

## The generated `Add{I}` uses a typed-client factory

`Add{I}` now registers the named `HttpClient` and a factory that calls the client's static `Create`. The `HttpClient` is named after the interface, as before. `Add{I}` no longer uses `AddHttpClient<TInterface, TClient>`, so no `ActivatorUtilities` is involved. The returned `IHttpClientBuilder` and the client name are unchanged, so `AddHttpMessageHandler`, `ConfigurePrimaryHttpMessageHandler` and similar calls keep working.

## `AddRestResilience` requires a generated client

`AddRestResilience<TInterface, TRestClient, TResilienceProxy>` now constrains `TRestClient` to `IGeneratedRestClient<TRestClient>`. It builds the client with `TRestClient.Create` and registers its serializers with `TRestClient.AddSerializers`. In 1.x it used `ActivatorUtilities`. As a result:

- An interface-level `[Serializer]` type is registered automatically. You can remove any `services.AddSingleton<T>()` you added for it; leaving it in place is harmless.
- A **hand-written** `TRestClient` no longer compiles with `AddRestResilience`. Either implement `IGeneratedRestClient<TSelf>` on it, or register the proxy yourself. `GetRequiredRestSerializer<TClient>()` applies the same serializer rules as the generated clients:

```csharp
services.AddHttpClient(nameof(IPaymentApi), c => c.BaseAddress = new Uri("https://payments.example.com"));
services.AddTransient<IPaymentApi>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IPaymentApi));
    var inner = new MyPaymentApiClient(http, sp.GetRequiredRestSerializer<IPaymentApi>());
    return new IPaymentApiResilienceProxy(inner, sp.GetRequiredService<PaymentApiResiliencePolicies>());
});
```

## Result methods return transport failures

In 1.x, a method declared as returning `Result<T, HttpError>` returned a failure only for a non-2xx status. A refused connection threw `HttpRequestException`, `HttpClient.Timeout` threw `TaskCanceledException`, and a 2xx body the serializer could not read threw, for example, `JsonException`. See [#299](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/299).

In 2.0, those three failures come back as a failed `Result`. `HttpError` gains two `init` properties to tell them apart:

- `Kind`, an `HttpErrorKind`: `Status`, the default, for a non-2xx response, or `Transport`, `Timeout` or `Deserialization`.
- `Exception`, the exception behind a `Transport`, `Timeout` or `Deserialization` failure.

A `Deserialization` failure keeps the real status code and headers. `Transport` and `Timeout` failures have no response, so their `StatusCode` is `0` and their `Headers` are empty; a `Transport` failure uses the status code an `HttpRequestException` carries, if any.

What has not changed:

- **Cancellation you ask for still throws** `OperationCanceledException`, when the method's `CancellationToken` is cancelled.
- **Methods that do not return a `Result` still throw** in every case.
- **Serializing the request body, and any other exception, still throw.** Only transport, timeout and response-deserialization failures become an `HttpError`.

Before, in 1.x:

```csharp
try
{
    var result = await api.GetUserAsync(42, ct);
    if (result.IsFailure)
        return Problem(statusCode: (int)result.Error.StatusCode);
    return Ok(result.Value);
}
catch (HttpRequestException ex)
{
    return Problem(ex.Message, statusCode: 503);
}
catch (TaskCanceledException) when (!ct.IsCancellationRequested)
{
    return Problem("Timed out", statusCode: 504);
}
catch (JsonException ex)
{
    return Problem(ex.Message, statusCode: 502);
}
```

After, in 2.0:

```csharp
var result = await api.GetUserAsync(42, ct);
if (result.IsSuccess)
    return Ok(result.Value);

return result.Error.Kind switch
{
    HttpErrorKind.Transport => Problem(result.Error.Message, statusCode: 503),
    HttpErrorKind.Timeout => Problem("Timed out", statusCode: 504),
    HttpErrorKind.Deserialization => Problem(result.Error.Message, statusCode: 502),
    _ => Problem(statusCode: (int)result.Error.StatusCode),
};
```

### How to migrate

- **Remove the `try`/`catch` for `HttpRequestException`, `TaskCanceledException` and serializer exceptions** around `Result` calls. Those catch blocks no longer run. Handle the failed `Result` instead, and switch on `Kind` where the reaction differs.
- **Check code that treats every failure as an HTTP status.** Code that reads `result.Error.StatusCode` now also sees `0` for a transport failure or a timeout. Check `Kind` first.
- **`[Retry]` no longer retries these failures on a `Result` method.** ZeroAlloc.Resilience retries only thrown exceptions and passes a returned `Result` through, so a refused connection or `HttpClient.Timeout` is returned after one attempt. To keep retrying them, declare the method with a plain return type so it throws, or retry the failed `Result` in the caller. Retrying on selected failed Results is tracked in [ZeroAlloc.Resilience#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/142). See [Resilience](resilience.md#result-returns-and-error-handling).
- **Header lookups on `HttpError.Headers` now ignore case.** `Headers["x-request-id"]` finds `X-Request-ID`. Lookups that worked before still work.
