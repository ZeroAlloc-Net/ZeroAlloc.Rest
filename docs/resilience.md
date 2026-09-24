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

`AddRestResilience<TInterface, TRestClient, TResilienceProxy>()` does three things:

1. Registers the `HttpClient` pipeline for `TInterface` / `TRestClient` via `AddHttpClient<TInterface, TRestClient>` — same named pipeline as the Rest generator's `AddI{Interface}()`, so `AddHttpMessageHandler`, `ConfigurePrimaryHttpMessageHandler`, etc. all apply normally.
2. Replaces the `TInterface` registration with a factory that constructs a fresh `TRestClient` via `IHttpClientFactory` and wraps it in `TResilienceProxy`.
3. Returns the `IHttpClientBuilder` so you can continue configuring the pipeline fluently.

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
| `[Retry]` | A returned `Result`, failed or successful, is passed through unchanged and not retried. Only a thrown exception is retried. |
| `[Timeout]` | Cancels the token; whatever the client returns or throws is passed through. |
| `[CircuitBreaker(Fallback = ...)]` | While open, the fallback's `Result` is returned. |
| `[CircuitBreaker]` without `Fallback`, `[RateLimit]` | Compile error ZR0003: the generator cannot build an `HttpError`. |
| `[Retry(NonThrowing = true)]` | Compile error ZR0003: `NonThrowing` requires `Result<T, ResilienceError>`. |

So a 429 or 503 returned as a failed `Result` is **not** retried. To retry on those, either declare the method with a plain return type so the client throws on non-success, or handle the failed `Result` in the caller. Retrying on selected failed Results is tracked in [ZeroAlloc.Resilience#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/142), and taking the delay from `Retry-After` in [#143](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/143).

Network failures, timeouts and malformed JSON still throw from a `Result<T, HttpError>` method rather than becoming an `HttpError`; see [#299](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/299). Those exceptions are what `[Retry]` retries.

See the [ZeroAlloc.Resilience result-return-types guide](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/blob/main/docs/guides/result-return-types.md) for the full rules.
