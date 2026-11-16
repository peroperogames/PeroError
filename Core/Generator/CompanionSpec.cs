/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: All the information needed to generate a companion class
 */

using System.Collections.Generic;

namespace PeroTools2.Error.Generator;

/// <summary>Describes one piece of extra data in the companion class constructor</summary>
internal sealed class CompanionArg
{
    /// <summary>Original name as written in the declaration, such as <c>path</c></summary>
    public string rawName;

    /// <summary>Name emitted into the code; keywords are prefixed with <c>@</c>, as in <c>@event</c></summary>
    public string emittedName;

    /// <summary>Fully qualified type name emitted into the code</summary>
    public string typeName;

    /// <summary>Documentation comment, may be null</summary>
    public string comment;
}

/// <summary>Everything needed to generate one companion class</summary>
internal sealed class CompanionSpec
{
    /// <summary>Namespace; null means the global namespace</summary>
    public string namespaceName;

    /// <summary>Fully qualified name of the error enum, such as <c>global::Foo.Bar.IOErr</c></summary>
    public string enumTypeName;

    /// <summary>Display name of the error enum (without global::), used in documentation comments</summary>
    public string enumDisplayName;

    /// <summary>Error enum member name (already keyword-escaped)</summary>
    public string enumMemberName;

    /// <summary>Generated companion class name (already keyword-escaped)</summary>
    public string className;

    /// <summary>Accessibility of the companion class, <c>public</c> or <c>internal</c></summary>
    public string accessibility;

    /// <summary>Extra data, appearing as constructor parameters in declaration order</summary>
    public List<CompanionArg> args = new();

    /// <summary>
    /// Default message template escaped for an interpolated string (without the surrounding
    /// <c>$"</c> and <c>"</c>).
    /// </summary>
    public string interpolatedMessage;

    /// <summary>Generator name, written into [GeneratedCode]</summary>
    public string generatorName;

    /// <summary>Generator version, written into [GeneratedCode]</summary>
    public string generatorVersion;
}