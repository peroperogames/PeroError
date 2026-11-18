/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Checks that every Result is consumed exactly once
 */

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// <c>Match()</c> / <c>Switch()</c>, discarding it as a statement, storing it in a field or property,
/// or awaiting a wrapper only to drop the value — does not consume the Result.
/// A Result that is consumed twice would hand its error back to the pool twice, so double
/// consumption is reported as well; assigning over a Result (<c>r = Load();</c>) drops the value it
/// held, which has its own diagnostic.
/// </para>
/// <para>
/// Consuming it in two places that can never both run is not double consumption: the two branches
/// of one <c>if</c> / <c>switch</c> / <c>?:</c>, and the code after a branch that ends with
/// <c>return</c> or <c>throw</c>. Results passed as <c>ref</c> / <c>out</c> / <c>in</c> parameters
/// stay owned by the caller and are not analyzed.
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
            Diagnostics.unconsumedResultParameter,
            Diagnostics.overwrittenResult);

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

        // All identifiers of the body are collected once, so every local / parameter resolves against
        // this list instead of walking the body again. Nested bodies are included on purpose: a Result
        // captured by a lambda has been used.
        var identifiers = new List<IdentifierNameSyntax>();
        foreach (var node in context.Node.DescendantNodes())
        {
            if (node is IdentifierNameSyntax identifier)
            {
                identifiers.Add(identifier);
            }
        }

        foreach (var declaration in declarations)
        {
            AnalyzeLocalConsumption(context, declaration, resultTypes, identifiers);
        }

        AnalyzeParameterConsumption(context, resultTypes, identifiers);
    }

    /// <summary>Bodies of nested lambdas / local functions are analyzed by their own run</summary>
    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    #region Parameters

    /// <summary>A Result that enters the method as a parameter is owned by the method as well</summary>
    private static void AnalyzeParameterConsumption(
        SyntaxNodeAnalysisContext context,
        ResultTypeInfo resultTypes,
        List<IdentifierNameSyntax> identifiers
    )
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
            if (symbol == null
                // ref / out / in parameters stay owned by the caller (Try-pattern, in-arguments)
                || symbol.RefKind != RefKind.None
                || !IsDirectResultType(symbol.Type, resultTypes))
            {
                continue;
            }

            var consumptionSites = new List<SyntaxNode>(2);
            var overwrites = new List<SyntaxNode>(1);
            CollectConsumptions(symbol, identifiers, model, ct, consumptionSites, overwrites);
            ReportConsumption(
                context, Diagnostics.unconsumedResultParameter, symbol.Name,
                parameter.Identifier.GetLocation(), consumptionSites, overwrites);
        }
    }

    #endregion

    #region Consumption accounting

    /// <summary>Reports a Result that is never consumed, dropped by an assignment, or consumed twice</summary>
    private static void ReportConsumption(
        SyntaxNodeAnalysisContext context,
        DiagnosticDescriptor unconsumedDescriptor,
        string name,
        Location declarationLocation,
        List<SyntaxNode> consumptionSites,
        List<SyntaxNode> overwrites
    )
    {
        if (consumptionSites.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(unconsumedDescriptor, declarationLocation, name));
            return;
        }

        // Assigning over the value drops the error that was held before: it never returns to the pool
        if (overwrites.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.overwrittenResult,
                overwrites[0].GetLocation(),
                name));
            return;
        }

        // Nothing can be consumed twice by a single place
        if (consumptionSites.Count < 2)
        {
            return;
        }

        // The branch path of every site is built once, so comparing two sites afterwards only looks
        // at the small precomputed sets instead of walking the syntax tree again for every pair
        var sites = new ConsumptionSite[consumptionSites.Count];
        for (int i = 0; i < sites.Length; i++)
        {
            sites[i] = new ConsumptionSite(consumptionSites[i]);
        }

        // Consuming it in two branches of the same if/switch is fine: only one of them runs
        for (int i = 0; i < sites.Length; i++)
        {
            for (int j = i + 1; j < sites.Length; j++)
            {
                if (sites[i].IsMutuallyExclusiveWith(sites[j]))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.doubleConsumedResult,
                    sites[j].location,
                    name));
                return;
            }
        }
    }

    /// <summary>A place where a Result is consumed, together with the branch path it sits on</summary>
    private sealed class ConsumptionSite
    {
        /// <summary>The consuming identifier</summary>
        private readonly SyntaxNode m_Node;

        /// <summary>Branch containers of the site, mapped to the index of the branch inside them</summary>
        private readonly Dictionary<SyntaxNode, int>? m_Guards;

        /// <summary>Branches that contain the site and always leave the method (<c>return</c> / <c>throw</c>)</summary>
        private readonly List<SyntaxNode>? m_ExitingBranches;

        public ConsumptionSite(SyntaxNode node)
        {
            m_Node = node;
            (m_Guards, m_ExitingBranches) = AnalyzeBranchPath(node);
        }

        /// <summary>Where the consumption happens</summary>
        public Location location => m_Node.GetLocation();

        /// <summary>
        /// Whether the two sites can never both run, because they sit in different branches of the same
        /// <c>if</c> / <c>switch</c> / <c>?:</c>, or because one of them sits in a branch that always
        /// leaves the method before the other one is reached.
        /// </summary>
        public bool IsMutuallyExclusiveWith(ConsumptionSite other) =>
            DivergesFrom(other) || ExitsBefore(other) || other.ExitsBefore(this);

        /// <summary>Whether the two sites were taken in different branches of one shared construct</summary>
        private bool DivergesFrom(ConsumptionSite other)
        {
            if (m_Guards == null || other.m_Guards == null)
            {
                return false;
            }

            // Probing the smaller map keeps the cost proportional to the shallower branch path
            var (first, second) = m_Guards.Count <= other.m_Guards.Count
                ? (m_Guards, other.m_Guards)
                : (other.m_Guards, m_Guards);

            foreach (var guard in first)
            {
                if (second.TryGetValue(guard.Key, out int branch) && branch != guard.Value)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether this site sits in an exiting branch that the other site is outside of</summary>
        private bool ExitsBefore(ConsumptionSite other)
        {
            if (m_ExitingBranches == null)
            {
                return false;
            }

            foreach (var branch in m_ExitingBranches)
            {
                if (!branch.Span.Contains(other.m_Node.Span))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The branch containers of a node, and the branches among them that always leave the method</summary>
        private static (Dictionary<SyntaxNode, int>? guards, List<SyntaxNode>? exitingBranches) AnalyzeBranchPath(SyntaxNode node)
        {
            Dictionary<SyntaxNode, int>? guards = null;
            List<SyntaxNode>? exitingBranches = null;

            for (SyntaxNode? current = node; current != null; current = current.Parent)
            {
                SyntaxNode container;
                SyntaxNode branch;
                int branchIndex;

                switch (current.Parent)
                {
                    case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, current):
                        (container, branch, branchIndex) = (ifStatement, ifStatement.Statement, 1);
                        break;

                    case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement } elseClause
                        when ReferenceEquals(elseClause.Statement, current):
                        (container, branch, branchIndex) = (ifStatement, elseClause.Statement, 2);
                        break;

                    case ConditionalExpressionSyntax conditional
                        when ReferenceEquals(conditional.WhenTrue, current) || ReferenceEquals(conditional.WhenFalse, current):
                        (container, branch, branchIndex) = (
                            conditional,
                            conditional,
                            ReferenceEquals(conditional.WhenTrue, current) ? 1 : 2);
                        break;

                    case SwitchStatementSyntax switchStatement when current is SwitchSectionSyntax section:
                        (container, branch, branchIndex) = (switchStatement, section, switchStatement.Sections.IndexOf(section) + 1);
                        break;

                    default:
                        continue;
                }

                guards ??= new Dictionary<SyntaxNode, int>(2);
                guards[container] = branchIndex;

                if (AlwaysExits(branch))
                {
                    (exitingBranches ??= new List<SyntaxNode>(1)).Add(branch);
                }
            }

            return (guards, exitingBranches);
        }
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
    #endregion

    #region Local variables

    private static void AnalyzeLocalConsumption(
        SyntaxNodeAnalysisContext context,
        LocalDeclarationStatementSyntax statement,
        ResultTypeInfo resultTypes,
        List<IdentifierNameSyntax> identifiers
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

            var local = model.GetDeclaredSymbol(declarator, ct);
            if (local is not ILocalSymbol localSymbol)
            {
                continue;
            }

            // The declared type is what the local ends up holding: the implicit conversion from
            // Err<T> or from T happens in the declaration, not in the initializer expression
            if (!IsDirectResultType(localSymbol.Type, resultTypes))
            {
                // Wrappers (Task<Result> etc.) are fine when they are awaited or handed on; a wrapper
                // that is only stored loses both the task and the Result it would have produced
                if (IsResultType(localSymbol.Type, resultTypes) && !IsAwaitedOrHandedOff(localSymbol, identifiers, model, ct))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.unconsumedResult,
                        declarator.Initializer.Value.GetLocation(),
                        DescribeExpression(declarator.Initializer.Value)));
                }

                continue;
            }

            var consumptionSites = new List<SyntaxNode>(2);
            var overwrites = new List<SyntaxNode>(1);
            if (declaredWithUsing)
            {
                // A using declaration disposes the Result once, at the end of the scope
                consumptionSites.Add(declarator);
            }

            CollectConsumptions(localSymbol, identifiers, model, ct, consumptionSites, overwrites);
            ReportConsumption(
                context, Diagnostics.unconsumedResultVariable, localSymbol.Name,
                declarator.Identifier.GetLocation(), consumptionSites, overwrites);
        }
    }

    /// <summary>
    /// Whether a wrapper local (<c>Task&lt;Result&gt;</c> / <c>UniTask&lt;Result&gt;</c> ...) is used:
    /// awaited, returned or passed on.
    /// </summary>
    private static bool IsAwaitedOrHandedOff(
        ISymbol localSymbol,
        List<IdentifierNameSyntax> identifiers,
        SemanticModel model,
        CancellationToken ct
    )
    {
        foreach (var identifier in identifiers)
        {
            if (identifier.Identifier.ValueText != localSymbol.Name
                || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, ct).Symbol, localSymbol))
            {
                continue;
            }

            if (identifier.Ancestors().OfType<AwaitExpressionSyntax>().Any()
                || IsHandedOffValue(identifier, model, ct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collects every place where the given symbol holding a Result is consumed</summary>
    private static void CollectConsumptions(
        ISymbol symbol,
        List<IdentifierNameSyntax> identifiers,
        SemanticModel model,
        CancellationToken ct,
        List<SyntaxNode> consumptionSites,
        List<SyntaxNode> overwrites
    )
    {
        // The name is filtered before GetSymbolInfo, which is the expensive query
        foreach (var identifier in identifiers)
        {
            if (identifier.Identifier.ValueText != symbol.Name
                || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, ct).Symbol, symbol))
            {
                continue;
            }

            if (IsWriteTarget(identifier))
            {
                overwrites.Add(identifier);
                continue;
            }

            if (IsConsumed(identifier, model, ct))
            {
                consumptionSites.Add(identifier);
            }
        }
    }

    /// <summary>Whether the identifier is written to instead of read (assignment target or <c>out</c> argument)</summary>
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

            // Initializers are judged by the analysis of the local they are assigned to. A field or
            // property initializer has no such analysis, so the Result stored there is not consumed:
            // its error would sit in the member and never return to the pool.
            case EqualsValueClauseSyntax equalsValue:
                return !IsMemberInitializer(equalsValue);

            // Assigned to an existing variable: the value lives on there. `_ = ...` is a discard and
            // assigning over a field / property drops the error instead of returning it.
            case AssignmentExpressionSyntax assignment when assignment.Right == node:
                return !IsDiscardTarget(assignment.Left) && !IsMemberTarget(assignment.Left, model, ct);

            // `yield return` hands the value to whoever enumerates the iterator
            case YieldStatementSyntax:
                return true;

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

    /// <summary>Whether the value is stored in a field / property initializer</summary>
    private static bool IsMemberInitializer(EqualsValueClauseSyntax equalsValue) => equalsValue.Parent switch
    {
        PropertyDeclarationSyntax => true,
        VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax } } => true,
        _ => false,
    };

    /// <summary>Whether the assignment target is a field / property, so the value is stored in a member</summary>
    private static bool IsMemberTarget(ExpressionSyntax target, SemanticModel model, CancellationToken ct) =>
        model.GetSymbolInfo(target, ct).Symbol is IFieldSymbol or IPropertySymbol;

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

            case YieldStatementSyntax:
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
        && resultTypes.TryGetResult(type, out var result)
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
