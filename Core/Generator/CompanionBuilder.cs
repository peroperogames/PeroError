/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Builds companion classes from [ErrMsg] / [ErrArg]
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace PeroTools2.Error.Generator;

/// <summary>
/// Reads the attributes on an error enum field into a <see cref="CompanionSpec"/> and runs every check.
/// </summary>
internal static class CompanionBuilder
{
    /// <summary>Parameter names reserved by the companion class; [ErrArg] cannot use them</summary>
    private static readonly HashSet<string> s_ReservedArgumentNames = new(StringComparer.Ordinal)
    {
        "msg",
        "causedBy",
        "ex",
        "callerMemberName",
        "callerFilePath",
        "callerLineNumber",
    };

    /// <summary>Handles one field: generate a companion class, report diagnostics, or return null</summary>
    public static CompanionModel Build(SemanticModel model, IFieldSymbol field, CancellationToken ct)
    {
        var compilation = model.Compilation;

        var messageAttribute = FindAttribute(field, compilation.GetTypeByMetadataName(PeroErrorGenerator.ErrMsgAttributeMetadataName));
        var argumentAttributes = FindAttributes(field, compilation.GetTypeByMetadataName(PeroErrorGenerator.ErrArgAttributeMetadataName));

        var fallbackLocation = GetFallbackLocation(field);

        if (field.ContainingType is not { TypeKind: TypeKind.Enum } enumSymbol)
        {
            // [ErrMsg] is only allowed on error enum members
            return messageAttribute == null
                ? null
                : CompanionModel.FromWarning(new CompanionDiagnostic(
                    Diagnostics.MessageOnNonEnumField,
                    GetLocation(messageAttribute, ct) ?? fallbackLocation,
                    field.Name));
        }

        if (messageAttribute == null)
        {
            // Without [ErrMsg] no code is generated; [ErrArg] alone is only a warning
            if (argumentAttributes.Count == 0)
            {
                return null;
            }

            return CompanionModel.FromWarning(new CompanionDiagnostic(
                Diagnostics.ArgumentWithoutMessage,
                GetLocation(argumentAttributes[0], ct) ?? fallbackLocation,
                field.Name));
        }

        return BuildCompanion(model, field, enumSymbol, messageAttribute, argumentAttributes, fallbackLocation, ct);
    }

    private static CompanionModel BuildCompanion(
        SemanticModel model,
        IFieldSymbol field,
        INamedTypeSymbol enumSymbol,
        AttributeData messageAttribute,
        List<AttributeData> argumentAttributes,
        Location fallbackLocation,
        CancellationToken ct
    )
    {
        var location = GetLocation(messageAttribute, ct) ?? fallbackLocation;
        var compilation = model.Compilation;

        // If the template constant cannot be read (for example, the user wrote an invalid expression),
        // the compiler has already reported an error, so do not report it again here.
        if (!TryReadMessageFormat(messageAttribute, out string format))
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<CompanionDiagnostic>();

        if (string.IsNullOrEmpty(format))
        {
            diagnostics.Add(new CompanionDiagnostic(Diagnostics.InvalidFormatString, location, "The message template cannot be empty"));
        }

        if (enumSymbol.ContainingType != null)
        {
            diagnostics.Add(new CompanionDiagnostic(Diagnostics.NestedEnumNotSupported, location, enumSymbol.Name));
        }

        var args = ReadArguments(argumentAttributes, compilation, fallbackLocation, ct, diagnostics, out var declaredNames);
        string className = ResolveClassName(messageAttribute, enumSymbol, field, location, diagnostics);

        CheckNameConflict(enumSymbol, className, compilation, location, diagnostics);
        string message = BuildMessage(format, args, declaredNames, location, diagnostics);

        if (ContainsError(diagnostics))
        {
            return CompanionModel.FromDiagnostics(diagnostics.ToImmutable());
        }

        var spec = new CompanionSpec
        {
            namespaceName = enumSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : enumSymbol.ContainingNamespace.ToDisplayString(),
            enumTypeName = enumSymbol.ToDisplayString(SymbolUtilities.TypeFormat),
            enumDisplayName = enumSymbol.ToDisplayString(),
            enumMemberName = SymbolUtilities.EscapeIdentifier(field.Name),
            className = SymbolUtilities.EscapeIdentifier(className),
            accessibility = enumSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            args = args,
            interpolatedMessage = message,
            generatorName = PeroErrorGenerator.GeneratorName,
            generatorVersion = PeroErrorGenerator.GeneratorVersion,
        };

        string hintName = SymbolUtilities.SanitizeHintName(enumSymbol.ToDisplayString() + "." + field.Name) + ".g.cs";
        return CompanionModel.FromSource(hintName, CompanionEmitter.Emit(spec), diagnostics.ToImmutable());
    }

    #region [ErrMsg]

    private static bool TryReadMessageFormat(AttributeData attribute, out string format)
    {
        format = null;
        if (attribute.ConstructorArguments.Length == 0)
        {
            return false;
        }

        var constant = attribute.ConstructorArguments[0];
        if (constant.Kind == TypedConstantKind.Error)
        {
            return false;
        }

        if (constant.Value is string text)
        {
            format = text;
            return true;
        }

        // [ErrMsg(null)]
        format = null;
        return true;
    }

    private static string ResolveClassName(
        AttributeData messageAttribute,
        INamedTypeSymbol enumSymbol,
        IFieldSymbol field,
        Location location,
        ImmutableArray<CompanionDiagnostic>.Builder diagnostics
    )
    {
        string className = null;
        foreach (var named in messageAttribute.NamedArguments)
        {
            if (named.Key == "Name" && named.Value.Value is string name && name.Length > 0)
            {
                className = name;
            }
        }

        if (className == null)
        {
            className = enumSymbol.Name + "_" + field.Name;
        }

        if (!SymbolUtilities.IsValidIdentifierName(className))
        {
            diagnostics.Add(new CompanionDiagnostic(Diagnostics.InvalidCompanionName, location, className));
        }

        return className;
    }

    private static void CheckNameConflict(
        INamedTypeSymbol enumSymbol,
        string className,
        Compilation compilation,
        Location location,
        ImmutableArray<CompanionDiagnostic>.Builder diagnostics
    )
    {
        if (string.IsNullOrEmpty(className))
        {
            return;
        }

        var ns = enumSymbol.ContainingNamespace;
        string fullName = ns.IsGlobalNamespace ? className : ns.ToDisplayString() + "." + className;
        if (compilation.GetTypeByMetadataName(fullName) != null)
        {
            diagnostics.Add(new CompanionDiagnostic(Diagnostics.CompanionNameConflict, location, className));
        }
    }

    private static string BuildMessage(
        string format,
        List<CompanionArg> args,
        HashSet<string> declaredNames,
        Location location,
        ImmutableArray<CompanionDiagnostic>.Builder diagnostics
    )
    {
        if (string.IsNullOrEmpty(format))
        {
            return string.Empty;
        }

        var identifiers = new Dictionary<string, string>(declaredNames.Count, StringComparer.Ordinal);
        // Put every declared name in first, so that "the argument declaration is wrong" does not also
        // report "the template references an undeclared argument".
        foreach (string declared in declaredNames)
        {
            identifiers[declared] = declared;
        }
        foreach (var arg in args)
        {
            identifiers[arg.rawName] = arg.emittedName;
        }

        return MessageFormat.Build(format, identifiers, diagnostics, location) ?? string.Empty;
    }

    #endregion

    #region [ErrArg]

    private static List<CompanionArg> ReadArguments(
        List<AttributeData> attributes,
        Compilation compilation,
        Location fallbackLocation,
        CancellationToken ct,
        ImmutableArray<CompanionDiagnostic>.Builder diagnostics,
        out HashSet<string> declaredNames
    )
    {
        var result = new List<CompanionArg>();
        declaredNames = new HashSet<string>(StringComparer.Ordinal);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            var location = GetLocation(attribute, ct) ?? fallbackLocation;
            string rawName = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string
                : null;

            if (rawName != null)
            {
                declaredNames.Add(rawName);
            }

            if (!SymbolUtilities.IsValidIdentifierName(rawName))
            {
                diagnostics.Add(new CompanionDiagnostic(Diagnostics.InvalidArgumentName, location, rawName ?? "<null>"));
                continue;
            }

            if (s_ReservedArgumentNames.Contains(rawName))
            {
                diagnostics.Add(new CompanionDiagnostic(Diagnostics.ReservedArgumentName, location, rawName));
                continue;
            }

            if (!seenNames.Add(rawName))
            {
                diagnostics.Add(new CompanionDiagnostic(Diagnostics.DuplicateArgumentName, location, rawName));
                continue;
            }

            var typeConstant = attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1] : default;
            if (!IsUsableParameterType(typeConstant.Value as ITypeSymbol))
            {
                diagnostics.Add(new CompanionDiagnostic(Diagnostics.InvalidArgumentType, location, rawName));
                continue;
            }

            var typeSymbol = (ITypeSymbol)typeConstant.Value;
            string comment = attribute.ConstructorArguments.Length > 2
                ? attribute.ConstructorArguments[2].Value as string
                : null;

            result.Add(new CompanionArg
            {
                rawName = rawName,
                emittedName = SymbolUtilities.EscapeIdentifier(rawName),
                typeName = typeSymbol.ToDisplayString(SymbolUtilities.TypeFormat),
                comment = comment,
            });
        }

        return result;
    }

    private static bool IsUsableParameterType(ITypeSymbol type) =>
        type != null
        && type.TypeKind != TypeKind.Error
        && type.TypeKind != TypeKind.Pointer
        && type.TypeKind != TypeKind.FunctionPointer
        && type.SpecialType != SpecialType.System_Void
        && !(type is INamedTypeSymbol { IsStatic: true });

    #endregion

    #region Utils

    private static AttributeData FindAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        if (attributeType == null)
        {
            return null;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                return attribute;
            }
        }
        return null;
    }

    private static List<AttributeData> FindAttributes(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        var result = new List<AttributeData>();
        if (attributeType == null)
        {
            return result;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                result.Add(attribute);
            }
        }
        return result;
    }

    private static Location GetFallbackLocation(ISymbol symbol) =>
        symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None;

    private static Location GetLocation(AttributeData attribute, CancellationToken ct)
    {
        var reference = attribute.ApplicationSyntaxReference;
        if (reference == null)
        {
            return null;
        }

        try
        {
            return reference.GetSyntax(ct).GetLocation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsError(ImmutableArray<CompanionDiagnostic>.Builder diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.isError)
            {
                return true;
            }
        }
        return false;
    }

    #endregion
}
