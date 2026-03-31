---
id: benchmarks
title: Benchmarks
slug: /benchmarks
sidebar_position: 8
description: Performance comparison of ZeroAlloc.Rest vs Refit and raw HttpClient.
---

# Benchmarks

## Methodology

All benchmarks use an in-memory `HttpMessageHandler` that returns a fixed pre-serialized JSON response. This isolates library overhead from network I/O. The measured time covers URL building, request construction, body serialization, sending, and response deserialization.

- Runtime: .NET 10.0.4, X64 RyuJIT AVX2
- Platform: Windows 11 (10.0.26200)
- Tool: BenchmarkDotNet v0.14.0
- Serializer: System.Text.Json (camelCase defaults)
- Response body: `{ "id": 1, "name": "Alice" }`
- Baseline: `RawHttpClient_Get` (manual `HttpClient` + `JsonSerializer.DeserializeAsync`)

Source: [`tests/ZeroAlloc.Rest.Benchmarks/RestClientBenchmarks.cs`](../tests/ZeroAlloc.Rest.Benchmarks/RestClientBenchmarks.cs)

## Results

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| RawHttpClient_Get | 1,648 ns | 1.00 | 1.38 KB | 1.00 |
| ZeroAlloc_Get | 1,933 ns | 1.21 | 1.74 KB | 1.27 |
| Refit_Get | 6,123 ns | 3.83 | 3.03 KB | 2.21 |
| | | | | |
| RawHttpClient_Post | 1,202 ns | 0.75 | 1.70 KB | 1.24 |
| ZeroAlloc_Post | 1,602 ns | 1.00 | 2.51 KB | 1.82 |
| Refit_Post | 3,528 ns | 2.21 | 3.55 KB | 2.58 |
| | | | | |
| RawHttpClient_QueryParam | 1,590 ns | 0.99 | 1.45 KB | 1.05 |
| ZeroAlloc_QueryParam | 2,474 ns | 1.55 | 1.85 KB | 1.35 |
| Refit_QueryParam | 13,509 ns | 8.45 | 3.67 KB | 2.67 |
| | | | | |
| RawHttpClient_Delete | 695 ns | 0.43 | 1.11 KB | 0.81 |
| ZeroAlloc_Delete | 1,084 ns | 0.68 | 1.48 KB | 1.07 |
| Refit_Delete | 4,510 ns | 2.82 | 2.61 KB | 1.90 |

Ratio is relative to `RawHttpClient_Get` (1,648 ns = 1.00×).

## How to reproduce

```sh
cd tests/ZeroAlloc.Rest.Benchmarks
dotnet run -c Release
```

BenchmarkDotNet requires Release mode. Debug builds produce incorrect numbers.

## Interpretation

**GET / POST:** ZeroAlloc.Rest adds ~20–30% overhead over raw `HttpClient`. The cost is the serialization dispatch layer and the generated method call frame. Refit pays for reflection-based attribute scanning and expression-tree invocation on every call — 3–4× slower.

**Query parameters:** The gap widens significantly. ZeroAlloc.Rest uses a `HeapPooledList<char>` rented from `ArrayPool<T>.Shared` to build the URL string, appending characters directly without intermediate string allocations. Refit rebuilds the URL via reflection on every call — 8.5× slower than baseline.

**DELETE (void return):** No deserialization path is needed. ZeroAlloc.Rest is 1.56× over raw `HttpClient`; Refit is 4.2× over raw.

**Allocations:** ZeroAlloc.Rest allocates 1.3–1.8× of raw `HttpClient` per call. Refit allocates 1.9–2.7×. At 10,000 req/s the difference is roughly 7–13 MB/s less pressure on the GC, reducing pause frequency in sustained-throughput workloads.
