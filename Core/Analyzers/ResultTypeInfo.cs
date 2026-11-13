/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Recognizes Result<TErr> / Result<T, TErr> and their async wrappers
 */

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace PeroTools2.Error.Analyzers;

/// <summary>
/// Determines whether a type is <c>Result&lt;TErr&gt;</c> / <c>Result&lt;T, TErr&gt;</c>, or an async
/// wrapper type that wraps a Result (<c>Task&lt;Result&gt;</c> / <c>UniTask&lt;Result&gt;</c>, etc.).
/// </summary>
internal sealed class ResultTypeInfo
{
    /// <summary>Maximum wrapper depth, so that pathological types cannot expand forever</summary>
    private const int max_wrapper_depth = 8;

    private readonly ImmutableArray<INamedTypeSymbol> m_Definitions;

    private ResultTypeInfo(ImmutableArray<INamedTypeSymbol> definitions) => m_Definitions = definitions;

    /// <summary>
    /// Returns null when the compilation does not reference PeroTools2.PeroError;
    /// the analyzer then exits immediately.
    /// </summary>
    public static ResultTypeInfo TryCreate(Compilation compilation)
    {
        var definitions = ImmutableArray.CreateBuilder<INamedTypeSymbol>(2);

        if (compilation.GetTypeByMetadataName("PeroTools2.Error.Result`1") is { } result1)
        {
            definitions.Add(result1);
        }
        if (compilation.GetTypeByMetadataName("PeroTools2.Error.Result`2") is { } result2)
        {
            definitions.Add(result2);
        }

        return definitions.Count == 0 ? null : new ResultTypeInfo(definitions.ToImmutable());
    }

    /// <summary>Whether the type itself, or the content it wraps, is a Result</summary>
    /// <param name="type">The type to check</param>
    /// <param name="result">The Result type left after the async wrappers are peeled off</param>
    public bool TryGetResult(ITypeSymbol type, out INamedTypeSymbol result)
    {
        result = null;

        var current = type;
        for (int depth = 0; depth < max_wrapper_depth; depth++)
        {
            if (current is not INamedTypeSymbol named)
            {
                return false;
            }

            if (IsResult(named))
            {
                result = named;
                return true;
            }

            if (!IsKnownWrapper(named))
            {
                return false;
            }

            current = named.TypeArguments[0];
        }

        return false;
    }

    private bool IsResult(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        foreach (var candidate in m_Definitions)
        {
            if (SymbolEqualityComparer.Default.Equals(definition, candidate))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Recognizes only a handful of async types that "wrap a single value".
    /// Name matching is used because the assemblies containing UniTask / Awaitable are not
    /// necessarily referenced by the analyzer.
    /// </summary>
    private static bool IsKnownWrapper(INamedTypeSymbol type)
    {
        if (type.Arity != 1 || type.TypeArguments.Length != 1)
        {
            return false;
        }

        switch (type.Name)
        {
            case "Task":
            case "ValueTask":
                return type.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
            // Cysharp.Threading.Tasks.UniTask<T>
            case "UniTask":
            // UnityEngine.Awaitable<T>
            case "Awaitable":
                return true;
            default:
                return false;
        }
    }
}