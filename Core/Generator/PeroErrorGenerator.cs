/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: PeroError source generator: generates companion error classes from error enums
 */

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PeroTools2.Error.Generator;

/// <summary>
/// The Roslyn source generator of PeroError.<br/>
/// It generates companion error classes from the <c>ErrMsgAttribute</c> / <c>ErrArgAttribute</c>
/// attributes on error enum members:
/// <code>
/// public enum IOErr
/// {
///     [ErrMsg("File {path} Do Not Exist")]
///     [ErrArg("path", typeof(string), "Path that was not found")]
///     NotFound,
/// }
///
/// // Generated:
/// // public sealed class IOErr_NotFound : Err&lt;IOErr&gt;
/// </code>
/// </summary>
/// <remarks>
/// This uses <see cref="ISourceGenerator"/> rather than an incremental generator because Unity's
/// Roslyn is still stuck at <c>Microsoft.CodeAnalysis 3.8</c> (the Unity manual "Roslyn analyzers and
/// source generators" explicitly requires 3.8), while incremental generators need 4.0+,
/// <c>ForAttributeWithMetadataName</c> needs 4.3+, and <c>ISyntaxContextReceiver</c> needs 4.0+.
/// A generator/analyzer built against 3.8 also loads in newer Roslyn (and in Rider / Visual Studio).
/// </remarks>
[Generator]
public sealed class PeroErrorGenerator : ISourceGenerator
{
    /// <summary>Generator name, written into the generated code's [GeneratedCode] attribute</summary>
    public const string generator_name = "PeroTools2.PeroError.Generator";

    /// <summary>Fully qualified metadata name of ErrMsgAttribute</summary>
    public const string err_msg_attribute_metadata_name = "PeroTools2.Error.ErrMsgAttribute";

    /// <summary>Fully qualified metadata name of ErrArgAttribute</summary>
    public const string err_arg_attribute_metadata_name = "PeroTools2.Error.ErrArgAttribute";

    /// <summary>Generator version, written into the generated code's [GeneratedCode] attribute</summary>
    public static readonly string generatorVersion =
        typeof(PeroErrorGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <inheritdoc />
    public void Initialize(GeneratorInitializationContext context) =>
        context.RegisterForSyntaxNotifications(static () => new CandidateReceiver());

    /// <inheritdoc />
    public void Execute(GeneratorExecutionContext context)
    {
        // The 3.8 [Generator] does not filter by language, so filter it here.
        if (context.Compilation.Language != LanguageNames.CSharp)
        {
            return;
        }

        if (context.SyntaxReceiver is not CandidateReceiver receiver)
        {
            return;
        }

        foreach (var candidate in receiver.candidates)
        {
            var model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
            if (model.GetDeclaredSymbol(candidate, context.CancellationToken) is not IFieldSymbol field)
            {
                continue;
            }

            var result = CompanionBuilder.Build(model, field, context.CancellationToken);
            if (result == null)
            {
                continue;
            }

            foreach (var diagnostic in result.diagnostics)
            {
                var reported = diagnostic.ToDiagnostic();
                if (reported != null)
                {
                    context.ReportDiagnostic(reported);
                }
            }

            if (result.source != null && result.hintName != null)
            {
                context.AddSource(result.hintName, SourceText.From(result.source, Encoding.UTF8));
            }
        }
    }

    /// <summary>Collects only members carrying a PeroError attribute, avoiding a full semantic walk</summary>
    private sealed class CandidateReceiver : ISyntaxReceiver
    {
        public readonly List<SyntaxNode> candidates = new();

        public void OnVisitSyntaxNode(SyntaxNode node)
        {
            switch (node)
            {
                case EnumMemberDeclarationSyntax member when HasPeroErrorAttribute(member.AttributeLists):
                    candidates.Add(member);
                    break;

                // [ErrMsg] on an ordinary field must be discovered as well, so that we can report
                // "only allowed on error enum members". Note that a VariableDeclarator's parent is a
                // VariableDeclaration; its grandparent is the FieldDeclaration.
                case VariableDeclaratorSyntax variable
                    when variable.Parent is VariableDeclarationSyntax
                         {
                             Parent: FieldDeclarationSyntax declaration
                         }
                         && HasPeroErrorAttribute(declaration.AttributeLists):
                    candidates.Add(variable);
                    break;
            }
        }

        private static bool HasPeroErrorAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            foreach (var list in attributeLists)
            {
                foreach (var attribute in list.Attributes)
                {
                    switch (GetLastName(attribute.Name))
                    {
                        case "ErrMsg":
                        case "ErrMsgAttribute":
                        case "ErrArg":
                        case "ErrArgAttribute":
                            return true;
                    }
                }
            }
            return false;
        }

        private static string GetLastName(NameSyntax name) => name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetLastName(qualified.Right),
            AliasQualifiedNameSyntax alias => GetLastName(alias.Name),
            _ => null,
        };
    }
}
