/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Checks whether the Result returned by a function is discarded
 */

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PeroTools2.Error.Analyzers;

/// <summary>
/// Checks the case where a function returning <c>Result&lt;TErr&gt;</c> / <c>Result&lt;T, TErr&gt;</c>
/// (or a wrapper such as <c>Task&lt;Result&gt;</c> / <c>UniTask&lt;Result&gt;</c>) is called but the
/// returned value is not handled.<br/>
/// </summary>
/// <remarks>
/// <para>
/// The criteria are aligned with Rust's <c>#[must_use]</c> + <c>unused_must_use</c> / <c>unused_variables</c>:
/// it only checks whether the value is dropped, not whether the failure branch is really handled.
/// </para>
/// <para>Forms treated as "already handled" (no warning) and the corresponding Rust code:</para>
/// <list type="bullet">
/// <item>Passed out as a return value or an expression-bodied result
/// (Rust: <c>return r;</c> or <c>?</c> propagation)</item>
/// <item>Assigned to a variable, field or property and read afterwards (Rust: <c>let x = f(); x.is_ok()</c>)</item>
/// <item>Passed as an argument to another function (Rust: <c>consume(f())</c>)</item>
/// <item>Used in a condition, pattern match or switch (Rust: <c>if let Err(e) = f()</c> / <c>match</c>)</item>
/// <item>Explicitly unwrapped: <c>.isOK</c> / <c>.err</c> / <c>.value</c> /
/// <c>.UnWarp()</c> (Rust: <c>.unwrap()</c> / <c>.ok()</c>)</item>
/// <item>Explicit discard: <c>_ = ...</c>, or a variable whose name starts with <c>_</c>
/// (Rust: <c>let _ = ...;</c> / <c>let _x = ...;</c>)</item>
/// </list>
/// <para>Forms that warn, corresponding to Rust's <c>unused_must_use</c> / <c>unused_variables</c>:</para>
/// <list type="bullet">
/// <item>Discarded directly by an expression statement: <c>DoWork();</c> /
/// <c>await DoWorkAsync();</c> (Rust: <c>f();</c>)</item>
/// <item>Discarded by a void-returning expression-bodied method / accessor / lambda:
/// <c>void M() =&gt; DoWork();</c></item>
/// <item>Stored into a local variable whose name does not start with <c>_</c>, and never read
/// afterwards (Rust: <c>let x = f();</c> with x unused)</item>
/// </list>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedResultAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.DiscardedResult, Diagnostics.UnusedResultVariable);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // Nothing to analyze when PeroTools2.PeroError is not referenced
        var resultTypes = ResultTypeInfo.TryCreate(context.Compilation);
        if (resultTypes == null)
        {
            return;
        }

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeExpressionStatement(ctx, resultTypes),
            SyntaxKind.ExpressionStatement);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeArrowExpressionClause(ctx, resultTypes),
            SyntaxKind.ArrowExpressionClause);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeExpressionBodiedLambda(ctx, resultTypes),
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeLocalDeclaration(ctx, resultTypes),
            SyntaxKind.LocalDeclarationStatement);
    }

    #region Expression statements

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context, ResultTypeInfo resultTypes)
    {
        var statement = (ExpressionStatementSyntax)context.Node;
        var expression = statement.Expression;

        // `_ = ...` is an explicit discard, so it counts as handled
        if (expression is AssignmentExpressionSyntax assignment
            && IsDiscard(assignment.Left, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        ReportIfDiscardedResult(context, resultTypes, expression);
    }

    private static bool IsDiscard(ExpressionSyntax target, SemanticModel model, CancellationToken ct) =>
        target is IdentifierNameSyntax { Identifier.ValueText: "_" }
        && model.GetSymbolInfo(target, ct).Symbol == null;

    #endregion

    #region Expression bodies

    private static void AnalyzeArrowExpressionClause(SyntaxNodeAnalysisContext context, ResultTypeInfo resultTypes)
    {
        var arrow = (ArrowExpressionClauseSyntax)context.Node;

        // The value on the right of the arrow is only discarded when the "host" returns void
        // (properties / methods returning a Result pass the value out, so they are not discards)
        if (context.SemanticModel.GetDeclaredSymbol(arrow.Parent, context.CancellationToken) is not IMethodSymbol { ReturnsVoid: true })
        {
            return;
        }

        ReportIfDiscardedResult(context, resultTypes, arrow.Expression);
    }

    private static void AnalyzeExpressionBodiedLambda(SyntaxNodeAnalysisContext context, ResultTypeInfo resultTypes)
    {
        var lambda = (LambdaExpressionSyntax)context.Node;
        var body = lambda.ExpressionBody;
        if (body == null)
        {
            // Statements inside a block-bodied lambda are handled by AnalyzeExpressionStatement
            return;
        }

        var delegateType = context.SemanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType as INamedTypeSymbol;
        if (delegateType?.DelegateInvokeMethod is not { ReturnsVoid: true })
        {
            return;
        }

        ReportIfDiscardedResult(context, resultTypes, body);
    }

    #endregion

    #region Local variables

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context, ResultTypeInfo resultTypes)
    {
        var statement = (LocalDeclarationStatementSyntax)context.Node;
        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        foreach (var declarator in statement.Declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            // Aligned with Rust: a name starting with '_' means "intentionally ignored", so no warning
            // (corresponds to let _ = ...; and let _x = ...;)
            string name = declarator.Identifier.ValueText;
            if (name.Length > 0 && name[0] == '_')
            {
                continue;
            }

            var valueType = model.GetTypeInfo(declarator.Initializer.Value, ct).Type;
            if (!resultTypes.TryGetResult(valueType, out _))
            {
                continue;
            }

            if (model.GetDeclaredSymbol(declarator, ct) is not ILocalSymbol local)
            {
                continue;
            }

            if (IsReadSomewhere(local, declarator, model, ct))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnusedResultVariable, declarator.Identifier.GetLocation(), local.Name));
        }
    }

    /// <summary>Whether the local variable is read anywhere in the scope of its declaration</summary>
    private static bool IsReadSomewhere(ILocalSymbol local, SyntaxNode declaration, SemanticModel model, CancellationToken ct)
    {
        var scope = declaration.Ancestors().FirstOrDefault(static node =>
            node is BaseMethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax
                or AnonymousFunctionExpressionSyntax
                or CompilationUnitSyntax);

        if (scope == null)
        {
            // Be conservative when no scope can be found and do not warn
            return true;
        }

        foreach (var node in scope.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier || identifier.Identifier.ValueText != local.Name)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, ct).Symbol, local))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Utils

    private static void ReportIfDiscardedResult(
        SyntaxNodeAnalysisContext context,
        ResultTypeInfo resultTypes,
        ExpressionSyntax expression
    )
    {
        // Only values produced by a call / creation are of interest: a bare identifier or a field
        // access does not count as discarding a call result
        if (!IsCallLike(expression))
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (!resultTypes.TryGetResult(type, out _))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DiscardedResult, expression.GetLocation(), DescribeExpression(expression)));
    }

    private static bool IsCallLike(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax => true,
        ObjectCreationExpressionSyntax => true,
        ConditionalAccessExpressionSyntax => true,
        // The value produced by await is always dropped, no matter whether a call or a variable is awaited
        AwaitExpressionSyntax => true,
        ParenthesizedExpressionSyntax parenthesized => IsCallLike(parenthesized.Expression),
        CastExpressionSyntax cast => IsCallLike(cast.Expression),
        _ => false,
    };

    /// <summary>Produces a human-readable name for the diagnostic message</summary>
    private static string DescribeExpression(ExpressionSyntax expression) => expression switch
    {
        AwaitExpressionSyntax awaitExpression => DescribeExpression(awaitExpression.Expression),
        InvocationExpressionSyntax invocation => invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => invocation.Expression.ToString(),
        },
        ObjectCreationExpressionSyntax creation => creation.Type.ToString(),
        ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.ToString(),
        ParenthesizedExpressionSyntax parenthesized => DescribeExpression(parenthesized.Expression),
        CastExpressionSyntax cast => DescribeExpression(cast.Expression),
        _ => expression.ToString(),
    };

    #endregion
}