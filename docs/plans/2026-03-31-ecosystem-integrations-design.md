# ZeroAlloc.Rest — Ecosystem Integrations Design

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate ZeroAlloc.Collections, ZeroAlloc.Results, and ZeroAlloc.Analyzers into ZeroAlloc.Rest — replacing `ApiResponse<T>` with `Result<T, HttpError>`, using pooled per-request `HeapPooledList<char>` for URL building, and fixing all analyzer-flagged issues.

**Architecture:** Three orthogonal changes: (1) `ApiResponse<T>` is removed and replaced by `Result<T, HttpError>` + `UnitResult<HttpError>` from `ZeroAlloc.Results`; (2) the generated URL builder switches from `System.Text.StringBuilder` to `HeapPooledList<char>` from `ZeroAlloc.Collections`, with a small internal extension helper in `ZeroAlloc.Rest` for ergonomic string appending; (3) `ZeroAlloc.Analyzers` gets a proper version pin in `Directory.Packages.props` and all newly-surfaced diagnostics are fixed.

**Tech Stack:** ZeroAlloc.Results 0.1.4, ZeroAlloc.Collections 0.1.3, ZeroAlloc.Analyzers 1.3.12

---

## New types

### `HttpError` (in `ZeroAlloc.Rest`)

```csharp
namespace ZeroAlloc.Rest;

public sealed record HttpError(
    System.Net.HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null);
```

### Remove: `ApiResponse<T>` (deleted from `ZeroAlloc.Rest`)

---

## Generator changes (MethodModel + ModelExtractor + ClientEmitter)

`MethodModel.ReturnsApiResponse` → renamed to `MethodModel.ReturnsResult`.

`ModelExtractor` detects `Result<T, HttpError>` return type and sets `ReturnsResult = true`, extracts `InnerTypeName = T`.

`ClientEmitter.EmitResponseHandling` emits three branches:
- `ReturnsVoid` → `EnsureSuccessStatusCode()` (unchanged)
- `ReturnsResult` → check `IsSuccessStatusCode`, deserialize → `Result<T,HttpError>.Success(content)` or build `HttpError` → `Result<T,HttpError>.Failure(error)`
- else → `EnsureSuccessStatusCode()` + deserialize (unchanged)

`ClientEmitter.EmitUrlBuilding` switches from `System.Text.StringBuilder` to `HeapPooledList<char>`:
- Uses `using var urlBuilder = new ZeroAlloc.Collections.HeapPooledList<char>(256);`
- Uses a `bool hasQuery` flag instead of trimming trailing `?`/`&`
- Each nullable query param: `if (x != null) { urlBuilder.Append(hasQuery ? '&' : '?'); ... hasQuery = true; }`
- Each non-nullable query param: same but unconditional
- Final: `var url = new string(urlBuilder.AsReadOnlySpan());`

`ClientEmitter` emits `using ZeroAlloc.Collections;` in the generated file header when URL building uses it.

### Internal helper (in `ZeroAlloc.Rest`)

```csharp
internal static class HeapPooledListExtensions
{
    internal static void Append(this HeapPooledList<char> list, ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++)
            list.Add(value[i]);
    }
    internal static void Append(this HeapPooledList<char> list, char value)
        => list.Add(value);
}
```

Generated code uses `urlBuilder.Append("key=")` and `urlBuilder.Append(escapedValue.AsSpan())` via this extension.

---

## Package changes

| File | Change |
|---|---|
| `Directory.Packages.props` | Add `ZeroAlloc.Results 0.1.4`, `ZeroAlloc.Collections 0.1.3`, `ZeroAlloc.Analyzers 1.3.12` |
| `src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj` | Add `<PackageReference Include="ZeroAlloc.Results" />` and `<PackageReference Include="ZeroAlloc.Collections" />` |
| `Directory.Build.props` | Replace existing `ZeroAlloc.Analyzers` entry (if present) — it's now versioned properly in `Directory.Packages.props` |

---

## Analyzer fixes

After adding `ZeroAlloc.Analyzers 1.3.12` properly versioned, build the solution and fix all newly surfaced diagnostics. Expected categories:
- String building patterns (use Span/pooling)
- Async state machine elision (elide async/await where possible)
- Boxing warnings on value types in collections
- LINQ-in-loops patterns
- Static lambda caching opportunities

Do not suppress diagnostics — fix the underlying code. Only add `<NoWarn>` as last resort with a comment explaining why.

---

## Test changes

- Remove tests referencing `ApiResponse<T>`
- Add generator tests: `Generator_Result_ReturnType_EmitsResultWrapping`
- Add unit tests: `HttpError` construction, `Result<T,HttpError>` success/failure paths in generated code
- Update integration tests: methods returning `Result<UserDto, HttpError>` checked for `.IsSuccess`, `.Value`, `.Error`
- Update `IUserApi` test interface in `ZeroAlloc.Rest.Integration.Tests` to use `Result<T, HttpError>` return types where applicable

---

## Docs changes

- Update `docs/advanced.md` — replace `ApiResponse<T>` section with `Result<T, HttpError>` section
- Update `docs/getting-started.md` — remove `ApiResponse<T>` reference
- Update `README.md` features list — replace `ApiResponse<T>` bullet with `Result<T, HttpError>` bullet
