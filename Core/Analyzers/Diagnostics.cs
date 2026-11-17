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

    /// <summary>A Result is produced and never consumed</summary>
    public static readonly DiagnosticDescriptor unconsumedResult = new(
        id: "PERR100",
        title: "Result is not consumed",
        messageFormat: "The Result returned by '{0}' is never consumed; dispose it (using / Dispose()), return it, pass it to another method, or wrap its error with Err.Wrap",
        category: category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Functions returning Result<TErr> / Result<T, TErr> (including Task/UniTask wrappers) "
                     + "hand ownership of the error to the caller; dropping the value neither reports nor releases it.");

    /// <summary>A local Result is never consumed</summary>
    public static readonly DiagnosticDescriptor unconsumedResultVariable = new(
        id: "PERR101",
        title: "Variable holding a Result is never consumed",
        messageFormat: "Variable '{0}' holds a Result that is never disposed, returned, passed on or wrapped",
        category: category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reading isOK / err / value or calling Unwrap() / Match() / Switch() does not consume a Result; "
                     + "the value still has to be disposed, returned, passed on, or have its error wrapped.");

    /// <summary>A Result passed in as a parameter is never consumed</summary>
    public static readonly DiagnosticDescriptor unconsumedResultParameter = new(
        id: "PERR103",
        title: "Parameter holding a Result is never consumed",
        messageFormat: "Parameter '{0}' holds a Result that is never disposed, returned, passed on or wrapped",
        category: category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A method that takes a Result takes ownership of it: it has to dispose it, return it, "
                     + "pass it on, or wrap its error.");

    /// <summary>A Result is consumed more than once</summary>
    public static readonly DiagnosticDescriptor doubleConsumedResult = new(
        id: "PERR102",
        title: "Result is consumed more than once",
        messageFormat: "Variable '{0}' is already consumed; a Result must be consumed exactly once",
        category: category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Result may only be disposed, returned, passed on or wrapped once; consuming it twice "
                     + "hands the same error out twice.");
}
