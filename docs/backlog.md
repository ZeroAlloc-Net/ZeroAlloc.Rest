# Backlog

Enhancements identified from real-world usage patterns. Items are independent and can be implemented in any order.

---

## 1. Static `[Header]` on methods

**Status:** Attribute + `Value` property already exist — generator support is missing.

The `HeaderAttribute` has a `Value` property intended for compile-time static headers on methods:

```csharp
[Get("/files/{path}")]
[Header("Accept", Value = "application/octet-stream")]
Task<byte[]> GetFileAsync(string path, CancellationToken ct = default);
```

Today, `Value` is never extracted by `ModelExtractor` and never emitted by `ClientEmitter`. The workaround is setting the header via `ConfigureHttpClient`, which is global — not per-method.

**Work needed:**
- Extract static headers from method-level `[Header]` attributes in `ModelExtractor.ExtractMethod`
- Add a `StaticHeaders` collection to `MethodModel`
- Emit `request.Headers.TryAddWithoutValidation(name, value)` in `ClientEmitter.EmitRequestCreation`

---

## 2. Multi-value query parameters (`IEnumerable<T>` with `[Query]`)

**Status:** Not supported — passing a collection for a `[Query]` parameter emits a single value via `.ToString()`.

APIs that accept repeated keys (`?tags=a&tags=b`) require manual query string construction today:

```csharp
[Get("/items")]
Task<List<ItemDto>> SearchAsync([Query] IEnumerable<string> tags, CancellationToken ct = default);
// desired: ?tags=a&tags=b
```

**Work needed:**
- Detect `IEnumerable<T>` (excluding `string`) on `[Query]` parameters in `ModelExtractor`
- Add an `IsCollection` flag to `ParameterModel`
- In `ClientEmitter`, emit a `foreach` loop over the collection, appending each value as a separate `key=value` pair
- Handle nullable collections

---

## 3. Form-encoded body (`[FormBody]`)

**Status:** Not supported — the generator only serializes bodies via `IRestSerializer` (JSON, MessagePack, etc.).

OAuth token endpoints and many legacy APIs require `application/x-www-form-urlencoded`. Today there is no way to declare this on the interface:

```csharp
[Post("/oauth/token")]
Task<TokenResponse> GetTokenAsync([FormBody] TokenRequest request, CancellationToken ct = default);
```

**Work needed:**
- Add `FormBodyAttribute` to `ZeroAlloc.Rest.Attributes`
- Detect it in `ModelExtractor` (new `ParameterKind.FormBody`)
- In `ClientEmitter`, emit `FormUrlEncodedContent` built from the parameter's public properties (source-generated via a helper, to stay AOT-safe — no reflection)
- Consider a `Dictionary<string, string>` overload as a simpler escape hatch
