# ZeroAlloc.Rest

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Rest.svg)](https://www.nuget.org/packages/ZeroAlloc.Rest)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Rest/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Rest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Describe your HTTP API as a C# interface and get a typed REST client generated at compile time: a Refit alternative with no reflection, NativeAOT-ready, and no allocation beyond what the same call written by hand costs.

## Why ZeroAlloc.Rest

- **Allocates exactly what hand-written `HttpClient` code does.** A generated GET of a small DTO allocates 1,688 B per call, the same as the request written by hand, and a test fails the build if it ever allocates more.
- **1.1–2.0× faster than Refit** with 1.1–1.35× less allocation per call, measured in the same run.
- **Works under NativeAOT and trimming.** No reflection, no IL emit, no `DynamicMethod`; CI publishes a NativeAOT app and fails on any trim or AOT warning.
- **Mistakes surface at compile time.** A route template that does not match the method's parameters, or a `Result` error type with no mapper, is a compiler diagnostic.

## Install

```bash
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.DependencyInjection
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

The source generator ships inside `ZeroAlloc.Rest`. The other two give you the `IHttpClientFactory` registration and the JSON serializer used below.

## Quick start

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

var services = new ServiceCollection();
services.AddIUserApi(options => // generated from the interface name
{
    options.BaseAddress = new Uri("https://jsonplaceholder.typicode.com");
    options.UseSerializer(new SystemTextJsonSerializer(AppJsonContext.Default));
});

var api = services.BuildServiceProvider().GetRequiredService<IUserApi>();
var user = await api.GetUserAsync(1);
Console.WriteLine($"{user.Id}: {user.Name}"); // 1: Leanne Graham

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);
}

public sealed record UserDto(int Id, string Name);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UserDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

The generator writes `UserApiClient`, a sealed class implementing `IUserApi`, at compile time. Inject `IUserApi` wherever you need it.

## What you get

- **Every parameter kind**: path segments including `{**path}` catch-alls, `[Query]`, `[Body]` and `[Header]`.
- **Errors as values**: return `Result<T, HttpError>` from [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) and 4xx/5xx responses, network failures and timeouts come back as a failure instead of an exception, or map them to your own error type.
- **Pluggable serializers**: System.Text.Json, MemoryPack, MessagePack, or your own, and a different one per method when an API mixes formats.
- **Streaming responses** for large bodies, without the copy `HttpClient` makes by default.
- **`IHttpClientFactory` integration** through a generated `Add{Interface}` extension, or construct the client by hand without DI.
- **Tracing and metrics** on the BCL `ActivitySource` and `Meter`, ready for OpenTelemetry: [Telemetry](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/telemetry.md).
- **OpenAPI import**: generate the interface, models and JSON context from a spec with the `zeroalloc generate` CLI or an MSBuild item: [OpenAPI code generation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/openapi-codegen.md).
- **Retry, timeout, circuit breaker and rate limiting** with `ZeroAlloc.Rest.Resilience` and [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience): [Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/resilience.md).
- **LLM clients**: `ZeroAlloc.Rest.Anthropic` for the Anthropic Messages API, with streaming, tools and prompt caching, and `ZeroAlloc.Rest.Ollama` for Ollama embeddings: [Anthropic](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/anthropic.md), [Ollama](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/ollama.md).

## Performance

Against Refit 16.4, in-memory handler, no network I/O. .NET 10.0.13, BenchmarkDotNet v0.15.8. Allocations are exact; speed is a same-run ratio.

| Call | ZA vs Refit | Refit alloc | ZA alloc |
|---|---:|---:|---:|
| GET | **1.5× faster** | 2,056 B | **1,584 B** |
| GET with query parameter | **2.0× faster** | 2,128 B | **1,616 B** |
| POST | **1.3× faster** | 2,816 B | **2,088 B** |

Refit scans attributes with reflection and invokes expression trees on every call; the generated client is plain code. Raw `HttpClient` rows, DELETE, `Result<T>`, serializer throughput and the per-call allocation breakdown: [docs/benchmarks.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/benchmarks.md).

## Learn more

- Documentation: [rest.zeroalloc.net](https://rest.zeroalloc.net)
- [Getting started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/getting-started.md), [parameters](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/parameters.md), [serialization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/serialization.md), [Native AOT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/native-aot.md), the [cookbook](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/tree/main/docs/cookbook) and [migrating to 3.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/docs/migrating-to-v3.md)
- Related packages: [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience), [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results), [ZeroAlloc.Serialisation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation)
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues)
- License: [MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/blob/main/LICENSE)
