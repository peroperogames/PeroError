/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Symbol handling utilities used by the generator
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PeroTools2.Error.Generator;

/// <summary>Utility methods related to symbols and code text</summary>
internal static class SymbolUtilities
{
    /// <summary>Type name format emitted into code: global:: prefix, keywords for built-in types</summary>
    public static readonly SymbolDisplayFormat TypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                               | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>Whether the name is a valid C# identifier (keywords are allowed)</summary>
    public static bool IsValidIdentifierName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.IsValidIdentifier(name));

    /// <summary>Emits an identifier into the code, prefixing keywords with <c>@</c></summary>
    public static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>Turns any name into a safe hint name fragment</summary>
    public static string SanitizeHintName(string name)
    {
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            bool valid = c == '.' || c == '_' || c == '-' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                         || (c >= '0' && c <= '9');
            if (!valid)
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }
}