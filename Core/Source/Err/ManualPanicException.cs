/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: ManualPanicException
 */

using System;
using System.Runtime.Serialization;

namespace PeroTools2.Error;

/// <summary>
/// Represents one manual panic
/// </summary>
[Serializable]
public class ManualPanicException : Exception
{
    /// <summary>Constructs from an error message</summary>
    public ManualPanicException(string message) : base(message) { }

#pragma warning disable SYSLIB0051 // Obsolete in .NET 8+, but still needed by the netstandard2.0 target
    /// <summary>Serialization constructor, used by the runtime</summary>
    protected ManualPanicException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore SYSLIB0051
}