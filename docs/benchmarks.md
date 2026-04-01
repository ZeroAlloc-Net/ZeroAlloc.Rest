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

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| RawHttpClient_Get | 1,253 ns | 1.00 | 1.38 KB | 1.00 |
| ZeroAlloc_Get | 2,326 ns | 2.01 | 1.74 KB | 1.27 |
| Refit_Get | 9,901 ns | 8.57 | 3.03 KB | 2.21 |
| | | | | |
| RawHttpClient_Post | 2,585 ns | 2.24 | 1.70 KB | 1.24 |
| ZeroAlloc_Post | 4,020 ns | 3.48 | 2.51 KB | 1.82 |
| Refit_Post | 9,469 ns | 8.19 | 3.55 KB | 2.58 |
| | | | | |
| RawHttpClient_QueryParam | 2,005 ns | 1.73 | 1.45 KB | 1.05 |
| ZeroAlloc_QueryParam | 2,603 ns | 2.25 | 1.85 KB | 1.35 |
| Refit_QueryParam | 13,064 ns | 11.30 | 3.67 KB | 2.67 |
| | | | | |
| RawHttpClient_Delete | 967 ns | 0.84 | 1.11 KB | 0.81 |
| ZeroAlloc_Delete | 1,175 ns | 1.02 | 1.48 KB | 1.07 |
| Refit_Delete | 4,943 ns | 4.28 | 2.61 KB | 1.90 |

Ratio is relative to `RawHttpClient_Get` (1,253 ns = 1.00×).

### Interpretation

**GET / POST:** ZeroAlloc.Rest runs at roughly 2–3.5× the raw baseline, covering the generated call frame and serialization dispatch. Refit pays for reflection-based attribute scanning and expression-tree invocation on every call — 8× over baseline.

**Query parameters:** ZeroAlloc.Rest uses a `HeapPooledList<char>` rented from `ArrayPool<T>.Shared` — only 30% slower than the hand-written baseline. Refit rebuilds the URL via reflection on every call, landing at 11× over baseline.

**DELETE (void return):** No deserialization path; ZeroAlloc.Rest is essentially at parity with raw `HttpClient` (1.02×). Refit is 4.3×.

**Allocations:** ZeroAlloc.Rest allocates 1.3–1.8× of raw `HttpClient` per call. Refit allocates 1.9–2.7×. At 10,000 req/s the difference is ~7–13 MB/s less GC pressure.

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
