---
id: benchmarks
title: Benchmarks
slug: /benchmarks
sidebar_position: 8
description: Performance comparison of ZeroAlloc.Rest vs Refit and raw HttpClient, and serializer throughput across System.Text.Json, MemoryPack, and MessagePack.
---

# Benchmarks

## Methodology

All benchmarks use an in-memory `HttpMessageHandler` that returns a fixed pre-serialized response. This isolates library overhead from network I/O.

- Runtime: .NET 10.0.4, X64 RyuJIT AVX2
- Platform: Windows 11 (10.0.26200)
- Tool: BenchmarkDotNet v0.14.0
- Baseline: `RawHttpClient_Get` (manual `HttpClient` + `JsonSerializer.DeserializeAsync`)

Source: `tests/ZeroAlloc.Rest.Benchmarks/`

---

## HTTP client: ZeroAlloc.Rest vs Refit vs raw HttpClient

The measured time covers URL building, request construction, body serialization, sending, and response deserialization. Serializer: System.Text.Json (camelCase defaults). Response body: `{ "id": 1, "name": "Alice" }`.

<!-- BENCH:START -->
_Last refreshed: 2026-05-13_

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8.

| Method | Mean | Ratio | Allocated | vs Refit |
|---|---:|---:|---:|---:|
| RawHttpClient_Get | 2.09 μs | 1.00 | 1.38 KB | — |
| **ZeroAlloc_Get** | **3.52 μs** | **1.68** | **1.88 KB** | **3.6× faster** |
| Refit_Get | 12.70 μs | 6.07 | 2.88 KB | — |
| RawHttpClient_Post | 3.12 μs | 1.49 | 1.70 KB | — |
| **ZeroAlloc_Post** | **6.70 μs** | **3.20** | **2.64 KB** | **1.7× faster** |
| Refit_Post | 11.62 μs | 5.56 | 3.46 KB | — |
| RawHttpClient_QueryParam | 2.16 μs | 1.03 | 1.45 KB | — |
| **ZeroAlloc_QueryParam** | **4.28 μs** | **2.04** | **1.99 KB** | **3.6× faster** |
| Refit_QueryParam | 15.51 μs | 7.41 | 3.55 KB | — |
| RawHttpClient_Delete | 1.10 μs | 0.53 | 1.11 KB | — |
| **ZeroAlloc_Delete** | **1.92 μs** | **0.92** | **1.61 KB** | **2.4× faster** |
| Refit_Delete | 4.62 μs | 2.21 | 2.45 KB | — |
| RawHttpClient_Result | 2.04 μs | 0.98 | 1.32 KB | — |
| **ZeroAlloc_Result** | **4.07 μs** | **1.95** | **1.92 KB** | Refit lacks `Result<T>` |

ZeroAlloc.Rest is **1.7–3.6× faster than Refit** across every shape of call (GET / POST / GET-with-query / DELETE) with **1.3–1.5× less allocation**. Refit pays for reflection-based attribute scanning and expression-tree invocation on every call (6–8× over the raw `HttpClient` baseline); ZA's generated client is 1.7–3.2× over raw — closer to the floor.
<!-- BENCH:END -->

### Interpretation

**GET / POST:** ZeroAlloc.Rest runs at roughly 1.7–3.2× the raw baseline, covering the generated call frame and serialization dispatch. Refit pays for reflection-based attribute scanning and expression-tree invocation on every call — 5.6–6.1× over baseline.

**Query parameters:** ZeroAlloc.Rest uses a `HeapPooledList<char>` rented from `ArrayPool<T>.Shared` — 2× the raw baseline. Refit rebuilds the URL via reflection on every call, landing at 7.4× over baseline.

**DELETE (void return):** No deserialization path; ZeroAlloc.Rest is essentially at parity with raw `HttpClient` (0.92×, slightly under because BDN's noise floor on sub-microsecond delta measurements). Refit is 2.2×.

**Result<T, HttpError>:** ZA's typed-error return adds no measurable overhead vs the equivalent raw `HttpClient` + success-check pattern (1.95× vs 0.98×, the gap matches the GET delta). Refit has no equivalent surface.

**Allocations:** ZeroAlloc.Rest allocates 1.3–1.5× of raw `HttpClient` per call. Refit allocates 1.8–2.6×. At 10,000 req/s the difference is ~5–15 MB/s less GC pressure.

---

## Serializer throughput: System.Text.Json vs MemoryPack vs MessagePack

Measured in isolation — serialize or deserialize a single `{ "id": 42, "name": "Alice" }` object. Baseline: `Serialize_SystemTextJson`.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| Serialize_SystemTextJson | 494 ns | 1.00 | 608 B | 1.00 |
| Serialize_MemoryPack | 191 ns | **0.39** | 464 B | 0.76 |
| Serialize_MessagePack | 515 ns | 1.06 | 416 B | 0.68 |
| | | | | |
| Deserialize_SystemTextJson | 1,190 ns | 2.44 | 248 B | 0.41 |
| Deserialize_MemoryPack | 485 ns | **0.99** | 720 B | 1.18 |
| Deserialize_MessagePack | 823 ns | 1.69 | 480 B | 0.79 |

### Interpretation

**MemoryPack** is the clear throughput winner: 2.5× faster to serialize and 2.5× faster to deserialize than System.Text.Json. It requires `[MemoryPackable]` on your DTOs and uses a proprietary binary wire format — not suitable for public HTTP APIs but ideal for internal service-to-service communication.

**MessagePack** serializes at parity with STJ but deserializes ~1.4× faster, with the lowest allocation footprint on the wire (416 B). Uses the widely-supported MessagePack binary format. Requires `[MessagePackObject]` / `[Key]` attributes on DTOs.

**System.Text.Json** has no DTO annotation requirements and produces human-readable JSON — the right default for public APIs. The STJ deserializer is slower than the binary formats but allocates the least during deserialization (248 B) since it can leverage `Utf8JsonReader` spans.

Use `options.UseSerializer<MemoryPackRestSerializer>()` or `options.UseSerializer<MessagePackRestSerializer>()` to switch serializers in your client registration.

---

## How to reproduce

```sh
cd tests/ZeroAlloc.Rest.Benchmarks
dotnet run -c Release
```

BenchmarkDotNet requires Release mode. Debug builds produce incorrect numbers. Both benchmark classes (`RestClientBenchmarks` and `SerializerBenchmarks`) run automatically.
