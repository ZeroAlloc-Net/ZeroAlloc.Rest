# Backlog

Enhancements identified from real-world usage patterns. Items are independent and can be implemented in any order.

---

## Shipped

The original three v1 deferred items have all shipped — kept here for traceability:

1. **Static `[Header]` on methods.** Extractor + emitter wired. `MethodModel.StaticHeaders` is populated from method-level `[Header(name, Value = ...)]` and emitted via `request.Headers.TryAddWithoutValidation(...)` in `ClientEmitter`.
2. **Multi-value query parameters (`IEnumerable<T>` with `[Query]`).** `ParameterModel.IsCollection` flag drives a `foreach` emission in `ClientEmitter`, producing the `?tags=a&tags=b` shape required by APIs that accept repeated keys.
3. **Form-encoded body (`[FormBody]`).** `FormBodyAttribute` + `ParameterKind.FormBody` end-to-end; `ClientEmitter` builds `FormUrlEncodedContent` from the parameter's public properties (AOT-safe, source-generated, no reflection). Useful for OAuth token endpoints and legacy form-urlencoded APIs.

---

## Open

(none — add new deferred items below as they accumulate)
