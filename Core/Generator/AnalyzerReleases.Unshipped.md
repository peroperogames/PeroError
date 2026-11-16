; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PERR001 | PeroError.Generator | Error | Invalid error message template
PERR002 | PeroError.Generator | Error | Message template references an undeclared argument
PERR003 | PeroError.Generator | Error | Unsupported message template format item
PERR004 | PeroError.Generator | Error | Duplicate [ErrArg] name
PERR005 | PeroError.Generator | Error | [ErrArg] name conflicts with a built-in parameter
PERR006 | PeroError.Generator | Error | Invalid [ErrArg] name
PERR007 | PeroError.Generator | Error | Invalid [ErrArg] type
PERR008 | PeroError.Generator | Error | Companion class name conflicts with an existing type
PERR009 | PeroError.Generator | Error | Companion classes are not supported for nested enums
PERR010 | PeroError.Generator | Warning | [ErrArg] has no effect
PERR011 | PeroError.Generator | Warning | [ErrMsg] is only allowed on error enum members
PERR014 | PeroError.Generator | Error | Invalid companion class name
PERR015 | PeroError.Generator | Error | Duplicate generated companion class name