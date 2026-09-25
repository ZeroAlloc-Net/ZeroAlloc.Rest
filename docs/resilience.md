# Resilience

`ZeroAlloc.Rest.Resilience` bridges ZeroAlloc.Rest typed clients with [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience) policies. Annotate your `[ZeroAllocRestClient]` interface with resilience attributes and replace the normal `AddI{Interface}()` call with `AddRestResilience<,,>()` — both source generators run independently at compile time; the bridge wires their outputs in the DI container.

## Install

```sh
dotnet add package ZeroAlloc.Rest.Resilience
dotnet add package ZeroAlloc.Resilience
```

The Resilience source generator ships inside the `ZeroAlloc.Resilience` package; there is no separate generator package to add. This page describes ZeroAlloc.Resilience 2.0 and later.

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
| `[Retry]` | `MaxAttempts`, `BackoffMs`, `Jitter` | Retries on transient failure with optional exponential back-off |
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

Resilience policies act on exceptions. They do not inspect a returned `HttpError`.

For a method that returns `Result<T, HttpError>`:

| Policy | Behaviour |
|---|---|
| `[Retry]` | A returned `Result`, failed or successful, is passed through unchanged and not retried. Only a thrown exception is retried. Transport failures and timeouts are returned, not thrown, so they are not retried. |
| `[Timeout]` | Cancels the token; whatever the client returns or throws is passed through. The client sees that cancellation as requested by its caller, so it throws `OperationCanceledException` rather than returning a `Timeout` failure. |
| `[CircuitBreaker(Fallback = ...)]` | While open, the fallback's `Result` is returned. |
| `[CircuitBreaker]` without `Fallback`, `[RateLimit]` | Compile error ZR0003: the generator cannot build an `HttpError`. |
| `[Retry(NonThrowing = true)]` | Compile error ZR0003: `NonThrowing` requires `Result<T, ResilienceError>`. |

Since 2.0, a `Result<T, HttpError>` method also returns network failures, timeouts and bodies it cannot deserialize as a failed `Result`, with `HttpError.Kind` set to `Transport`, `Timeout` or `Deserialization`, instead of throwing them; see [Advanced](advanced.md). Those failures are no longer thrown, so `[Retry]` no longer retries them either.

So neither a 429 or 503 returned as a failed `Result` nor a refused connection or a timeout on such a method is retried. Only what still throws is retried: caller cancellation, which is never retried, and exceptions that are not transport, timeout or deserialization failures. To retry on transport failures and timeouts, either declare the method with a plain return type so the client throws, or handle the failed `Result` in the caller. Retrying on selected failed Results is tracked in [ZeroAlloc.Resilience#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/142), and taking the delay from `Retry-After` in [#143](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/143).

`[Timeout]` works by cancelling the token it passes to the client. To the client that is cancellation its caller asked for, so it still throws `OperationCanceledException`, and the policy handles it as before. Only a timeout inside the client, such as `HttpClient.Timeout`, becomes a failed `Result` with `Kind` set to `Timeout`.

See the [ZeroAlloc.Resilience result-return-types guide](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/blob/main/docs/guides/result-return-types.md) for the full rules.
