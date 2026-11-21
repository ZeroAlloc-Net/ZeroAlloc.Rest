; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category             | New Severity | Old Category             | Old Severity | Notes
--------|--------------------------|--------------|--------------------------|--------------|---------------------------------------------
ZRA006  | ZeroAlloc.Rest.Generator | Error        | ZeroAlloc.Rest.Generator | Warning      | No client can be generated for the interface
