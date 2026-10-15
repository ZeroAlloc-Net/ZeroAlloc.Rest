# ZeroAlloc.Rest

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Rest.svg)](https://www.nuget.org/packages/ZeroAlloc.Rest)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

**ZeroAlloc.Rest** is a source-generated, Native AOT-compatible REST client for .NET 10+. Define your HTTP API as a C# interface — the Roslyn generator emits a fully type-safe, zero-reflection implementation at compile time. No runtime code generation, no IL emit, and no per-call allocation beyond what the same call written by hand against `HttpClient` allocates.

## Install

The source generator is bundled into the main package. `ZeroAlloc.Rest` alone generates the client and
its constructor; add `ZeroAlloc.Rest.DependencyInjection` for the generated `Add{I}` extension and
`IHttpClientFactory` integration used below:

```sh
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.DependencyInjection
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

Or via `<PackageReference>`:

```xml
<PackageReference Include="ZeroAlloc.Rest" Version="x.y.z" />
<PackageReference Include="ZeroAlloc.Rest.DependencyInjection" Version="x.y.z" />
<PackageReference Include="ZeroAlloc.Rest.SystemTextJson" Version="x.y.z" />
```

> The standalone `ZeroAlloc.Rest.Generator` package is still published for backwards compatibility with existing direct PackageReferences, but new consumers should reference only `ZeroAlloc.Rest`.
>
> Building a client by hand instead of through DI? Skip `ZeroAlloc.Rest.DependencyInjection` — see
> [Dependency Injection: Without dependency injection](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/dependency-injection.md#without-dependency-injection).

### Optional: resilience policies

To add retry, timeout, and circuit-breaker behaviour to a client, install the bridge package alongside [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience):

```sh
dotnet add package ZeroAlloc.Rest.Resilience
dotnet add package ZeroAlloc.Resilience
```

## Quick Start

**1. Define your API interface:**

```csharp
using ZeroAlloc.Rest.Attributes;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> ListUsersAsync([Query] string? name = null, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest request, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}
```

**2. Register in DI:**

```csharp
using System.Text.Json.Serialization;
using ZeroAlloc.Rest.SystemTextJson;

// Every type the client reads or writes needs an entry: a context-only serializer throws for any other.
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(CreateUserRequest))]
internal partial class AppJsonContext : JsonSerializerContext { }

builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer(new SystemTextJsonSerializer(AppJsonContext.Default));
});
```

**3. Inject and use:**

```csharp
public class UserService(IUserApi api)
{
    public Task<UserDto> GetAsync(int id) => api.GetUserAsync(id);
}
```

The generator produces `UserApiClient` — a sealed class implementing `IUserApi` — at compile time. No reflection, no proxies, no `DynamicMethod`.

## Performance

Measured on .NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8. In-memory handler; no real network I/O.
See [docs/benchmarks.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/benchmarks.md) for methodology and full results.

| Method | Mean | vs Refit | Allocated |
|---|---:|---:|---:|
| Raw HttpClient (GET baseline) | 2.09 μs | — | 1.38 KB |
| **ZeroAlloc.Rest GET** | 3.52 μs | **3.6× faster** | 1.88 KB |
| Refit GET | 12.70 μs | 1× | 2.88 KB |
| **ZeroAlloc.Rest POST** | 6.70 μs | **1.7× faster** | 2.64 KB |
| Refit POST | 11.62 μs | 1× | 3.46 KB |
| **ZeroAlloc.Rest QueryParam** | 4.28 μs | **3.6× faster** | 1.99 KB |
| Refit QueryParam | 15.51 μs | 1× | 3.55 KB |
| **ZeroAlloc.Rest DELETE** | 1.92 μs | **2.4× faster** | 1.61 KB |
| Refit DELETE | 4.62 μs | 1× | 2.45 KB |

ZA is **1.7–3.6× faster than Refit** across every call shape with **1.3–1.5× less allocation**. Refit pays for reflection-based attribute scanning + expression-tree invocation on every call; ZA's generated client lives within 1.7–3.2× of the raw HttpClient floor.

Allocation is pinned by `GetCallAllocationTests` with `AllocationGate`: a generated GET of a small DTO against an in-memory handler allocates **1,688 B per call** on .NET 10 x64, which is exactly what the same request written by hand allocates. Most of it is `HttpClient`, the request and response objects and the DTO. The generated call adds a 40 B URL string and 136 B for its per-request `Accept` header over a bare `HttpClient.GetAsync` with a literal URL.

The 136 B is what `HttpClient` charges for sending any header, not a cost of how the generated code adds it. `HttpClient.SendAsync` merges `DefaultRequestHeaders` into each request's own header collection, which allocates the same header store and entry that `TryAddWithoutValidation` does. Measured over one `HttpClient` and a stub handler, a GET allocates 1,192 B per call whether `Accept` is added per request or set once on `DefaultRequestHeaders`, and 1,056 B only when no `Accept` is sent at all. Moving the header to `DefaultRequestHeaders` would therefore save nothing, while changing what goes on the wire: stream-returning methods would start sending `Accept: application/json`, and a request-level `Accept` header would suppress the serializer's content type. The per-request header stays by design; see [#441](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/441).

## Features

- **Source-generated** — zero runtime reflection; compile-time type safety
- **Native AOT compatible** — no `DynamicMethod`, no IL emit, no `Type.GetType`
- **Per-method serializer override** — `[Serializer(typeof(MySerializer))]` for mixed protocols
- **Path, query, body, and header parameters** — `{id}`, `{**path}` catch-all, `[Query]`, `[Body]`, `[Header("X-Api-Key")]`
- **`Result<T, HttpError>`** — typed success/error returns via `ZeroAlloc.Results`; no exception on 4xx/5xx, network failures, timeouts or unreadable response bodies — or your own error type via `[ErrorMapper]`
- **OpenAPI code generation** — the `zeroalloc generate` CLI (`ZeroAlloc.Rest.Tools`), or `<ZeroAllocApiSpec>` items with the `ZeroAlloc.Rest.Tools.MSBuild` task, generates the interface, its models and a JSON context
- **Pluggable serializers** — System.Text.Json (`ZeroAlloc.Rest.SystemTextJson`), MemoryPack (`ZeroAlloc.Rest.MemoryPack`), MessagePack (`ZeroAlloc.Rest.MessagePack`), or bring your own `IRestSerializer`
- **IHttpClientFactory integration** — generated `Add{I}` extension method named after the interface, such as `AddIUserApi`
- **Resilience bridge** — `ZeroAlloc.Rest.Resilience` wraps any client with `[Retry]`, `[Timeout]`, `[CircuitBreaker]`, and `[RateLimit]` via `AddRestResilience<,,>()`

## Telemetry

Every generated client emits OpenTelemetry-compatible signals out of the box, with no extra dependency beyond the BCL `System.Diagnostics` primitives.

**Tracing.** An `ActivitySource("ZeroAlloc.Rest")` produces one span per call, named `Interface.Method`. Tags: `http.method`, `http.status_code`, `server.address`, `rest.method`. A failure sets `ActivityStatusCode.Error` with the exception's message. Cancellation through the method's `CancellationToken` is not a failure: the span keeps status `Unset` and gets the tag `rest.cancelled` = `true`. Any other cancellation, such as `HttpClient.Timeout`, is a failure.

**Metrics.** The generator emits `rest.requests_total` (counter) and `rest.request_duration_ms` (histogram) under the `ZeroAlloc.Rest` Meter. Tags: `http.method`, `http.status_code`, `server.address`, `rest.method` (the generated `Interface.Method` identifier).

Every call records exactly one `rest.request_duration_ms` sample. A call that gets a response increments `rest.requests_total` and records its duration with all four tags, whatever the status and even when reading the body then fails. A call that ends before any response, such as a request body the serializer cannot write, a transport error, a timeout or a cancellation, records its duration with only `http.method` and `rest.method` and leaves the counter untouched, so the histogram count minus the counter is the number of calls that never got a response.

**Subscription:**
```csharp
services.AddOpenTelemetry()
        .WithTracing(t => t.AddSource("ZeroAlloc.Rest"))
        .WithMetrics(m => m.AddMeter("ZeroAlloc.Rest"));
```

## Documentation

| Page | Description |
|---|---|
| [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/getting-started.md) | Install, register, and make your first call |
| [Routing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/routing.md) | Route templates and path parameters |
| [Parameters](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/parameters.md) | Query, body, header, and path parameters |
| [Serialization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/serialization.md) | Built-in serializers and custom `IRestSerializer` |
| [Dependency Injection](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/dependency-injection.md) | Generated DI extension and `IHttpClientFactory` |
| [Native AOT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/native-aot.md) | AOT safety guarantees and publish configuration |
| [OpenAPI Code Generation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/openapi-codegen.md) | Generate interfaces from OpenAPI specs |
| [Benchmarks](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/benchmarks.md) | Performance comparison vs Refit and raw HttpClient |
| [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/testing.md) | Testing patterns with WireMock.Net |
| [Advanced](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/advanced.md) | `Result<T, HttpError>`, or your own error type via `[ErrorMapper]`, multiple serializers, edge cases |
| [Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/resilience.md) | Retry, timeout, circuit-breaker, and rate-limit via `ZeroAlloc.Rest.Resilience` |
| [Migrating to 2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/migrating-to-v2.md) | Upgrading from 1.x |
| [Migrating to 3.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/migrating-to-v3.md) | Upgrading from 2.x |
| [Cookbook](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/tree/main/docs/cookbook/) | End-to-end recipes |

## License

MIT
