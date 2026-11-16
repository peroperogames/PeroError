/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Diagnostic descriptions for the Result usage analysis
 */

using Microsoft.CodeAnalysis;

namespace PeroTools2.Error.Analyzers;

/// <summary>Diagnostics produced by the Result usage analysis</summary>
internal static class Diagnostics
{
    private const string category = "PeroError.Usage";

    /// <summary>The Result in a function's return value is discarded</summary>
    public static readonly DiagnosticDescriptor discardedResult = new(
        id: "PERR100",
        title: "Result is discarded",
        messageFormat: "The Result returned by '{0}' is discarded; check isOK and handle the error, or use \"_ = ...\" to ignore it explicitly",
        category: category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Functions returning Result<TErr> / Result<T, TErr> (including Task/UniTask wrappers) "
                     + "carry the failure information in the return value; discarding it lets the error vanish silently.");

    /// <summary>A local variable holding a Result is never read</summary>
    public static readonly DiagnosticDescriptor unusedResultVariable = new(
        id: "PERR101",
        title: "Variable holding a Result is never used",
        messageFormat: "Variable '{0}' holds a Result that is never read; check isOK and handle the error, or use \"_ = ...\" to ignore it explicitly",
        category: category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Storing a Result in a variable without reading it discards its failure information.");
}