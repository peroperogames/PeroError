; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PERR100 | PeroError.Usage | Warning | A call to a function returning a Result discards the returned value
PERR101 | PeroError.Usage | Warning | A local variable holding a Result is never read