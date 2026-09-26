# Resilience

`ZeroAlloc.Rest.Resilience` bridges ZeroAlloc.Rest typed clients with [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience) policies. Annotate your `[ZeroAllocRestClient]` interface with resilience attributes and replace the normal `AddI{Interface}()` call with `AddRestResilience<,,>()` — both source generators run independently at compile time; the bridge wires their outputs in the DI container.

## Install

```sh
dotnet add package ZeroAlloc.Rest.Resilience
dotnet add package ZeroAlloc.Resilience
```

The Resilience source generator ships inside the `ZeroAlloc.Resilience` package; there is no separate generator package to add. This page describes ZeroAlloc.Resilience 2.0 and later; retrying a failed `Result` needs 3.2.0 or later.

## Quick Start

**1. Annotate your interface with resilience attributes:**

```csharp
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Resilience;

[ZeroAllocRestClient]
[Retry(MaxAttempts = 3, BackoffMs = 200)]
[Timeout(Ms = 5000)]
[CircuitBreaker(MaxFailures = 5, ResetMs = 30_000)]
public interface IPaymentApi
{
    [Post("/payments")]
    Task<PaymentResult> ChargeAsync([Body] ChargeRequest request, CancellationToken ct = default);
}
```

Both generators process `IPaymentApi`:
- The **Rest generator** emits `PaymentApiClient` (the concrete HTTP client).
- The **Resilience generator** emits `IPaymentApiResilienceProxy` (the policy wrapper), `PaymentApiResiliencePolicies` (the policy settings, defaulting to the attribute values) and `AddPaymentApiResiliencePolicies()`.

**2. Register with `AddRestResilience`:**

```csharp
// The proxy's policy settings: attribute values, optionally changed here, for example from IOptions.
builder.Services.AddPaymentApiResiliencePolicies((sp, p) =>
    p.Retry = new RetryPolicy(maxAttempts: 5, backoffMs: 250, jitter: true, perAttemptTimeoutMs: 0));

builder.Services.AddRestResilience<
    IPaymentApi,
    PaymentApiClient,          // Rest-generated client
    IPaymentApiResilienceProxy // Resilience-generated proxy
>(
    resilienceFactory: (client, sp) => new IPaymentApiResilienceProxy(
        client,
        sp.GetRequiredService<PaymentApiResiliencePolicies>()),
    configure: options =>
    {
        options.BaseAddress = new Uri("https://payments.example.com");
        options.UseSerializer<SystemTextJsonSerializer>();
    }
);
```

**3. Inject and use — same as without resilience:**

```csharp
public class OrderService(IPaymentApi payments)
{
    public async Task<PaymentResult> ChargeAsync(ChargeRequest req, CancellationToken ct)
        => await payments.ChargeAsync(req, ct);
}
```

A call that throws is retried; a sustained outage opens the circuit breaker, and calls then throw `ResilienceException` instead of waiting on a failing server.

Call `AddPaymentApiResiliencePolicies()` even without a callback: the factory resolves the policies from the container. Do not also call the Resilience generator's `AddPaymentApiResilience<TImpl>()`; `AddRestResilience` already registers `IPaymentApi`.

## How It Works

`AddRestResilience<TInterface, TRestClient, TResilienceProxy>()` does four things:

1. Registers the client's serializers exactly as the generated `AddI{Interface}()` does, through `TRestClient.AddSerializers`.
2. Registers the `HttpClient` pipeline named after `TInterface`. This is the same named pipeline as the Rest generator's `AddI{Interface}()`, so `AddHttpMessageHandler`, `ConfigurePrimaryHttpMessageHandler` and similar calls apply normally.
3. Replaces the `TInterface` registration with a factory that builds a fresh `TRestClient` with `TRestClient.Create`, from an `HttpClient` taken from `IHttpClientFactory`, and wraps it in `TResilienceProxy`.
4. Returns the `IHttpClientBuilder` so you can continue configuring the pipeline fluently.

`TRestClient` must be the client the Rest generator emits. Every generated client implements `IGeneratedRestClient<TSelf>`, whose static `Create` and `AddSerializers` members let the bridge build and register it with no reflection and no `ActivatorUtilities`, so the bridge is Native AOT-clean. A hand-written client does not satisfy that constraint; see [Migrating to 2.0](migrating-to-v2.md#addrestresilience-requires-a-generated-client).

## Serializer selection

`AddRestResilience` picks the client's serializer in the same order as the generated `Add{I}`:

1. **Interface-level `[Serializer(typeof(T))]`.** The client resolves `T` from the container. `AddRestResilience` registers `T` as a singleton with `TryAddSingleton`, just as the generated `Add{I}` does, so no extra registration is needed. The app-wide default does not apply, and setting `UseSerializer` in `configure` throws an `InvalidOperationException` at registration, naming the interface and `T`.
2. **`options.UseSerializer<T>()` or `options.UseSerializer(instance)`** in the `configure` callback. It applies to this client only, and needs a container with keyed-service support.
3. **The app-wide `IRestSerializer`**, for example from `services.AddRestSerializer<T>()`.

A client with none of these fails when it is resolved, with an `InvalidOperationException` that names the interface. See [Serialization](serialization.md#choosing-the-serializer-for-a-client).

## Available Attributes

All attributes are from `ZeroAlloc.Resilience`. See the [ZeroAlloc.Resilience README](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience) for the full attribute reference.

| Attribute | Key Properties | Description |
|-----------|---------------|-------------|
| `[Retry]` | `MaxAttempts`, `BackoffMs`, `Jitter`, `RetryWhen`, `RetryOnException`, `DelayHint`, `MaxDelayMs` | Retries on transient failure with optional exponential back-off; since 3.2.0 also retries a failed `Result` that `RetryWhen` calls transient, and can take the delay from the failure |
| `[Timeout]` | `Ms` (required) | Per-call deadline; cancels the token passed to the client, so the call ends with `OperationCanceledException` |
| `[CircuitBreaker]` | `MaxFailures`, `ResetMs`, `HalfOpenProbes` | Opens circuit after consecutive failures; rejects calls while open |
| `[RateLimit]` | `MaxPerSecond` (required), `BurstSize` | Token-bucket rate limiter; throws `ResilienceException` when exhausted |

Attributes can be applied at interface level (all methods) or per method. Each method-level attribute gets its own settings slot, for example `ChargeAsyncRetry`, and its own state.

## Configuring the HTTP Pipeline

The `IHttpClientBuilder` returned by `AddRestResilience` supports the full ASP.NET Core HTTP client pipeline:

```csharp
builder.Services
    .AddRestResilience<IPaymentApi, PaymentApiClient, IPaymentApiResilienceProxy>(...)
    .AddHttpMessageHandler<AuthHeaderHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });
```

## Result Returns and Error Handling

Resilience policies act on exceptions. `[Retry]` also acts on a returned failed `Result` when you name a `RetryWhen` predicate, since ZeroAlloc.Resilience 3.2.0; the other policies do not inspect a returned `HttpError`.

For a method that returns `Result<T, HttpError>`:

| Policy | Behaviour |
|---|---|
| `[Retry]` | Without `RetryWhen`, a returned `Result`, failed or successful, is passed through unchanged and not retried; only a thrown exception is retried. With `RetryWhen`, a failed `Result` the predicate calls transient is retried, and the last failed `Result` is returned unchanged once attempts run out. See [Retrying a failed Result](#retrying-a-failed-result). |
| `[Timeout]` | Cancels the token; whatever the client returns or throws is passed through. The client sees that cancellation as requested by its caller, so it throws `OperationCanceledException` rather than returning a `Timeout` failure. |
| `[CircuitBreaker(Fallback = ...)]` | While open, the fallback's `Result` is returned. |
| `[CircuitBreaker]` without `Fallback`, `[RateLimit]` | Compile error ZR0003: the generator cannot build an `HttpError`. |
| `[Retry(NonThrowing = true)]` | Compile error ZR0003: `NonThrowing` requires `Result<T, ResilienceError>`. |

Since 2.0, a `Result<T, HttpError>` method also returns network failures, timeouts and bodies it cannot deserialize as a failed `Result`, with `HttpError.Kind` set to `Transport`, `Timeout` or `Deserialization`, instead of throwing them; see [Advanced](advanced.md). Those failures are no longer thrown, so `[Retry]` no longer retries them either.

Transport failures and timeouts on such a method are returned as a failed `Result`, so without `RetryWhen` they are not retried.

### Retrying a failed Result

Name a static predicate with `RetryWhen`, and optionally a static delay hint with `DelayHint`, both declared on the interface. `HttpError.GetRetryAfter()` reads the `Retry-After` header in delta-seconds or HTTP-date form:

```csharp
[ZeroAllocRestClient]
[Retry(MaxAttempts = 4, BackoffMs = 500, Jitter = true,
       RetryWhen = nameof(IsTransient), DelayHint = nameof(RetryAfter), MaxDelayMs = 30_000)]
public interface IPaymentApi
{
    [Post("/payments")]
    Task<Result<PaymentResult, HttpError>> ChargeAsync([Body] ChargeRequest request, CancellationToken ct = default);

    static bool IsTransient(HttpError e) =>
        e.Kind is HttpErrorKind.Transport or HttpErrorKind.Timeout
        || (int)e.StatusCode is 429 or 502 or 503 or 504;

    static TimeSpan? RetryAfter(HttpError e) => e.GetRetryAfter();
}
```

- A failed `Result` for which `IsTransient` returns `true` is retried, and counts as a failure for `[CircuitBreaker]`. Any other failed `Result`, such as a 422, is returned at once and counts as a success, because the server answered.
- The delay is `RetryAfter`'s value when it returns one, and the computed back-off otherwise. **Always set `MaxDelayMs` when the delay comes from a server header**: it caps every delay, and without it a server can make the call wait as long as it likes.
- When every attempt fails, the last failed `Result` is returned unchanged, so the caller sees the real final error.
- The predicate and hint run outside the retried call: an exception they throw reaches the caller and is not retried.

With a [mapped error type](#mapped-error-types), write the predicate and hint over `TError` instead of `HttpError`. The full rules, including the `Exception` overload of `DelayHint`, `RetryOnException` and diagnostics ZR0009 and ZR0010, are in the ZeroAlloc.Resilience [retry guide](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/blob/main/docs/core-concepts/retry.md).

`[Timeout]` works by cancelling the token it passes to the client. To the client that is cancellation its caller asked for, so it still throws `OperationCanceledException`, and the policy handles it as before. Only a timeout inside the client, such as `HttpClient.Timeout`, becomes a failed `Result` with `Kind` set to `Timeout`.

### Mapped error types

A method that returns `Result<T, TError>` through an [`[ErrorMapper]`](advanced.md#your-own-error-type-errormapper) works through the bridge exactly like the generated `Add{I}`. `AddRestResilience` registers the mapper through the client's `AddSerializers` and builds the client with the concrete mapper type, so a host registration of `IHttpErrorMapper<TError>` does not replace it.

The policy rules in the table above apply with `TError` in place of `HttpError`. `[Retry]` passes the mapped failure through unless `RetryWhen` names a predicate over `TError`. `[CircuitBreaker]` without `Fallback`, and `[RateLimit]`, are compile error ZR0003, because the resilience generator cannot build a `TError`.

See the [ZeroAlloc.Resilience result-return-types guide](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/blob/main/docs/guides/result-return-types.md) for the full rules.
