; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PERR100 | PeroError.Usage | Warning | A Result is produced and never consumed
PERR101 | PeroError.Usage | Warning | A local Result is never consumed
PERR102 | PeroError.Usage | Warning | A Result is consumed more than once
PERR103 | PeroError.Usage | Warning | A Result parameter is never consumed
