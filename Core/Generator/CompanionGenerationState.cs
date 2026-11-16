/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Tracks the companion classes generated during one generator run
 */

using System;
using System.Collections.Generic;

namespace PeroTools2.Error.Generator;

/// <summary>
/// Remembers which enum member generated which class during one generator run, so that two
/// members asking for the same class name are reported as a PERR diagnostic instead of turning
/// into a CS0101 inside the generated files.
/// </summary>
internal sealed class CompanionGenerationState
{
    /// <summary>Metadata name of the generated class -> "Enum.Member" that generated it</summary>
    private readonly Dictionary<string, string> m_Owners = new(StringComparer.Ordinal);

    /// <summary>Gets the member that already generated the given class, if any</summary>
    public bool TryGetOwner(string fullClassName, out string owner) => m_Owners.TryGetValue(fullClassName, out owner);

    /// <summary>Reserves the class name for the given member</summary>
    public void Record(string fullClassName, string owner)
    {
        if (fullClassName != null)
        {
            m_Owners[fullClassName] = owner;
        }
    }
}
