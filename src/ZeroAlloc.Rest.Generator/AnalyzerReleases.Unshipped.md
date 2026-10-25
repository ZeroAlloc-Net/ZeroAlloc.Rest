; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                 | Severity | Notes
--------|--------------------------|----------|------------------------------------------------------
ZRA007  | ZeroAlloc.Rest.Generator | Error    | IAsyncEnumerable<T> and [EventStream] must go together
ZRA008  | ZeroAlloc.Rest.Generator | Error    | Invalid [EventType] mapping
