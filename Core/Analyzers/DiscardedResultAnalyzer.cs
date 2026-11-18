/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Checks that every Result is consumed exactly once
 */

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PeroTools2.Error.Analyzers;

/// <summary>
/// Checks that every <c>Result&lt;TErr&gt;</c> / <c>Result&lt;T, TErr&gt;</c> value
/// (including the ones carried by <c>Task</c> / <c>ValueTask</c> / <c>UniTask</c> / <c>Awaitable</c>)
/// is consumed <b>exactly once</b>.
/// </summary>
/// <remarks>
/// <para>A Result is consumed when, and only when:</para>
/// <list type="number">
/// <item>it is disposed in the current method — <c>using var r = ...</c>, <c>using (...) { }</c>
/// or an explicit <c>r.Dispose()</c>;</item>
/// <item>it is returned from the method (<c>return r;</c>, expression-bodied members and lambdas);</item>
/// <item>it is passed as an argument to another method;</item>
/// <item>its error is handed over: wrapped into a new error with <c>Err.Wrap</c>, propagated with
/// <c>AddCallerFrame</c>, or returned / passed on as an argument.</item>
/// </list>
/// <para>
/// Every other use — reading <c>isOK</c> / <c>err</c> / <c>value</c>, calling <c>Unwrap()</c> /
/// <c>Match()</c> / <c>Switch()</c>, discarding it as a statement — does not consume the Result.
/// A Result that is consumed twice would hand its error back to the pool twice, so double
/// consumption is reported as well.
/// </para>
/// <para>
/// Consuming it in two places that can never both run is not double consumption: the two branches
/// of one <c>if</c> / <c>switch</c> / <c>?:</c>, and the code after a branch that ends with
/// <c>return</c> or <c>throw</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedResultAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.unconsumedResult,
            Diagnostics.unconsumedResultVariable,
            Diagnostics.doubleConsumedResult,
            Diagnostics.unconsumedResultParameter);

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

        // Every kind of body is analyzed once; nested lambdas / local functions get their own run
        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeBody(ctx, resultTypes),
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.FieldDeclaration,
            SyntaxKind.EventFieldDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
    }

    /// <summary>Analyzes one body: the produced Results in it and the locals that hold them</summary>
    private static void AnalyzeBody(SyntaxNodeAnalysisContext context, ResultTypeInfo resultTypes)
    {
        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        var origins = new HashSet<ExpressionSyntax>();
        var declarations = new List<LocalDeclarationStatementSyntax>();

        foreach (var node in context.Node.DescendantNodes(
                     // The node itself is always entered; nested function bodies get their own run
                     descendIntoChildren: n => n == context.Node || !IsNestedFunction(n)))
        {
            switch (node)
            {
                case LocalDeclarationStatementSyntax declaration:
                    declarations.Add(declaration);
                    break;

                case ExpressionSyntax expression
                    when IsProducedValue(expression)
                         && IsResultType(model.GetTypeInfo(expression, ct).Type, resultTypes):
                    // Collapse chained / branched producers (await, ?:, parentheses, casts) into one value
                    origins.Add(Unwrap(expression));
                    break;
            }
        }

        foreach (var origin in origins)
        {
            ReportIfNotConsumed(context, model, ct, origin);
        }

        foreach (var declaration in declarations)
        {
            AnalyzeLocalConsumption(context, declaration, resultTypes);
        }

        AnalyzeParameterConsumption(context, resultTypes);
    }

    /// <summary>Bodies of nested lambdas / local functions are analyzed by their own run</summary>
    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    #region Parameters

    /// <summary>A Result that enters the method as a parameter is owned by the method as well</summary>
    private static void AnalyzeParameterConsumption(SyntaxNodeAnalysisContext context, ResultTypeInfo resultTypes)
    {
        // A declaration without a body (an interface member, an abstract / extern method, or the
        // declaration half of a partial method) has no implementation that could consume anything
        if (context.Node is MethodDeclarationSyntax { Body: null, ExpressionBody: null })
        {
            return;
        }

        var parameterList = context.Node switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            LocalFunctionStatementSyntax localFunction => localFunction.ParameterList,
            _ => null,
        };

        if (parameterList == null)
        {
            return;
        }

        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        foreach (var parameter in parameterList.Parameters)
        {
            var symbol = model.GetDeclaredSymbol(parameter, ct);
            if (symbol == null || !IsDirectResultType(symbol.Type, resultTypes))
            {
                continue;
            }

            var consumptionSites = new List<SyntaxNode>(2);
            CollectConsumptions(symbol, context.Node, model, ct, consumptionSites);
            ReportConsumption(context, Diagnostics.unconsumedResultParameter, symbol.Name, parameter.Identifier.GetLocation(), consumptionSites);
        }
    }

    #endregion

    #region Consumption accounting

    /// <summary>Reports a Result that is never consumed, or consumed twice on the same path</summary>
    private static void ReportConsumption(
        SyntaxNodeAnalysisContext context,
        DiagnosticDescriptor unconsumedDescriptor,
        string name,
        Location declarationLocation,
        List<SyntaxNode> consumptionSites
    )
    {
        if (consumptionSites.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(unconsumedDescriptor, declarationLocation, name));
            return;
        }

        // Consuming it in two branches of the same if/switch is fine: only one of them runs
        for (int i = 0; i < consumptionSites.Count; i++)
        {
            for (int j = i + 1; j < consumptionSites.Count; j++)
            {
                if (AreMutuallyExclusive(consumptionSites[i], consumptionSites[j]))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.doubleConsumedResult,
                    consumptionSites[j].GetLocation(),
                    name));
                return;
            }
        }
    }

    /// <summary>
    /// Whether two places can never both run, because they sit in different branches of the same
    /// <c>if</c> / <c>switch</c> / <c>?:</c>, or because one of them sits in a branch that always
    /// leaves the method before the other one is reached.
    /// </summary>
    private static bool AreMutuallyExclusive(SyntaxNode first, SyntaxNode second) =>
        AreInDifferentBranches(first, second) || ExitsBefore(first, second) || ExitsBefore(second, first);

    /// <summary>
    /// Whether two places sit in different branches of the same <c>if</c> / <c>switch</c> / <c>?:</c>.
    /// </summary>
    private static bool AreInDifferentBranches(SyntaxNode first, SyntaxNode second)
    {
        var firstGuards = GetBranchGuards(first);
        if (firstGuards.Count == 0)
        {
            return false;
        }

        var secondGuards = GetBranchGuards(second);
        foreach (var guard in firstGuards)
        {
            if (secondGuards.TryGetValue(guard.Key, out int secondBranch) && secondBranch != guard.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="node"/> sits in a branch that never falls through to the code after
    /// its owner — the branch returns or throws — while <paramref name="other"/> sits outside of it.
    /// </summary>
    /// <remarks>
    /// Only <c>return</c> and <c>throw</c> count as leaving the method; <c>break</c> / <c>continue</c>
    /// leave the loop instead, so the code after the loop can still run on the next iteration.
    /// </remarks>
    private static bool ExitsBefore(SyntaxNode node, SyntaxNode other)
    {
        for (SyntaxNode? current = node; current != null; current = current.Parent)
        {
            SyntaxNode? branch = current.Parent switch
            {
                IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, current)
                    => ifStatement.Statement,

                ElseClauseSyntax { Parent: IfStatementSyntax ifStatement } elseClause
                    when ReferenceEquals(elseClause.Statement, current) => elseClause.Statement,

                SwitchStatementSyntax when current is SwitchSectionSyntax section => section,
                _ => null,
            };

            if (branch != null && AlwaysExits(branch) && !branch.Span.Contains(other.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the statement always leaves the current method</summary>
    private static bool AlwaysExits(SyntaxNode? statement)
    {
        switch (statement)
        {
            case null:
                return false;

            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return true;

            case BlockSyntax block:
                return block.Statements.Count > 0
                       && AlwaysExits(block.Statements[block.Statements.Count - 1]);

            case SwitchSectionSyntax section:
                return section.Statements.Count > 0
                       && AlwaysExits(section.Statements[section.Statements.Count - 1]);

            // Only a complete if/else can never fall through
            case IfStatementSyntax ifStatement:
                return ifStatement.Else != null
                       && AlwaysExits(ifStatement.Statement)
                       && AlwaysExits(ifStatement.Else.Statement);

            default:
                return false;
        }
    }

    /// <summary>The branch containers (and the branch index) a node sits in</summary>
    private static Dictionary<SyntaxNode, int> GetBranchGuards(SyntaxNode node)
    {
        var guards = new Dictionary<SyntaxNode, int>();
        for (var current = node; current != null; current = current.Parent)
        {
            switch (current.Parent)
            {
                case IfStatementSyntax ifStatement:
                    guards[ifStatement] = ReferenceEquals(ifStatement.Statement, current) ? 1 : 2;
                    break;

                case ConditionalExpressionSyntax conditional:
                    guards[conditional] = ReferenceEquals(conditional.WhenTrue, current) ? 1 : 2;
                    break;

                case SwitchStatementSyntax switchStatement when current is SwitchSectionSyntax section:
                    guards[switchStatement] = switchStatement.Sections.IndexOf(section) + 1;
                    break;
            }
        }

        return guards;
    }

    #endregion

    #region Local variables

    private static void AnalyzeLocalConsumption(
        SyntaxNodeAnalysisContext context,
        LocalDeclarationStatementSyntax statement,
        ResultTypeInfo resultTypes
    )
    {
        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        // `using var r = ...;` and a declaration inside `using (...) { }` are consumed by the using
        bool declaredWithUsing = statement.UsingKeyword != default || statement.Parent is UsingStatementSyntax;

        foreach (var declarator in statement.Declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            // Wrapper types (Task<Result> etc.) are reported where they are awaited or dropped
            if (!IsDirectResultType(model.GetTypeInfo(declarator.Initializer.Value, ct).Type, resultTypes))
            {
                continue;
            }

            if (model.GetDeclaredSymbol(declarator, ct) is not ILocalSymbol local)
            {
                continue;
            }

            var consumptionSites = new List<SyntaxNode>(2);
            if (declaredWithUsing)
            {
                // A using declaration disposes the Result once, at the end of the scope
                consumptionSites.Add(declarator);
            }

            CollectConsumptions(local, statement.Parent, model, ct, consumptionSites);
            ReportConsumption(context, Diagnostics.unconsumedResultVariable, local.Name, declarator.Identifier.GetLocation(), consumptionSites);
        }
    }

    /// <summary>Collects every place where the given symbol holding a Result is consumed</summary>
    private static void CollectConsumptions(
        ISymbol symbol,
        SyntaxNode? scope,
        SemanticModel model,
        CancellationToken ct,
        List<SyntaxNode> consumptionSites
    )
    {
        if (scope == null)
        {
            return;
        }

        foreach (var node in scope.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier || identifier.Identifier.ValueText != symbol.Name)
            {
                continue;
            }

            if (IsWriteTarget(identifier))
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, ct).Symbol, symbol))
            {
                continue;
            }

            if (IsConsumed(identifier, model, ct))
            {
                consumptionSites.Add(identifier);
            }
        }
    }

    /// <summary>Whether the identifier is only written to (assignment target or <c>out</c> argument)</summary>
    private static bool IsWriteTarget(IdentifierNameSyntax identifier)
    {
        if (identifier.Parent is AssignmentExpressionSyntax assignment
            && assignment.Left == identifier
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return true;
        }

        return identifier.Parent is ArgumentSyntax argument
               && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
    }

    #endregion

    #region Consumption rules

    /// <summary>Whether the expression is a place where a fresh value comes into existence</summary>
    private static bool IsProducedValue(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax
            or AwaitExpressionSyntax
            or ObjectCreationExpressionSyntax
            or ConditionalAccessExpressionSyntax;

    /// <summary>
    /// Folds the expressions that carry a value further up without changing it, so the value is
    /// judged by where it finally ends up.
    /// </summary>
    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        var node = expression;
        while (true)
        {
            switch (node.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized when parenthesized.Expression == node:
                    node = parenthesized;
                    continue;
                case CastExpressionSyntax cast when cast.Expression == node:
                    node = cast;
                    continue;
                case AwaitExpressionSyntax awaitExpression when awaitExpression.Expression == node:
                    node = awaitExpression;
                    continue;
                case ConditionalExpressionSyntax conditional
                    when conditional.WhenTrue == node || conditional.WhenFalse == node:
                    node = conditional;
                    continue;
                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right == node:
                    node = binary;
                    continue;
                default:
                    return node;
            }
        }
    }

    /// <summary>Whether the value of the expression is consumed by one of the four allowed ways</summary>
    private static bool IsConsumed(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        var node = Unwrap(expression);

        switch (node.Parent)
        {
            // Passed as an argument to another method
            case ArgumentSyntax argument:
                return argument.Parent is ArgumentListSyntax;

            // Returned from the current method
            case ReturnStatementSyntax:
                return true;

            case ArrowExpressionClauseSyntax arrow:
                return !IsVoidReturning(arrow.Parent, model, ct);

            case LambdaExpressionSyntax lambda:
                return !IsVoidReturning(lambda, model, ct);

            // Disposed by the using statement that owns it
            case UsingStatementSyntax:
                return true;

            // Initializers are judged by the analysis of the field / local they are assigned to
            case EqualsValueClauseSyntax:
                return true;

            // Assigned to an existing variable: the value lives on there. `_ = ...` is a discard and
            // therefore does not consume anything.
            case AssignmentExpressionSyntax assignment when assignment.Right == node:
                return !IsDiscardTarget(assignment.Left);

            // Dispose() on the Result, or its error being handed over
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == node:
                return IsDisposeCall(memberAccess) || IsErrHandOff(memberAccess, model, ct);

            default:
                return false;
        }
    }

    /// <summary>Skips the null-forgiving operator (<c>!</c>) that may sit between two member accesses</summary>
    private static ExpressionSyntax SkipNullForgiving(ExpressionSyntax expression)
    {
        while (expression.Parent is PostfixUnaryExpressionSyntax postfix
               && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
               && postfix.Operand == expression)
        {
            expression = postfix;
        }

        return expression;
    }
    private static bool IsDiscardTarget(ExpressionSyntax target) =>
        target is IdentifierNameSyntax { Identifier.ValueText: "_" };

    private static bool IsDisposeCall(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.ValueText != "Dispose")
        {
            return false;
        }

        var receiver = SkipNullForgiving(memberAccess);
        return receiver.Parent is InvocationExpressionSyntax invocation && invocation.Expression == receiver;
    }

    /// <summary>
    /// Detects the ways the error of the Result is handed over to somebody else:
    /// <c>result.err.Wrap(...)</c> / <c>result.err.AddCallerFrame(...)</c>, or the error value itself
    /// being returned or passed as an argument.
    /// </summary>
    private static bool IsErrHandOff(MemberAccessExpressionSyntax errAccess, SemanticModel model, CancellationToken ct)
    {
        if (errAccess.Name.Identifier.ValueText != "err")
        {
            return false;
        }

        var errorReceiver = SkipNullForgiving(errAccess);

        // The error value itself leaves the method: ownership goes with it
        if (IsHandedOffValue(errorReceiver, model, ct))
        {
            return true;
        }

        if (errorReceiver.Parent is not MemberAccessExpressionSyntax handOffAccess
            || handOffAccess.Expression != errorReceiver
            || handOffAccess.Name.Identifier.ValueText is not ("Wrap" or "AddCallerFrame"))
        {
            return false;
        }

        var handOffReceiver = SkipNullForgiving(handOffAccess);
        if (handOffReceiver.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != handOffReceiver)
        {
            return false;
        }

        return model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol { Name: "Wrap" or "AddCallerFrame" };
    }

    /// <summary>Whether the expression leaves the method as its return value or as an argument</summary>
    private static bool IsHandedOffValue(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        var node = Unwrap(expression);
        switch (node.Parent)
        {
            case ArgumentSyntax argument:
                return argument.Parent is ArgumentListSyntax;

            case ReturnStatementSyntax:
                return true;

            case ArrowExpressionClauseSyntax arrow:
                return !IsVoidReturning(arrow.Parent, model, ct);

            case LambdaExpressionSyntax lambda:
                return !IsVoidReturning(lambda, model, ct);

            default:
                return false;
        }
    }

    /// <summary>Whether the owner of an expression body returns void (so the value is discarded)</summary>
    private static bool IsVoidReturning(SyntaxNode? owner, SemanticModel model, CancellationToken ct)
    {
        if (owner == null)
        {
            return false;
        }

        if (owner is LambdaExpressionSyntax lambda)
        {
            return model.GetTypeInfo(lambda, ct).ConvertedType
                   is INamedTypeSymbol { DelegateInvokeMethod: { ReturnsVoid: true } };
        }

        return model.GetDeclaredSymbol(owner, ct) is IMethodSymbol { ReturnsVoid: true };
    }

    #endregion

    #region Utils

    private static bool IsResultType(ITypeSymbol? type, ResultTypeInfo resultTypes) =>
        type != null && resultTypes.TryGetResult(type, out _);

    private static bool IsDirectResultType(ITypeSymbol? type, ResultTypeInfo resultTypes) =>
        type != null
        && resultTypes.TryGetResult(type, out INamedTypeSymbol result)
        && SymbolEqualityComparer.Default.Equals(type, result);

    private static void ReportIfNotConsumed(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        CancellationToken ct,
        ExpressionSyntax origin
    )
    {
        if (IsConsumed(origin, model, ct))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.unconsumedResult,
            origin.GetLocation(),
            DescribeExpression(origin)));
    }

    /// <summary>Human readable name used in the diagnostic message</summary>
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
