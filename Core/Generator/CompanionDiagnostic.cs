/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Comparable diagnostic information, used for incremental generator caching
 */

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace PeroTools2.Error.Generator;

/// <summary>
/// Diagnostic information produced during generation.<br/>
/// Pipeline values of an incremental generator must be comparable, and <see cref="Diagnostic"/> is
/// awkward to compare, so only the descriptor/location/message arguments are kept here and turned back
/// into a <see cref="Diagnostic"/> in the output stage.
/// </summary>
internal readonly struct CompanionDiagnostic : IEquatable<CompanionDiagnostic>
{
    private readonly DiagnosticDescriptor m_Descriptor;
    private readonly Location m_Location;
    private readonly ImmutableArray<string> m_MessageArgs;

    public CompanionDiagnostic(DiagnosticDescriptor descriptor, Location location, params string[] messageArgs)
    {
        m_Descriptor = descriptor;
        m_Location = location;
        m_MessageArgs = messageArgs == null || messageArgs.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(messageArgs);
    }

    /// <summary>Whether this diagnostic blocks code generation</summary>
    public bool isError => m_Descriptor != null && m_Descriptor.DefaultSeverity == DiagnosticSeverity.Error;

    /// <summary>Restores the diagnostic handed to the compiler</summary>
    public Diagnostic ToDiagnostic()
    {
        if (m_Descriptor == null)
        {
            return null;
        }

        var location = m_Location ?? Location.None;
        if (m_MessageArgs.IsDefaultOrEmpty)
        {
            return Diagnostic.Create(m_Descriptor, location);
        }

        var args = new object[m_MessageArgs.Length];
        for (int i = 0; i < m_MessageArgs.Length; i++)
        {
            args[i] = m_MessageArgs[i];
        }
        return Diagnostic.Create(m_Descriptor, location, args);
    }

    public bool Equals(CompanionDiagnostic other) =>
        string.Equals(m_Descriptor?.Id, other.m_Descriptor?.Id, StringComparison.Ordinal)
        && LocationEquals(m_Location, other.m_Location)
        && MessageArgsEquals(m_MessageArgs, other.m_MessageArgs);

    public override bool Equals(object obj) => obj is CompanionDiagnostic other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = m_Descriptor?.Id.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (m_Location?.SourceSpan.Start ?? 0);
            hash = (hash * 397) ^ (m_MessageArgs.IsDefaultOrEmpty ? 0 : m_MessageArgs.Length);
            return hash;
        }
    }

    private static bool LocationEquals(Location a, Location b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a == null || b == null)
        {
            return false;
        }
        return a.Kind == b.Kind
               && ReferenceEquals(a.SourceTree, b.SourceTree)
               && a.SourceSpan == b.SourceSpan;
    }

    private static bool MessageArgsEquals(ImmutableArray<string> a, ImmutableArray<string> b)
    {
        if (a.IsDefaultOrEmpty && b.IsDefaultOrEmpty)
        {
            return true;
        }
        if (a.IsDefaultOrEmpty || b.IsDefaultOrEmpty || a.Length != b.Length)
        {
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}