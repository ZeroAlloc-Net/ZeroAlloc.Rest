# Resilience

`ZeroAlloc.Rest.Resilience` bridges ZeroAlloc.Rest typed clients with [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience) policies. Annotate your `[ZeroAllocRestClient]` interface with resilience attributes and replace the normal `AddI{Interface}()` call with `AddRestResilience<,,>()` — both source generators run independently at compile time; the bridge wires their outputs in the DI container.

## Install

```sh
dotnet add package ZeroAlloc.Rest.Resilience
dotnet add package ZeroAlloc.Resilience
```

The Resilience generator must be added as an analyzer:

```xml
<PackageReference Include="ZeroAlloc.Resilience.Generator"
                  Version="x.y.z"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## Quick Start

**1. Annotate your interface with resilience attributes:**

```csharp
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Resilience.Attributes;

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
- The **Resilience generator** emits `IPaymentApiResilienceProxy` (the policy wrapper).

**2. Register with `AddRestResilience`:**

```csharp
builder.Services.AddRestResilience<
    IPaymentApi,
    PaymentApiClient,          // Rest-generated client
    IPaymentApiResilienceProxy // Resilience-generated proxy
>(
    resilienceFactory: (client, sp) => new IPaymentApiResilienceProxy(
        client,
        sp.GetRequiredService<RetryPolicy>(),
        sp.GetRequiredService<TimeoutPolicy>(),
        sp.GetRequiredService<CircuitBreakerPolicy>()
    ),
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

Any transient failure is automatically retried; a sustained outage trips the circuit breaker and returns a `ResilienceException` rather than hanging.

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
| `[Timeout]` | `Ms` (required) | Per-call deadline; throws `ResilienceException` on expiry |
| `[CircuitBreaker]` | `MaxFailures`, `ResetMs`, `HalfOpenProbes` | Opens circuit after consecutive failures; rejects calls while open |
| `[RateLimit]` | `MaxPerSecond` (required), `BurstSize` | Token-bucket rate limiter; throws `ResilienceException` when exhausted |

Attributes can be applied at interface level (all methods) or per method.

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

When an interface method returns `Result<T, HttpError>` (from `ZeroAlloc.Results`), resilience policies inspect the `HttpError` rather than catching exceptions. Configure this on the Resilience side by using `NonThrowing = true` on `[Retry]` — see the [ZeroAlloc.Resilience result-return-types guide](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/blob/main/docs/guides/result-return-types.md).
