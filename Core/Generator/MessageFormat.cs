/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Parses the [ErrMsg] message template into an interpolated string
 */

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace PeroTools2.Error.Generator;

/// <summary>
/// Converts the message template of <c>ErrMsgAttribute</c> (written as an interpolated string) into content
/// that can be put inside <c>$"..."</c> directly.<br/>
/// Supports <c>{name}</c> / <c>{name,alignment}</c> / <c>{name:format}</c>, plus <c>{{</c> / <c>}}</c> escapes.
/// </summary>
internal static class MessageFormat
{
    /// <summary>
    /// Parses the message template.
    /// </summary>
    /// <param name="format">Message template</param>
    /// <param name="identifiers">Argument name =&gt; identifier emitted into the code</param>
    /// <param name="diagnostics">Diagnostics produced while parsing</param>
    /// <param name="location">Location of the diagnostic</param>
    /// <returns>Content that can go inside <c>$"..."</c> directly; null when parsing fails</returns>
    public static string Build(
        string format,
        IReadOnlyDictionary<string, string> identifiers,
        ImmutableArray<CompanionDiagnostic>.Builder diagnostics,
        Location location
    )
    {
        var sb = new StringBuilder(format.Length + 16);
        int index = 0;
        while (index < format.Length)
        {
            char c = format[index];
            if (c == '{')
            {
                // Escaped literal '{'
                if (index + 1 < format.Length && format[index + 1] == '{')
                {
                    sb.Append("{{");
                    index += 2;
                    continue;
                }

                int end = format.IndexOf('}', index + 1);
                if (end < 0)
                {
                    diagnostics.Add(new CompanionDiagnostic(
                        Diagnostics.invalidFormatString, location, "Missing '}' to match the '{'"));
                    return null;
                }

                string hole = format.Substring(index + 1, end - index - 1);
                if (hole.IndexOf('{') >= 0)
                {
                    diagnostics.Add(new CompanionDiagnostic(
                        Diagnostics.invalidFormatString, location, "A format item cannot contain a nested '{'"));
                    return null;
                }

                switch (TryParseHole(hole, identifiers, out string rebuilt, out string badName))
                {
                    case HoleResult.Ok:
                        sb.Append('{').Append(rebuilt).Append('}');
                        break;
                    case HoleResult.UnknownName:
                        diagnostics.Add(new CompanionDiagnostic(
                            Diagnostics.unknownFormatArgument, location, badName));
                        return null;
                    case HoleResult.IllegalCharacter:
                        diagnostics.Add(new CompanionDiagnostic(
                            Diagnostics.invalidFormatString,
                            location,
                            "A format specifier must not contain quotes, backslashes, braces or control characters"));
                        return null;
                    case HoleResult.BadAlignment:
                        diagnostics.Add(new CompanionDiagnostic(
                            Diagnostics.invalidFormatString,
                            location,
                            "The alignment of a format item must be an integer"));
                        return null;
                    default:
                        diagnostics.Add(new CompanionDiagnostic(
                            Diagnostics.unsupportedFormatExpression, location, hole.Trim()));
                        return null;
                }

                index = end + 1;
                continue;
            }

            if (c == '}')
            {
                // Escaped literal '}'
                if (index + 1 < format.Length && format[index + 1] == '}')
                {
                    sb.Append("}}");
                    index += 2;
                    continue;
                }

                diagnostics.Add(new CompanionDiagnostic(
                    Diagnostics.invalidFormatString, location, "Unexpected '}'"));
                return null;
            }

            AppendLiteralChar(sb, c);
            index++;
        }

        return sb.ToString();
    }

    private enum HoleResult
    {
        Ok,
        UnknownName,
        BadSyntax,
        IllegalCharacter,
        BadAlignment,
    }

    /// <summary>Parses one format item; on success returns the interpolated string content</summary>
    private static HoleResult TryParseHole(
        string hole,
        IReadOnlyDictionary<string, string> identifiers,
        out string rebuilt,
        out string badName
    )
    {
        rebuilt = null;
        badName = null;

        int index = 0;
        while (index < hole.Length && char.IsWhiteSpace(hole[index]))
        {
            index++;
        }

        int nameStart = index;
        bool first = true;
        while (index < hole.Length && (hole[index] == '_' || char.IsLetterOrDigit(hole[index])))
        {
            if (first && !(hole[index] == '_' || char.IsLetter(hole[index])))
            {
                // Must not start with a digit
                return HoleResult.BadSyntax;
            }
            first = false;
            index++;
        }

        string name = hole.Substring(nameStart, index - nameStart);
        if (name.Length == 0)
        {
            return HoleResult.BadSyntax;
        }

        // The rest may only be ',alignment' or ':format'
        string rest = hole.Substring(index);
        HoleResult restResult = ValidateRest(rest);
        if (restResult != HoleResult.Ok)
        {
            return restResult;
        }

        if (!identifiers.TryGetValue(name, out string emittedName))
        {
            badName = name;
            return HoleResult.UnknownName;
        }

        rebuilt = emittedName + rest;
        return HoleResult.Ok;
    }

    /// <summary>
    /// Validates the ',alignment' / ':format' tail of a format item.
    /// </summary>
    /// <remarks>
    /// The tail is copied verbatim into the generated <c>$"..."</c> literal, so it must not be able
    /// to end that literal: quotes, backslashes, braces and control characters are rejected here so
    /// the author gets a PERR diagnostic on the attribute instead of a syntax error in the generated
    /// file. The alignment has to be an integer for the same reason.
    /// </remarks>
    private static HoleResult ValidateRest(string rest)
    {
        string trimmed = rest.Trim();
        if (trimmed.Length == 0)
        {
            return HoleResult.Ok;
        }

        char lead = trimmed[0];
        if (lead != ',' && lead != ':')
        {
            return HoleResult.BadSyntax;
        }

        foreach (char c in trimmed)
        {
            if (c == '"' || c == '\\' || c == '{' || c == '}' || char.IsControl(c))
            {
                return HoleResult.IllegalCharacter;
            }
        }

        if (lead == ',')
        {
            int colon = trimmed.IndexOf(':');
            string alignment = colon >= 0 ? trimmed.Substring(1, colon - 1) : trimmed.Substring(1);
            if (!int.TryParse(alignment.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            {
                return HoleResult.BadAlignment;
            }
        }

        return HoleResult.Ok;
    }

    /// <summary>Appends a literal character to the interpolated string, escaping it when required</summary>
    private static void AppendLiteralChar(StringBuilder sb, char c)
    {
        switch (c)
        {
            case '\\':
                sb.Append("\\\\");
                break;
            case '"':
                sb.Append("\\\"");
                break;
            case '\0':
                sb.Append("\\0");
                break;
            case '\a':
                sb.Append("\\a");
                break;
            case '\b':
                sb.Append("\\b");
                break;
            case '\f':
                sb.Append("\\f");
                break;
            case '\n':
                sb.Append("\\n");
                break;
            case '\r':
                sb.Append("\\r");
                break;
            case '\t':
                sb.Append("\\t");
                break;
            case '\v':
                sb.Append("\\v");
                break;
            default:
                if (char.IsControl(c))
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
                break;
        }
    }
}