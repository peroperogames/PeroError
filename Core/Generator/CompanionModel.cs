/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Generation result: either a source file or a set of diagnostics
 */

using System.Collections.Immutable;

namespace PeroTools2.Error.Generator;

/// <summary>Result for one error enum member: either a source file or only diagnostics</summary>
internal sealed class CompanionModel
{
    /// <summary>Generated source; null means nothing was produced</summary>
    public readonly string source;

    /// <summary>Hint name used for the generated source; null means nothing was produced</summary>
    public readonly string hintName;

    /// <summary>Diagnostics produced while processing</summary>
    public readonly ImmutableArray<CompanionDiagnostic> diagnostics;

    private CompanionModel(string hintName, string source, ImmutableArray<CompanionDiagnostic> diagnostics)
    {
        this.hintName = hintName;
        this.source = source;
        this.diagnostics = diagnostics.IsDefault ? ImmutableArray<CompanionDiagnostic>.Empty : diagnostics;
    }

    /// <summary>A source file was generated</summary>
    public static CompanionModel FromSource(string hintName, string source, ImmutableArray<CompanionDiagnostic> diagnostics) =>
        new(hintName, source, diagnostics);

    /// <summary>Diagnostics only, no source</summary>
    public static CompanionModel FromDiagnostics(ImmutableArray<CompanionDiagnostic> diagnostics) =>
        new(null, null, diagnostics);

    /// <summary>Only a single warning</summary>
    public static CompanionModel FromWarning(CompanionDiagnostic diagnostic) =>
        new(null, null, ImmutableArray.Create(diagnostic));
}
