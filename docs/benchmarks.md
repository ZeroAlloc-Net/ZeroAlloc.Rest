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

Measured 2026-10-21 on an otherwise idle machine:

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Every benchmark ran out of process in three launches, pinned to two performance cores. Ratios
come from one run, with the lowest and highest of the three launches in brackets. Refit 16.4.0,
MemoryPack 1.21.4, MessagePack 3.1.12.

Source: `tests/ZeroAlloc.Rest.Benchmarks/`

---

## HTTP client: ZeroAlloc.Rest vs Refit vs raw HttpClient

The measured time covers URL building, request construction, body serialization, sending, and response deserialization. Serializer: System.Text.Json (camelCase defaults). Response body: `{ "id": 1, "name": "Alice" }`.

<!-- BENCH:START -->
_Last refreshed: 2026-10-21_

Each row's time relative to the raw `HttpClient` call of the same shape, and Refit's relative to ZeroAlloc.Rest's.

| Call | Raw `HttpClient` | ZeroAlloc.Rest | Refit | ZeroAlloc.Rest vs Refit | Allocated: raw / ZA / Refit |
|---|---:|---:|---:|---:|---:|
| GET | 1.00 | 1.24 | 1.86 | **1.49× faster** (1.47–1.51) | 1,408 / **1,584** / 2,056 B |
| POST | 1.00 | 1.27 | 1.63 | **1.29× faster** (1.26–1.31) | 1,744 / **2,088** / 2,816 B |
| GET with query parameter | 1.00 | 1.22 | 2.39 | **1.96× faster** (1.57–2.30) | 1,480 / **1,616** / 2,128 B |
| DELETE | 1.00 | 1.21 | 1.32 | **1.09× faster** (0.99–1.17) | 1,136 / **1,312** / 1,416 B |
| GET returning `Result<T, HttpError>` | 1.00 | 1.24 | — | Refit lacks `Result<T>` | 1,352 / **1,632** / — |

ZeroAlloc.Rest is **1.1–2.0× faster than Refit** across every shape of call (GET / POST / GET-with-query / DELETE) and allocates **1.1–1.35× less**. It runs at 1.21–1.27× the raw `HttpClient` baseline on every shape. Refit, at 1.3–2.4× over raw, has narrowed the gap since the May capture, which measured it against ZeroAlloc.Rest at 1.7–3.6×; Refit has been upgraded three times since then, to 16.2, 16.3 and 16.4.
<!-- BENCH:END -->

### Interpretation

**GET / POST:** ZeroAlloc.Rest runs at roughly 1.24–1.27× the raw baseline, covering the generated call frame and serialization dispatch. Refit pays for reflection-based attribute scanning and expression-tree invocation on every call — 1.6–1.9× over baseline.

**Query parameters:** ZeroAlloc.Rest writes the route, the escaped values and the query string into one stack buffer and allocates only the final URL string; values of BCL types such as `int`, `Guid` or `DateTimeOffset` are formatted straight into that buffer, without an intermediate string. Refit rebuilds the URL via reflection on every call, landing at 2.4× over baseline, the widest gap in the table.

**DELETE (void return):** No deserialization path, so all three are closest here: ZeroAlloc.Rest at 1.21× raw, Refit at 1.32×.

**Result<T, HttpError>:** ZA's typed-error return costs the same over its raw `HttpClient` equivalent as a plain GET, 1.24×. Refit has no equivalent surface.

**Allocations:** ZeroAlloc.Rest allocates 1.1–1.2× of raw `HttpClient` per call. Refit allocates 1.25–1.6×.

---

## Allocation per call

Allocation is pinned by `GetCallAllocationTests` with `AllocationGate`: a generated GET of a small DTO against an in-memory handler allocates **1,688 B per call** on .NET 10 x64, which is exactly what the same request written by hand allocates. Most of it is `HttpClient`, the request and response objects and the DTO. The generated call adds a 40 B URL string and 136 B for its per-request `Accept` header over a bare `HttpClient.GetAsync` with a literal URL.

The 136 B is what `HttpClient` charges for sending any header, not a cost of how the generated code adds it. `HttpClient.SendAsync` merges `DefaultRequestHeaders` into each request's own header collection, which allocates the same header store and entry that `TryAddWithoutValidation` does. Measured over one `HttpClient` and a stub handler, a GET allocates 1,192 B per call whether `Accept` is added per request or set once on `DefaultRequestHeaders`, and 1,056 B only when no `Accept` is sent at all. Moving the header to `DefaultRequestHeaders` would therefore save nothing, while changing what goes on the wire: stream-returning methods would start sending `Accept: application/json`, and a request-level `Accept` header would suppress the serializer's content type. The per-request header stays by design; see [#441](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/441).

---

## Serializer throughput: System.Text.Json vs MemoryPack vs MessagePack

Measured in isolation — serialize or deserialize a single `{ "id": 42, "name": "Alice" }` object. Baseline: `Serialize_SystemTextJson`.

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| Serialize_SystemTextJson | 1.00 | | 608 B |
| Serialize_MemoryPack | **0.40** | 0.40–0.41 | 416 B |
| Serialize_MessagePack | 1.01 | 0.90–1.04 | 416 B |
| | | | |
| Deserialize_SystemTextJson | 1.79 | 1.77–1.81 | 200 B |
| Deserialize_MemoryPack | **0.49** | 0.48–0.50 | 200 B |
| Deserialize_MessagePack | 1.13 | 1.10–1.17 | 200 B |

### Interpretation

**MemoryPack** is the clear throughput winner: 2.5× faster to serialize and 3.7× faster to deserialize than System.Text.Json. It requires `[MemoryPackable]` on your DTOs and uses a proprietary binary wire format — not suitable for public HTTP APIs but ideal for internal service-to-service communication.

**MessagePack** serializes at parity with STJ but deserializes ~1.6× faster, and serializes with less allocation (416 B against 608 B). Uses the widely-supported MessagePack binary format. Requires `[MessagePackObject]` / `[Key]` attributes on DTOs.

**System.Text.Json** has no DTO annotation requirements and produces human-readable JSON — the right default for public APIs. It is the slowest of the three here. All three deserializers allocate the same 200 B, the deserialized object itself.

Use `options.UseSerializer(new MemoryPackRestSerializer(types => types.Add<UserDto>()))` or `options.UseSerializer<MessagePackRestSerializer>()` to switch serializers in your client registration. MemoryPack needs each `[MemoryPackable]` type registered; see [Serialization](serialization.md#memorypack).

---

## Response bodies: buffered vs `StreamResponses`

`ResponseBodyBenchmarks` reads the same JSON `UserDto[]` response through a generated client, once
with the default buffering and once with [`StreamResponses`](advanced.md#streaming-responses). The
in-memory handler answers with a `Content-Length` and a stream that cannot seek, as a connection
does. `Users` sets the body size: 1 user is about 25 bytes, 1000 users about 30 KB.

| Method | Users | Allocated | Alloc Ratio |
|---|---:|---:|---:|
| Buffered | 1 | 2,112 B | 1.00 |
| Streamed | 1 | 1,992 B | 0.94 |
| Buffered | 1000 | 127,296 B | 1.00 |
| Streamed | 1000 | 98,425 B | 0.77 |

Streaming removes the copy `HttpClient` makes of the body, about 28 KB for the 30 KB body. What
remains is the deserialized array. Streamed takes 0.97× and 1.02× the buffered time for 1 and
1000 users, within the launch spread of each other.

`MemoryPackBodyBenchmarks` measures `MemoryPackRestSerializer` on its own. It reads a body through
pooled buffers that it clears before returning them, so for 1000 users, about 20 KB, reading a
body allocates 80 KB, which is the result, instead of 122 KB from a stream that cannot seek or
101 KB from a seekable one. Writing a body allocates nothing instead of 64 KB;
`MemoryPackSerializerBufferTests` pins the write at 0 B.

---

## How to reproduce

```sh
cd tests/ZeroAlloc.Rest.Benchmarks
dotnet run -c Release -- --filter "*" --launchCount 3
```

BenchmarkDotNet requires Release mode. Debug builds produce incorrect numbers. `--filter "*"` runs
every benchmark class; without a filter, BenchmarkDotNet asks which class to run. To run one class,
name it, for example `--filter "*ResponseBodyBenchmarks*"`.
